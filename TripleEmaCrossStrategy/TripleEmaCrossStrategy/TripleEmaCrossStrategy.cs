// ----------------------------------------------------------------------------------------------------
//
//    Triple EMA Cross Strategy
//
//    Uses three EMAs (fast/medium/slow). A signal fires whenever any faster EMA crosses a slower
//    one - fast/medium, fast/slow, or medium/slow. When "Confirm With Slow EMA" is on, the fast vs.
//    slow EMA relationship gates every signal: longs only fire while fast EMA > slow EMA, shorts
//    only fire while fast EMA < slow EMA (e.g. fast=27 > slow=211 required for any long). Every
//    entry signal closes the opposite position (if any) before opening the new one.
//
//    Long entry:  (fast crosses over medium, OR fast crosses over slow, OR medium crosses over slow)
//                 AND (fast EMA > slow EMA, if confirmation is on)
//    Short entry: (fast crosses under medium, OR fast crosses under slow, OR medium crosses under slow)
//                 AND (fast EMA < slow EMA, if confirmation is on)
//
//    Signals are evaluated once per closed bar (OnBar).
//
// ----------------------------------------------------------------------------------------------------

using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None, AddIndicators = true)]
    public class TripleEmaCrossStrategy : Robot
    {
        [Parameter("Quantity (Lots)", Group = "Volume", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double Quantity { get; set; }

        [Parameter("Source", Group = "Moving Averages")]
        public DataSeries SourceSeries { get; set; }

        [Parameter("Fast EMA Periods (prime, 29-100)", Group = "Moving Averages", DefaultValue = 29, MinValue = 29, MaxValue = 100, Step = 1)]
        public int FastPeriods { get; set; }

        [Parameter("Medium EMA Periods (prime, 50-150)", Group = "Moving Averages", DefaultValue = 53, MinValue = 50, MaxValue = 150, Step = 1)]
        public int MediumPeriods { get; set; }

        [Parameter("Slow EMA Periods (prime, 200-300)", Group = "Moving Averages", DefaultValue = 211, MinValue = 200, MaxValue = 300, Step = 1)]
        public int SlowPeriods { get; set; }

        [Parameter("Confirm With Slow EMA", Group = "Moving Averages", DefaultValue = true)]
        public bool ConfirmWithSlowEma { get; set; }

        [Parameter("Stop Loss (pips, 0 = off)", Group = "Risk", DefaultValue = 20, MinValue = 0)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (pips, 0 = off)", Group = "Risk", DefaultValue = 40, MinValue = 0)]
        public double TakeProfitPips { get; set; }

        [Parameter("Mark Signals On Chart", Group = "Chart", DefaultValue = true)]
        public bool MarkSignals { get; set; }

        [Parameter("Show Colored EMA Lines", Group = "Chart", DefaultValue = true)]
        public bool ShowEmaLines { get; set; }

        private ExponentialMovingAverage _fastMa;
        private ExponentialMovingAverage _mediumMa;
        private ExponentialMovingAverage _slowMa;
        private const string Label = "TripleEmaCrossStrategy";

        private enum Signal
        {
            None,
            Long,
            Short
        }

        protected override void OnStart()
        {
            _fastMa = Indicators.ExponentialMovingAverage(SourceSeries, FastPeriods);
            _mediumMa = Indicators.ExponentialMovingAverage(SourceSeries, MediumPeriods);
            _slowMa = Indicators.ExponentialMovingAverage(SourceSeries, SlowPeriods);

            Positions.Closed += OnPositionClosed;

            // Print("TripleEmaCrossStrategy started on {0} {1}: fast={2}, medium={3}, slow={4}, confirmWithSlowEma={5}, quantity={6}, sl={7}, tp={8}",
            //     SymbolName, TimeFrame, FastPeriods, MediumPeriods, SlowPeriods, ConfirmWithSlowEma, Quantity, StopLossPips, TakeProfitPips);

            if (MarkSignals)
                MarkHistoricalSignals();
        }

        private void MarkHistoricalSignals()
        {
            // Index Bars.ClosePrices.Count - 1 is the still-forming current bar, skip it - only
            // mark bars that were fully closed when this warm-up runs.
            var firstIndex = System.Math.Max(2, System.Math.Max(FastPeriods, System.Math.Max(MediumPeriods, SlowPeriods)));
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
            var signal = GetCross(_fastMa, _mediumMa, index);
            var source = "fast/medium";

            if (signal == Signal.None)
            {
                signal = GetCross(_fastMa, _slowMa, index);
                source = "fast/slow";
            }

            if (signal == Signal.None)
            {
                signal = GetCross(_mediumMa, _slowMa, index);
                source = "medium/slow";
            }

            if (signal == Signal.None)
                return Signal.None;

            // Print("Raw {0} cross on {1} ({2}): fast={3}, medium={4}, slow={5}",
            //     signal, Bars.OpenTimes[index], source, _fastMa.Result[index], _mediumMa.Result[index], _slowMa.Result[index]);

            if (!ConfirmWithSlowEma)
                return signal;

            var fastAboveSlow = _fastMa.Result[index] > _slowMa.Result[index];

            if (signal == Signal.Long && fastAboveSlow)
                return Signal.Long;

            if (signal == Signal.Short && !fastAboveSlow)
                return Signal.Short;

            // Print("Filtered out {0} signal from {1} cross: fast={2}, slow={3} does not confirm direction",
            //     signal, source, _fastMa.Result[index], _slowMa.Result[index]);

            return Signal.None;
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
        // through this API - fast=yellow, medium=blue, slow=red.
        private void DrawEmaLines(int index)
        {
            if (Chart == null || index < 1)
                return;

            var fastName = string.Format("TripleEmaCross_Fast_{0}", Bars.OpenTimes[index].Ticks);
            var mediumName = string.Format("TripleEmaCross_Medium_{0}", Bars.OpenTimes[index].Ticks);
            var slowName = string.Format("TripleEmaCross_Slow_{0}", Bars.OpenTimes[index].Ticks);

            Chart.DrawTrendLine(fastName, index - 1, _fastMa.Result[index - 1], index, _fastMa.Result[index], Color.Yellow, 2);
            Chart.DrawTrendLine(mediumName, index - 1, _mediumMa.Result[index - 1], index, _mediumMa.Result[index], Color.Blue, 2);
            Chart.DrawTrendLine(slowName, index - 1, _slowMa.Result[index - 1], index, _slowMa.Result[index], Color.Red, 2);
        }

        private void DrawSignalMarker(int index, Signal signal)
        {
            if (Chart == null)
                return;

            var name = string.Format("TripleEmaCross_{0}_{1}", signal, Bars.OpenTimes[index].Ticks);
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

            if (ShowEmaLines)
                DrawEmaLines(index);

            var signal = GetSignal(index);

            if (signal == Signal.None)
                return;

            if (MarkSignals)
                DrawSignalMarker(index, signal);

            var longPosition = Positions.Find(Label, SymbolName, TradeType.Buy);
            var shortPosition = Positions.Find(Label, SymbolName, TradeType.Sell);

            if (signal == Signal.Long)
            {
                // Print("Long signal on {0}: price={1}, fast={2}, medium={3}, slow={4}",
                //     Bars.OpenTimes[index], Bars.ClosePrices[index], _fastMa.Result[index], _mediumMa.Result[index], _slowMa.Result[index]);

                if (shortPosition != null)
                {
                    var closeResult = ClosePosition(shortPosition);
                    // Print("Closed short position {0}: success={1}, error={2}", shortPosition.Id, closeResult.IsSuccessful, closeResult.Error);
                }

                if (longPosition == null)
                {
                    var openResult = ExecuteMarketOrder(TradeType.Buy, SymbolName, VolumeInUnits, Label, StopLossPips == 0 ? (double?)null : StopLossPips, TakeProfitPips == 0 ? (double?)null : TakeProfitPips);
                    // Print("Opened long: success={0}, error={1}, positionId={2}, volume={3}",
                    //     openResult.IsSuccessful, openResult.Error, openResult.Position != null ? openResult.Position.Id.ToString() : "n/a", VolumeInUnits);
                }
            }
            else if (signal == Signal.Short)
            {
                // Print("Short signal on {0}: price={1}, fast={2}, medium={3}, slow={4}",
                //     Bars.OpenTimes[index], Bars.ClosePrices[index], _fastMa.Result[index], _mediumMa.Result[index], _slowMa.Result[index]);

                if (longPosition != null)
                {
                    var closeResult = ClosePosition(longPosition);
                    // Print("Closed long position {0}: success={1}, error={2}", longPosition.Id, closeResult.IsSuccessful, closeResult.Error);
                }

                if (shortPosition == null)
                {
                    var openResult = ExecuteMarketOrder(TradeType.Sell, SymbolName, VolumeInUnits, Label, StopLossPips == 0 ? (double?)null : StopLossPips, TakeProfitPips == 0 ? (double?)null : TakeProfitPips);
                    // Print("Opened short: success={0}, error={1}, positionId={2}, volume={3}",
                    //     openResult.IsSuccessful, openResult.Error, openResult.Position != null ? openResult.Position.Id.ToString() : "n/a", VolumeInUnits);
                }
            }
        }

        private double VolumeInUnits
        {
            get { return Symbol.QuantityToVolumeInUnits(Quantity); }
        }
    }
}
