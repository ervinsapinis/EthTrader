using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kraken.Net.Enums;
using Kraken.Net.Objects.Models;
using EthTrader.Extensions;
using KrakenTelegramBot.Utils;
using EthTrader.Configuration;
using EthTrader.Utilities;

namespace EthTrader.Services
{
    public class StrategyService(KrakenService krakenService, TelegramService telegramService)
    {
        private readonly KrakenService _krakenService = krakenService;
        private readonly TelegramService _telegramService = telegramService;
        private readonly BotSettings _botSettings = ConfigLoader.BotSettings;
        private readonly RiskSettings _riskSettings = ConfigLoader.RiskSettings;
        private const KlineInterval Interval = KlineInterval.OneHour;

        public async Task ExecuteStrategyAsync(CancellationToken ct = default)
        {
            try
            {
                await CheckBuySignalsAsync(ct);
                await CheckSellSignalsAsync(ct);
                
                // Log performance metrics periodically
                if (DateTime.Now.Hour == 0 && DateTime.Now.Minute < 5)
                {
                    var performance = await TradeTracking.GetPerformanceMetricsAsync();
                    await _telegramService.SendNotificationAsync(performance.ToString());
                }
            }
            catch (Exception ex)
            {
                await ErrorLogger.LogErrorAsync("StrategyService.ExecuteStrategyAsync", "Error in strategy execution", ex);
                await _telegramService.SendNotificationAsync($"Error in strategy execution: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets historical data for the configured trading pair
        /// </summary>
        public async Task<List<KrakenKline>> GetHistoricalDataAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var klinesResult = await _krakenService.ExchangeData.GetKlinesAsync(
                    _botSettings.TradingPair, Interval, startDate, endDate, ct);
                
                if (!klinesResult.Success || klinesResult.Data == null || !klinesResult.Data.Any())
                {
                    throw new Exception($"Failed to fetch historical data: {klinesResult.Error}");
                }
                
                return klinesResult.Data.ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching historical data: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Runs a backtest of the strategy on historical data
        /// </summary>
        public async Task<BacktestResult> RunBacktestAsync(DateTime startDate, DateTime endDate, decimal initialCapital = 1000m)
        {
            try
            {
                // Fetch historical data
                var historicalData = await GetHistoricalDataAsync(startDate, endDate, default);
                
                // Run backtest
                var engine = new BacktestEngine(_botSettings, _riskSettings, initialCapital);
                var result = await engine.RunBacktestAsync(historicalData);
                
                // Log results
                Console.WriteLine(result.ToString());
                await _telegramService.SendNotificationAsync(result.ToString());
                
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Backtest error: {ex.Message}");
                await _telegramService.SendNotificationAsync($"Backtest error: {ex.Message}");
                throw;
            }
        }

        private async Task CheckBuySignalsAsync(CancellationToken ct = default)
        {   
            // Retrieve klines using configured count
            var klinesResult = await _krakenService.ExchangeData.GetKlinesLimitedAsync(
                _botSettings.TradingPair, Interval, _botSettings.KlineCount, ct);
            if (!klinesResult.Success || klinesResult.Data == null || klinesResult.Data.Count == 0)
            {
                await _telegramService.SendNotificationAsync("Error fetching klines: " + klinesResult.Error);
                return;
            }

            // Extract closing prices using ClosePrice
            var closes = klinesResult.Data.Select(k => k.ClosePrice).ToList();

            // Calculate RSI using configuration
            var rsiValues = IndicatorUtils.CalculateRsi(closes, _botSettings.RsiPeriod);
            if (rsiValues == null || rsiValues.Count == 0)
            {
                await _telegramService.SendNotificationAsync("Not enough data to calculate RSI.");
                return;
            }
            var latestRsi = rsiValues.Last();

            // Calculate SMA for trend awareness
            decimal sma = IndicatorUtils.CalculateSma(closes, _botSettings.SmaPeriod);
            decimal currentPrice = closes.Last();
            bool isDowntrend = currentPrice < sma;
            decimal adaptiveThreshold = isDowntrend ? _botSettings.DowntrendOversoldThreshold : _botSettings.DefaultOversoldThreshold;

            // Calculate MACD for additional confirmation (optional)
            var macdResult = IndicatorUtils.CalculateMacd(closes);
            if (macdResult.Histogram == null || macdResult.Histogram.Count == 0)
            {
                await _telegramService.SendNotificationAsync("Not enough data to calculate MACD.");
                return;
            }
            decimal latestHistogram = macdResult.Histogram.Last();

            Console.WriteLine($"Latest RSI: {latestRsi:F2} | SMA({_botSettings.SmaPeriod}): {sma:F2} | MACD Histogram: {latestHistogram:F2} | Current Price: {currentPrice:F2}");
            Console.WriteLine(isDowntrend ? "Downtrend detected." : "Uptrend detected.");

            // Get volume data for confirmation
            var volumes = klinesResult.Data.Select(k => k.Volume).ToList();
            var volumeMA = IndicatorUtils.CalculateVolumeMA(volumes, _botSettings.VolumeAvgPeriod);
            
            // Check if current volume is above average (indicating stronger move)
            bool volumeConfirmation = false;
            if (volumeMA.Count > 0)
            {
                decimal currentVolume = volumes.Last();
                decimal avgVolume = volumeMA.Last();
                volumeConfirmation = currentVolume >= avgVolume * _botSettings.MinVolumeMultiplier;
            }
            
            // Get high and low prices for ATR calculation
            var highs = klinesResult.Data.Select(k => k.HighPrice).ToList();
            var lows = klinesResult.Data.Select(k => k.LowPrice).ToList();
            
            // Calculate ATR for volatility-based position sizing
            var atrValues = IndicatorUtils.CalculateAtr(highs, lows, closes, _botSettings.AtrPeriod);
            decimal currentAtr = (atrValues != null && atrValues.Count > 0) ? atrValues.Last() : currentPrice * 0.02m; // Default to 2% if can't calculate
            
            // Decide to trade if conditions are met:
            // 1. RSI below adaptive threshold
            // 2. MACD histogram positive
            // 3. Volume confirmation
            if (latestRsi < adaptiveThreshold && latestHistogram > 0 && volumeConfirmation)
            {
                // Fetch current equity from Kraken
                var balancesResult = await _krakenService.GetBalancesAsync(ct);
                if (!balancesResult.Success)
                {
                    await _telegramService.SendNotificationAsync("Error fetching account balance: " + balancesResult.Error);
                    return;
                }

                // Assume that the EUR balance is under "ZEUR" (Kraken naming)
                if (!balancesResult.Data.TryGetValue("ZEUR", out decimal currentEquity) || currentEquity <= 0)
                {
                    await _telegramService.SendNotificationAsync("Insufficient EUR balance or unable to retrieve balance.");
                    return;
                }

                // Get risk percentage based on account size
                decimal riskPercentage = GetRiskPercentageFromSettings(currentEquity);
                
                // Calculate position size based on risk and volatility
                decimal riskAmount = currentEquity * riskPercentage;
                
                // Adjust risk based on volatility (ATR)
                decimal volatilityRatio = currentAtr / currentPrice;
                decimal adjustedRisk = riskPercentage;
                
                // If market is more volatile than usual, reduce position size
                if (volatilityRatio > _botSettings.MaxVolatilityRisk)
                {
                    adjustedRisk = riskPercentage * (_botSettings.MaxVolatilityRisk / volatilityRatio);
                    await _telegramService.SendNotificationAsync(
                        $"High volatility detected ({volatilityRatio:P2}). Reducing risk from {riskPercentage:P2} to {adjustedRisk:P2}");
                }
                
                decimal quantityToBuy = RiskManagementUtils.CalculatePositionSize(
                    currentEquity, currentPrice, adjustedRisk, _botSettings.StopLossPercentage);
                
                // Calculate the EUR value of the order
                decimal orderValueEur = quantityToBuy * currentPrice;
                
                // Check if we have enough balance and adjust if needed
                if (orderValueEur > currentEquity)
                {
                    // Adjust to use available balance (with small buffer for fees)
                    decimal adjustmentFactor = (currentEquity * 0.995m) / orderValueEur;
                    quantityToBuy *= adjustmentFactor;
                    orderValueEur = quantityToBuy * currentPrice;
                    
                    await _telegramService.SendNotificationAsync(
                        $"Warning: Order size adjusted to match available balance. Original: {orderValueEur / adjustmentFactor:F2} EUR, Adjusted: {orderValueEur:F2} EUR");
                }
                
                // Ensure minimum order size (Kraken typically requires orders > 0.002 ETH)
                if (quantityToBuy < 0.002m)
                {
                    await _telegramService.SendNotificationAsync(
                        $"Calculated position size ({quantityToBuy:F6} ETH) is below minimum order size. No order placed.");
                    return;
                }

                string orderMessage = $"Conditions met:\nRSI: {latestRsi:F2} (< {adaptiveThreshold}), MACD Histogram: {latestHistogram:F2}.\n" +
                    $"Volume confirmation: Current volume is {volumes.Last():F2} (vs avg {volumeMA.Last():F2}).\n" +
                    $"Equity: {currentEquity} EUR, Risk: {adjustedRisk:P0}, Volatility: {volatilityRatio:P2}.\n" +
                    $"Calculated to buy {quantityToBuy:F6} ETH at {currentPrice:F2} EUR (Total: {orderValueEur:F2} EUR).";

                var orderResult = await _krakenService.PlaceMarketBuyOrderAsync(_botSettings.TradingPair, quantityToBuy, ct);
                if (orderResult.Success)
                {
                    // Log the trade
                    await TradeTracking.LogTradeAsync(new TradeRecord
                    {
                        OrderId = orderResult.Data.OrderId,
                        Timestamp = DateTime.UtcNow,
                        Symbol = _botSettings.TradingPair,
                        Quantity = quantityToBuy,
                        Price = currentPrice,
                        Side = "Buy",
                        Type = "Entry",
                        RemainingPosition = quantityToBuy
                    });
                    
                    orderMessage += "\nBuy order executed successfully.";
                    // Calculate the stop-loss price and place a stop-loss order
                    decimal stopPrice = currentPrice * (1 - _botSettings.StopLossPercentage);
                    var stopLossResult = await _krakenService.PlaceStopLossOrderAsync(_botSettings.TradingPair, quantityToBuy, stopPrice, ct);
                    if (stopLossResult.Success)
                        orderMessage += $"\nStop-loss order placed at {stopPrice:F2} EUR.";
                    else
                        orderMessage += $"\nError placing stop-loss order: {stopLossResult.Error}";
                }
                else
                {
                    orderMessage += "\nError executing buy order: " + orderResult.Error;
                }
                Console.WriteLine(orderMessage);
                await _telegramService.SendNotificationAsync(orderMessage);
            }
            else
            {
                string holdMessage = $"No trade: Latest RSI is {latestRsi:F2} (adaptive threshold: {adaptiveThreshold}), " +
                    $"MACD histogram is {latestHistogram:F2}, " +
                    $"Volume confirmation: {(volumeConfirmation ? "Yes" : "No")}";
                Console.WriteLine(holdMessage);
                await _telegramService.SendNotificationAsync(holdMessage);
            }
        }

        private async Task CheckSellSignalsAsync(CancellationToken ct = default)
        {
            // Get current ETH position
            var balancesResult = await _krakenService.GetBalancesAsync(ct);
            if (!balancesResult.Success)
            {
                await _telegramService.SendNotificationAsync("Error fetching balances: " + balancesResult.Error);
                return;
            }

            // Check if we have any ETH to sell
            if (!balancesResult.Data.TryGetValue("XETH", out decimal ethBalance) || ethBalance <= 0.002m)
            {
                // No ETH position or too small to sell
                return;
            }

            // Retrieve klines for analysis
            var klinesResult = await _krakenService.ExchangeData.GetKlinesLimitedAsync(
                _botSettings.TradingPair, Interval, _botSettings.KlineCount, ct);
            if (!klinesResult.Success || klinesResult.Data == null || !klinesResult.Data.Any())
            {
                await _telegramService.SendNotificationAsync("Error fetching klines: " + klinesResult.Error);
                return;
            }

            // Extract closing prices
            var closes = klinesResult.Data.Select(k => k.ClosePrice).ToList();
            decimal currentPrice = closes.Last();

            // Calculate indicators
            var rsiValues = IndicatorUtils.CalculateRsi(closes, _botSettings.RsiPeriod);
            if (rsiValues == null || !rsiValues.Any())
            {
                await _telegramService.SendNotificationAsync("Not enough data to calculate RSI for sell signals.");
                return;
            }
            decimal latestRsi = rsiValues.Last();
            decimal sma = IndicatorUtils.CalculateSma(closes, _botSettings.SmaPeriod);
            var (macdLine, signalLine, histogram) = IndicatorUtils.CalculateMacd(closes);
            if (histogram == null || histogram.Count == 0)
            {
                await _telegramService.SendNotificationAsync("Not enough data to calculate MACD for sell signals.");
                return;
            }
            decimal latestHistogram = histogram.Last();

            // Get entry price from trade history
            decimal? storedEntryPrice = await TradeTracking.GetEstimatedEntryPriceAsync(_botSettings.TradingPair);
            decimal entryPrice = storedEntryPrice ?? currentPrice * 0.9m; // Assume 10% profit if unknown
            decimal currentProfit = (currentPrice - entryPrice) / entryPrice;

            // Check for partial profit taking opportunities
            bool shouldSell = false;
            string sellReason = "";
            decimal quantityToSell = 0;
            string exitType = "";

            // 1. First profit target (sell 30% of position)
            if (currentProfit >= _botSettings.FirstProfitTarget && 
                currentProfit < _botSettings.SecondProfitTarget)
            {
                // Check if we've already taken partial profits at this level
                decimal currentPosition = await TradeTracking.GetCurrentPositionSizeAsync(_botSettings.TradingPair);
                if (currentPosition >= ethBalance * 0.99m) // No partial exits yet
                {
                    shouldSell = true;
                    sellReason = $"First profit target reached: {currentProfit:P2}";
                    quantityToSell = ethBalance * _botSettings.FirstSellPercentage;
                    exitType = "PartialExit";
                }
            }
            // 2. Second profit target (sell 40% of original position)
            else if (currentProfit >= _botSettings.SecondProfitTarget && 
                     currentProfit < _botSettings.FinalProfitTarget)
            {
                decimal currentPosition = await TradeTracking.GetCurrentPositionSizeAsync(_botSettings.TradingPair);
                if (currentPosition >= ethBalance * 0.75m) // Only first partial exit taken
                {
                    shouldSell = true;
                    sellReason = $"Second profit target reached: {currentProfit:P2}";
                    quantityToSell = ethBalance * _botSettings.SecondSellPercentage;
                    exitType = "PartialExit";
                }
            }
            // 3. Final profit target (sell remaining position)
            else if (currentProfit >= _botSettings.FinalProfitTarget)
            {
                shouldSell = true;
                sellReason = $"Final profit target reached: {currentProfit:P2}";
                quantityToSell = ethBalance; // Sell all remaining
                exitType = "FinalExit";
            }
            // 4. Take profit at overbought RSI
            else if (latestRsi > 70)
            {
                shouldSell = true;
                sellReason = $"RSI overbought at {latestRsi:F2}";
                quantityToSell = ethBalance; // Sell all
                exitType = "FinalExit";
            }
            // 5. MACD bearish crossover while in profit
            else if (latestHistogram < 0 && 
                     histogram.Skip(histogram.Count - 2).First() > 0 && 
                     currentProfit > 0.05m)
            {
                shouldSell = true;
                sellReason = $"MACD bearish crossover while in profit: {currentProfit:P2}";
                quantityToSell = ethBalance; // Sell all
                exitType = "FinalExit";
            }
            // 6. Trend reversal (price below SMA) while in good profit
            else if (currentPrice < sma && currentProfit > 0.08m)
            {
                shouldSell = true;
                sellReason = $"Trend reversal while in profit: {currentProfit:P2}";
                quantityToSell = ethBalance; // Sell all
                exitType = "FinalExit";
            }

            // Check if we should set up a trailing stop instead of selling
            if (!shouldSell && currentProfit >= _botSettings.TrailingStopActivationProfit)
            {
                // Set up trailing stop for remaining position
                decimal trailingStopOffset = currentPrice * _botSettings.TrailingStopPercentage;
                
                string trailingStopMessage = $"Setting trailing stop: Current profit {currentProfit:P2} exceeds activation threshold.\n" +
                    $"Current price: {currentPrice:F2} EUR, Trailing offset: {trailingStopOffset:F2} EUR";
                
                await _telegramService.SendNotificationAsync(trailingStopMessage);
                
                var trailingStopResult = await _krakenService.PlaceTrailingStopOrderAsync(
                    _botSettings.TradingPair, ethBalance, _botSettings.TrailingStopPercentage, ct);
                
                if (trailingStopResult.Success)
                {
                    await _telegramService.SendNotificationAsync($"Trailing stop set successfully. Order ID: {trailingStopResult.Data.OrderId}");
                }
                else
                {
                    await _telegramService.SendNotificationAsync($"Error setting trailing stop: {trailingStopResult.Error}");
                }
            }

            if (shouldSell && quantityToSell > 0)
            {
                // Ensure we're not trying to sell more than we have
                quantityToSell = Math.Min(quantityToSell, ethBalance);
                
                // Ensure minimum order size
                if (quantityToSell < 0.002m)
                {
                    quantityToSell = ethBalance; // If remaining amount is too small, sell all
                    exitType = "FinalExit";
                }
                
                string sellMessage = $"Sell signal: {sellReason}\n" +
                    $"Current price: {currentPrice:F2} EUR, Entry price: {entryPrice:F2} EUR\n" +
                    $"Profit: {currentProfit:P2}, Selling {quantityToSell:F6} ETH";
                
                await _telegramService.SendNotificationAsync(sellMessage);
                
                // Place market sell order
                var sellResult = await _krakenService.PlaceMarketSellOrderAsync(_botSettings.TradingPair, quantityToSell, ct);
                
                if (sellResult.Success)
                {
                    // Log the trade with profit information
                    await TradeTracking.LogTradeAsync(new TradeRecord
                    {
                        OrderId = sellResult.Data.OrderId,
                        Timestamp = DateTime.UtcNow,
                        Symbol = _botSettings.TradingPair,
                        Quantity = quantityToSell,
                        Price = currentPrice,
                        Side = "Sell",
                        Type = exitType,
                        RemainingPosition = ethBalance - quantityToSell,
                        ProfitPercentage = currentProfit
                    });
                    
                    await _telegramService.SendNotificationAsync($"Sell order executed successfully. Order ID: {sellResult.Data.OrderId}");
                }
                else
                {
                    await _telegramService.SendNotificationAsync($"Error executing sell order: {sellResult.Error}");
                }
            }
        }
        
        
        /// <summary>
        /// Gets the appropriate risk percentage based on account equity and risk settings
        /// </summary>
        private decimal GetRiskPercentageFromSettings(decimal equity)
        {
            if (equity < 150)
                return _riskSettings.Tier1;
            else if (equity < 350)
                return _riskSettings.Tier2;
            else if (equity < 500)
                return _riskSettings.Tier3;
            else if (equity < 800)
                return _riskSettings.Tier4;
            else if (equity < 1500)
                return _riskSettings.Tier5;
            else
                return _riskSettings.TierAbove;
        }
    }
}
