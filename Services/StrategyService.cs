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
