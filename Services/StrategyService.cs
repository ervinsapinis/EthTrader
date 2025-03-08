using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kraken.Net.Enums;
using KrakenTelegramBot.Extensions;
using KrakenTelegramBot.Utils;
using EthTrader.Configuration;

namespace KrakenTelegramBot.Services
{
    public class StrategyService
    {
        private readonly KrakenService _krakenService;
        private readonly TelegramService _telegramService;
        private readonly BotSettings _botSettings;
        private readonly RiskSettings _riskSettings;
        private const KlineInterval Interval = KlineInterval.OneHour;

        public StrategyService(
            KrakenService krakenService, 
            TelegramService telegramService)
        {
            _krakenService = krakenService;
            _telegramService = telegramService;
            _botSettings = ConfigLoader.BotSettings;
            _riskSettings = ConfigLoader.RiskSettings;
        }

        public async Task ExecuteStrategyAsync(CancellationToken ct = default)
        {
            await CheckBuySignalsAsync(ct);
            await CheckSellSignalsAsync(ct);
        }

        private async Task CheckBuySignalsAsync(CancellationToken ct = default)
        {   
            // Retrieve klines using configured count
            var klinesResult = await _krakenService.ExchangeData.GetKlinesLimitedAsync(
                _botSettings.TradingPair, Interval, _botSettings.KlineCount, ct);
            if (!klinesResult.Success || klinesResult.Data == null || !klinesResult.Data.Any())
            {
                await _telegramService.SendNotificationAsync("Error fetching klines: " + klinesResult.Error);
                return;
            }

            // Extract closing prices using ClosePrice
            var closes = klinesResult.Data.Select(k => k.ClosePrice).ToList();

            // Calculate RSI using configuration
            var rsiValues = IndicatorUtils.CalculateRsi(closes, _botSettings.RsiPeriod);
            if (!rsiValues.Any())
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
            decimal latestHistogram = macdResult.Histogram.Last();

            Console.WriteLine($"Latest RSI: {latestRsi:F2} | SMA({_botSettings.SmaPeriod}): {sma:F2} | MACD Histogram: {latestHistogram:F2} | Current Price: {currentPrice:F2}");
            Console.WriteLine(isDowntrend ? "Downtrend detected." : "Uptrend detected.");

            // Decide to trade if conditions are met:
            // Adaptive threshold and MACD confirmation.
            if (latestRsi < adaptiveThreshold && latestHistogram > 0)
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
                
                // Calculate position size based on risk
                decimal quantityToBuy = RiskManagementUtils.CalculatePositionSize(
                    currentEquity, currentPrice, riskPercentage, _botSettings.StopLossPercentage);
                
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
                    $"Equity: {currentEquity} EUR, Risk: {riskPercentage:P0}.\n" +
                    $"Calculated to buy {quantityToBuy:F6} ETH at {currentPrice:F2} EUR (Total: {orderValueEur:F2} EUR).";

                var orderResult = await _krakenService.PlaceMarketBuyOrderAsync(_botSettings.TradingPair, quantityToBuy, ct);
                if (orderResult.Success)
                {
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
                string holdMessage = $"No trade: Latest RSI is {latestRsi:F2} (adaptive threshold: {adaptiveThreshold}), or MACD histogram is not positive (latest: {latestHistogram:F2}).";
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
            decimal latestRsi = rsiValues.Last();
            decimal sma = IndicatorUtils.CalculateSma(closes, _botSettings.SmaPeriod);
            var macdResult = IndicatorUtils.CalculateMacd(closes);
            decimal latestHistogram = macdResult.Histogram.Last();

            // Get entry price (if available) or estimate it
            decimal entryPrice = await GetEstimatedEntryPriceAsync(ct) ?? currentPrice * 0.9m; // Assume 10% profit if unknown
            decimal currentProfit = (currentPrice - entryPrice) / entryPrice;

            // Check sell conditions
            bool shouldSell = false;
            string sellReason = "";

            // 1. Take profit at overbought RSI
            if (latestRsi > 70)
            {
                shouldSell = true;
                sellReason = $"RSI overbought at {latestRsi:F2}";
            }
            // 2. Take profit at target percentage
            else if (currentProfit >= 0.15m) // 15% profit target
            {
                shouldSell = true;
                sellReason = $"Profit target reached: {currentProfit:P2}";
            }
            // 3. MACD bearish crossover while in profit
            else if (latestHistogram < 0 && macdResult.Histogram.Skip(macdResult.Histogram.Count - 2).First() > 0 && currentProfit > 0.05m)
            {
                shouldSell = true;
                sellReason = $"MACD bearish crossover while in profit: {currentProfit:P2}";
            }
            // 4. Trend reversal (price below SMA) while in good profit
            else if (currentPrice < sma && currentProfit > 0.08m)
            {
                shouldSell = true;
                sellReason = $"Trend reversal while in profit: {currentProfit:P2}";
            }

            if (shouldSell)
            {
                // Determine how much to sell (all ETH in this case)
                decimal quantityToSell = ethBalance;
                
                string sellMessage = $"Sell signal: {sellReason}\n" +
                    $"Current price: {currentPrice:F2} EUR, Entry price: {entryPrice:F2} EUR\n" +
                    $"Profit: {currentProfit:P2}, Selling {quantityToSell:F6} ETH";
                
                await _telegramService.SendNotificationAsync(sellMessage);
                
                // Place market sell order
                var sellResult = await _krakenService.PlaceMarketSellOrderAsync(_botSettings.TradingPair, quantityToSell, ct);
                
                if (sellResult.Success)
                {
                    await _telegramService.SendNotificationAsync($"Sell order executed successfully. Order ID: {sellResult.Data.OrderId}");
                }
                else
                {
                    await _telegramService.SendNotificationAsync($"Error executing sell order: {sellResult.Error}");
                }
            }
        }
        
        /// <summary>
        /// Gets the estimated entry price for the current ETH position
        /// </summary>
        private async Task<decimal?> GetEstimatedEntryPriceAsync(CancellationToken ct)
        {
            // In a real implementation, you would:
            // 1. Query your trade history from Kraken
            // 2. Find the most recent buy orders for ETH
            // 3. Calculate the weighted average entry price
            
            // For now, we'll return null (unknown entry price)
            return null;
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
