using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace EthTrader.Configuration
{
    public static class ConfigLoader
    {
        private static BotSettings _botSettings;
        private static RiskSettings _riskSettings;

        public static BotSettings BotSettings 
        { 
            get 
            {
                if (_botSettings == null)
                    LoadConfigurations();
                return _botSettings;
            }
        }

        public static RiskSettings RiskSettings
        {
            get
            {
                if (_riskSettings == null)
                    LoadConfigurations();
                return _riskSettings;
            }
        }

        public static void LoadConfigurations()
        {
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false)
                    .Build();

                _botSettings = configuration.GetSection("BotSettings").Get<BotSettings>() 
                    ?? throw new InvalidOperationException("BotSettings section is missing from configuration");
                
                _riskSettings = configuration.GetSection("RiskSettings").Get<RiskSettings>()
                    ?? throw new InvalidOperationException("RiskSettings section is missing from configuration");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                // Use defaults if configuration loading fails
                _botSettings = new BotSettings
                {
                    TradingPair = "ETH/EUR",
                    KlineCount = 50,
                    RsiPeriod = 14,
                    DefaultOversoldThreshold = 50m,
                    DowntrendOversoldThreshold = 40m,
                    FixedEurInvestment = 50m,
                    SmaPeriod = 50,
                    StopLossPercentage = 0.05m
                };
                
                _riskSettings = new RiskSettings
                {
                    Tier1 = 0.20m,
                    Tier2 = 0.15m,
                    Tier3 = 0.10m,
                    Tier4 = 0.05m,
                    Tier5 = 0.03m,
                    TierAbove = 0.02m
                };
            }
        }
    }
}
