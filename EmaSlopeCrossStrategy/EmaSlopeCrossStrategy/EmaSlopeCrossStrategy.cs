// ----------------------------------------------------------------------------------------------------
//
//    EMA Slope + EMA Cross Strategy (port of ChartArt's TradingView strategy)
//    https://www.tradingview.com/script/XLGBqrZq-EMA-Slope-EMA-Cross-Strategy-by-ChartArt/
//
//    Uses three EMAs (fast/mid/slow) and the slope (bar-to-bar change) of price and the fast/mid
//    EMAs to switch between a long and a short position. The strategy is always in the market -
//    every entry signal closes the opposite position (if any) before opening the new one.
//
//    Long entry:
//      - price crosses under the slow EMA, OR
//      - price fell, the fast EMA fell, price crossed under the fast EMA, and the mid EMA rose
//
//    Short entry:
//      - price crosses over the slow EMA, OR
//      - price rose, the fast EMA rose, price crossed over the fast EMA, and the mid EMA fell
//
//    Signals are evaluated once per closed bar (OnBar), matching the original Pine Script, which
//    only re-paints on bar close. On start, the same logic is replayed over history so past signals
//    are marked on the chart too.
//
// ----------------------------------------------------------------------------------------------------

using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None, AddIndicators = true)]
    public class EmaSlopeCrossStrategy : Robot
    {
        [Parameter("Quantity (Lots)", Group = "Volume", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double Quantity { get; set; }

        [Parameter("Source", Group = "Moving Averages")]
        public DataSeries SourceSeries { get; set; }

        [Parameter("Fast EMA Periods", Group = "Moving Averages", DefaultValue = 2, MinValue = 1, MaxValue = 20, Step = 1)]
        public int FastPeriods { get; set; }

        [Parameter("Mid EMA Periods", Group = "Moving Averages", DefaultValue = 4, MinValue = 2, MaxValue = 50, Step = 1)]
        public int MidPeriods { get; set; }

        [Parameter("Slow EMA Periods", Group = "Moving Averages", DefaultValue = 20, MinValue = 5, MaxValue = 200, Step = 5)]
        public int SlowPeriods { get; set; }

        [Parameter("Stop Loss (pips, 0 = off)", Group = "Risk", DefaultValue = 0, MinValue = 0)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (pips, 0 = off)", Group = "Risk", DefaultValue = 0, MinValue = 0)]
        public double TakeProfitPips { get; set; }

        [Parameter("Mark Past Signals", Group = "Chart", DefaultValue = true)]
        public bool MarkPastSignals { get; set; }

        [Parameter("Show Bar Trend Color", Group = "Chart", DefaultValue = true)]
        public bool ShowBarColors { get; set; }

        [Parameter("Show Colored EMA Lines", Group = "Chart", DefaultValue = true)]
        public bool ShowMovingAverages { get; set; }

        private ExponentialMovingAverage _fastMa;
        private ExponentialMovingAverage _midMa;
        private ExponentialMovingAverage _slowMa;
        private const string Label = "EmaSlopeCrossStrategy";

        private enum Signal
        {
            None,
            Long,
            Short
        }

        protected override void OnStart()
        {
            _fastMa = Indicators.ExponentialMovingAverage(SourceSeries, FastPeriods);
            _midMa = Indicators.ExponentialMovingAverage(SourceSeries, MidPeriods);
            _slowMa = Indicators.ExponentialMovingAverage(SourceSeries, SlowPeriods);

            Positions.Closed += OnPositionClosed;

            if (MarkPastSignals)
                MarkHistoricalSignals();
        }

        private void MarkHistoricalSignals()
        {
            // Index Bars.ClosePrices.Count - 1 is the still-forming current bar, skip it - only
            // mark bars that were fully closed when this warm-up runs.
            var firstIndex = System.Math.Max(2, System.Math.Max(FastPeriods, System.Math.Max(MidPeriods, SlowPeriods)));
            var lastIndex = Bars.ClosePrices.Count - 2;

            var marked = 0;

            for (var index = firstIndex; index <= lastIndex; index++)
            {
                DrawTrendVisuals(index);

                var signal = GetSignal(index);

                if (signal == Signal.None)
                    continue;

                DrawSignalMarker(index, signal);
                marked++;
            }

            Print("Marked {0} past signal(s) on the chart.", marked);
        }

        private Signal GetSignal(int index)
        {
            var price0 = Bars.ClosePrices[index];
            var price1 = Bars.ClosePrices[index - 1];

            var fast0 = _fastMa.Result[index];
            var fast1 = _fastMa.Result[index - 1];

            var mid0 = _midMa.Result[index];
            var mid1 = _midMa.Result[index - 1];

            var slow0 = _slowMa.Result[index];
            var slow1 = _slowMa.Result[index - 1];

            var crossUnderSlow = price1 >= slow1 && price0 < slow0;
            var crossOverSlow = price1 <= slow1 && price0 > slow0;
            var crossUnderFast = price1 >= fast1 && price0 < fast0;
            var crossOverFast = price1 <= fast1 && price0 > fast0;

            var priceChange = price0 - price1;
            var fastChange = fast0 - fast1;
            var midChange = mid0 - mid1;

            var longSignal = crossUnderSlow || (priceChange < 0 && fastChange < 0 && crossUnderFast && midChange > 0);
            var shortSignal = crossOverSlow || (priceChange > 0 && fastChange > 0 && crossOverFast && midChange < 0);

            if (longSignal)
                return Signal.Long;

            if (shortSignal)
                return Signal.Short;

            return Signal.None;
        }

        private void DrawSignalMarker(int index, Signal signal)
        {
            if (Chart == null)
                return;

            var name = string.Format("EmaSlopeCross_{0}_{1}", signal, Bars.OpenTimes[index].Ticks);
            var offset = Symbol.PipSize * 10;

            if (signal == Signal.Long)
                Chart.DrawIcon(name, ChartIconType.UpArrow, index, Bars.LowPrices[index] - offset, Color.LimeGreen);
            else if (signal == Signal.Short)
                Chart.DrawIcon(name, ChartIconType.DownArrow, index, Bars.HighPrices[index] + offset, Color.Red);
        }

        // Mirrors the original Pine Script's barcolor() and colored EMA plots: green/lime when both
        // the mid and slow EMA are rising, red when both are falling, blue otherwise. cBots can't
        // recolor the actual candlesticks (that's indicator-only in cAlgo), so the bar trend is shown
        // as a small colored square below each bar instead.
        private void DrawTrendVisuals(int index)
        {
            if (Chart == null || index < 1)
                return;

            var midChange = _midMa.Result[index] - _midMa.Result[index - 1];
            var slowChange = _slowMa.Result[index] - _slowMa.Result[index - 1];

            if (ShowBarColors)
            {
                var up = midChange > 0 && slowChange > 0;
                var down = midChange < 0 && slowChange < 0;
                var barColor = up ? Color.Green : down ? Color.Red : Color.Blue;

                var barColorName = string.Format("EmaSlopeCross_BarColor_{0}", Bars.OpenTimes[index].Ticks);
                Chart.DrawIcon(barColorName, ChartIconType.Square, index, Bars.LowPrices[index] - Symbol.PipSize * 20, barColor);
            }

            if (ShowMovingAverages)
            {
                var midColor = midChange > 0 ? Color.Lime : midChange < 0 ? Color.Red : Color.Blue;
                var slowColor = slowChange > 0 ? Color.Lime : slowChange < 0 ? Color.Red : Color.Blue;

                var midLineName = string.Format("EmaSlopeCross_MA2_{0}", Bars.OpenTimes[index].Ticks);
                var slowLineName = string.Format("EmaSlopeCross_MA3_{0}", Bars.OpenTimes[index].Ticks);

                Chart.DrawTrendLine(midLineName, index - 1, _midMa.Result[index - 1], index, _midMa.Result[index], midColor, 2);
                Chart.DrawTrendLine(slowLineName, index - 1, _slowMa.Result[index - 1], index, _slowMa.Result[index], slowColor, 3);
            }
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            var position = args.Position;

            if (position.Label != Label || position.SymbolName != SymbolName)
                return;

            Print("Position closed [{0}]: {1} {2} units, entry={3}, netProfit={4} {5}, pips={6}, reason={7}",
                position.Id, position.TradeType, position.VolumeInUnits, position.EntryPrice,
                position.NetProfit, Account.Asset.Name, position.Pips, args.Reason);
        }

        protected override void OnBar()
        {
            // Index of the bar that just closed.
            var index = Bars.ClosePrices.Count - 2;

            DrawTrendVisuals(index);

            var signal = GetSignal(index);

            if (signal == Signal.None)
                return;

            DrawSignalMarker(index, signal);

            var longPosition = Positions.Find(Label, SymbolName, TradeType.Buy);
            var shortPosition = Positions.Find(Label, SymbolName, TradeType.Sell);

            if (signal == Signal.Long)
            {
                Print("Long signal on {0}: price={1}, fast={2}, mid={3}, slow={4}",
                    Bars.OpenTimes[index], Bars.ClosePrices[index], _fastMa.Result[index], _midMa.Result[index], _slowMa.Result[index]);

                if (shortPosition != null)
                {
                    var closeResult = ClosePosition(shortPosition);
                    Print("Closed short position {0}: success={1}, error={2}", shortPosition.Id, closeResult.IsSuccessful, closeResult.Error);
                }

                if (longPosition == null)
                {
                    var openResult = ExecuteMarketOrder(TradeType.Buy, SymbolName, VolumeInUnits, Label, StopLossPips == 0 ? (double?)null : StopLossPips, TakeProfitPips == 0 ? (double?)null : TakeProfitPips);
                    Print("Opened long: success={0}, error={1}", openResult.IsSuccessful, openResult.Error);
                }
            }
            else if (signal == Signal.Short)
            {
                Print("Short signal on {0}: price={1}, fast={2}, mid={3}, slow={4}",
                    Bars.OpenTimes[index], Bars.ClosePrices[index], _fastMa.Result[index], _midMa.Result[index], _slowMa.Result[index]);

                if (longPosition != null)
                {
                    var closeResult = ClosePosition(longPosition);
                    Print("Closed long position {0}: success={1}, error={2}", longPosition.Id, closeResult.IsSuccessful, closeResult.Error);
                }

                if (shortPosition == null)
                {
                    var openResult = ExecuteMarketOrder(TradeType.Sell, SymbolName, VolumeInUnits, Label, StopLossPips == 0 ? (double?)null : StopLossPips, TakeProfitPips == 0 ? (double?)null : TakeProfitPips);
                    Print("Opened short: success={0}, error={1}", openResult.IsSuccessful, openResult.Error);
                }
            }
        }

        private double VolumeInUnits
        {
            get { return Symbol.QuantityToVolumeInUnits(Quantity); }
        }
    }
}
