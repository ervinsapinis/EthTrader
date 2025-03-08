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
        public string OrderId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Symbol { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public string Side { get; set; } // "Buy" or "Sell"
        public string Type { get; set; } // "Entry", "PartialExit", "StopLoss", "FinalExit"
        public decimal? RemainingPosition { get; set; }
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
            return latestPositionTrade.RemainingPosition.Value;
        }
    }
}
