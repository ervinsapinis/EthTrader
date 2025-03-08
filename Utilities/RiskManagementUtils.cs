using System;
using System.Linq;

namespace KrakenTelegramBot.Utils
{
    public static class RiskManagementUtils
    {
        /// <summary>
        /// Calculates the position size (number of units to buy) based on risk.
        /// </summary>
        /// <param name="equity">Current account equity in EUR.</param>
        /// <param name="currentPrice">Current asset price in EUR.</param>
        /// <param name="riskPercentage">Fraction of equity to risk (e.g. 0.02 for 2%).</param>
        /// <param name="stopLossPercentage">Stop-loss percentage (e.g. 0.05 for 5%).</param>
        /// <returns>Position size in asset units.</returns>
        public static decimal CalculatePositionSize(decimal equity, decimal currentPrice, decimal riskPercentage, decimal stopLossPercentage)
        {
            // Calculate the absolute amount of capital we are willing to risk.
            decimal riskAmount = equity * riskPercentage;
            // Calculate the price drop (in EUR) corresponding to the stop-loss percentage.
            decimal stopLossDistance = currentPrice * stopLossPercentage;
            // Position size (in asset units) = risk amount divided by the stop-loss distance.
            return riskAmount / stopLossDistance;
        }

        /// <summary>
        /// Calculates the maximum position size based on available equity
        /// </summary>
        /// <param name="availableEquity">Available equity in EUR</param>
        /// <param name="currentPrice">Current asset price in EUR</param>
        /// <returns>Maximum position size in asset units</returns>
        public static decimal CalculateMaxPositionSize(decimal availableEquity, decimal currentPrice)
        {
            // Allow for some buffer (0.5%) for fees
            return (availableEquity * 0.995m) / currentPrice;
        }
    }
}
