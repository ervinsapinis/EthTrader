using KrakenTelegramBot.Services;

namespace KrakenTelegramBot
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting Kraken Trading Bot...");

            // Initialize services
            var krakenService = new KrakenService();
            var telegramService = new TelegramService();
            var strategyService = new StrategyService(krakenService, telegramService);

            // Execute the trading strategy
            await strategyService.ExecuteStrategyAsync();

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}
