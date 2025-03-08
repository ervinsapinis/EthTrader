using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kraken.Net.Enums;
using KrakenTelegramBot.Extensions;
using KrakenTelegramBot.Utils;

namespace KrakenTelegramBot.Services
{
    public class StrategyService
    {
        private readonly KrakenService _krakenService;
        private readonly TelegramService _telegramService;
        private const KlineInterval Interval = KlineInterval.OneHour;



        // Remove hard-coded constants; we'll use configuration.
        // For dynamic risk sizing, we'll still get the current equity from Kraken.
        // For now, we'll simulate equity if necessary.
        private const decimal SimulatedEquity = 500m; // For testing only.

        public StrategyService(KrakenService krakenService, TelegramService telegramService)
        {
            _krakenService = krakenService;
            _telegramService = telegramService;
        }

        public async Task ExecuteStrategyAsync(CancellationToken ct = default)
        {   
            // Load settings from configuration
            var botSettings = Config.BotSettings;
            var riskSettings = Config.RiskSettings;

            // Retrieve klines using configured count
            var klinesResult = await _krakenService.ExchangeData.GetKlinesLimitedAsync(
                botSettings.TradingPair, Interval, botSettings.KlineCount, ct);
            if (!klinesResult.Success || klinesResult.Data == null || !klinesResult.Data.Any())
            {
                await _telegramService.SendNotificationAsync("Error fetching klines: " + klinesResult.Error);
                return;
            }

            // Extract closing prices using ClosePrice
            var closes = klinesResult.Data.Select(k => k.ClosePrice).ToList();

            // Calculate RSI using configuration
            var rsiValues = IndicatorUtils.CalculateRsi(closes, botSettings.RsiPeriod);
            if (!rsiValues.Any())
            {
                await _telegramService.SendNotificationAsync("Not enough data to calculate RSI.");
                return;
            }
            var latestRsi = rsiValues.Last();

            // Calculate SMA for trend awareness
            decimal sma = IndicatorUtils.CalculateSma(closes, botSettings.SmaPeriod);
            decimal currentPrice = closes.Last();
            bool isDowntrend = currentPrice < sma;
            decimal adaptiveThreshold = isDowntrend ? botSettings.DowntrendOversoldThreshold : botSettings.DefaultOversoldThreshold;

            // Calculate MACD for additional confirmation (optional)
            var macdResult = IndicatorUtils.CalculateMacd(closes);
            decimal latestHistogram = macdResult.Histogram.Last();

            Console.WriteLine($"Latest RSI: {latestRsi:F2} | SMA({botSettings.SmaPeriod}): {sma:F2} | MACD Histogram: {latestHistogram:F2} | Current Price: {currentPrice:F2}");
            Console.WriteLine(isDowntrend ? "Downtrend detected." : "Uptrend detected.");

            // Decide to trade if conditions are met:
            // Adaptive threshold and MACD confirmation.
            if (latestRsi < adaptiveThreshold && latestHistogram > 0)
            {
                // Fetch current equity from Kraken; here we simulate it.
                var balancesResult = await _krakenService.GetBalancesAsync(ct);
                if (!balancesResult.Success)
                {
                    await _telegramService.SendNotificationAsync("Error fetching account balance: " + balancesResult.Error);
                    return;
                }

                // Assume that the EUR balance is under "ZEUR" (Kraken may use different naming)
                decimal currentEquity = balancesResult.Data.ContainsKey("ZEUR") ? balancesResult.Data["ZEUR"] : SimulatedEquity;
                decimal riskPercentage = RiskManagementUtils.GetRiskPercentage(currentEquity);
                decimal quantityToBuy = RiskManagementUtils.CalculatePositionSize(currentEquity, currentPrice, riskPercentage, botSettings.StopLossPercentage);

                string orderMessage = $"Conditions met:\nRSI: {latestRsi:F2} (< {adaptiveThreshold}), MACD Histogram: {latestHistogram:F2}.\nEquity: {currentEquity} EUR, Risk: {riskPercentage:P0}.\nCalculated to buy {quantityToBuy:F6} ETH at {currentPrice:F2} EUR.";

                var orderResult = await _krakenService.PlaceMarketBuyOrderAsync(botSettings.TradingPair, quantityToBuy, ct);
                if (orderResult.Success)
                {
                    orderMessage += "\nBuy order executed successfully.";
                    // Calculate the stop-loss price and place a stop-loss order
                    decimal stopPrice = currentPrice * (1 - botSettings.StopLossPercentage);
                    var stopLossResult = await _krakenService.PlaceStopLossOrderAsync(botSettings.TradingPair, quantityToBuy, stopPrice, ct);
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
    }
}
