using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EthTrader.Configuration;
using Kraken.Net.Objects.Models;

namespace KrakenTelegramBot.Utils
{
    public class OptimizationResult
    {
        public decimal RsiOversoldThreshold { get; set; }
        public decimal RsiOverboughtThreshold { get; set; }
        public int RsiPeriod { get; set; }
        public int SmaPeriod { get; set; }
        public decimal StopLossPercentage { get; set; }
        public decimal ProfitTarget { get; set; }
        public decimal TotalReturn { get; set; }
        public decimal MaxDrawdown { get; set; }
        public int TotalTrades { get; set; }
        public decimal WinRate { get; set; }
        
        public override string ToString()
        {
            return $"Optimization Results:\n" +
                   $"RSI Period: {RsiPeriod}\n" +
                   $"RSI Oversold: {RsiOversoldThreshold}\n" +
                   $"RSI Overbought: {RsiOverboughtThreshold}\n" +
                   $"SMA Period: {SmaPeriod}\n" +
                   $"Stop Loss: {StopLossPercentage:P2}\n" +
                   $"Profit Target: {ProfitTarget:P2}\n" +
                   $"Total Return: {TotalReturn:P2}\n" +
                   $"Win Rate: {WinRate:P2}\n" +
                   $"Total Trades: {TotalTrades}\n" +
                   $"Max Drawdown: {MaxDrawdown:P2}";
        }
    }
    
    public class ParameterOptimizer
    {
        private readonly List<KrakenKline> _historicalData;
        private readonly decimal _initialCapital;
        private readonly RiskSettings _riskSettings;
        
        public ParameterOptimizer(List<KrakenKline> historicalData, decimal initialCapital, RiskSettings riskSettings)
        {
            _historicalData = historicalData;
            _initialCapital = initialCapital;
            _riskSettings = riskSettings;
        }
        
        public async Task<OptimizationResult> OptimizeParametersAsync()
        {
            Console.WriteLine("Starting parameter optimization...");
            
            var bestResult = new OptimizationResult();
            decimal bestReturn = -1;
            
            // Define parameter ranges to test
            var rsiPeriods = new[] { 7, 9, 14, 21 };
            var rsiOversoldThresholds = new[] { 30m, 35m, 40m, 45m, 50m };
            var rsiOverboughtThresholds = new[] { 65m, 70m, 75m, 80m };
            var smaPeriods = new[] { 20, 50, 100, 200 };
            var stopLossPercentages = new[] { 0.03m, 0.05m, 0.07m, 0.10m };
            var profitTargets = new[] { 0.05m, 0.10m, 0.15m, 0.20m };
            
            int totalCombinations = rsiPeriods.Length * rsiOversoldThresholds.Length * 
                                   rsiOverboughtThresholds.Length * smaPeriods.Length * 
                                   stopLossPercentages.Length * profitTargets.Length;
            
            Console.WriteLine($"Testing {totalCombinations} parameter combinations...");
            int combinationsTested = 0;
            
            foreach (var rsiPeriod in rsiPeriods)
            {
                foreach (var rsiOversold in rsiOversoldThresholds)
                {
                    foreach (var rsiOverbought in rsiOverboughtThresholds)
                    {
                        foreach (var smaPeriod in smaPeriods)
                        {
                            foreach (var stopLoss in stopLossPercentages)
                            {
                                foreach (var profitTarget in profitTargets)
                                {
                                    // Create settings with current parameters
                                    var botSettings = new BotSettings
                                    {
                                        TradingPair = "ETH/EUR",
                                        KlineCount = 50,
                                        RsiPeriod = rsiPeriod,
                                        DefaultOversoldThreshold = rsiOversold,
                                        DowntrendOversoldThreshold = rsiOversold + 5, // Slightly higher for downtrends
                                        SmaPeriod = smaPeriod,
                                        StopLossPercentage = stopLoss,
                                        FinalProfitTarget = profitTarget,
                                        FirstProfitTarget = profitTarget * 0.33m,
                                        SecondProfitTarget = profitTarget * 0.66m,
                                        VolumeAvgPeriod = 20,
                                        MinVolumeMultiplier = 1.2m,
                                        AtrPeriod = 14,
                                        MaxVolatilityRisk = 0.15m,
                                        FirstSellPercentage = 0.3m,
                                        SecondSellPercentage = 0.4m,
                                        TrailingStopActivationProfit = profitTarget * 0.5m,
                                        TrailingStopPercentage = stopLoss
                                    };
                                    
                                    // Run backtest with these parameters
                                    var engine = new BacktestEngine(botSettings, _riskSettings, _initialCapital);
                                    var result = await engine.RunBacktestAsync(_historicalData);
                                    
                                    combinationsTested++;
                                    if (combinationsTested % 100 == 0 || combinationsTested == totalCombinations)
                                    {
                                        Console.WriteLine($"Progress: {combinationsTested}/{totalCombinations} combinations tested ({(decimal)combinationsTested/totalCombinations:P0})");
                                    }
                                    
                                    // Check if this is the best result so far
                                    // We prioritize return, but also consider drawdown and number of trades
                                    decimal score = result.TotalReturn - (result.MaxDrawdown * 0.5m);
                                    
                                    if (score > bestReturn && result.TotalTrades >= 5)
                                    {
                                        bestReturn = score;
                                        bestResult = new OptimizationResult
                                        {
                                            RsiPeriod = rsiPeriod,
                                            RsiOversoldThreshold = rsiOversold,
                                            RsiOverboughtThreshold = rsiOverbought,
                                            SmaPeriod = smaPeriod,
                                            StopLossPercentage = stopLoss,
                                            ProfitTarget = profitTarget,
                                            TotalReturn = result.TotalReturn,
                                            MaxDrawdown = result.MaxDrawdown,
                                            TotalTrades = result.TotalTrades,
                                            WinRate = result.WinRate
                                        };
                                        
                                        Console.WriteLine($"New best parameters found: Return: {result.TotalReturn:P2}, Drawdown: {result.MaxDrawdown:P2}, Trades: {result.TotalTrades}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            Console.WriteLine("Parameter optimization completed!");
            Console.WriteLine(bestResult.ToString());
            
            return bestResult;
        }
    }
}
