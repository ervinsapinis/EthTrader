using System;
using System.Collections.Generic;

namespace KrakenTelegramBot.Utils
{
    public static class IndicatorUtils
    {
        // Standard RSI calculation
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
    }
}
