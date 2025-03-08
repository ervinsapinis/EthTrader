using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kraken.Net.Clients;
using Kraken.Net.Enums;
using Kraken.Net.Objects;
using CryptoExchange.Net.Authentication;
using Kraken.Net.Objects.Models;
using CryptoExchange.Net.Objects;
using Kraken.Net.Interfaces.Clients.SpotApi;
using EthTrader.Utilities;
using System.Net;
using System.Net.Http;

namespace EthTrader.Services
{
    public class KrakenService
    {
        private readonly KrakenRestClient _restClient;

        // Expose the exchange data client so extensions can be used
        public IKrakenRestClientSpotApiExchangeData ExchangeData => _restClient.SpotApi.ExchangeData;

        public KrakenService()
        {
            var krakenApiKey = Environment.GetEnvironmentVariable("KRAKEN_API_KEY");
            var krakenApiSecret = Environment.GetEnvironmentVariable("KRAKEN_API_SECRET");

            if (string.IsNullOrEmpty(krakenApiKey) || string.IsNullOrEmpty(krakenApiSecret))
                throw new Exception("Kraken API credentials are not set.");

            _restClient = new KrakenRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(krakenApiKey, krakenApiSecret);
            });
        }

        public async Task<WebCallResult<Dictionary<string, decimal>>> GetBalancesAsync(CancellationToken ct = default)
        {
            return await _restClient.SpotApi.Account.GetBalancesAsync(ct: ct);
        }

        // Optionally, you can wrap other methods here too.
        public async Task<WebCallResult<KrakenPlacedOrder>> PlaceMarketBuyOrderAsync(string tradingPair, decimal quantity, CancellationToken ct = default)
        {
            return await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol: tradingPair,
                side: OrderSide.Buy,
                type: OrderType.Market,
                quantity: quantity,
                ct: ct);
        }

        public async Task<WebCallResult<KrakenPlacedOrder>> PlaceStopLossOrderAsync(
            string tradingPair,
            decimal quantity,
            decimal stopPrice,
            CancellationToken ct = default)
        {
            // Using Kraken's order endpoint for stop-loss orders:
            return await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol: tradingPair,
                side: OrderSide.Sell,
                type: OrderType.StopLoss,  // or OrderType.StopLossLimit if needed
                quantity: quantity,
                price: stopPrice,          // the trigger price for stop-loss
                ct: ct
            );
        }

        public async Task<WebCallResult<KrakenPlacedOrder>> PlaceMarketSellOrderAsync(
            string tradingPair,
            decimal quantity,
            CancellationToken ct = default)
        {
            return await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol: tradingPair,
                side: OrderSide.Sell,
                type: OrderType.Market,
                quantity: quantity,
                ct: ct);
        }

        public async Task<WebCallResult<KrakenPlacedOrder>> PlaceTrailingStopOrderAsync(
            string tradingPair,
            decimal quantity,
            decimal trailingOffset,
            CancellationToken ct = default)
        {
            // Note: Kraken doesn't directly support trailing stops via API
            // We'll use a stop loss order instead and note that in production
            // you would need to implement trailing stop logic manually

            // Get current price
            var tickerResult = await _restClient.SpotApi.ExchangeData.GetTickerAsync(tradingPair, ct);
            if (!tickerResult.Success || tickerResult.Data.Count == 0)
            {
                // Use the constructor that just takes an error
                return new WebCallResult<KrakenPlacedOrder>(
                    new ServerError(tickerResult.Error?.Code ?? 0, tickerResult.Error?.Message ?? "No ticker data available")
                );
            }

            decimal currentPrice = tickerResult.Data.First().Value.LastTrade.Price;

            // Calculate stop price and round to 2 decimal places as required by Kraken
            decimal stopPrice = Math.Round(currentPrice * (1 - trailingOffset), 2);

            return await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol: tradingPair,
                side: OrderSide.Sell,
                type: OrderType.StopLoss,
                quantity: quantity,
                price: stopPrice,
                ct: ct);
        }
    }
}