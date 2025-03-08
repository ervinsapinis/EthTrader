﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kraken.Net.Clients;
using Kraken.Net.Enums;
using Kraken.Net.Objects;
using CryptoExchange.Net.Authentication;
using Kraken.Net.Objects.Models;
using CryptoExchange.Net.Objects;
using Kraken.Net.Interfaces.Clients.SpotApi;

namespace KrakenTelegramBot.Services
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
            // Note: Implementation depends on Kraken API support for trailing stops
            // This is a simplified example
            return await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol: tradingPair,
                side: OrderSide.Sell,
                type: OrderType.TrailingStopMarket,
                quantity: quantity,
                trailingDelta: trailingOffset,
                ct: ct);
        }

    }
}
