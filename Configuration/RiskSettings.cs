using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EthTrader.Configuration
{
    public class RiskSettings
    {
        public decimal Tier1 { get; set; }
        public decimal Tier2 { get; set; }
        public decimal Tier3 { get; set; }
        public decimal Tier4 { get; set; }
        public decimal Tier5 { get; set; }
        public decimal TierAbove { get; set; }
    }
}
