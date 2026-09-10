// ----------------------------------------------------------------------------------------------------
//
//    Price EMA Cross Strategy
//
//    Uses a single EMA. A signal fires when the closed bar's close price crosses the EMA.
//    Every entry signal closes the opposite position (if any) before opening the new one.
//
//    Long entry:  close price crosses over the EMA
//    Short entry: close price crosses under the EMA
//
//    Signals are evaluated once per closed bar (OnBar).
//
// ----------------------------------------------------------------------------------------------------

using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None, AddIndicators = true)]
    public class PriceEmaCrossStrategy : Robot
    {
        [Parameter("Quantity (Lots)", Group = "Volume", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double Quantity { get; set; }

        [Parameter("Source", Group = "Moving Average")]
        public DataSeries SourceSeries { get; set; }

        [Parameter("EMA Periods", Group = "Moving Average", DefaultValue = 50, MinValue = 1, MaxValue = 400, Step = 1)]
        public int EmaPeriods { get; set; }

        [Parameter("Stop Loss (pips, 0 = off)", Group = "Risk", DefaultValue = 20, MinValue = 0)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (pips, 0 = off)", Group = "Risk", DefaultValue = 40, MinValue = 0)]
        public double TakeProfitPips { get; set; }

        [Parameter("Mark Signals On Chart", Group = "Chart", DefaultValue = true)]
        public bool MarkSignals { get; set; }

        [Parameter("Show EMA Line", Group = "Chart", DefaultValue = true)]
        public bool ShowEmaLine { get; set; }

        private ExponentialMovingAverage _ema;
        private const string Label = "PriceEmaCrossStrategy";

        private enum Signal
        {
            None,
            Long,
            Short
        }

        protected override void OnStart()
        {
            _ema = Indicators.ExponentialMovingAverage(SourceSeries, EmaPeriods);

            Positions.Closed += OnPositionClosed;

            // Print("PriceEmaCrossStrategy started on {0} {1}: emaPeriods={2}, quantity={3}, sl={4}, tp={5}",
            //     SymbolName, TimeFrame, EmaPeriods, Quantity, StopLossPips, TakeProfitPips);

            if (MarkSignals)
                MarkHistoricalSignals();
        }

        private void MarkHistoricalSignals()
        {
            // Index Bars.ClosePrices.Count - 1 is the still-forming current bar, skip it - only
            // mark bars that were fully closed when this warm-up runs.
            var firstIndex = System.Math.Max(2, EmaPeriods);
            var lastIndex = Bars.ClosePrices.Count - 2;

            var marked = 0;

            for (var index = firstIndex; index <= lastIndex; index++)
            {
                if (ShowEmaLine)
                    DrawEmaLine(index);

                var signal = GetSignal(index);

                if (signal == Signal.None)
                    continue;

                DrawSignalMarker(index, signal);
                marked++;
            }

            // Print("Marked {0} past signal(s) on the chart.", marked);
        }

        // Returns Long when the close price crosses over the EMA, Short when it crosses under, None otherwise.
        private Signal GetSignal(int index)
        {
            var close0 = Bars.ClosePrices[index];
            var close1 = Bars.ClosePrices[index - 1];

            var ema0 = _ema.Result[index];
            var ema1 = _ema.Result[index - 1];

            if (close1 <= ema1 && close0 > ema0)
                return Signal.Long;

            if (close1 >= ema1 && close0 < ema0)
                return Signal.Short;

            return Signal.None;
        }

        private void DrawEmaLine(int index)
        {
            if (Chart == null || index < 1)
                return;

            var name = string.Format("PriceEmaCross_Ema_{0}", Bars.OpenTimes[index].Ticks);

            Chart.DrawTrendLine(name, index - 1, _ema.Result[index - 1], index, _ema.Result[index], Color.Yellow, 2);
        }

        private void DrawSignalMarker(int index, Signal signal)
        {
            if (Chart == null)
                return;

            var name = string.Format("PriceEmaCross_{0}_{1}", signal, Bars.OpenTimes[index].Ticks);
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

            if (ShowEmaLine)
                DrawEmaLine(index);

            var signal = GetSignal(index);

            if (signal == Signal.None)
                return;

            if (MarkSignals)
                DrawSignalMarker(index, signal);

            var longPosition = Positions.Find(Label, SymbolName, TradeType.Buy);
            var shortPosition = Positions.Find(Label, SymbolName, TradeType.Sell);

            if (signal == Signal.Long)
            {
                // Print("Long signal on {0}: price={1}, ema={2}",
                //     Bars.OpenTimes[index], Bars.ClosePrices[index], _ema.Result[index]);

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
                // Print("Short signal on {0}: price={1}, ema={2}",
                //     Bars.OpenTimes[index], Bars.ClosePrices[index], _ema.Result[index]);

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
