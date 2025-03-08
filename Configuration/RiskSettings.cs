using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EthTrader.Configuration
{
    public class RiskSettings
    {
        // Default risk percentages for different account sizes
        public decimal Tier1 { get; set; } = 0.20m;    // 20% risk if equity is less than 150 EUR
        public decimal Tier2 { get; set; } = 0.15m;    // 15% for 150-350 EUR
        public decimal Tier3 { get; set; } = 0.10m;    // 10% for 350-500 EUR
        public decimal Tier4 { get; set; } = 0.05m;    // 5% for 500-800 EUR
        public decimal Tier5 { get; set; } = 0.03m;    // 3% for 800-1500 EUR
        public decimal TierAbove { get; set; } = 0.02m; // 2% for larger accounts
    }
}
