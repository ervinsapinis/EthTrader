using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace KrakenTelegramBot.Utils
{
    public class TradeRecord
    {
        public string? OrderId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Symbol { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public string? Side { get; set; } // "Buy" or "Sell"
        public string? Type { get; set; } // "Entry", "PartialExit", "StopLoss", "FinalExit"
        public decimal? RemainingPosition { get; set; }
        public decimal? ProfitPercentage { get; set; } // Track profit for each trade
    }

    public static class TradeTracking
    {
        private static readonly string TradeLogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "trade_history.json");
            
        public static async Task LogTradeAsync(TradeRecord trade)
        {
            var trades = await GetTradeHistoryAsync();
            trades.Add(trade);
            await SaveTradeHistoryAsync(trades);
        }
        
        public static async Task<List<TradeRecord>> GetTradeHistoryAsync()
        {
            if (!File.Exists(TradeLogPath))
                return new List<TradeRecord>();
                
            try
            {
                string json = await File.ReadAllTextAsync(TradeLogPath);
                return JsonSerializer.Deserialize<List<TradeRecord>>(json) ?? new List<TradeRecord>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading trade history: {ex.Message}");
                return new List<TradeRecord>();
            }
        }
        
        private static async Task SaveTradeHistoryAsync(List<TradeRecord> trades)
        {
            try
            {
                string json = JsonSerializer.Serialize(trades, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(TradeLogPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving trade history: {ex.Message}");
            }
        }
        
        public static async Task<decimal?> GetEstimatedEntryPriceAsync(string symbol)
        {
            var trades = await GetTradeHistoryAsync();
            
            // Filter to only buy trades for this symbol
            var buyTrades = trades.FindAll(t => 
                t.Symbol == symbol && 
                t.Side == "Buy" && 
                t.Type == "Entry");
                
            if (buyTrades.Count == 0)
                return null;
                
            // Get the most recent buy trade
            var latestBuy = buyTrades.OrderByDescending(t => t.Timestamp).First();
            return latestBuy.Price;
        }
        
        public static async Task<decimal> GetCurrentPositionSizeAsync(string symbol)
        {
            var trades = await GetTradeHistoryAsync();
            
            // Get the most recent trade with RemainingPosition info
            var positionTrades = trades.FindAll(t => 
                t.Symbol == symbol && 
                t.RemainingPosition.HasValue);
                
            if (positionTrades.Count == 0)
                return 0;
                
            var latestPositionTrade = positionTrades.OrderByDescending(t => t.Timestamp).First();
            return latestPositionTrade.RemainingPosition ?? 0;
        }

        /// <summary>
        /// Gets the performance metrics for the trading strategy
        /// </summary>
        public static async Task<TradingPerformance> GetPerformanceMetricsAsync()
        {
            var trades = await GetTradeHistoryAsync();
            var performance = new TradingPerformance();
            
            if (trades.Count == 0)
                return performance;
                
            // Calculate win rate, average profit, etc.
            var completedTrades = trades.Where(t => 
                t.Type == "FinalExit" || 
                t.Type == "StopLoss").ToList();
                
            performance.TotalTrades = completedTrades.Count;
            
            if (performance.TotalTrades > 0)
            {
                var winningTrades = completedTrades.Count(t => t.ProfitPercentage.HasValue && t.ProfitPercentage.Value > 0);
                performance.WinRate = (decimal)winningTrades / performance.TotalTrades;
                
                if (completedTrades.Any(t => t.ProfitPercentage.HasValue))
                {
                    performance.AverageProfit = completedTrades
                        .Where(t => t.ProfitPercentage.HasValue)
                        .Average(t => t.ProfitPercentage!.Value);
                        
                    performance.MaxProfit = completedTrades
                        .Where(t => t.ProfitPercentage.HasValue)
                        .Max(t => t.ProfitPercentage!.Value);
                        
                    performance.MaxLoss = completedTrades
                        .Where(t => t.ProfitPercentage.HasValue)
                        .Min(t => t.ProfitPercentage!.Value);
                }
            }
            
            // Calculate current open positions
            var symbols = trades.Select(t => t.Symbol).Distinct();
            foreach (var symbol in symbols)
            {
                if (symbol != null)
                {
                    var position = await GetCurrentPositionSizeAsync(symbol);
                    if (position > 0)
                    {
                        performance.OpenPositions.Add(new OpenPosition
                        {
                            Symbol = symbol,
                            Size = position,
                            EntryPrice = await GetEstimatedEntryPriceAsync(symbol) ?? 0
                        });
                    }
                }
            }
            
            return performance;
        }
    }
}
