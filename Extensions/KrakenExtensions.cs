using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kraken.Net.Interfaces.Clients.SpotApi;
using Kraken.Net.Objects.Models;
using CryptoExchange.Net.Objects;

namespace KrakenTelegramBot.Extensions
{
    public static class KrakenExtensions
    {
        public static async Task<WebCallResult<IEnumerable<KrakenKline>>> GetKlinesLimitedAsync(
            this IKrakenRestClientSpotApiExchangeData exchangeData,
            string symbol,
            Kraken.Net.Enums.KlineInterval interval,
            int limit,
            CancellationToken ct = default)
        {
            // Call the Kraken API without a limit parameter
            var result = await exchangeData.GetKlinesAsync(symbol, interval, ct: ct);
            if (!result.Success || result.Data == null)
            {
                return new WebCallResult<IEnumerable<KrakenKline>>(result.Error);
            }

            // Access the actual klines via the Data property of KrakenKlinesResult
            var klines = result.Data.Data;
            if (klines == null)
            {
                return new WebCallResult<IEnumerable<KrakenKline>>(result.Error);
            }

            var limitedData = klines.Take(limit);

            return new WebCallResult<IEnumerable<KrakenKline>>(
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
                limitedData,
                result.Error
            );
        }
    }
}
