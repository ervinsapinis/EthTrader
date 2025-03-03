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
        private const int KlineCount = 50;  // Changed from 'limit' to 'count'
        private const int RsiPeriod = 14;
        private const decimal OversoldThreshold = 30m;
        private const decimal FixedEurInvestment = 50m; // amount in EUR to spend per trade

        public StrategyService(KrakenService krakenService, TelegramService telegramService)
        {
            _krakenService = krakenService;
            _telegramService = telegramService;
        }

        public async Task ExecuteStrategyAsync(CancellationToken ct = default)
        {
            // Retrieve klines
            var klinesResult = await _krakenService.ExchangeData.GetKlinesLimitedAsync(TradingPair, Interval, KlineCount, ct);
            if (!klinesResult.Success || klinesResult.Data == null || !klinesResult.Data.Any())
            {
                await _telegramService.SendNotificationAsync("Error fetching klines: " + klinesResult.Error);
                return;
            }

            // Extract closing prices using ClosePrice instead of Close
            var closes = klinesResult.Data.Select(k => k.ClosePrice).ToList();
            var rsiValues = IndicatorUtils.CalculateRsi(closes, RsiPeriod);
            if (!rsiValues.Any())
            {
                await _telegramService.SendNotificationAsync("Not enough data to calculate RSI.");
                return;
            }

            var latestRsi = rsiValues.Last();
            Console.WriteLine($"Latest RSI: {latestRsi:F2}");

            if (latestRsi < OversoldThreshold)
            {
                decimal currentPrice = closes.Last();
                decimal quantityToBuy = FixedEurInvestment / currentPrice;
                string orderMessage = $"RSI {latestRsi:F2} below {OversoldThreshold}. Buying {quantityToBuy:F6} ETH at {currentPrice:F2} EUR.";

                var orderResult = await _krakenService.PlaceMarketBuyOrderAsync(TradingPair, quantityToBuy, ct);
                if (orderResult.Success)
                {
                    orderMessage += "\nBuy order executed successfully.";
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
                string holdMessage = $"RSI {latestRsi:F2} is above threshold. No trade executed.";
                Console.WriteLine(holdMessage);
                await _telegramService.SendNotificationAsync(holdMessage);
            }
        }
    }
}
