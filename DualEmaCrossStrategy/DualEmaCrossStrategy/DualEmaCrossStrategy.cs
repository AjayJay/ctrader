// ----------------------------------------------------------------------------------------------------
//
//    Dual EMA Cross Strategy
//
//    Uses two EMAs (fast/slow). A signal fires when the fast EMA crosses the slow EMA. Every entry
//    signal closes the opposite position (if any) before opening the new one.
//
//    Long entry:  fast crosses over slow
//    Short entry: fast crosses under slow
//
//    Signals are evaluated once per closed bar (OnBar). Optional filters/risk features (all off by
//    default, so default behavior is unchanged):
//      - ADX regime filter: ignore crosses that happen while the market isn't trending.
//      - ATR-based stop loss / take profit: size stops off current volatility instead of fixed pips.
//      - Risk % position sizing: size volume off account risk instead of a fixed lot size.
//      - ATR trailing stop: tighten the stop loss as a position moves into profit.
//      - Minimum breakout filter: require price to clear the slow EMA by a fraction of ATR before
//        accepting a cross, filtering out barely-there crosses.
//
// ----------------------------------------------------------------------------------------------------

using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None, AddIndicators = true)]
    public class DualEmaCrossStrategy : Robot
    {
        [Parameter("Quantity (Lots)", Group = "Volume", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double Quantity { get; set; }

        [Parameter("Source", Group = "Moving Averages")]
        public DataSeries SourceSeries { get; set; }

        [Parameter("Fast EMA Periods (prime, 29-100)", Group = "Moving Averages", DefaultValue = 29, MinValue = 29, MaxValue = 100, Step = 1)]
        public int FastPeriods { get; set; }

        [Parameter("Slow EMA Periods (prime, 100-300)", Group = "Moving Averages", DefaultValue = 211, MinValue = 50, MaxValue = 300, Step = 1)]
        public int SlowPeriods { get; set; }

        [Parameter("Use ADX Filter", Group = "Regime Filter", DefaultValue = false)]
        public bool UseAdxFilter { get; set; }

        [Parameter("ADX Period", Group = "Regime Filter", DefaultValue = 14, MinValue = 2)]
        public int AdxPeriod { get; set; }

        [Parameter("ADX Threshold", Group = "Regime Filter", DefaultValue = 20, MinValue = 0)]
        public double AdxThreshold { get; set; }

        [Parameter("Minimum Breakout (x ATR, 0 = off)", Group = "Regime Filter", DefaultValue = 0, MinValue = 0)]
        public double MinBreakoutAtr { get; set; }

        [Parameter("Stop Loss (pips, 0 = off)", Group = "Risk", DefaultValue = 20, MinValue = 0)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (pips, 0 = off)", Group = "Risk", DefaultValue = 40, MinValue = 0)]
        public double TakeProfitPips { get; set; }

        [Parameter("Use ATR Stops", Group = "Risk", DefaultValue = false)]
        public bool UseAtrStops { get; set; }

        [Parameter("ATR Period", Group = "Risk", DefaultValue = 14, MinValue = 1)]
        public int AtrPeriod { get; set; }

        [Parameter("ATR Stop Loss Multiple", Group = "Risk", DefaultValue = 2.0, MinValue = 0.1)]
        public double AtrStopLossMultiple { get; set; }

        [Parameter("ATR Take Profit Multiple", Group = "Risk", DefaultValue = 3.0, MinValue = 0.1)]
        public double AtrTakeProfitMultiple { get; set; }

        [Parameter("Risk Percent Of Balance (0 = fixed lots)", Group = "Risk", DefaultValue = 0, MinValue = 0)]
        public double RiskPercent { get; set; }

        [Parameter("Use Trailing Stop", Group = "Exit", DefaultValue = false)]
        public bool UseTrailingStop { get; set; }

        [Parameter("Trailing Stop (x ATR)", Group = "Exit", DefaultValue = 1.5, MinValue = 0.1)]
        public double TrailingStopAtrMultiple { get; set; }

        [Parameter("Mark Signals On Chart", Group = "Chart", DefaultValue = true)]
        public bool MarkSignals { get; set; }

        [Parameter("Show Colored EMA Lines", Group = "Chart", DefaultValue = true)]
        public bool ShowEmaLines { get; set; }

        private ExponentialMovingAverage _fastMa;
        private ExponentialMovingAverage _slowMa;
        private DirectionalMovementSystem _adx;
        private AverageTrueRange _atr;
        private const string Label = "DualEmaCrossStrategy";

        private enum Signal
        {
            None,
            Long,
            Short
        }

        protected override void OnStart()
        {
            _fastMa = Indicators.ExponentialMovingAverage(SourceSeries, FastPeriods);
            _slowMa = Indicators.ExponentialMovingAverage(SourceSeries, SlowPeriods);
            _adx = Indicators.DirectionalMovementSystem(AdxPeriod);
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);

            Positions.Closed += OnPositionClosed;

            // Print("DualEmaCrossStrategy started on {0} {1}: fast={2}, slow={3}, quantity={4}, sl={5}, tp={6}",
            //     SymbolName, TimeFrame, FastPeriods, SlowPeriods, Quantity, StopLossPips, TakeProfitPips);

            if (MarkSignals)
                MarkHistoricalSignals();
        }

        private void MarkHistoricalSignals()
        {
            // Index Bars.ClosePrices.Count - 1 is the still-forming current bar, skip it - only
            // mark bars that were fully closed when this warm-up runs.
            var firstIndex = Math.Max(2, Math.Max(Math.Max(FastPeriods, SlowPeriods), Math.Max(AdxPeriod, AtrPeriod)));
            var lastIndex = Bars.ClosePrices.Count - 2;

            var marked = 0;

            for (var index = firstIndex; index <= lastIndex; index++)
            {
                if (ShowEmaLines)
                    DrawEmaLines(index);

                var signal = GetSignal(index);

                if (signal == Signal.None)
                    continue;

                DrawSignalMarker(index, signal);
                marked++;
            }

            // Print("Marked {0} past signal(s) on the chart.", marked);
        }

        private Signal GetSignal(int index)
        {
            var signal = GetCross(_fastMa, _slowMa, index);

            if (signal == Signal.None)
                return Signal.None;

            if (UseAdxFilter && _adx.ADX[index] < AdxThreshold)
                return Signal.None;

            if (MinBreakoutAtr > 0)
            {
                var breakoutDistance = Math.Abs(Bars.ClosePrices[index] - _slowMa.Result[index]);

                if (breakoutDistance < MinBreakoutAtr * _atr.Result[index])
                    return Signal.None;
            }

            return signal;
        }

        // Returns Long when `faster` crosses over `slower`, Short when it crosses under, None otherwise.
        private static Signal GetCross(ExponentialMovingAverage faster, ExponentialMovingAverage slower, int index)
        {
            var faster0 = faster.Result[index];
            var faster1 = faster.Result[index - 1];

            var slower0 = slower.Result[index];
            var slower1 = slower.Result[index - 1];

            if (faster1 <= slower1 && faster0 > slower0)
                return Signal.Long;

            if (faster1 >= slower1 && faster0 < slower0)
                return Signal.Short;

            return Signal.None;
        }

        // Colors the EMAs since the auto-plotted indicator lines don't expose per-instance colors
        // through this API - fast=yellow, slow=red.
        private void DrawEmaLines(int index)
        {
            if (Chart == null || index < 1)
                return;

            var fastName = string.Format("DualEmaCross_Fast_{0}", Bars.OpenTimes[index].Ticks);
            var slowName = string.Format("DualEmaCross_Slow_{0}", Bars.OpenTimes[index].Ticks);

            Chart.DrawTrendLine(fastName, index - 1, _fastMa.Result[index - 1], index, _fastMa.Result[index], Color.Yellow, 2);
            Chart.DrawTrendLine(slowName, index - 1, _slowMa.Result[index - 1], index, _slowMa.Result[index], Color.Red, 2);
        }

        private void DrawSignalMarker(int index, Signal signal)
        {
            if (Chart == null)
                return;

            var name = string.Format("DualEmaCross_{0}_{1}", signal, Bars.OpenTimes[index].Ticks);
            var offset = Symbol.PipSize * 10;

            if (signal == Signal.Long)
                Chart.DrawIcon(name, ChartIconType.UpArrow, index, Bars.LowPrices[index] - offset, Color.LimeGreen);
            else if (signal == Signal.Short)
                Chart.DrawIcon(name, ChartIconType.DownArrow, index, Bars.HighPrices[index] + offset, Color.Red);
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            var position = args.Position;

            if (position.Label != Label || position.SymbolName != SymbolName)
                return;

            // Print("Position closed [{0}]: {1} {2} units, entry={3}, netProfit={4} {5}, pips={6}, reason={7}",
            //     position.Id, position.TradeType, position.VolumeInUnits, position.EntryPrice,
            //     position.NetProfit, Account.Asset.Name, position.Pips, args.Reason);
        }

        protected override void OnBar()
        {
            // Index of the bar that just closed.
            var index = Bars.ClosePrices.Count - 2;
            var warmupIndex = Math.Max(Math.Max(FastPeriods, SlowPeriods), Math.Max(AdxPeriod, AtrPeriod));

            if (ShowEmaLines)
                DrawEmaLines(index);

            if (index < warmupIndex)
                return;

            var signal = GetSignal(index);

            if (signal != Signal.None)
                HandleSignal(index, signal);

            if (UseTrailingStop)
                ApplyTrailingStop(index);
        }

        private void HandleSignal(int index, Signal signal)
        {
            if (MarkSignals)
                DrawSignalMarker(index, signal);

            var longPosition = Positions.Find(Label, SymbolName, TradeType.Buy);
            var shortPosition = Positions.Find(Label, SymbolName, TradeType.Sell);

            var stopLossPips = GetStopLossPips(index);
            var takeProfitPips = GetTakeProfitPips(index);
            var volumeInUnits = GetVolumeInUnits(stopLossPips);

            if (signal == Signal.Long)
            {
                // Print("Long signal on {0}: price={1}, fast={2}, slow={3}",
                //     Bars.OpenTimes[index], Bars.ClosePrices[index], _fastMa.Result[index], _slowMa.Result[index]);

                if (shortPosition != null)
                {
                    var closeResult = ClosePosition(shortPosition);
                    // Print("Closed short position {0}: success={1}, error={2}", shortPosition.Id, closeResult.IsSuccessful, closeResult.Error);
                }

                if (longPosition == null)
                {
                    var openResult = ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeInUnits, Label, stopLossPips, takeProfitPips);
                    // Print("Opened long: success={0}, error={1}, positionId={2}, volume={3}",
                    //     openResult.IsSuccessful, openResult.Error, openResult.Position != null ? openResult.Position.Id.ToString() : "n/a", volumeInUnits);
                }
            }
            else if (signal == Signal.Short)
            {
                // Print("Short signal on {0}: price={1}, fast={2}, slow={3}",
                //     Bars.OpenTimes[index], Bars.ClosePrices[index], _fastMa.Result[index], _slowMa.Result[index]);

                if (longPosition != null)
                {
                    var closeResult = ClosePosition(longPosition);
                    // Print("Closed long position {0}: success={1}, error={2}", longPosition.Id, closeResult.IsSuccessful, closeResult.Error);
                }

                if (shortPosition == null)
                {
                    var openResult = ExecuteMarketOrder(TradeType.Sell, SymbolName, volumeInUnits, Label, stopLossPips, takeProfitPips);
                    // Print("Opened short: success={0}, error={1}, positionId={2}, volume={3}",
                    //     openResult.IsSuccessful, openResult.Error, openResult.Position != null ? openResult.Position.Id.ToString() : "n/a", volumeInUnits);
                }
            }
        }

        private double? GetStopLossPips(int index)
        {
            if (UseAtrStops)
                return _atr.Result[index] / Symbol.PipSize * AtrStopLossMultiple;

            return StopLossPips == 0 ? (double?)null : StopLossPips;
        }

        private double? GetTakeProfitPips(int index)
        {
            if (UseAtrStops)
                return _atr.Result[index] / Symbol.PipSize * AtrTakeProfitMultiple;

            return TakeProfitPips == 0 ? (double?)null : TakeProfitPips;
        }

        // Sizes volume off account risk when a stop-loss distance is known and RiskPercent is set,
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

            return VolumeInUnits;
        }

        // Tightens (never loosens) the stop loss of any open position opened by this bot to trail
        // behind price by a multiple of ATR, locking in profit as a trend continues.
        private void ApplyTrailingStop(int index)
        {
            var trailDistance = _atr.Result[index] * TrailingStopAtrMultiple;
            var closePrice = Bars.ClosePrices[index];

            var longPosition = Positions.Find(Label, SymbolName, TradeType.Buy);

            if (longPosition != null)
            {
                var candidateStop = closePrice - trailDistance;

                if (!longPosition.StopLoss.HasValue || candidateStop > longPosition.StopLoss.Value)
                    longPosition.ModifyStopLossPrice(candidateStop);
            }

            var shortPosition = Positions.Find(Label, SymbolName, TradeType.Sell);

            if (shortPosition != null)
            {
                var candidateStop = closePrice + trailDistance;

                if (!shortPosition.StopLoss.HasValue || candidateStop < shortPosition.StopLoss.Value)
                    shortPosition.ModifyStopLossPrice(candidateStop);
            }
        }

        private double VolumeInUnits
        {
            get { return Symbol.QuantityToVolumeInUnits(Quantity); }
        }
    }
}
