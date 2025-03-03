namespace KrakenTelegramBot.Utils
{
    public static class IndicatorUtils
    {
        public static List<decimal> CalculateRsi(List<decimal> closes, int period)
        {
            var rsiValues = new List<decimal>();
            if (closes.Count < period + 1)
                return rsiValues;

            decimal gain = 0m, loss = 0m;

            for (int i = 1; i <= period; i++)
            {
                decimal change = closes[i] - closes[i - 1];
                if (change > 0)
                    gain += change;
                else
                    loss -= change;
            }

            decimal avgGain = gain / period;
            decimal avgLoss = loss / period;
            decimal rs = avgLoss == 0 ? 0 : avgGain / avgLoss;
            rsiValues.Add(100 - (100 / (1 + rs)));

            for (int i = period + 1; i < closes.Count; i++)
            {
                decimal change = closes[i] - closes[i - 1];
                decimal currentGain = change > 0 ? change : 0;
                decimal currentLoss = change < 0 ? -change : 0;

                avgGain = ((avgGain * (period - 1)) + currentGain) / period;
                avgLoss = ((avgLoss * (period - 1)) + currentLoss) / period;
                rs = avgLoss == 0 ? 0 : avgGain / avgLoss;
                rsiValues.Add(100 - (100 / (1 + rs)));
            }

            return rsiValues;
        }

        public static decimal CalculateSma(List<decimal> prices, int period)
        {
            if (prices == null || prices.Count < period)
                throw new ArgumentException("Not enough data to calculate SMA.");

            // Using Skip to get the last 'period' number of prices:
            return prices.Skip(prices.Count - period).Average();
        }

        public static List<decimal> CalculateEma(List<decimal> prices, int period)
        {
            if (prices == null || prices.Count < period)
                throw new ArgumentException("Not enough data to calculate EMA.");

            var ema = new List<decimal>();
            decimal multiplier = 2m / (period + 1);
            // Start with the simple average of the first 'period' prices
            decimal sum = prices.Take(period).Sum();
            decimal prevEma = sum / period;
            ema.Add(prevEma);

            // Calculate EMA for the rest
            for (int i = period; i < prices.Count; i++)
            {
                decimal currentPrice = prices[i];
                decimal currentEma = ((currentPrice - prevEma) * multiplier) + prevEma;
                ema.Add(currentEma);
                prevEma = currentEma;
            }
            return ema;
        }

        // New: Calculate MACD, signal line, and histogram
        // Default parameters: shortPeriod=12, longPeriod=26, signalPeriod=9
        public static (List<decimal> MacdLine, List<decimal> SignalLine, List<decimal> Histogram) CalculateMacd(
            List<decimal> prices, int shortPeriod = 12, int longPeriod = 26, int signalPeriod = 9)
        {
            // Calculate short-term and long-term EMAs.
            var shortEma = CalculateEma(prices, shortPeriod);
            var longEma = CalculateEma(prices, longPeriod);

            // Align arrays: the first available MACD value corresponds to index longPeriod - 1 in prices.
            int offset = longPeriod - shortPeriod;
            var macdLine = new List<decimal>();
            for (int i = 0; i < longEma.Count; i++)
            {
                macdLine.Add(shortEma[i + offset] - longEma[i]);
            }

            // Calculate the signal line as the EMA of the MACD line.
            var signalLine = CalculateEma(macdLine, signalPeriod);
            var histogram = new List<decimal>();
            int signalOffset = signalPeriod - 1;
            for (int i = 0; i < signalLine.Count; i++)
            {
                histogram.Add(macdLine[i + signalOffset] - signalLine[i]);
            }

            return (macdLine, signalLine, histogram);
        }
    }
}
