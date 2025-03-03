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

        // Strategy parameters
        private const string TradingPair = "ETH/EUR";
        private const KlineInterval Interval = KlineInterval.OneHour;
        private const int KlineCount = 50;
        private const int RsiPeriod = 14;
        private const decimal DefaultOversoldThreshold = 30m;
        private const decimal DowntrendOversoldThreshold = 20m;
        // Stop-loss percentage (e.g., 5% below entry price)
        private const decimal StopLossPercentage = 0.05m;

        // For adaptive risk, we'll use our dynamic tiers.
        // (The risk percentage is determined by current equity.)

        // For this example, let's simulate current equity.
        // In a live bot, you would fetch this from your Kraken account.
        private const decimal SimulatedEquity = 500m;

        public StrategyService(KrakenService krakenService, TelegramService telegramService)
        {
            _krakenService = krakenService;
            _telegramService = telegramService;
        }

        public async Task ExecuteStrategyAsync(CancellationToken ct = default)
        {
            var settings = Config.BotSettings;
            // Retrieve klines
            var klinesResult = await _krakenService.ExchangeData.GetKlinesLimitedAsync(TradingPair, Interval, KlineCount, ct);
            if (!klinesResult.Success || klinesResult.Data == null || !klinesResult.Data.Any())
            {
                await _telegramService.SendNotificationAsync("Error fetching klines: " + klinesResult.Error);
                return;
            }

            // Extract closing prices using ClosePrice
            var closes = klinesResult.Data.Select(k => k.ClosePrice).ToList();

            // Calculate RSI
            var rsiValues = IndicatorUtils.CalculateRsi(closes, RsiPeriod);
            if (!rsiValues.Any())
            {
                await _telegramService.SendNotificationAsync("Not enough data to calculate RSI.");
                return;
            }
            var latestRsi = rsiValues.Last();

            // Calculate SMA for trend awareness
            decimal sma = IndicatorUtils.CalculateSma(closes, 50);
            decimal currentPrice = closes.Last();
            bool isDowntrend = currentPrice < sma;
            decimal adaptiveOversoldThreshold = isDowntrend ? DowntrendOversoldThreshold : DefaultOversoldThreshold;

            // Calculate MACD for additional confirmation (optional)
            var macdResult = IndicatorUtils.CalculateMacd(closes);
            decimal latestHistogram = macdResult.Histogram.Last();

            Console.WriteLine($"Latest RSI: {latestRsi:F2} | SMA: {sma:F2} | MACD Histogram: {latestHistogram:F2} | Current Price: {currentPrice:F2}");
            Console.WriteLine(isDowntrend ? "Downtrend detected." : "Uptrend detected.");

            // Decide to trade if conditions are met
            // Now, instead of using a fixed investment, calculate the position size dynamically.
            if (latestRsi < adaptiveOversoldThreshold && latestHistogram > 0)
            {
                // Retrieve current equity; here we simulate with SimulatedEquity.
                var balancesResult = await _krakenService.GetBalancesAsync(ct);
                if (!balancesResult.Success)
                {
                    await _telegramService.SendNotificationAsync("Error fetching account balance: " + balancesResult.Error);
                    return;
                }

                // Assume that the balance for EUR is under the key "ZEUR" (check Kraken's asset naming conventions)
                decimal currentEquity = balancesResult.Data.ContainsKey("ZEUR") ? balancesResult.Data["ZEUR"] : 0m;
                decimal riskPercentage = RiskManagementUtils.GetRiskPercentage(currentEquity);
                decimal quantityToBuy = RiskManagementUtils.CalculatePositionSize(currentEquity, currentPrice, riskPercentage, StopLossPercentage);

                string orderMessage = $"Conditions met:\nRSI: {latestRsi:F2} (< {adaptiveOversoldThreshold}), MACD Histogram: {latestHistogram:F2}.\nCurrent Equity: {currentEquity} EUR, Risk: {riskPercentage:P0}.\nCalculated to buy {quantityToBuy:F6} ETH at {currentPrice:F2} EUR.";

                var orderResult = await _krakenService.PlaceMarketBuyOrderAsync(TradingPair, quantityToBuy, ct);
                if (orderResult.Success)
                {
                    orderMessage += "\nBuy order executed successfully.";
                    decimal stopPrice = currentPrice * (1 - settings.StopLossPercentage);
                    var stopLossResult = await _krakenService.PlaceStopLossOrderAsync(settings.TradingPair, quantityToBuy, stopPrice, ct);
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
                string holdMessage = $"No trade: Latest RSI is {latestRsi:F2} (adaptive threshold: {adaptiveOversoldThreshold}), or MACD histogram is not positive (latest: {latestHistogram:F2}).";
                Console.WriteLine(holdMessage);
                await _telegramService.SendNotificationAsync(holdMessage);
            }
        }
    }
}