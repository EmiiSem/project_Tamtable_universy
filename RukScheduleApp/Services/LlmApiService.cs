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

            var projectId = await OpenAiConfigReader.GetProjectIdAsync();
            var chatUrl = await OpenAiConfigReader.GetChatCompletionsUrlAsync();

            var contextData = "Данные о расписании в ближайшие 7 дней отсутствуют.";
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

            var systemPrompt = $"""
            Ты умный ассистент расписания университета.
            Используй следующие данные для ответа:
            {contextData}

            Правила:
            1. Если пользователь спрашивает про расписание, а данных нет (список пуст), отвечай вежливо, что расписание на ближайшие дни отсутствует.
            2. Если данные есть, отвечай подробно по структуре:
            - ФИО преподавателя
            - Предмет
            - Аудитория
            - Время пары
            3. Отвечай вежливо и на русском языке.
            """;

            var requestBody = new
            {
                modelUri = $"gpt://{projectId}/yandexgpt-lite/latest",
                completionOptions = new
                {
                    stream = false,
                    temperature = 0.3,
                    maxTokens = 500
                },
                messages = new object[]
                {
                    new { role = "system", text = systemPrompt },
                    new { role = "user", text = userQuestion }
                }
            };

            using var httpContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Api-Key {apiKey}");
            _httpClient.DefaultRequestHeaders.Add("x-folder-id", projectId);

            using var request = new HttpRequestMessage(HttpMethod.Post, chatUrl)
            {
                Content = httpContent
            };

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                return $"Не удалось связаться с Yandex Cloud API: {ex.Message}";
            }

            var responseString = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return $"Ошибка AI API: {response.StatusCode}. {TrimForUi(responseString)}";
            }

            try
            {
                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
                var alternative = root.GetProperty("result").GetProperty("alternatives")[0];
                var text = alternative.GetProperty("message").GetProperty("text").GetString();
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