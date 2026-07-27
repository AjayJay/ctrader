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
    private TextBlock _statusText;

    private Style _selectedButtonStyle;
    private Style _unselectedButtonStyle;

    protected override void OnStart()
    {
        BuildPanel();
        Positions.Closed += OnPositionClosed;
    }

    protected override void OnTick()
    {
    }

    protected override void OnStop()
    {
        Positions.Closed -= OnPositionClosed;
    }

    private void BuildPanel()
    {
        _unselectedButtonStyle = new Style(DefaultStyles.ButtonStyle);
        _unselectedButtonStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#333333"), ControlState.DarkTheme);
        _unselectedButtonStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#333333"), ControlState.LightTheme);
        _unselectedButtonStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.DarkTheme);
        _unselectedButtonStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.LightTheme);

        _selectedButtonStyle = new Style(DefaultStyles.ButtonStyle);
        _selectedButtonStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#2E7D32"), ControlState.DarkTheme);
        _selectedButtonStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#2E7D32"), ControlState.LightTheme);
        _selectedButtonStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.DarkTheme);
        _selectedButtonStyle.Set(ControlProperty.ForegroundColor, Color.FromHex("#FFFFFF"), ControlState.LightTheme);

        var panelBackgroundStyle = new Style();
        panelBackgroundStyle.Set(ControlProperty.CornerRadius, 3);
        panelBackgroundStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#292929"), ControlState.DarkTheme);
        panelBackgroundStyle.Set(ControlProperty.BackgroundColor, Color.FromHex("#FFFFFF"), ControlState.LightTheme);
        panelBackgroundStyle.Set(ControlProperty.BorderColor, Color.FromHex("#3C3C3C"), ControlState.DarkTheme);
        panelBackgroundStyle.Set(ControlProperty.BorderColor, Color.FromHex("#C3C3C3"), ControlState.LightTheme);
        panelBackgroundStyle.Set(ControlProperty.BorderThickness, new Thickness(1));

        var mainPanel = new StackPanel();

        var headerBorder = new Border
        {
            BorderThickness = "0 0 0 1"
        };
        var header = new TextBlock
        {
            Text = "Scalper Auto-Trader",
            Margin = "10 7"
        };
        headerBorder.Child = header;
        mainPanel.AddChild(headerBorder);

        var contentPanel = new StackPanel
        {
            Margin = 10
        };

        var directionGrid = new Grid(1, 3);
        directionGrid.Columns[1].SetWidthInPixels(5);

        _buyButton = new Button { Text = "BUY", Style = _unselectedButtonStyle, Height = 28 };
        _sellButton = new Button { Text = "SELL", Style = _unselectedButtonStyle, Height = 28 };
        _buyButton.Click += args => SelectDirection(TradeType.Buy);
        _sellButton.Click += args => SelectDirection(TradeType.Sell);

        directionGrid.AddChild(_buyButton, 0, 0);
        directionGrid.AddChild(_sellButton, 0, 2);
        contentPanel.AddChild(directionGrid);

        _lotsInput = AddLabeledInput(contentPanel, "Lots", "0.01");
        _tpInput = AddLabeledInput(contentPanel, "Take Profit ($)", "10.00");
        _slInput = AddLabeledInput(contentPanel, "Stop Loss ($)", "10.00");

        var controlGrid = new Grid(1, 3);
        controlGrid.Columns[1].SetWidthInPixels(5);

        _startButton = new Button { Text = "START", Height = 28, IsEnabled = false };
        _stopButton = new Button { Text = "STOP", Height = 28, IsEnabled = false };
        _startButton.Click += args => OnStartClicked();
        _stopButton.Click += args => OnStopClicked();

        controlGrid.AddChild(_startButton, 0, 0);
        controlGrid.AddChild(_stopButton, 0, 2);
        controlGrid.Margin = "0 10 0 0";
        contentPanel.AddChild(controlGrid);

        _statusText = new TextBlock { Text = "Idle. Select Buy or Sell.", Margin = "0 10 0 0" };
        contentPanel.AddChild(_statusText);

        mainPanel.AddChild(contentPanel);

        var border = new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Style = panelBackgroundStyle,
            Margin = "20 40 20 20",
            Width = 225,
            Child = mainPanel
        };

        Chart.AddControl(border);
    }

    private TextBox AddLabeledInput(StackPanel parent, string labelText, string defaultValue)
    {
        var label = new TextBlock { Text = labelText, Margin = "0 8 0 2" };
        var input = new TextBox { Text = defaultValue, Height = 24 };
        parent.AddChild(label);
        parent.AddChild(input);
        return input;
    }

    private void SelectDirection(TradeType direction)
    {
        if (_isRunning)
            return;

        _selectedDirection = direction;
        _buyButton.Style = direction == TradeType.Buy ? _selectedButtonStyle : _unselectedButtonStyle;
        _sellButton.Style = direction == TradeType.Sell ? _selectedButtonStyle : _unselectedButtonStyle;
        _startButton.IsEnabled = true;
        UpdateStatusText($"Direction selected: {direction}");
    }

    private void OnStartClicked()
    {
        if (_isRunning || _selectedDirection == null)
            return;

        if (!TryReadAndValidateInputs(out var lots, out var tpUsd, out var slUsd, out var error))
        {
            UpdateStatusText(error);
            return;
        }

        _isRunning = true;
        _buyButton.IsEnabled = false;
        _sellButton.IsEnabled = false;
        _startButton.IsEnabled = false;
        _stopButton.IsEnabled = true;

        if (!HasOpenBotPosition())
            PlaceEntryOrder(_selectedDirection.Value, lots, tpUsd, slUsd);
        else
            UpdateStatusText("Resuming: existing position found, waiting for it to close.");
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

    private void PlaceEntryOrder(TradeType direction, double lots, double tpUsd, double slUsd)
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

        var result = ExecuteMarketOrder(direction, SymbolName, volumeInUnits, BotLabel, slPips, tpPips);

        UpdateStatusText(result.IsSuccessful
            ? $"Position opened: {direction} {lots} lots."
            : $"Order failed: {result.Error}");
    }

    private double MoneyToPips(double money, double volumeInUnits)
    {
        if (volumeInUnits <= 0 || Symbol.PipValue <= 0)
            return double.NaN;

        return money / (Symbol.PipValue * volumeInUnits);
    }

    private bool HasOpenBotPosition()
    {
        return Positions.Any(p => p.SymbolName == SymbolName && p.Label == BotLabel);
    }

    private void OnPositionClosed(PositionClosedEventArgs args)
    {
        var position = args.Position;
        if (position.SymbolName != SymbolName || position.Label != BotLabel)
            return;

        if (!_isRunning || _selectedDirection == null || HasOpenBotPosition())
            return;

        if (!TryReadAndValidateInputs(out var lots, out var tpUsd, out var slUsd, out var error))
        {
            UpdateStatusText($"Re-entry skipped: {error}");
            OnStopClicked();
            return;
        }

        UpdateStatusText($"Position closed ({args.Reason}). Re-entering {_selectedDirection.Value}.");
        PlaceEntryOrder(_selectedDirection.Value, lots, tpUsd, slUsd);
    }

    private bool TryReadAndValidateInputs(out double lots, out double tpUsd, out double slUsd, out string error)
    {
        error = null;

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

        return true;
    }

    private void UpdateStatusText(string text)
    {
        _statusText.Text = text;
        Print(text);
    }
}
