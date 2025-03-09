using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EthTrader.Configuration;
using Kraken.Net.Objects.Models;

namespace KrakenTelegramBot.Utils
{
    public class BacktestResult
    {
        public decimal InitialCapital { get; set; }
        public decimal FinalCapital { get; set; }
        public decimal TotalReturn { get; set; }
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public decimal WinRate => TotalTrades > 0 ? (decimal)WinningTrades / TotalTrades : 0;
        public decimal MaxDrawdown { get; set; }
        public List<TradeRecord> Trades { get; set; } = new List<TradeRecord>();
        
        public override string ToString()
        {
            return $"Backtest Results:\n" +
                   $"Initial Capital: {InitialCapital:F2} EUR\n" +
                   $"Final Capital: {FinalCapital:F2} EUR\n" +
                   $"Total Return: {TotalReturn:P2}\n" +
                   $"Total Trades: {TotalTrades}\n" +
                   $"Win Rate: {WinRate:P2}\n" +
                   $"Max Drawdown: {MaxDrawdown:P2}";
        }
    }
    
    public class BacktestEngine
    {
        private readonly BotSettings _botSettings;
        private readonly RiskSettings _riskSettings;
        private decimal _capital;
        private decimal _initialCapital;
        private decimal _peak;
        private decimal _maxDrawdown;
        private decimal _ethPosition;
        private decimal? _entryPrice;
        private List<TradeRecord> _trades = new List<TradeRecord>();
        
        public BacktestEngine(BotSettings botSettings, RiskSettings riskSettings, decimal initialCapital)
        {
            _botSettings = botSettings;
            _riskSettings = riskSettings;
            _capital = initialCapital;
            _initialCapital = initialCapital;
            _peak = initialCapital;
            _maxDrawdown = 0;
            _ethPosition = 0;
        }

        public Task<BacktestResult> RunBacktestAsync(List<KrakenKline> klines)
        {
            // Reset state
            _capital = _initialCapital;
            _peak = _initialCapital;
            _maxDrawdown = 0;
            _ethPosition = 0;
            _entryPrice = null;
            _trades.Clear();

            // Process each candle
            for (int i = _botSettings.KlineCount; i < klines.Count; i++)
            {
                // Get data window for analysis
                var window = klines.Skip(i - _botSettings.KlineCount).Take(_botSettings.KlineCount).ToList();

                // Check for buy signal
                if (_ethPosition == 0)
                {
                    var buySignal = CheckBuySignal(window);
                    if (buySignal)
                    {
                        ExecuteBuy(window.Last(), i);
                    }
                }
                // Check for sell signal
                else if (_ethPosition > 0 && _entryPrice.HasValue)
                {
                    var sellSignal = CheckSellSignal(window, _entryPrice.Value);
                    if (sellSignal)
                    {
                        ExecuteSell(window.Last(), i, "FinalExit");
                    }

                    // Check stop loss
                    var currentPrice = window.Last().ClosePrice;
                    var stopPrice = _entryPrice.Value * (1 - _botSettings.StopLossPercentage);
                    if (currentPrice <= stopPrice)
                    {
                        ExecuteSell(window.Last(), i, "StopLoss");
                    }
                }

                // Update max drawdown
                UpdateDrawdown();
            }

            // Close any remaining position at the end
            if (_ethPosition > 0 && klines.Count != 0)
            {
                ExecuteSell(klines.Last(), klines.Count - 1, "FinalExit");
            }

            // Calculate results
            var result = new BacktestResult
            {
                InitialCapital = _initialCapital,
                FinalCapital = _capital,
                TotalReturn = (_capital / _initialCapital) - 1,
                TotalTrades = _trades.Count(t => t.Type == "Entry"),
                WinningTrades = _trades.Count(t => t.Type == "FinalExit" && t.ProfitPercentage > 0),
                LosingTrades = _trades.Count(t => t.Type == "FinalExit" && t.ProfitPercentage <= 0) +
                               _trades.Count(t => t.Type == "StopLoss"),
                MaxDrawdown = _maxDrawdown,
                Trades = _trades
            };

            // Since there are no async operations in this method, we can simply return the result wrapped in a completed task
            return Task.FromResult(result);
        }
        private bool CheckBuySignal(List<KrakenKline> window)
        {
            // Extract data
            var closes = window.Select(k => k.ClosePrice).ToList();
            var volumes = window.Select(k => k.Volume).ToList();
            var highs = window.Select(k => k.HighPrice).ToList();
            var lows = window.Select(k => k.LowPrice).ToList();
            
            // Calculate indicators
            var rsiValues = IndicatorUtils.CalculateRsi(closes, _botSettings.RsiPeriod);
            if (rsiValues.Count == 0) return false;
            
            var latestRsi = rsiValues.Last();
            var sma = IndicatorUtils.CalculateSma(closes, _botSettings.SmaPeriod);
            var macdResult = IndicatorUtils.CalculateMacd(closes);
            var latestHistogram = macdResult.Histogram.Last();
            
            // Check trend
            var currentPrice = closes.Last();
            var isDowntrend = currentPrice < sma;
            var adaptiveThreshold = isDowntrend ? 
                _botSettings.DowntrendOversoldThreshold : 
                _botSettings.DefaultOversoldThreshold;
            
            // Check volume
            var volumeMA = IndicatorUtils.CalculateVolumeMA(volumes, _botSettings.VolumeAvgPeriod);
            bool volumeConfirmation = false;
            if (volumeMA.Count != 0)
            {
                var currentVolume = volumes.Last();
                var avgVolume = volumeMA.Last();
                volumeConfirmation = currentVolume >= avgVolume * _botSettings.MinVolumeMultiplier;
            }
            
            // Return buy signal
            return latestRsi < adaptiveThreshold && latestHistogram > 0 && volumeConfirmation;
        }
        
        private bool CheckSellSignal(List<KrakenKline> window, decimal entryPrice)
        {
            // Extract data
            var closes = window.Select(k => k.ClosePrice).ToList();
            
            // Calculate indicators
            var rsiValues = IndicatorUtils.CalculateRsi(closes, _botSettings.RsiPeriod);
            if (rsiValues.Count == 0) return false;
            
            var latestRsi = rsiValues.Last();
            var sma = IndicatorUtils.CalculateSma(closes, _botSettings.SmaPeriod);
            var macdResult = IndicatorUtils.CalculateMacd(closes);
            var latestHistogram = macdResult.Histogram.Last();
            
            // Calculate profit
            var currentPrice = closes.Last();
            var currentProfit = (currentPrice - entryPrice) / entryPrice;
            
            // Check sell conditions
            if (latestRsi > 70) return true;
            if (currentProfit >= _botSettings.FinalProfitTarget) return true;
            if (latestHistogram < 0 && 
                macdResult.Histogram.Skip(macdResult.Histogram.Count - 2).First() > 0 && 
                currentProfit > 0.05m) return true;
            if (currentPrice < sma && currentProfit > 0.08m) return true;
            
            return false;
        }

        private void ExecuteBuy(KrakenKline kline, int index)
        {
            var currentPrice = kline.ClosePrice;

            // Get risk percentage based on account size
            var riskPercentage = GetRiskPercentageFromSettings(_capital);

            // Calculate position size
            var quantityToBuy = RiskManagementUtils.CalculatePositionSize(
                _capital, currentPrice, riskPercentage, _botSettings.StopLossPercentage);

            // Check if we have enough capital
            var orderValue = quantityToBuy * currentPrice;
            if (orderValue > _capital)
            {
                quantityToBuy = (_capital * 0.995m) / currentPrice;
                orderValue = quantityToBuy * currentPrice;
            }

            // Execute trade
            _capital -= orderValue;
            _ethPosition = quantityToBuy;
            _entryPrice = currentPrice;

            // Log trade
            _trades.Add(new TradeRecord
            {
                OrderIds = new[] { $"BT-{index}" },  // Changed from OrderId to OrderIds with an array
                Timestamp = kline.OpenTime,
                Symbol = _botSettings.TradingPair,
                Quantity = quantityToBuy,
                Price = currentPrice,
                Side = "Buy",
                Type = "Entry",
                RemainingPosition = quantityToBuy
            });
        }

        private void ExecuteSell(KrakenKline kline, int index, string exitType)
        {
            var currentPrice = kline.ClosePrice;

            // Calculate profit
            var profitPercentage = _entryPrice.HasValue ?
                (currentPrice - _entryPrice.Value) / _entryPrice.Value : 0;

            // Execute trade
            var orderValue = _ethPosition * currentPrice;
            _capital += orderValue;

            // Log trade
            _trades.Add(new TradeRecord
            {
                OrderIds = new[] { $"BT-{index}" },  // Changed from OrderId to OrderIds with an array
                Timestamp = kline.OpenTime,
                Symbol = _botSettings.TradingPair,
                Quantity = _ethPosition,
                Price = currentPrice,
                Side = "Sell",
                Type = exitType,
                RemainingPosition = 0,
                ProfitPercentage = profitPercentage
            });

            // Reset position
            _ethPosition = 0;
            _entryPrice = null;
        }
        private void UpdateDrawdown()
        {
            // Calculate current equity
            var totalEquity = _capital;
            if (_ethPosition > 0 && _entryPrice.HasValue)
            {
                // Add the current value of ETH position
                var lastTrade = _trades.LastOrDefault();
                if (lastTrade != null)
                {
                    totalEquity += _ethPosition * lastTrade.Price;
                }
            }
            
            // Update peak
            if (totalEquity > _peak)
            {
                _peak = totalEquity;
            }
            
            // Calculate drawdown
            if (_peak > 0)
            {
                var currentDrawdown = 1 - (totalEquity / _peak);
                if (currentDrawdown > _maxDrawdown)
                {
                    _maxDrawdown = currentDrawdown;
                }
            }
        }
        
        private decimal GetRiskPercentageFromSettings(decimal equity)
        {
            if (equity < 150)
                return _riskSettings.Tier1;
            else if (equity < 350)
                return _riskSettings.Tier2;
            else if (equity < 500)
                return _riskSettings.Tier3;
            else if (equity < 800)
                return _riskSettings.Tier4;
            else if (equity < 1500)
                return _riskSettings.Tier5;
            else
                return _riskSettings.TierAbove;
        }
    }
}
