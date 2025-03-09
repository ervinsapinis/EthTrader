using Kraken.Net.Enums;

namespace EthTrader.Models
{
    /// <summary>
    /// Simplified model for open orders from Kraken
    /// </summary>
    public class KrakenOpenOrder
    {
        public string Id { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public OrderType Type { get; set; }
        public OrderSide Side { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
    }
}