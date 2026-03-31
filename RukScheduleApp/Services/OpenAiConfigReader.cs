using System.Text.Json;

namespace RukScheduleApp.Services;

/// <summary>
/// Ключ и опционально BaseUrl читаются из Resources/Raw/openai_config.json.
/// BaseUrl — для OpenAI-совместимого прокси (если прямой api.openai.com недоступен).
/// Дополнительно: OPENAI_API_KEY, OPENAI_BASE_URL.
/// </summary>
public static class OpenAiConfigReader
{
    private const string AssetFileName = "openai_config.json";
    private const string ExampleAssetFileName = "openai_config.example.json";
    private const string DefaultBaseUrl = "https://llm.api.cloud.yandex.net/foundationModels/v1/chatCompletion";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _initialized;
    private static string? _apiKey;
    private static string? _baseUrl;
    private static string? _projectId;

    public static async Task<string?> GetApiKeyAsync()
    {
        await EnsureInitializedAsync();
        return NormalizeKey(_apiKey);
    }

    public static async Task<string?> GetProjectIdAsync()
    {
        await EnsureInitializedAsync();
        return _projectId;
    }

    /// <summary>Полный URL вызова chat/completions.</summary>
    public static async Task<string> GetChatCompletionsUrlAsync()
    {
        await EnsureInitializedAsync();
        var b = string.IsNullOrWhiteSpace(_baseUrl) ? DefaultBaseUrl : _baseUrl!;
        return b;
    }

    private static async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await Gate.WaitAsync();
        try
        {
            if (_initialized)
                return;

            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync(AssetFileName);
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("OpenAI", out var openAi))
                {
                    if (openAi.TryGetProperty("ApiKey", out var keyEl))
                        _apiKey = keyEl.GetString();
                    if (openAi.TryGetProperty("BaseUrl", out var baseEl))
                        _baseUrl = baseEl.GetString();
                    if (openAi.TryGetProperty("ProjectId", out var projectEl))
                        _projectId = projectEl.GetString();
                }
            }
            catch (FileNotFoundException)
            {
                _apiKey = null;
                _projectId = null;
            }

            // Если реального конфигурационного файла нет или ключ пустой — пробуем example.
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                try
                {
                    await using var stream = await FileSystem.OpenAppPackageFileAsync(ExampleAssetFileName);
                    using var reader = new StreamReader(stream);
                    var json = await reader.ReadToEndAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("OpenAI", out var openAi))
                    {
                        if (openAi.TryGetProperty("ApiKey", out var keyEl))
                            _apiKey = keyEl.GetString();
                        if (openAi.TryGetProperty("BaseUrl", out var baseEl))
                            _baseUrl = baseEl.GetString();
                        if (openAi.TryGetProperty("ProjectId", out var projectEl))
                            _projectId = projectEl.GetString();
                    }
                }
                catch (FileNotFoundException)
                {
                    _apiKey = null;
                }
            }

            if (string.IsNullOrWhiteSpace(_apiKey))
                _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            if (string.IsNullOrWhiteSpace(_baseUrl))
                _baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");

            if (string.IsNullOrWhiteSpace(_projectId))
                _projectId = Environment.GetEnvironmentVariable("OPENAI_PROJECT_ID") ?? Environment.GetEnvironmentVariable("YC_FOLDER_ID");

            _initialized = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        return key.Trim();
    }
}