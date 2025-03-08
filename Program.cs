using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using KrakenTelegramBot.Services;
using KrakenTelegramBot.Utils;
using EthTrader.Configuration;

namespace KrakenTelegramBot
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("ETH Trading Bot - Starting...");

            try
            {
                // Initialize services
                var krakenService = new KrakenService();
                var telegramService = new TelegramService();
                var strategyService = new StrategyService(krakenService, telegramService);

                // Create command line interface
                var rootCommand = new RootCommand("ETH Trading Bot");
                
                // Add run command
                var runCommand = new Command("run", "Run the trading bot");
                runCommand.SetHandler(async () =>
                {
                    await RunTradingBot(strategyService, telegramService);
                });
                
                // Add backtest command
                var backtestCommand = new Command("backtest", "Run a backtest of the strategy");
                
                var startOption = new Option<DateTime>(
                    "--start",
                    "Start date for backtest (yyyy-MM-dd)");
                startOption.IsRequired = true;
                
                var endOption = new Option<DateTime>(
                    "--end",
                    "End date for backtest (yyyy-MM-dd)");
                endOption.IsRequired = true;
                
                var capitalOption = new Option<decimal>(
                    "--capital",
                    () => 1000m,
                    "Initial capital for backtest in EUR");
                
                backtestCommand.AddOption(startOption);
                backtestCommand.AddOption(endOption);
                backtestCommand.AddOption(capitalOption);
                
                backtestCommand.SetHandler(async (DateTime start, DateTime end, decimal capital) =>
                {
                    await RunBacktest(strategyService, telegramService, start, end, capital);
                }, startOption, endOption, capitalOption);
                
                // Add dashboard command
                var dashboardCommand = new Command("dashboard", "Show trading performance dashboard");
                dashboardCommand.SetHandler(async () =>
                {
                    await ShowDashboard(telegramService);
                });
                
                // Add optimize command
                var optimizeCommand = new Command("optimize", "Optimize strategy parameters using historical data");
                
                var optimizeStartOption = new Option<DateTime>(
                    "--start",
                    "Start date for optimization (yyyy-MM-dd)");
                optimizeStartOption.IsRequired = true;
                
                var optimizeEndOption = new Option<DateTime>(
                    "--end",
                    "End date for optimization (yyyy-MM-dd)");
                optimizeEndOption.IsRequired = true;
                
                var optimizeCapitalOption = new Option<decimal>(
                    "--capital",
                    () => 1000m,
                    "Initial capital for optimization in EUR");
                
                optimizeCommand.AddOption(optimizeStartOption);
                optimizeCommand.AddOption(optimizeEndOption);
                optimizeCommand.AddOption(optimizeCapitalOption);
                
                optimizeCommand.SetHandler(async (DateTime start, DateTime end, decimal capital) =>
                {
                    await OptimizeParameters(strategyService, telegramService, start, end, capital);
                }, optimizeStartOption, optimizeEndOption, optimizeCapitalOption);
                
                // Add commands to root
                rootCommand.AddCommand(runCommand);
                rootCommand.AddCommand(backtestCommand);
                rootCommand.AddCommand(dashboardCommand);
                rootCommand.AddCommand(optimizeCommand);
                
                // If no args, default to run
                if (args.Length == 0)
                {
                    args = new[] { "run" };
                }
                
                // Parse and execute
                return await rootCommand.InvokeAsync(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }
        
        private static async Task RunTradingBot(StrategyService strategyService, TelegramService telegramService)
        {
            Console.WriteLine("Trading bot started. Press Ctrl+C to stop.");
            await telegramService.SendNotificationAsync("Trading bot started");
            
            // Set up cancellation token
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => 
            {
                e.Cancel = true;
                cts.Cancel();
            };
            
            try
            {
                // Execute the trading strategy in a loop
                while (!cts.Token.IsCancellationRequested)
                {
                    await strategyService.ExecuteStrategyAsync(cts.Token);
                    
                    // Wait for a specified interval before checking again
                    await Task.Delay(TimeSpan.FromMinutes(30), cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Trading bot stopped by user.");
                await telegramService.SendNotificationAsync("Trading bot stopped by user");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in trading loop: {ex.Message}");
                await telegramService.SendNotificationAsync($"Trading bot error: {ex.Message}");
            }
        }
        
        private static async Task RunBacktest(
            StrategyService strategyService, 
            TelegramService telegramService,
            DateTime startDate,
            DateTime endDate,
            decimal initialCapital)
        {
            Console.WriteLine($"Running backtest from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} with {initialCapital} EUR");
            await telegramService.SendNotificationAsync(
                $"Starting backtest: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} with {initialCapital} EUR");
            
            try
            {
                var result = await strategyService.RunBacktestAsync(startDate, endDate, initialCapital);
                
                Console.WriteLine("\nBacktest completed successfully!");
                Console.WriteLine(result.ToString());
                
                // Display more detailed statistics
                Console.WriteLine($"\nDetailed Statistics:");
                Console.WriteLine($"Total Trades: {result.TotalTrades}");
                Console.WriteLine($"Winning Trades: {result.WinningTrades}");
                Console.WriteLine($"Losing Trades: {result.LosingTrades}");
                Console.WriteLine($"Win Rate: {result.WinRate:P2}");
                Console.WriteLine($"Initial Capital: {result.InitialCapital:F2} EUR");
                Console.WriteLine($"Final Capital: {result.FinalCapital:F2} EUR");
                Console.WriteLine($"Total Return: {result.TotalReturn:P2}");
                Console.WriteLine($"Max Drawdown: {result.MaxDrawdown:P2}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Backtest failed: {ex.Message}");
                await telegramService.SendNotificationAsync($"Backtest failed: {ex.Message}");
            }
        }
        
        private static async Task ShowDashboard(TelegramService telegramService)
        {
            try
            {
                var performance = await TradeTracking.GetPerformanceMetricsAsync();
                
                Console.WriteLine("\n=== Trading Performance Dashboard ===\n");
                Console.WriteLine(performance.ToString());
                
                if (performance.OpenPositions.Count > 0)
                {
                    Console.WriteLine("\nOpen Positions:");
                    foreach (var position in performance.OpenPositions)
                    {
                        Console.WriteLine($"  {position.Symbol}: {position.Size:F6} @ {position.EntryPrice:F2} EUR");
                    }
                }
                
                await telegramService.SendNotificationAsync(performance.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing dashboard: {ex.Message}");
            }
        }
        
        private static async Task OptimizeParameters(
            StrategyService strategyService,
            TelegramService telegramService,
            DateTime startDate,
            DateTime endDate,
            decimal initialCapital)
        {
            Console.WriteLine($"Starting parameter optimization from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
            await telegramService.SendNotificationAsync(
                $"Starting parameter optimization: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
            
            try
            {
                // Fetch historical data
                var klinesResult = await strategyService.GetHistoricalDataAsync(startDate, endDate);
                
                if (klinesResult == null || !klinesResult.Any())
                {
                    throw new Exception("Failed to fetch historical data for optimization");
                }
                
                // Run optimization
                var optimizer = new ParameterOptimizer(
                    klinesResult, 
                    initialCapital, 
                    ConfigLoader.RiskSettings);
                    
                var result = await optimizer.OptimizeParametersAsync();
                
                // Display results
                Console.WriteLine("\nOptimization completed successfully!");
                Console.WriteLine(result.ToString());
                
                // Send results to Telegram
                await telegramService.SendNotificationAsync(
                    $"Parameter optimization completed!\n\n{result}");
                
                // Suggest applying the optimized parameters
                Console.WriteLine("\nTo apply these optimized parameters, update your appsettings.json file with these values.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Optimization failed: {ex.Message}");
                await telegramService.SendNotificationAsync($"Optimization failed: {ex.Message}");
            }
        }
    }
}
