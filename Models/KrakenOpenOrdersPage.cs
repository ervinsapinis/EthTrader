using System.Collections.Generic;

namespace EthTrader.Models
{
    public class KrakenOpenOrdersPage
    {
        private readonly List<KrakenOpenOrder> _orders = new List<KrakenOpenOrder>();
        
        public IEnumerable<KrakenOpenOrder> Orders => _orders;
        
        public void Add(KrakenOpenOrder order)
        {
            _orders.Add(order);
        }
        
        public int Count => _orders.Count;
        
        public KrakenOpenOrder this[int index] => _orders[index];
    }
}
