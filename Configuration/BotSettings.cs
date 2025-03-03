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
    }
}
