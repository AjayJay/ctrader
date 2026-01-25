using cAlgo.API;
using cAlgo.API.Internals;
using System;
using System.Linq;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class LadderOrderBotUSD : Robot
    {
        private Button buyButton;
        private Button sellButton;
        private TextBlock cooldownTextBlock;

        private ComboBox volumeBox;
        private TextBox slUsdBox;
        private TextBox tpUsdBox;
        private TextBox ordersBox;

        private const int CooldownMinutes = 120;
        private DateTime lastUIUpdateTime = DateTime.MinValue;

        protected override void OnStart()
        {
            BuildUI();
            UpdateUIForCooldown();
        }

        protected override void OnTick()
        {
            // Update UI every second to show cooldown countdown
            if ((Server.Time - lastUIUpdateTime).TotalSeconds >= 1)
            {
                UpdateUIForCooldown();
                lastUIUpdateTime = Server.Time;
            }
        }

        private void BuildUI()
        {
            var mainPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = "10"
            };

            volumeBox = CreateVolumeComboBox(mainPanel, "Volume (lots)", "0.01");
            slUsdBox = CreateInput(mainPanel, "Stop Loss (USD)", "10");
            tpUsdBox = CreateInput(mainPanel, "Take Profit (USD)", "10");
            ordersBox = CreateInput(mainPanel, "Number of Orders", "1");

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

            // Cooldown display text block
            cooldownTextBlock = new TextBlock
            {
                Text = "",
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                ForegroundColor = Color.Orange,
                Margin = "0 5 0 0",
                Height = 30,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            mainPanel.AddChild(buyButton);
            mainPanel.AddChild(sellButton);
            mainPanel.AddChild(cooldownTextBlock);

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

        private TextBox CreateInput(StackPanel panel, string label, string defaultValue)
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
                Height = 22
            };
            
            inputPanel.AddChild(box);
            panel.AddChild(inputPanel);
            return box;
        }

        private void PlaceLadderOrders(TradeType tradeType)
        {
            // Get the most recent order placement time from existing positions or stored data
            DateTime? lastOrderPlacementTime = GetLastOrderPlacementTime();

            // Check cooldown period based on position's last order placement time
            if (lastOrderPlacementTime.HasValue)
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
                Print("Error: Invalid Stop Loss. Please enter a non-negative number.");
                return;
            }

            if (!double.TryParse(tpUsdBox.Text, out double tpUsd) || tpUsd < 0)
            {
                Print("Error: Invalid Take Profit. Please enter a non-negative number.");
                return;
            }

            if (!int.TryParse(ordersBox.Text, out int orderCount) || orderCount < 1)
            {
                Print("Error: Invalid number of orders. Please enter a positive integer (at least 1).");
                return;
            }

            // Risk Management: Check if total risk exceeds 20% of account balance
            double accountBalance = Account.Balance;
            double maxRiskAmount = accountBalance * 0.20; // 20% of balance
            
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

            // 1️⃣ Market Order (First order at market price)
            var result = ExecuteMarketOrder(
                tradeType,
                SymbolName,
                volumeInUnits,
                "LadderUSD",
                slPips,
                tpPips
            );

            if (!result.IsSuccessful)
            {
                Print($"Market order failed: {result.Error}");
                return;
            }

            // Calculate the take profit price of the first order
            double currentEntryPrice = result.Position.EntryPrice;
            double stopPrice = GetTakeProfitPrice(currentEntryPrice, tpPips, tradeType);
            double limitPrice = GetLimitPrice(stopPrice, tradeType);

            // 2️⃣ Ladder Orders (Subsequent orders as stop limit orders at the profit price of the previous order)
            for (int i = 2; i <= orderCount; i++)
            {
                var stopLimitResult = PlaceStopLimitOrder(
                    tradeType,
                    SymbolName,
                    volumeInUnits,
                    stopPrice,
                    limitPrice,
                    "LadderUSD",
                    slPips,
                    tpPips
                );

                if (stopLimitResult.IsSuccessful)
                {
                    Print($"Stop limit order {i} placed: Stop={stopPrice:F5}, Limit={limitPrice:F5} (TP price of previous order)");
                    // Next order will be placed at the take profit price of this stop limit order
                    currentEntryPrice = stopPrice;
                    stopPrice = GetTakeProfitPrice(currentEntryPrice, tpPips, tradeType);
                    limitPrice = GetLimitPrice(stopPrice, tradeType);
                }
                else
                {
                    Print($"Stop limit order {i} failed: {stopLimitResult.Error}");
                    // Even if order fails, continue with next price calculation
                    currentEntryPrice = stopPrice;
                    stopPrice = GetTakeProfitPrice(currentEntryPrice, tpPips, tradeType);
                    limitPrice = GetLimitPrice(stopPrice, tradeType);
                }
            }

            // Update cooldown timer after successful order placement
            if (result.IsSuccessful)
            {
                Print($"Cooldown activated. Next orders can be placed after: {Server.Time.AddMinutes(CooldownMinutes):HH:mm:ss}");
                UpdateUIForCooldown();
            }
        }

        private void UpdateUIForCooldown()
        {
            DateTime? lastOrderPlacementTime = GetLastOrderPlacementTime();
            bool isCooldownActive = false;
            string remainingTimeText = "";

            if (lastOrderPlacementTime.HasValue)
            {
                TimeSpan timeSinceLastOrder = Server.Time - lastOrderPlacementTime.Value;
                if (timeSinceLastOrder.TotalMinutes < CooldownMinutes)
                {
                    isCooldownActive = true;
                    double remainingMinutes = CooldownMinutes - timeSinceLastOrder.TotalMinutes;
                    int minutes = (int)remainingMinutes;
                    int seconds = (int)((remainingMinutes - minutes) * 60);
                    remainingTimeText = $"Cooldown: {minutes:D2}:{seconds:D2} remaining";
                }
            }

            // Show/hide buttons and cooldown text based on cooldown status
            if (isCooldownActive)
            {
                buyButton.IsVisible = false;
                sellButton.IsVisible = false;
                cooldownTextBlock.IsVisible = true;
                cooldownTextBlock.Text = remainingTimeText;
            }
            else
            {
                buyButton.IsVisible = true;
                sellButton.IsVisible = true;
                cooldownTextBlock.IsVisible = false;
                cooldownTextBlock.Text = "";
            }
        }

        private DateTime? GetLastOrderPlacementTime()
        {
            // Check existing positions with the "LadderUSD" label to get the most recent one
            // Positions persist across app restarts, so this will maintain cooldown
            var ladderPositions = Positions.Where(p => p.Label == "LadderUSD" && p.SymbolName == SymbolName).ToList();
            
            if (ladderPositions.Any())
            {
                // Get the most recent position's creation time
                var mostRecentPosition = ladderPositions.OrderByDescending(p => p.EntryTime).First();
                return mostRecentPosition.EntryTime;
            }

            return null;
        }

        private double MoneyToPips(double money, double volume)
        {
            if (volume == 0 || Symbol.PipValue == 0)
            {
                Print("Error: Invalid volume or pip value");
                return 0;
            }
            return money / (Symbol.PipValue * volume);
        }

        private double GetTakeProfitPrice(double entryPrice, double tpPips, TradeType tradeType)
        {
            double tpDistance = tpPips * Symbol.PipSize;

            // Calculate the take profit price (where the previous order would close at profit)
            if (tradeType == TradeType.Buy)
                return entryPrice + tpDistance;
            else
                return entryPrice - tpDistance;
        }

        private double GetLimitPrice(double stopPrice, TradeType tradeType)
        {
            // For stop limit orders:
            // Buy: Limit price should be >= stop price (we'll set it equal to stop price)
            // Sell: Limit price should be <= stop price (we'll set it equal to stop price)
            return stopPrice;
        }
    }
}
