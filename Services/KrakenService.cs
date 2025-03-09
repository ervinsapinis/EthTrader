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
using EthTrader.Models;

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
            // First, check if there are any existing open orders for this trading pair
            var openOrdersResult = await _restClient.SpotApi.Trading.GetOpenOrdersAsync(ct: ct);
            if (!openOrdersResult.Success)
            {
                return new WebCallResult<KrakenPlacedOrder>(
                    new ServerError(openOrdersResult.Error?.Code ?? 0, openOrdersResult.Error?.Message ?? "Failed to fetch open orders")
                );
            }

            // Check if there's already a stop loss order for this trading pair
            var existingStopOrders = openOrdersResult.Data.Open
                .Where(o => o.Value.OrderDetails.Symbol == tradingPair &&
                          o.Value.OrderDetails.Type == OrderType.StopLoss)
                .ToList();

            if (existingStopOrders.Any())
            {
                // Cancel existing stop orders before placing a new one
                foreach (var order in existingStopOrders)
                {
                    await _restClient.SpotApi.Trading.CancelOrderAsync(order.Key, ct: ct);
                    await Task.Delay(500); // Small delay to ensure order is cancelled
                }

                // Add a longer delay to ensure orders are fully removed
                await Task.Delay(2000);

                await ErrorLogger.LogErrorAsync("KrakenService",
                    $"Cancelled {existingStopOrders.Count} existing stop orders before placing new trailing stop");
            }

            // Get current price
            var tickerResult = await _restClient.SpotApi.ExchangeData.GetTickerAsync(tradingPair, ct);
            if (!tickerResult.Success || tickerResult.Data.Count == 0)
            {
                return new WebCallResult<KrakenPlacedOrder>(
                    new ServerError(tickerResult.Error?.Code ?? 0, tickerResult.Error?.Message ?? "No ticker data available")
                );
            }

            decimal currentPrice = tickerResult.Data.First().Value.LastTrade.Price;

            // Calculate stop price and round to 2 decimal places as required by Kraken
            decimal stopPrice = Math.Round(currentPrice * (1 - trailingOffset), 2);

            // Try multiple quantity reductions until we succeed or hit the minimum
            decimal[] reductionFactors = { 0.95m, 0.90m, 0.85m, 0.80m };

            foreach (var factor in reductionFactors)
            {
                // Adjust quantity to account for potential fees or reserved amounts
                decimal adjustedQuantity = Math.Round(quantity * factor, 8);

                // Ensure it still meets minimum order size (typically 0.002 ETH for Kraken)
                if (adjustedQuantity < 0.002m)
                {
                    continue; // Try the next factor if this makes it too small
                }

                // Log the adjustment
                string logMessage = $"Trying trailing stop with {factor:P0} of balance: {adjustedQuantity} ETH (original: {quantity} ETH)";
                await ErrorLogger.LogErrorAsync("KrakenService", logMessage);

                var orderResult = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                    symbol: tradingPair,
                    side: OrderSide.Sell,
                    type: OrderType.StopLoss,
                    quantity: adjustedQuantity,
                    price: stopPrice,
                    ct: ct);

                if (orderResult.Success)
                {
                    // If successful, return the result
                    string successMessage = $"Successfully placed trailing stop using {factor:P0} of available balance";
                    await ErrorLogger.LogErrorAsync("KrakenService", successMessage);
                    return orderResult;
                }

                // If we hit a different error than insufficient funds, stop trying
                if (orderResult.Error?.Message != null &&
                    !orderResult.Error.Message.Contains("Insufficient funds"))
                {
                    return orderResult;
                }

                // Add a delay before trying with a lower amount
                await Task.Delay(500);
            }

            // If we get here, all attempts failed - try one last attempt with a very small amount
            decimal minimumQuantity = 0.002m; // Kraken's minimum order size for ETH

            if (quantity > minimumQuantity * 1.1m) // Only if we have enough for minimum + buffer
            {
                string lastAttemptMessage = $"All reduction attempts failed, trying minimum order size: {minimumQuantity} ETH";
                await ErrorLogger.LogErrorAsync("KrakenService", lastAttemptMessage);

                return await _restClient.SpotApi.Trading.PlaceOrderAsync(
                    symbol: tradingPair,
                    side: OrderSide.Sell,
                    type: OrderType.StopLoss,
                    quantity: minimumQuantity,
                    price: stopPrice,
                    ct: ct);
            }

            // If even that failed or we don't have enough for minimum, return the last error
            return new WebCallResult<KrakenPlacedOrder>(
                new ServerError(0, "Failed to place order with any quantity reduction")
            );
        }

        /// <summary>
        /// Gets all open orders for the account
        /// </summary>
        public async Task<WebCallResult<IEnumerable<Models.KrakenOpenOrder>>> GetOpenOrdersAsync(CancellationToken ct = default)
        {
            var result = await _restClient.SpotApi.Trading.GetOpenOrdersAsync(ct: ct);

            if (!result.Success)
            {
                // Use the error constructor for WebCallResult
                return new WebCallResult<IEnumerable<Models.KrakenOpenOrder>>(
                    result.Error ?? new ServerError(0, "Unknown error")
                );
            }

            // Convert from Kraken's OpenOrdersPage to our custom KrakenOpenOrder
            var orders = new List<Models.KrakenOpenOrder>();

            foreach (var orderEntry in result.Data.Open)
            {
                orders.Add(new Models.KrakenOpenOrder
                {
                    Id = orderEntry.Key,
                    Symbol = orderEntry.Value.OrderDetails.Symbol,
                    Type = orderEntry.Value.OrderDetails.Type,
                    Side = orderEntry.Value.OrderDetails.Side,
                    Quantity = orderEntry.Value.Quantity,
                    Price = orderEntry.Value.Price
                });
            }

            // Use the full constructor to create a success WebCallResult
            return new WebCallResult<IEnumerable<Models.KrakenOpenOrder>>(
                result.ResponseStatusCode,
                result.ResponseHeaders,
                result.ResponseTime,
                result.ResponseLength,
                result.OriginalData,
                result.RequestId,
                result.RequestUrl,
                result.RequestBody,
                result.RequestMethod,
                result.RequestHeaders,
                result.DataSource,
                orders,  // Our converted data
                null     // No error for success
            );
        }
    }
}
