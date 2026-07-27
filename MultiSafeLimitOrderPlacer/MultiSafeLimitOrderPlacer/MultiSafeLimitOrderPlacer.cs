using cAlgo.API;
using System;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class LadderOrderBotUSD : Robot
    {
        private enum InputType
        {
            DecimalPositive,
            IntegerPositive
        }

        private Button buyButton;
        private Button sellButton;

        private ComboBox volumeBox;
        private TextBox slUsdBox;
        private TextBox tpUsdBox;
        private TextBox ordersBox;
        private TextBox differenceBox;

        private DateTime? lastOrderPlacementTime = null;
        private const int CooldownMinutes = 90;
        private const double riskPercent=0.33;
        private const string LadderLabel = "LadderUSD";

        private EmaChartDrawer emaChartDrawer;

        protected override void OnStart()
        {
            emaChartDrawer = new EmaChartDrawer(Bars, Chart, Indicators);
            emaChartDrawer.DrawIfReady();

            BuildUI();
        }

        protected override void OnBar()
        {
            emaChartDrawer.DrawIfReady();
        }

        private void BuildUI()
        {
            var mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = "10"
            };

            volumeBox = CreateVolumeComboBox(mainPanel, "Volume (lots)", "0.01");
            slUsdBox = CreateValidatedInput(mainPanel, "Stop Loss (USD)", "10.00", InputType.DecimalPositive);
            tpUsdBox = CreateValidatedInput(mainPanel, "Take Profit (USD)", "10.00", InputType.DecimalPositive);
            ordersBox = CreateValidatedInput(mainPanel, "Number of Orders", "1", InputType.IntegerPositive);
            differenceBox = CreateValidatedInput(mainPanel, "Difference (Pips)", "1000.0", InputType.DecimalPositive);

            buyButton = new Button
            {
                Text = "BUY LADDER",
                BackgroundColor = Color.Green,
                Margin = "0 5 0 0",
                Height = 30
            };
            sellButton = new Button
            {
                Text = "SELL LADDER",
                BackgroundColor = Color.Red,
                Margin = "0 5 0 0",
                Height = 30
            };

            buyButton.Click += args => PlaceLadderOrders(TradeType.Buy);
            sellButton.Click += args => PlaceLadderOrders(TradeType.Sell);

            mainPanel.AddChild(buyButton);
            mainPanel.AddChild(sellButton);

            var border = new Border
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                BackgroundColor = Color.FromArgb(240, 30, 30, 30),
                BorderColor = Color.FromArgb(255, 60, 60, 60),
                BorderThickness = "1",
                CornerRadius = 5,
                Margin = "10 40 10 10",
                Width = 200,
                Padding = "5",
                Child = mainPanel
            };

            Chart.AddControl(border);
        }

        private ComboBox CreateVolumeComboBox(StackPanel panel, string label, string defaultValue)
        {
            var inputPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = "0 5 0 0"
            };

            inputPanel.AddChild(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Margin = "0 0 0 2"
            });

            var comboBox = new ComboBox
            {
                Height = 22
            };

            // Add volume options from 0.01 to 1.00 in increments of 0.01
            for (double i = 0.01; i <= 1.00; i += 0.01)
            {
                string optionText = $"{i:F2} Lots";
                comboBox.AddItem(optionText);
            }

            // Add larger volume options: 1.1, 1.2, ..., 10.0 in increments of 0.1
            for (double i = 1.1; i <= 10.0; i += 0.1)
            {
                string optionText = $"{i:F1} Lots";
                comboBox.AddItem(optionText);
            }

            // Set default value
            comboBox.SelectedItem = $"{defaultValue} Lots";

            inputPanel.AddChild(comboBox);
            panel.AddChild(inputPanel);
            return comboBox;
        }

        private TextBox CreateValidatedInput(StackPanel panel, string label, string defaultValue, InputType inputType)
        {
            var inputPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = "0 5 0 0"
            };

            inputPanel.AddChild(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Margin = "0 0 0 2"
            });

            var box = new TextBox
            {
                Text = defaultValue,
                Height = 22,
                BackgroundColor = Color.FromArgb(255, 240, 255, 240), // Light green for valid
                ForegroundColor = Color.Black // Black text for contrast
            };

            // Add text changed validation
            box.TextChanged += (args) => ValidateInput(box, inputType);

            // Initial validation
            ValidateInput(box, inputType);

            inputPanel.AddChild(box);
            panel.AddChild(inputPanel);
            return box;
        }

        private void ValidateInput(TextBox textBox, InputType inputType)
        {
            string originalText = textBox.Text;
            string cleanedText = CleanInput(originalText, inputType);

            // Update text if it was modified
            if (cleanedText != originalText)
            {
                textBox.Text = cleanedText;
                return; // TextChanged will be triggered again, so validation will run
            }

            string text = cleanedText.Trim();

            bool isValid = false;

            switch (inputType)
            {
                case InputType.DecimalPositive:
                    if (textBox == slUsdBox)
                    {
                        isValid = double.TryParse(text, out double slValue) && slValue >= 0.1 && slValue <= 100.0;
                    }
                    else if (textBox == tpUsdBox)
                    {
                        isValid = double.TryParse(text, out double tpValue) && tpValue >= 0.1 && tpValue <= 50.0;
                    }
                    else if (textBox == differenceBox)
                    {
                        isValid = double.TryParse(text, out double diffValue) && diffValue >= 1.0 && diffValue <= 100.0;
                    }
                    else
                    {
                        isValid = double.TryParse(text, out double decimalValue) && decimalValue >= 0;
                    }
                    break;
                case InputType.IntegerPositive:
                    if (textBox == ordersBox)
                    {
                        isValid = int.TryParse(text, out int orderValue) && orderValue >= 1 && orderValue <= 20;
                    }
                    else
                    {
                        isValid = int.TryParse(text, out int intValue) && intValue >= 1;
                    }
                    break;
            }

            // Visual feedback
            if (isValid)
            {
                textBox.BackgroundColor = Color.FromArgb(255, 240, 255, 240); // Light green
                textBox.ForegroundColor = Color.Black; // Black text for contrast
            }
            else
            {
                textBox.BackgroundColor = Color.FromArgb(255, 255, 240, 240); // Light red
                textBox.ForegroundColor = Color.Black; // Black text for contrast
            }
        }

        private string CleanInput(string input, InputType inputType)
        {
            if (string.IsNullOrEmpty(input)) return input;

            switch (inputType)
            {
                case InputType.DecimalPositive:
                    // Allow digits, decimal point, and backspace
                    var result = new System.Text.StringBuilder();
                    bool hasDecimal = false;
                    foreach (char c in input)
                    {
                        if (char.IsDigit(c))
                        {
                            result.Append(c);
                        }
                        else if (c == '.' && !hasDecimal)
                        {
                            result.Append(c);
                            hasDecimal = true;
                        }
                    }
                    return result.ToString();
                case InputType.IntegerPositive:
                    // Allow only digits
                    var intResult = new System.Text.StringBuilder();
                    foreach (char c in input)
                    {
                        if (char.IsDigit(c))
                        {
                            intResult.Append(c);
                        }
                    }
                    return intResult.ToString();
                default:
                    return input;
            }
        }

        private void PlaceLadderOrders(TradeType tradeType)
        {
            // Only enforce cooldown while the previous ladder still has active exposure.
            if (lastOrderPlacementTime.HasValue && HasActiveLadderExposure())
            {
                TimeSpan timeSinceLastOrder = Server.Time - lastOrderPlacementTime.Value;
                if (timeSinceLastOrder.TotalMinutes < CooldownMinutes)
                {
                    double remainingMinutes = CooldownMinutes - timeSinceLastOrder.TotalMinutes;
                    Print($"Error: Cooldown period active. Please wait {remainingMinutes:F1} more minutes before placing new orders.");
                    Print($"Last order placed at: {lastOrderPlacementTime.Value:HH:mm:ss}");
                    Print($"Next order can be placed after: {lastOrderPlacementTime.Value.AddMinutes(CooldownMinutes):HH:mm:ss}");
                    return;
                }
            }
            else
            {
                lastOrderPlacementTime = null;
            }

            // Extract volume value from ComboBox (format: "0.02 Lots")
            string volumeText = volumeBox.SelectedItem?.ToString() ?? "";
            if (string.IsNullOrEmpty(volumeText))
            {
                Print("Please select a volume");
                return;
            }
            string volumeValue = volumeText.Replace(" Lots", "").Trim();

            if (!double.TryParse(volumeValue, out double volumeLots) || volumeLots <= 0)
            {
                Print("Invalid volume");
                return;
            }

            double volumeInUnits = Symbol.QuantityToVolumeInUnits(volumeLots);

            // Ensure volume is within valid range
            if (volumeInUnits < (double)Symbol.VolumeInUnitsMin)
            {
                volumeInUnits = (double)Symbol.VolumeInUnitsMin;
            }

            if (volumeInUnits > (double)Symbol.VolumeInUnitsMax)
            {
                Print("Volume too large");
                return;
            }

            Print($"Final volume: {volumeInUnits} units ({volumeLots} lots)");


            if (!double.TryParse(slUsdBox.Text, out double slUsd) || slUsd < 0)
            {
                Print("Error: Invalid Stop Loss. Please enter a non-negative number (decimals allowed, e.g., 10.50).");
                return;
            }

            if (!double.TryParse(tpUsdBox.Text, out double tpUsd) || tpUsd < 0)
            {
                Print("Error: Invalid Take Profit. Please enter a non-negative number (decimals allowed, e.g., 2.50).");
                return;
            }

            if (!int.TryParse(ordersBox.Text, out int orderCount) || orderCount < 1)
            {
                Print("Error: Invalid number of orders. Please enter a positive integer (at least 1).");
                return;
            }

            if (!double.TryParse(differenceBox.Text, out double differencePips) || differencePips <= 0)
            {
                Print("Error: Invalid difference amount. Please enter a positive number in pips (decimals allowed, e.g., 10.5).");
                return;
            }

            // Risk Management: Check if total risk exceeds 30% of account balance
            double accountBalance = Account.Balance;
            double maxRiskAmount = accountBalance * riskPercent; 

            // Calculate existing risk from open positions
            double existingRisk = 0;
            foreach (var position in Positions)
            {
                if (position.StopLoss.HasValue)
                {
                    // Calculate stop loss distance in pips
                    double slDistancePips;
                    if (position.TradeType == TradeType.Buy)
                    {
                        slDistancePips = (position.EntryPrice - position.StopLoss.Value) / Symbol.PipSize;
                    }
                    else
                    {
                        slDistancePips = (position.StopLoss.Value - position.EntryPrice) / Symbol.PipSize;
                    }

                    // Convert pips to money: risk = pips * pipValue * volume
                    if (slDistancePips > 0 && Symbol.PipValue > 0)
                    {
                        double positionRisk = slDistancePips * Symbol.PipValue * position.VolumeInUnits;
                        existingRisk += positionRisk;
                    }
                }
            }

            double availableRisk = maxRiskAmount - existingRisk;
            double totalRiskAmount = slUsd * orderCount; // Total risk across all orders

            Print($"Account Balance: ${accountBalance:F2}");
            Print($"Maximum Risk Allowed (30%): ${maxRiskAmount:F2}");
            if (existingRisk > 0)
            {
                Print($"Existing Risk from Open Positions: ${existingRisk:F2}");
                Print($"Available Risk Remaining: ${availableRisk:F2}");
            }
            else
            {
                Print($"Available Risk: ${availableRisk:F2}");
            }
            Print($"Total Risk Requested: ${totalRiskAmount:F2} ({orderCount} orders × ${slUsd:F2} SL)");

            if (availableRisk <= 0)
            {
                Print($"Error: No available risk remaining. Existing risk (${existingRisk:F2}) already exceeds or equals 30% limit (${maxRiskAmount:F2})");
                return;
            }

            if (totalRiskAmount > availableRisk)
            {
                double maxSlPerOrder = availableRisk / orderCount;
                Print($"Error: Total risk (${totalRiskAmount:F2}) exceeds available risk limit (${availableRisk:F2})");
                Print($"To place {orderCount} orders, maximum SL per order: ${maxSlPerOrder:F2}");
                return;
            }

            Print($"Risk check passed. Proceeding to place orders...");

            double slPips = MoneyToPips(slUsd, volumeInUnits);
            double tpPips = MoneyToPips(tpUsd, volumeInUnits);

            if (double.IsNaN(slPips) || double.IsNaN(tpPips))
            {
                Print("Error: Could not calculate stop loss or take profit pips due to invalid pip value");
                return;
            }
            double currentPrice = tradeType == TradeType.Buy ? Symbol.Ask : Symbol.Bid;

            // Place limit orders with difference amount spacing
            int successfulOrders = 0;
            for (int i = 1; i <= orderCount; i++)
            {
                double limitPrice = GetLimitPriceForOrder(currentPrice, differencePips, tradeType, i);

                var limitResult = PlaceLimitOrder(
                    tradeType,
                    SymbolName,
                    volumeInUnits,
                    limitPrice,
                    LadderLabel,
                    slPips,
                    tpPips
                );

                if (limitResult.IsSuccessful)
                {
                    successfulOrders++;
                    Print($"Limit order {i} placed: Price={limitPrice:F5} (Difference: {differencePips * i} pips from market)");
                }
                else
                {
                    Print($"Limit order {i} failed: {limitResult.Error}");
                }
            }

            if (successfulOrders > 0)
            {
                double actualRiskPlaced = slUsd * successfulOrders;
                Print($"Summary: {successfulOrders} out of {orderCount} orders placed successfully");
                Print($"Total risk placed: ${actualRiskPlaced:F2} (${slUsd:F2} per order)");

                // Update cooldown timer
                lastOrderPlacementTime = Server.Time;
                Print($"Cooldown activated. Next orders can be placed after: {lastOrderPlacementTime.Value.AddMinutes(CooldownMinutes):HH:mm:ss}");
            }
        }

        private bool HasActiveLadderExposure()
        {
            foreach (var position in Positions)
            {
                if (position.SymbolName == SymbolName && position.Label == LadderLabel)
                {
                    return true;
                }
            }

            foreach (var order in PendingOrders)
            {
                if (order.SymbolName == SymbolName && order.Label == LadderLabel)
                {
                    return true;
                }
            }

            return false;
        }

        private double MoneyToPips(double money, double volume)
        {
            if (volume == 0 || Symbol.PipValue == 0)
            {
                Print("Error: Invalid volume or pip value for MoneyToPips calculation");
                return double.NaN; // Return NaN to indicate error
            }
            return money / (Symbol.PipValue * volume);
        }

        private double GetLimitPriceForOrder(double currentPrice, double differencePips, TradeType tradeType, int orderNumber)
        {
            double differenceDistance = differencePips * Symbol.PipSize * orderNumber;

            // For buy limit orders: place below current price
            // For sell limit orders: place above current price
            if (tradeType == TradeType.Buy)
                return currentPrice - differenceDistance;
            else
                return currentPrice + differenceDistance;
        }
    }
}
