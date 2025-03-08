using System;
using Kraken.Net.Enums;

namespace EthTrader.Models
{
    public class KrakenOpenOrder
    {
        public string Id { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public OrderType Type { get; set; }
        public OrderSide Side { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public DateTime CreateTime { get; set; }
    }
}
