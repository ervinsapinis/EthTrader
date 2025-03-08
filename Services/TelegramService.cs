using System;
using System.Threading.Tasks;
using Telegram.Bot;

namespace EthTrader.Services
{
    public class TelegramService
    {
        private readonly TelegramBotClient _botClient;
        private readonly long _chatId;

        public TelegramService()
        {
            var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            var chatIdStr = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

            if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatIdStr) || !long.TryParse(chatIdStr, out _chatId))
            {
                throw new Exception("Telegram credentials are not properly set.");
            }

            _botClient = new TelegramBotClient(botToken);
        }

        public async Task SendNotificationAsync(string message)
        {
            try
            {
                var sentMessage = await _botClient.SendTextMessageAsync(_chatId, message);
                Console.WriteLine($"Telegram message sent: {sentMessage.Text}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending Telegram message: " + ex.Message);
            }
        }
    }
}
