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
        /// Determines the risk percentage based on current equity.
        /// </summary>
        /// <param name="equity">Current account equity in EUR.</param>
        /// <returns>Risk percentage as a decimal.</returns>
        public static decimal GetRiskPercentage(decimal equity)
        {
            if (equity < 150)
                return 0.10m;   // 10% risk if equity is less than 150 EUR.
            else if (equity < 350)
                return 0.075m;  // 7.5%
            else if (equity < 500)
                return 0.05m;   // 5%
            else if (equity < 800)
                return 0.025m;  // 2.5%
            else if (equity < 1500)
                return 0.015m;  // 1.5%
            else
                return 0.01m;   // 1% for larger accounts.
        }
    }
}
