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
//    only re-paints on bar close.
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

        [Parameter("Fast EMA Periods", Group = "Moving Averages", DefaultValue = 2, MinValue = 1)]
        public int FastPeriods { get; set; }

        [Parameter("Mid EMA Periods", Group = "Moving Averages", DefaultValue = 4, MinValue = 1)]
        public int MidPeriods { get; set; }

        [Parameter("Slow EMA Periods", Group = "Moving Averages", DefaultValue = 20, MinValue = 1)]
        public int SlowPeriods { get; set; }

        [Parameter("Stop Loss (pips, 0 = off)", Group = "Risk", DefaultValue = 0, MinValue = 0)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (pips, 0 = off)", Group = "Risk", DefaultValue = 0, MinValue = 0)]
        public double TakeProfitPips { get; set; }

        private ExponentialMovingAverage _fastMa;
        private ExponentialMovingAverage _midMa;
        private ExponentialMovingAverage _slowMa;
        private const string Label = "EmaSlopeCrossStrategy";

        protected override void OnStart()
        {
            _fastMa = Indicators.ExponentialMovingAverage(SourceSeries, FastPeriods);
            _midMa = Indicators.ExponentialMovingAverage(SourceSeries, MidPeriods);
            _slowMa = Indicators.ExponentialMovingAverage(SourceSeries, SlowPeriods);
        }

        protected override void OnBar()
        {
            // Index 1 = the bar that just closed, index 2 = the bar before it.
            var price0 = Bars.ClosePrices.Last(1);
            var price1 = Bars.ClosePrices.Last(2);

            var fast0 = _fastMa.Result.Last(1);
            var fast1 = _fastMa.Result.Last(2);

            var mid0 = _midMa.Result.Last(1);
            var mid1 = _midMa.Result.Last(2);

            var slow0 = _slowMa.Result.Last(1);
            var slow1 = _slowMa.Result.Last(2);

            var crossUnderSlow = price1 >= slow1 && price0 < slow0;
            var crossOverSlow = price1 <= slow1 && price0 > slow0;
            var crossUnderFast = price1 >= fast1 && price0 < fast0;
            var crossOverFast = price1 <= fast1 && price0 > fast0;

            var priceChange = price0 - price1;
            var fastChange = fast0 - fast1;
            var midChange = mid0 - mid1;

            var longSignal = crossUnderSlow || (priceChange < 0 && fastChange < 0 && crossUnderFast && midChange > 0);
            var shortSignal = crossOverSlow || (priceChange > 0 && fastChange > 0 && crossOverFast && midChange < 0);

            var longPosition = Positions.Find(Label, SymbolName, TradeType.Buy);
            var shortPosition = Positions.Find(Label, SymbolName, TradeType.Sell);

            if (longSignal)
            {
                if (shortPosition != null)
                    ClosePosition(shortPosition);

                if (longPosition == null)
                    ExecuteMarketOrder(TradeType.Buy, SymbolName, VolumeInUnits, Label, StopLossPips == 0 ? (double?)null : StopLossPips, TakeProfitPips == 0 ? (double?)null : TakeProfitPips);
            }
            else if (shortSignal)
            {
                if (longPosition != null)
                    ClosePosition(longPosition);

                if (shortPosition == null)
                    ExecuteMarketOrder(TradeType.Sell, SymbolName, VolumeInUnits, Label, StopLossPips == 0 ? (double?)null : StopLossPips, TakeProfitPips == 0 ? (double?)null : TakeProfitPips);
            }
        }

        private double VolumeInUnits
        {
            get { return Symbol.QuantityToVolumeInUnits(Quantity); }
        }
    }
}
