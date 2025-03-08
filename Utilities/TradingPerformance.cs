using System;
using System.Collections.Generic;

namespace KrakenTelegramBot.Utils
{
    public class OpenPosition
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Size { get; set; }
        public decimal EntryPrice { get; set; }
    }
    
    public class TradingPerformance
    {
        public int TotalTrades { get; set; }
        public decimal WinRate { get; set; }
        public decimal AverageProfit { get; set; }
        public decimal MaxProfit { get; set; }
        public decimal MaxLoss { get; set; }
        public List<OpenPosition> OpenPositions { get; set; } = new List<OpenPosition>();
        
        public override string ToString()
        {
            return $"Performance Summary:\n" +
                   $"Total Trades: {TotalTrades}\n" +
                   $"Win Rate: {WinRate:P2}\n" +
                   $"Average Profit: {AverageProfit:P2}\n" +
                   $"Max Profit: {MaxProfit:P2}\n" +
                   $"Max Loss: {MaxLoss:P2}\n" +
                   $"Open Positions: {OpenPositions.Count}";
        }
    }
}
