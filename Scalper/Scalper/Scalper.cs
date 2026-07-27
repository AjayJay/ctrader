using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots;

[Robot(AccessRights = AccessRights.None, AddIndicators = true)]
public class Scalper : Robot
{
    private const string BotLabel = "ScalperAutoBot";

    private TradeType? _selectedDirection;
    private bool _isRunning;

    private Button _buyButton;
    private Button _sellButton;
    private Button _startButton;
    private Button _stopButton;
    private TextBox _lotsInput;
    private TextBox _tpInput;
    private TextBox _slInput;
    private TextBox _diffPipsInput;
    private TextBox _waitMinutesInput;
    private TextBlock _statusText;

    private Style _buySelectedStyle;
    private Style _buyUnselectedStyle;
    private Style _sellSelectedStyle;
    private Style _sellUnselectedStyle;

    private EmaChartDrawer _emaChartDrawer;

    private int? _activeOrderId;

    protected override void OnStart()
    {
        BuildPanel();
        Positions.Closed += OnPositionClosed;
        PendingOrders.Filled += OnPendingOrderFilled;
        PendingOrders.Cancelled += OnPendingOrderCancelled;

        _emaChartDrawer = new EmaChartDrawer(Bars, Chart, Indicators);
        _emaChartDrawer.DrawIfReady();
    }

    protected override void OnTick()
    {
    }

    protected override void OnBar()
    {
        _emaChartDrawer.DrawIfReady();
    }

    protected override void OnStop()
    {
        Positions.Closed -= OnPositionClosed;
        PendingOrders.Filled -= OnPendingOrderFilled;
        PendingOrders.Cancelled -= OnPendingOrderCancelled;
    }

    private void BuildPanel()
    {
        _buyUnselectedStyle = new Style(DefaultStyles.ButtonStyle);
        _buyUnselectedStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#1B4D22"), ControlState.DarkTheme);
        _buyUnselectedStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#1B4D22"), ControlState.LightTheme);
        _buyUnselectedStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#CFEFD4"), ControlState.DarkTheme);
        _buyUnselectedStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#CFEFD4"), ControlState.LightTheme);

        _buySelectedStyle = new Style(DefaultStyles.ButtonStyle);
        _buySelectedStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#2E9E3F"), ControlState.DarkTheme);
        _buySelectedStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#2E9E3F"), ControlState.LightTheme);
        _buySelectedStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.DarkTheme);
        _buySelectedStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.LightTheme);

        _sellUnselectedStyle = new Style(DefaultStyles.ButtonStyle);
        _sellUnselectedStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#5C1A1A"), ControlState.DarkTheme);
        _sellUnselectedStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#5C1A1A"), ControlState.LightTheme);
        _sellUnselectedStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#F5CFCF"), ControlState.DarkTheme);
        _sellUnselectedStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#F5CFCF"), ControlState.LightTheme);

        _sellSelectedStyle = new Style(DefaultStyles.ButtonStyle);
        _sellSelectedStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#D32F2F"), ControlState.DarkTheme);
        _sellSelectedStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#D32F2F"), ControlState.LightTheme);
        _sellSelectedStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.DarkTheme);
        _sellSelectedStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.LightTheme);

        var panelBackgroundStyle = new Style();
        panelBackgroundStyle.Set(ControlProperty.CornerRadius, 3);
        panelBackgroundStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#292929"), ControlState.DarkTheme);
        panelBackgroundStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#FFFFFF"), ControlState.LightTheme);
        panelBackgroundStyle.Set(ControlProperty.BorderColor, Color.FromHex("#3C3C3C"), ControlState.DarkTheme);
        panelBackgroundStyle.Set(ControlProperty.BorderColor, Color.FromHex("#C3C3C3"), ControlState.LightTheme);
        panelBackgroundStyle.Set(ControlProperty.BorderThickness, new Thickness(1));

        var startButtonStyle = new Style(DefaultStyles.ButtonStyle);
        startButtonStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#1565C0"), ControlState.DarkTheme);
        startButtonStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#1565C0"), ControlState.LightTheme);
        startButtonStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.DarkTheme);
        startButtonStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.LightTheme);

        var stopButtonStyle = new Style(DefaultStyles.ButtonStyle);
        stopButtonStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#E65100"), ControlState.DarkTheme);
        stopButtonStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#E65100"), ControlState.LightTheme);
        stopButtonStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.DarkTheme);
        stopButtonStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.LightTheme);

        var mainPanel = new StackPanel();

        var headerBorder = new Border
        {
            BorderThickness = "0 0 0 1"
        };
        var header = new TextBlock
        {
            Text = "Scalper Auto-Trader",
            Margin = "10 7",
            FontWeight = FontWeight.Bold
        };
        headerBorder.Child = header;
        mainPanel.AddChild(headerBorder);

        var contentPanel = new StackPanel
        {
            Margin = 10
        };

        AddSectionLabel(contentPanel, "DIRECTION", topMargin: 0);

        var directionGrid = new Grid(1, 3);
        directionGrid.Columns[1].SetWidthInPixels(6);

        _buyButton = new Button { Text = "BUY", Style = _buyUnselectedStyle, Height = 30 };
        _sellButton = new Button { Text = "SELL", Style = _sellUnselectedStyle, Height = 30 };
        _buyButton.Click += args => SelectDirection(TradeType.Buy);
        _sellButton.Click += args => SelectDirection(TradeType.Sell);

        directionGrid.AddChild(_buyButton, 0, 0);
        directionGrid.AddChild(_sellButton, 0, 2);
        contentPanel.AddChild(directionGrid);

        AddSectionLabel(contentPanel, "SIZE & TARGETS");
        _lotsInput = AddLabeledInput(contentPanel, "Lots", "0.01", IsPositiveDouble);

        var targetsGrid = new Grid(1, 3);
        targetsGrid.Columns[1].SetWidthInPixels(6);
        _tpInput = AddLabeledInputToGrid(targetsGrid, 0, "Take Profit ($)", "10.00", IsPositiveDouble);
        _slInput = AddLabeledInputToGrid(targetsGrid, 2, "Stop Loss ($)", "10.00", IsPositiveDouble);
        contentPanel.AddChild(targetsGrid);

        AddSectionLabel(contentPanel, "ENTRY TIMING");
        var entryGrid = new Grid(1, 3);
        entryGrid.Columns[1].SetWidthInPixels(6);
        _diffPipsInput = AddLabeledInputToGrid(entryGrid, 0, "Diff Pips", "1000", IsPositiveDouble);
        _waitMinutesInput = AddLabeledInputToGrid(entryGrid, 2, "Wait (min)", "5", IsValidWaitMinutes);
        contentPanel.AddChild(entryGrid);

        var controlGrid = new Grid(1, 3);
        controlGrid.Columns[1].SetWidthInPixels(6);

        _startButton = new Button { Text = "START", Style = startButtonStyle, Height = 30, IsEnabled = false };
        _stopButton = new Button { Text = "STOP", Style = stopButtonStyle, Height = 30, IsEnabled = false };
        _startButton.Click += args => OnStartClicked();
        _stopButton.Click += args => OnStopClicked();

        controlGrid.AddChild(_startButton, 0, 0);
        controlGrid.AddChild(_stopButton, 0, 2);
        controlGrid.Margin = "0 14 0 0";
        contentPanel.AddChild(controlGrid);

        _statusText = new TextBlock
        {
            Text = "Idle. Select Buy or Sell.",
            Margin = "0 10 0 0",
            ForegroundColor = Color.FromHex("#AAAAAA")
        };
        contentPanel.AddChild(_statusText);

        mainPanel.AddChild(contentPanel);

        var border = new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = panelBackgroundStyle,
            Margin = "20 40 20 20",
            Width = 250,
            Child = mainPanel
        };

        Chart.AddControl(border);
    }

    private void AddSectionLabel(StackPanel parent, string text, int topMargin = 14)
    {
        var label = new TextBlock
        {
            Text = text,
            Margin = $"0 {topMargin} 0 4",
            FontSize = 10,
            ForegroundColor = Color.FromHex("#888888")
        };
        parent.AddChild(label);
    }

    private TextBox AddLabeledInput(StackPanel parent, string labelText, string defaultValue, Func<string, bool> isValid)
    {
        var stack = new StackPanel();
        var label = new TextBlock { Text = labelText, Margin = "0 0 0 2", FontSize = 11 };
        var input = CreateInput(defaultValue, isValid);

        stack.AddChild(label);
        stack.AddChild(input);
        parent.AddChild(stack);
        return input;
    }

    private TextBox AddLabeledInputToGrid(Grid grid, int column, string labelText, string defaultValue, Func<string, bool> isValid)
    {
        var stack = new StackPanel();
        var label = new TextBlock { Text = labelText, Margin = "0 6 0 2", FontSize = 11 };
        var input = CreateInput(defaultValue, isValid);

        stack.AddChild(label);
        stack.AddChild(input);
        grid.AddChild(stack, 0, column);
        return input;
    }

    private TextBox CreateInput(string defaultValue, Func<string, bool> isValid)
    {
        var input = new TextBox
        {
            Text = defaultValue,
            Height = 22,
            FontSize = 11,
            ForegroundColor = Color.Black
        };

        input.TextChanged += args => ApplyValidationColor(input, isValid(input.Text));
        ApplyValidationColor(input, isValid(defaultValue));

        return input;
    }

    private void ApplyValidationColor(TextBox input, bool isValid)
    {
        input.BackgroundColor = isValid
            ? Color.FromArgb(255, 240, 255, 240)
            : Color.FromArgb(255, 255, 240, 240);
    }

    private bool IsPositiveDouble(string text)
    {
        return double.TryParse(text, out var value) && value > 0;
    }

    private bool IsValidWaitMinutes(string text)
    {
        return int.TryParse(text, out var value) && value >= 1 && value <= 60;
    }

    private void SelectDirection(TradeType direction)
    {
        if (_isRunning)
            return;

        _selectedDirection = direction;
        _buyButton.Style = direction == TradeType.Buy ? _buySelectedStyle : _buyUnselectedStyle;
        _sellButton.Style = direction == TradeType.Sell ? _sellSelectedStyle : _sellUnselectedStyle;
        _startButton.IsEnabled = true;
        UpdateStatusText($"Direction selected: {direction}");
    }

    private void OnStartClicked()
    {
        if (_isRunning || _selectedDirection == null)
            return;

        if (!TryReadAndValidateInputs(out var lots, out var tpUsd, out var slUsd, out var diffPips, out var waitMinutes, out var error))
        {
            UpdateStatusText(error);
            return;
        }

        _isRunning = true;
        _buyButton.IsEnabled = false;
        _sellButton.IsEnabled = false;
        _startButton.IsEnabled = false;
        _stopButton.IsEnabled = true;

        if (!HasActiveBotOrder())
        {
            PlaceEntryOrder(_selectedDirection.Value, lots, tpUsd, slUsd, diffPips, waitMinutes);
        }
        else
        {
            _activeOrderId = PendingOrders.FirstOrDefault(o => o.SymbolName == SymbolName && o.Label == BotLabel)?.Id;
            UpdateStatusText("Resuming: existing position/order found, waiting.");
        }
    }

    private void OnStopClicked()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _buyButton.IsEnabled = true;
        _sellButton.IsEnabled = true;
        _startButton.IsEnabled = true;
        _stopButton.IsEnabled = false;
        UpdateStatusText("Stopped. Open position (if any) left running with existing TP/SL.");
    }

    private void PlaceEntryOrder(TradeType direction, double lots, double tpUsd, double slUsd, double diffPips, int waitMinutes)
    {
        var volumeInUnits = Symbol.QuantityToVolumeInUnits(lots);
        volumeInUnits = Math.Max(Symbol.VolumeInUnitsMin, Math.Min(Symbol.VolumeInUnitsMax, volumeInUnits));

        var tpPips = MoneyToPips(tpUsd, volumeInUnits);
        var slPips = MoneyToPips(slUsd, volumeInUnits);

        if (double.IsNaN(tpPips) || double.IsNaN(slPips))
        {
            UpdateStatusText("Invalid TP/SL conversion, aborting entry.");
            return;
        }

        var currentPrice = direction == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
        var targetPrice = GetLimitPriceForOrder(currentPrice, diffPips, direction);
        var expiration = Server.Time.AddMinutes(waitMinutes);

        var result = PlaceLimitOrder(direction, SymbolName, volumeInUnits, targetPrice, BotLabel,
            slPips, tpPips, ProtectionType.Relative, expiration);

        _activeOrderId = result.IsSuccessful ? result.PendingOrder?.Id : null;

        UpdateStatusText(result.IsSuccessful
            ? $"Limit order placed: {direction} {lots} lots @ {targetPrice:F5}, expires {expiration:HH:mm:ss}."
            : $"Limit order failed: {result.Error}");
    }

    private double GetLimitPriceForOrder(double currentPrice, double diffPips, TradeType direction)
    {
        var distance = diffPips * Symbol.PipSize;
        return direction == TradeType.Buy ? currentPrice - distance : currentPrice + distance;
    }

    private double MoneyToPips(double money, double volumeInUnits)
    {
        if (volumeInUnits <= 0 || Symbol.PipValue <= 0)
            return double.NaN;

        return money / (Symbol.PipValue * volumeInUnits);
    }

    private bool HasActiveBotOrder()
    {
        if (Positions.Any(p => p.SymbolName == SymbolName && p.Label == BotLabel))
            return true;

        if (PendingOrders.Any(o => o.SymbolName == SymbolName && o.Label == BotLabel))
            return true;

        return false;
    }

    private void OnPositionClosed(PositionClosedEventArgs args)
    {
        var position = args.Position;
        if (position.SymbolName != SymbolName || position.Label != BotLabel)
            return;

        if (!_isRunning || _selectedDirection == null || HasActiveBotOrder())
            return;

        if (!TryReadAndValidateInputs(out var lots, out var tpUsd, out var slUsd, out var diffPips, out var waitMinutes, out var error))
        {
            UpdateStatusText($"Re-entry skipped: {error}");
            OnStopClicked();
            return;
        }

        UpdateStatusText($"Position closed ({args.Reason}). Re-entering {_selectedDirection.Value}.");
        PlaceEntryOrder(_selectedDirection.Value, lots, tpUsd, slUsd, diffPips, waitMinutes);
    }

    private void OnPendingOrderFilled(PendingOrderFilledEventArgs args)
    {
        var position = args.Position;
        if (position.SymbolName != SymbolName || position.Label != BotLabel)
            return;

        _activeOrderId = null;
        UpdateStatusText($"Limit order filled: {position.TradeType} {position.VolumeInUnits} units @ {position.EntryPrice:F5}.");
    }

    private void OnPendingOrderCancelled(PendingOrderCancelledEventArgs args)
    {
        var order = args.PendingOrder;
        var isOurOrder = _activeOrderId.HasValue
            ? order.Id == _activeOrderId.Value
            : order.SymbolName == SymbolName && order.Label == BotLabel;

        if (!isOurOrder)
            return;

        Print($"Bot pending order {order.Id} cancelled. Reason: {args.Reason}");

        if (args.Reason == PendingOrderCancellationReason.Rejected)
        {
            _activeOrderId = null;
            return;
        }

        _activeOrderId = null;

        if (!_isRunning || _selectedDirection == null || HasActiveBotOrder())
            return;

        if (!TryReadAndValidateInputs(out var lots, out var tpUsd, out var slUsd, out var diffPips, out var waitMinutes, out var error))
        {
            UpdateStatusText($"Auto-replace skipped: {error}");
            OnStopClicked();
            return;
        }

        UpdateStatusText($"Limit order {args.Reason.ToString().ToLowerInvariant()}, unfilled. Replacing at recalculated price.");
        PlaceEntryOrder(_selectedDirection.Value, lots, tpUsd, slUsd, diffPips, waitMinutes);
    }

    private bool TryReadAndValidateInputs(out double lots, out double tpUsd, out double slUsd, out double diffPips, out int waitMinutes, out string error)
    {
        error = null;
        diffPips = 0;
        waitMinutes = 0;

        if (!double.TryParse(_lotsInput.Text, out lots) || lots <= 0)
        {
            error = "Invalid Lots.";
            tpUsd = slUsd = 0;
            return false;
        }

        if (!double.TryParse(_tpInput.Text, out tpUsd) || tpUsd <= 0)
        {
            error = "Take Profit ($) must be > 0.";
            slUsd = 0;
            return false;
        }

        if (!double.TryParse(_slInput.Text, out slUsd) || slUsd <= 0)
        {
            error = "Stop Loss ($) must be > 0.";
            return false;
        }

        if (!double.TryParse(_diffPipsInput.Text, out diffPips) || diffPips <= 0)
        {
            error = "Diff Pips must be > 0.";
            return false;
        }

        if (!int.TryParse(_waitMinutesInput.Text, out waitMinutes) || waitMinutes < 1 || waitMinutes > 60)
        {
            error = "Wait (min) must be an integer between 1 and 60.";
            return false;
        }

        return true;
    }

    private void UpdateStatusText(string text)
    {
        _statusText.Text = text;
        _statusText.ForegroundColor = ClassifyStatusColor(text);
        Print(text);
    }

    private Color ClassifyStatusColor(string text)
    {
        var lower = text.ToLowerInvariant();

        if (lower.Contains("failed") || lower.Contains("invalid") || lower.Contains("skipped") || lower.Contains("must be"))
            return Color.FromHex("#E57373");

        if (lower.Contains("placed") || lower.Contains("filled") || lower.Contains("opened"))
            return Color.FromHex("#81C784");

        return Color.FromHex("#AAAAAA");
    }
}
