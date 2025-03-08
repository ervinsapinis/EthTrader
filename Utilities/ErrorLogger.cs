using System;
using System.IO;
using System.Threading.Tasks;

namespace EthTrader.Utilities
{
    public static class ErrorLogger
    {
        private const string LogFilePath = "errors.txt";

        public static async Task LogErrorAsync(string source, string message, Exception ex = null)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logEntry = $"[{timestamp}] [{source}] {message}";
                
                if (ex != null)
                {
                    logEntry += $"\nException: {ex.GetType().Name}: {ex.Message}";
                    logEntry += $"\nStack Trace: {ex.StackTrace}";
                    
                    if (ex.InnerException != null)
                    {
                        logEntry += $"\nInner Exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
                    }
                }
                
                logEntry += "\n----------------------------------------\n";
                
                await File.AppendAllTextAsync(LogFilePath, logEntry);
                
                // Also output to console for immediate visibility
                Console.WriteLine($"ERROR: {message}");
            }
            catch (Exception logEx)
            {
                // If logging itself fails, at least try to write to console
                Console.WriteLine($"Failed to log error: {logEx.Message}");
                Console.WriteLine($"Original error: {message}");
            }
        }
    }
}
