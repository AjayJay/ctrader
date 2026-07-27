using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots;

internal sealed class EmaChartDrawer
{
    private const int MaxBarsToDraw = 500;
    private const string Ema211Prefix = "EMA211_";
    private const string Ema53Prefix = "EMA53_";
    private const string Ema27Prefix = "EMA27_";

    private readonly Bars _bars;
    private readonly Chart _chart;
    private readonly ExponentialMovingAverage _ema211;
    private readonly ExponentialMovingAverage _ema53;
    private readonly ExponentialMovingAverage _ema27;

    public EmaChartDrawer(Bars bars, Chart chart, IIndicatorsAccessor indicators)
    {
        _bars = bars;
        _chart = chart;
        _ema211 = indicators.ExponentialMovingAverage(bars.ClosePrices, 211);
        _ema53 = indicators.ExponentialMovingAverage(bars.ClosePrices, 53);
        _ema27 = indicators.ExponentialMovingAverage(bars.ClosePrices, 27);
    }

    public void DrawIfReady()
    {
        if (_bars.ClosePrices.Count >= 212)
            Draw();
    }

    private void Draw()
    {
        var startIndex = Math.Max(211, _bars.ClosePrices.Count - MaxBarsToDraw);
        var endIndex = _bars.ClosePrices.Count - 1;
        var activeLineNames = new HashSet<string>();

        for (var i = Math.Max(startIndex, 212); i <= endIndex; i++)
            DrawSegment(Ema211Prefix, i, _ema211, Color.Red, activeLineNames);

        for (var i = Math.Max(startIndex, 54); i <= endIndex; i++)
            DrawSegment(Ema53Prefix, i, _ema53, Color.Blue, activeLineNames);

        for (var i = Math.Max(startIndex, 28); i <= endIndex; i++)
            DrawSegment(Ema27Prefix, i, _ema27, Color.Yellow, activeLineNames);

        ClearStaleLines(activeLineNames);
    }

    private void DrawSegment(string namePrefix, int barIndex, ExponentialMovingAverage ema, Color color, HashSet<string> activeLineNames)
    {
        var previousValue = ema.Result[barIndex - 1];
        var currentValue = ema.Result[barIndex];

        if (double.IsNaN(previousValue) || double.IsNaN(currentValue))
            return;

        var name = $"{namePrefix}{_bars.OpenTimes[barIndex]:yyyyMMddHHmmss}";
        activeLineNames.Add(name);

        _chart.DrawTrendLine(
            name,
            _bars.OpenTimes[barIndex - 1],
            previousValue,
            _bars.OpenTimes[barIndex],
            currentValue,
            color,
            5,
            LineStyle.Solid);
    }

    private void ClearStaleLines(HashSet<string> activeLineNames)
    {
        var objectsToRemove = new List<string>();

        foreach (var obj in _chart.Objects)
            if (IsEmaLine(obj.Name) && !activeLineNames.Contains(obj.Name))
                objectsToRemove.Add(obj.Name);

        foreach (var name in objectsToRemove)
            _chart.RemoveObject(name);
    }

    private static bool IsEmaLine(string name)
    {
        return name.StartsWith(Ema211Prefix) || name.StartsWith(Ema53Prefix) || name.StartsWith(Ema27Prefix);
    }
}
