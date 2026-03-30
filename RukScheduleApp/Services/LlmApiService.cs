using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RukScheduleApp.Models;

namespace RukScheduleApp.Services
{
    public class LlmApiService
    {
        private readonly HttpClient _httpClient;

        public LlmApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> GetAnswerAsync(string userQuestion, List<ScheduleItem>? scheduleContext)
        {
            var apiKey = await OpenAiConfigReader.GetApiKeyAsync();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return "Укажите ключ OpenAI в файле Resources/Raw/openai_config.json (поле OpenAI.ApiKey), пересоберите приложение. Дополнительно можно задать переменную OPENAI_API_KEY.";
            }

            var chatUrl = await OpenAiConfigReader.GetChatCompletionsUrlAsync();

            var contextData = "Нет данных о расписании.";
            if (scheduleContext != null && scheduleContext.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Текущее загруженное расписание:");
                foreach (var item in scheduleContext)
                {
                    sb.AppendLine($"- Преподаватель: {item.Teacher}");
                    sb.AppendLine($"  Предмет: {item.Subject}");
                    sb.AppendLine($"  Аудитория: {item.Room}");
                    sb.AppendLine($"  Время: {item.Time}");
                    sb.AppendLine($"  Дата: {item.Date:dd.MM.yyyy}");
                    sb.AppendLine("-------------------");
                }
                contextData = sb.ToString();
            }
            else
            {
                contextData = "Расписание пустое. Пар нет.";
            }

            var systemPrompt = $"""
Ты умный ассистент расписания университета.
Используй следующие данные для ответа:
{contextData}

Правила:
1. Если пользователь спрашивает про расписание, а данных нет (список пуст), отвечай строго: 'Пар у преподавателя нету, выберите другую дату'.
2. Если данные есть, отвечай подробно по структуре:
   - ФИО преподавателя
   - Предмет
   - Аудитория
   - Время пары
3. Отвечай вежливо и на русском языке.
""";

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userQuestion }
                }
            };

            using var httpContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, chatUrl)
            {
                Content = httpContent
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                return $"Не удалось связаться с OpenAI: {ex.Message}";
            }

            var responseString = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (responseString.Contains("unsupported_country", StringComparison.OrdinalIgnoreCase))
                    return "OpenAI недоступен из вашего региона. Включите VPN и перезапустите приложение; при ошибках SSL попробуйте другой узел VPN. Альтернатива: в openai_config.json укажите OpenAI-совместимый прокси в поле BaseUrl (и свой ключ у этого провайдера).";
                if (responseString.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase))
                    return "Квота OpenAI исчерпана: проверьте баланс и тариф на platform.openai.com.";
                return $"Ошибка AI API: {response.StatusCode}. {TrimForUi(responseString)}";
            }

            try
            {
                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
                var choice = root.GetProperty("choices")[0];
                var text = choice.GetProperty("message").GetProperty("content").GetString();
                return string.IsNullOrWhiteSpace(text) ? "(Пустой ответ модели)" : text.Trim();
            }
            catch
            {
                return $"Ответ API в неожиданном формате: {TrimForUi(responseString)}";
            }
        }

        private static string TrimForUi(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Length <= 400 ? s : s[..400] + "…";
        }
    }
}
