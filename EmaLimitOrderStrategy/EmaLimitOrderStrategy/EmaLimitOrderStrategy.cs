// ----------------------------------------------------------------------------------------------------
//
//    EMA Limit Order Strategy
//
//    Continuously rests a single pending limit order priced at the fast or slow EMA (configurable),
//    on the side implied by the current EMA relationship:
//
//      fast EMA < slow EMA  ->  resting Sell Limit
//      fast EMA > slow EMA  ->  resting Buy Limit
//
//    Only one position is ever open at a time. While no position is open, the resting order is
//    repriced every bar to keep tracking the EMA (not just when the fast/slow relationship flips),
//    and it flips side immediately if the relationship flips while it is still unfilled. Once a
//    position opens, order management pauses; as soon as that position closes (TP, SL, or manual),
//    the bot immediately re-arms a fresh resting order at the then-current EMA level/direction.
//
// ----------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots;

public enum LimitLevel
{
    FastEma,
    SlowEma
}

[Robot(AccessRights = AccessRights.None, AddIndicators = true)]
public class EmaLimitOrderStrategy : Robot
{
    [Parameter("Source", Group = "Moving Averages")]
    public DataSeries SourceSeries { get; set; }

    [Parameter("Fast EMA Periods", Group = "Moving Averages", DefaultValue = 29, MinValue = 5, MaxValue = 100)]
    public int FastPeriods { get; set; }

    [Parameter("Slow EMA Periods", Group = "Moving Averages", DefaultValue = 211, MinValue = 50, MaxValue = 300)]
    public int SlowPeriods { get; set; }

    [Parameter("Limit Price Level", Group = "Order Management", DefaultValue = LimitLevel.FastEma)]
    public LimitLevel PriceLevel { get; set; }

    [Parameter("Replace Tolerance (pips)", Group = "Order Management", DefaultValue = 2.0, MinValue = 0.1)]
    public double ReplaceTolerancePips { get; set; }

    [Parameter("Order Expiration (minutes, 0 = none)", Group = "Order Management", DefaultValue = 0, MinValue = 0)]
    public int ExpirationMinutes { get; set; }

    [Parameter("Re-evaluate On Every Tick", Group = "Order Management", DefaultValue = false)]
    public bool EvaluateOnTick { get; set; }

    [Parameter("Quantity (Lots)", Group = "Volume", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
    public double Quantity { get; set; }

    [Parameter("Stop Loss (pips, 0 = off)", Group = "Risk", DefaultValue = 20, MinValue = 0)]
    public double StopLossPips { get; set; }

    [Parameter("Take Profit (pips, 0 = off)", Group = "Risk", DefaultValue = 40, MinValue = 0)]
    public double TakeProfitPips { get; set; }

    [Parameter("Risk Percent Of Balance (0 = fixed lots)", Group = "Risk", DefaultValue = 0, MinValue = 0)]
    public double RiskPercent { get; set; }

    [Parameter("Show EMA Lines", Group = "Chart", DefaultValue = true)]
    public bool ShowEmaLines { get; set; }

    [Parameter("Mark Direction Flips", Group = "Chart", DefaultValue = true)]
    public bool MarkSignals { get; set; }

    private string Label => string.Format("EmaLimitOrderStrategy_{0}", TimeFrame);

    private ExponentialMovingAverage _fastMa;
    private ExponentialMovingAverage _slowMa;
    private TradeType? _lastDrawnDirection;

    protected override void OnStart()
    {
        _fastMa = Indicators.ExponentialMovingAverage(SourceSeries, FastPeriods);
        _slowMa = Indicators.ExponentialMovingAverage(SourceSeries, SlowPeriods);

        Positions.Closed += OnPositionClosed;
        PendingOrders.Filled += OnPendingOrderFilled;
        PendingOrders.Cancelled += OnPendingOrderCancelled;

        // Print("EmaLimitOrderStrategy started on {0} {1}: fast={2}, slow={3}, level={4}, tolerance={5}p, qty={6}, sl={7}, tp={8}",
        //     SymbolName, TimeFrame, FastPeriods, SlowPeriods, PriceLevel, ReplaceTolerancePips, Quantity, StopLossPips, TakeProfitPips);

        if (ShowEmaLines || MarkSignals)
            WarmUpChart();
    }

    protected override void OnStop()
    {
        Positions.Closed -= OnPositionClosed;
        PendingOrders.Filled -= OnPendingOrderFilled;
        PendingOrders.Cancelled -= OnPendingOrderCancelled;
    }

    protected override void OnBar()
    {
        // Index of the bar that just closed.
        var index = Bars.ClosePrices.Count - 2;

        if (ShowEmaLines)
            DrawEmaLines(index);

        var warmupIndex = Math.Max(FastPeriods, SlowPeriods);

        if (index < warmupIndex)
            return;

        if (MarkSignals)
            MarkDirectionIfChanged(index);

        Evaluate(index);
    }

    protected override void OnTick()
    {
        if (!EvaluateOnTick)
            return;

        Evaluate(GetEvaluationIndex());
    }

    private int GetEvaluationIndex()
    {
        return EvaluateOnTick ? Bars.ClosePrices.Count - 1 : Bars.ClosePrices.Count - 2;
    }

    // Re-derives desired direction/price from current EMA state and reconciles the resting order
    // against it. Safe to call from OnBar, OnTick, or any event handler - it never assumes anything
    // about prior in-memory state, only what PendingOrders/Positions currently report.
    private void Evaluate(int index)
    {
        var warmupIndex = Math.Max(FastPeriods, SlowPeriods);

        if (index < warmupIndex)
            return;

        // Single-position constraint: never place or maintain a resting order while a position
        // opened by this bot is still open.
        if (Positions.Any(p => p.SymbolName == SymbolName && p.Label == Label))
            return;

        var fast = _fastMa.Result[index];
        var slow = _slowMa.Result[index];

        if (double.IsNaN(fast) || double.IsNaN(slow) || fast == slow)
            return;

        var direction = fast > slow ? TradeType.Buy : TradeType.Sell;
        var targetPrice = PriceLevel == LimitLevel.FastEma ? fast : slow;

        ManagePendingOrder(direction, targetPrice);
    }

    private void ManagePendingOrder(TradeType direction, double targetPrice)
    {
        var existing = PendingOrders.FirstOrDefault(o => o.SymbolName == SymbolName && o.Label == Label);

        if (existing == null)
        {
            PlaceOrder(direction, targetPrice);
            return;
        }

        if (existing.TradeType != direction)
        {
            // Print("Direction flipped to {0} while {1} order [{2}] still resting - cancelling for replacement.",
            //     direction, existing.TradeType, existing.Id);
            CancelOrder(existing);
            return;
        }

        var toleranceDistance = ReplaceTolerancePips * Symbol.PipSize;
        var drift = Math.Abs(existing.TargetPrice - targetPrice);

        if (drift < toleranceDistance)
            return;

        // Print("Repricing {0} order [{1}]: old={2:F5}, new={3:F5}, drift={4:F1}p (tolerance={5:F1}p).",
        //     direction, existing.Id, existing.TargetPrice, targetPrice, drift / Symbol.PipSize, ReplaceTolerancePips);
        CancelOrder(existing);
    }

    private void CancelOrder(PendingOrder order)
    {
        var result = order.Cancel();

        // if (!result.IsSuccessful)
        //     Print("Cancel failed for order [{0}]: {1}", order.Id, result.Error);
    }

    private void PlaceOrder(TradeType direction, double targetPrice)
    {
        double? stopLossPips = StopLossPips == 0 ? (double?)null : StopLossPips;
        double? takeProfitPips = TakeProfitPips == 0 ? (double?)null : TakeProfitPips;
        var volumeInUnits = GetVolumeInUnits(stopLossPips);
        DateTime? expiration = ExpirationMinutes > 0 ? Server.Time.AddMinutes(ExpirationMinutes) : (DateTime?)null;

        var result = PlaceLimitOrder(direction, SymbolName, volumeInUnits, targetPrice, Label,
            stopLossPips, takeProfitPips, ProtectionType.Relative, expiration);

        // if (result.IsSuccessful)
        //     Print("Placed {0} limit @ {1:F5} ({2}): id={3}, volume={4}, sl={5}p, tp={6}p.",
        //         direction, targetPrice, PriceLevel, result.PendingOrder.Id, volumeInUnits, stopLossPips, takeProfitPips);
        // else
        //     Print("Failed to place {0} limit @ {1:F5}: {2}", direction, targetPrice, result.Error);
    }

    // Sizes volume off account risk when RiskPercent is set and a stop-loss distance is known,
    // otherwise falls back to the fixed Quantity parameter.
    private double GetVolumeInUnits(double? stopLossPips)
    {
        if (RiskPercent > 0 && stopLossPips.HasValue && stopLossPips.Value > 0)
        {
            var riskAmount = Account.Balance * RiskPercent / 100;
            var riskPerUnit = stopLossPips.Value * Symbol.PipValue;

            if (riskPerUnit > 0)
                return Symbol.NormalizeVolumeInUnits(riskAmount / riskPerUnit, RoundingMode.Down);
        }

        return Symbol.QuantityToVolumeInUnits(Quantity);
    }

    private void OnPendingOrderFilled(PendingOrderFilledEventArgs args)
    {
        var position = args.Position;

        if (position.SymbolName != SymbolName || position.Label != Label)
            return;

        // Print("Pending order filled: {0} {1} units @ {2:F5}, positionId={3}.",
        //     position.TradeType, position.VolumeInUnits, position.EntryPrice, position.Id);
    }

    private void OnPendingOrderCancelled(PendingOrderCancelledEventArgs args)
    {
        var order = args.PendingOrder;

        if (order.SymbolName != SymbolName || order.Label != Label)
            return;

        // Print("Pending order [{0}] {1} cancelled: reason={2}.", order.Id, order.TradeType, args.Reason);

        if (args.Reason == PendingOrderCancellationReason.Rejected)
            return;

        // If a position already exists (race with a fill), Evaluate() will no-op anyway, but skip
        // the call entirely for clarity.
        if (Positions.Any(p => p.SymbolName == SymbolName && p.Label == Label))
            return;

        Evaluate(GetEvaluationIndex());
    }

    private void OnPositionClosed(PositionClosedEventArgs args)
    {
        var position = args.Position;

        if (position.SymbolName != SymbolName || position.Label != Label)
            return;

        // Print("Position closed [{0}]: {1} {2} units, entry={3:F5}, netProfit={4:F2} {5}, reason={6}. Re-arming.",
        //     position.Id, position.TradeType, position.VolumeInUnits, position.EntryPrice,
        //     position.NetProfit, Account.Asset.Name, args.Reason);

        Evaluate(GetEvaluationIndex());
    }

    private void WarmUpChart()
    {
        var firstIndex = Math.Max(2, Math.Max(FastPeriods, SlowPeriods));
        var lastIndex = Bars.ClosePrices.Count - 2;

        for (var index = firstIndex; index <= lastIndex; index++)
        {
            if (ShowEmaLines)
                DrawEmaLines(index);

            if (MarkSignals)
                MarkDirectionIfChanged(index);
        }
    }

    // Colors the EMAs since the auto-plotted indicator lines don't expose per-instance colors
    // through this API - fast=yellow, slow=red.
    private void DrawEmaLines(int index)
    {
        if (Chart == null || index < 1)
            return;

        var fastName = string.Format("EmaLimitOrder_Fast_{0}", Bars.OpenTimes[index].Ticks);
        var slowName = string.Format("EmaLimitOrder_Slow_{0}", Bars.OpenTimes[index].Ticks);

        Chart.DrawTrendLine(fastName, index - 1, _fastMa.Result[index - 1], index, _fastMa.Result[index], Color.Yellow, 2);
        Chart.DrawTrendLine(slowName, index - 1, _slowMa.Result[index - 1], index, _slowMa.Result[index], Color.Red, 2);
    }

    private void MarkDirectionIfChanged(int index)
    {
        var fast = _fastMa.Result[index];
        var slow = _slowMa.Result[index];

        if (double.IsNaN(fast) || double.IsNaN(slow) || fast == slow)
            return;

        var direction = fast > slow ? TradeType.Buy : TradeType.Sell;

        if (_lastDrawnDirection.HasValue && _lastDrawnDirection.Value == direction)
            return;

        DrawFlipMarker(index, direction);
        _lastDrawnDirection = direction;
    }

    private void DrawFlipMarker(int index, TradeType direction)
    {
        if (Chart == null)
            return;

        var name = string.Format("EmaLimitOrder_{0}_{1}", direction, Bars.OpenTimes[index].Ticks);
        var offset = Symbol.PipSize * 10;

        if (direction == TradeType.Buy)
            Chart.DrawIcon(name, ChartIconType.UpArrow, index, Bars.LowPrices[index] - offset, Color.LimeGreen);
        else
            Chart.DrawIcon(name, ChartIconType.DownArrow, index, Bars.HighPrices[index] + offset, Color.Red);
    }
}
