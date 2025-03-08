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
            while (true)
            {
                await strategyService.ExecuteStrategyAsync();
                // Wait for a specified interval before checking again (e.g., 1 hour)
                await Task.Delay(TimeSpan.FromMinutes(30));
            }

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}
