// ----------------------------------------------------------------------------------------------------
//
//    EMA Slope Strategy
//
//    Trades the direction of a single, configurable EMA over a configurable lookback window:
//
//      EMA strictly rising over the last N bars  ->  Buy
//      EMA strictly falling over the last N bars ->  Sell
//      anything else (flat/mixed)                ->  no signal, hold current state
//
//    Only one position is ever open at a time. When the signal flips against an open position,
//    that position is closed immediately and a new one is opened on the new side (reversal).
//    Entries can be placed either at market or as a resting limit order pegged to the current EMA
//    price (repriced every bar, cancelled/flipped immediately if the signal changes while unfilled).
//
// ----------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots;

public enum EntryMode
{
    Market,
    EmaPrice
}

[Robot(AccessRights = AccessRights.None, AddIndicators = true)]
public class EmaSlopeStrategy : Robot
{
    [Parameter("Source", Group = "Moving Average")]
    public DataSeries SourceSeries { get; set; }

    [Parameter("EMA Periods", Group = "Moving Average", DefaultValue = 50, MinValue = 2, MaxValue = 500)]
    public int EmaPeriods { get; set; }

    [Parameter("Lookback Period (bars)", Group = "Moving Average", DefaultValue = 5, MinValue = 2, MaxValue = 100)]
    public int LookbackPeriod { get; set; }

    [Parameter("Entry Mode", Group = "Order Management", DefaultValue = EntryMode.Market)]
    public EntryMode OrderEntryMode { get; set; }

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

    [Parameter("Show EMA Line", Group = "Chart", DefaultValue = true)]
    public bool ShowEmaLine { get; set; }

    [Parameter("Mark Direction Flips", Group = "Chart", DefaultValue = true)]
    public bool MarkSignals { get; set; }

    private string Label => string.Format("EmaSlopeStrategy_{0}", TimeFrame);

    private ExponentialMovingAverage _ema;
    private TradeType? _lastDrawnDirection;

    protected override void OnStart()
    {
        _ema = Indicators.ExponentialMovingAverage(SourceSeries, EmaPeriods);

        Positions.Closed += OnPositionClosed;
        PendingOrders.Filled += OnPendingOrderFilled;
        PendingOrders.Cancelled += OnPendingOrderCancelled;

        if (ShowEmaLine || MarkSignals)
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

        if (ShowEmaLine)
            DrawEmaLine(index);

        if (index < WarmupIndex)
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

    private int WarmupIndex => EmaPeriods + LookbackPeriod;

    private int GetEvaluationIndex()
    {
        return EvaluateOnTick ? Bars.ClosePrices.Count - 1 : Bars.ClosePrices.Count - 2;
    }

    // Strictly-monotonic slope over the lookback window: every consecutive step must move the same
    // direction for a signal to fire. Mixed or flat sequences produce no signal.
    private TradeType? GetSignal(int index)
    {
        if (index - LookbackPeriod < 0)
            return null;

        var rising = true;
        var falling = true;

        for (var i = index - LookbackPeriod; i < index; i++)
        {
            var current = _ema.Result[i];
            var next = _ema.Result[i + 1];

            if (double.IsNaN(current) || double.IsNaN(next))
                return null;

            if (next <= current)
                rising = false;

            if (next >= current)
                falling = false;
        }

        if (rising)
            return TradeType.Buy;

        if (falling)
            return TradeType.Sell;

        return null;
    }

    // Re-derives the desired direction from current EMA state and reconciles positions/orders
    // against it. Safe to call from OnBar, OnTick, or any event handler.
    private void Evaluate(int index)
    {
        if (index < WarmupIndex)
            return;

        var signal = GetSignal(index);

        if (signal == null)
            return;

        var position = Positions.FirstOrDefault(p => p.SymbolName == SymbolName && p.Label == Label);

        if (position != null)
        {
            if (position.TradeType == signal.Value)
                return;

            ClosePosition(position);
            position = null;
        }

        if (OrderEntryMode == EntryMode.Market)
        {
            PlaceMarketEntry(signal.Value);
        }
        else
        {
            ManagePendingOrder(signal.Value, _ema.Result[index]);
        }
    }

    private void PlaceMarketEntry(TradeType direction)
    {
        double? stopLossPips = StopLossPips == 0 ? (double?)null : StopLossPips;
        double? takeProfitPips = TakeProfitPips == 0 ? (double?)null : TakeProfitPips;
        var volumeInUnits = GetVolumeInUnits(stopLossPips);

        ExecuteMarketOrder(direction, SymbolName, volumeInUnits, Label, stopLossPips, takeProfitPips);
    }

    private void ManagePendingOrder(TradeType direction, double targetPrice)
    {
        var existing = PendingOrders.FirstOrDefault(o => o.SymbolName == SymbolName && o.Label == Label);

        if (existing == null)
        {
            PlaceLimitEntry(direction, targetPrice);
            return;
        }

        if (existing.TradeType != direction)
        {
            existing.Cancel();
            return;
        }

        var toleranceDistance = ReplaceTolerancePips * Symbol.PipSize;
        var drift = Math.Abs(existing.TargetPrice - targetPrice);

        if (drift < toleranceDistance)
            return;

        existing.Cancel();
    }

    private void PlaceLimitEntry(TradeType direction, double targetPrice)
    {
        double? stopLossPips = StopLossPips == 0 ? (double?)null : StopLossPips;
        double? takeProfitPips = TakeProfitPips == 0 ? (double?)null : TakeProfitPips;
        var volumeInUnits = GetVolumeInUnits(stopLossPips);
        DateTime? expiration = ExpirationMinutes > 0 ? Server.Time.AddMinutes(ExpirationMinutes) : (DateTime?)null;

        PlaceLimitOrder(direction, SymbolName, volumeInUnits, targetPrice, Label,
            stopLossPips, takeProfitPips, ProtectionType.Relative, expiration);
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
        // No extra bookkeeping needed - the resulting position now matches the signal that placed
        // the order, and Evaluate() treats an aligned position as a no-op.
    }

    private void OnPendingOrderCancelled(PendingOrderCancelledEventArgs args)
    {
        var order = args.PendingOrder;

        if (order.SymbolName != SymbolName || order.Label != Label)
            return;

        if (args.Reason == PendingOrderCancellationReason.Rejected)
            return;

        Evaluate(GetEvaluationIndex());
    }

    private void OnPositionClosed(PositionClosedEventArgs args)
    {
        var position = args.Position;

        if (position.SymbolName != SymbolName || position.Label != Label)
            return;

        Evaluate(GetEvaluationIndex());
    }

    private void WarmUpChart()
    {
        var firstIndex = Math.Max(2, WarmupIndex);
        var lastIndex = Bars.ClosePrices.Count - 2;

        for (var index = firstIndex; index <= lastIndex; index++)
        {
            if (ShowEmaLine)
                DrawEmaLine(index);

            if (MarkSignals)
                MarkDirectionIfChanged(index);
        }
    }

    private void DrawEmaLine(int index)
    {
        if (Chart == null || index < 1)
            return;

        var name = string.Format("EmaSlope_Ema_{0}", Bars.OpenTimes[index].Ticks);

        Chart.DrawTrendLine(name, index - 1, _ema.Result[index - 1], index, _ema.Result[index], Color.Yellow, 2);
    }

    private void MarkDirectionIfChanged(int index)
    {
        var signal = GetSignal(index);

        if (signal == null || (_lastDrawnDirection.HasValue && _lastDrawnDirection.Value == signal.Value))
            return;

        DrawFlipMarker(index, signal.Value);
        _lastDrawnDirection = signal.Value;
    }

    private void DrawFlipMarker(int index, TradeType direction)
    {
        if (Chart == null)
            return;

        var name = string.Format("EmaSlope_{0}_{1}", direction, Bars.OpenTimes[index].Ticks);
        var offset = Symbol.PipSize * 10;

        if (direction == TradeType.Buy)
            Chart.DrawIcon(name, ChartIconType.UpArrow, index, Bars.LowPrices[index] - offset, Color.LimeGreen);
        else
            Chart.DrawIcon(name, ChartIconType.DownArrow, index, Bars.HighPrices[index] + offset, Color.Red);
    }
}
