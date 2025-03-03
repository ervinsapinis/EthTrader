using System;
using System.IO;
using EthTrader.Configuration;
using Microsoft.Extensions.Configuration;

namespace KrakenTelegramBot
{
    public static class Config
    {
        public static BotSettings BotSettings { get; }
        public static RiskSettings RiskSettings { get; }

        static Config()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            var configuration = builder.Build();

            BotSettings = configuration.GetSection("BotSettings").Get<BotSettings>();
            RiskSettings = configuration.GetSection("RiskSettings").Get<RiskSettings>();
        }
    }
}
