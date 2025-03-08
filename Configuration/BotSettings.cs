using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EthTrader.Configuration
{
    public class BotSettings
    {
        public required string TradingPair { get; set; }
        public int KlineCount { get; set; }
        public int RsiPeriod { get; set; }
        public decimal DefaultOversoldThreshold { get; set; }
        public decimal DowntrendOversoldThreshold { get; set; }
        public decimal FixedEurInvestment { get; set; }
        public int SmaPeriod { get; set; }
        public decimal StopLossPercentage { get; set; }
        
        // Volume settings
        public int VolumeAvgPeriod { get; set; } = 20;
        public decimal MinVolumeMultiplier { get; set; } = 1.2m;
        
        // Volatility settings
        public int AtrPeriod { get; set; } = 14;
        public decimal MaxVolatilityRisk { get; set; } = 0.15m;
        
        // Profit taking settings
        public decimal FirstProfitTarget { get; set; } = 0.05m; // 5%
        public decimal SecondProfitTarget { get; set; } = 0.10m; // 10%
        public decimal FinalProfitTarget { get; set; } = 0.15m; // 15%
        public decimal FirstSellPercentage { get; set; } = 0.3m; // 30% of position
        public decimal SecondSellPercentage { get; set; } = 0.4m; // 40% of position
        
        // Trailing stop settings
        public decimal TrailingStopActivationProfit { get; set; } = 0.05m; // 5%
        public decimal TrailingStopPercentage { get; set; } = 0.03m; // 3%
    }
}
