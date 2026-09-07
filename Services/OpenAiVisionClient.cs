using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Madoyaku.Models;

namespace Madoyaku.Services;

public sealed class OpenAiVisionClient
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://api.openai.com/"),
        Timeout = TimeSpan.FromSeconds(90)
    };

    public async Task<TranslationHistoryItem> TranslateAsync(
        byte[] image,
        AppSettings settings,
        IReadOnlyList<TranslationHistoryItem> history,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (TestMode.IsEnabled)
        {
            await Task.Delay(350, cancellationToken);
            return new TranslationHistoryItem(TestMode.SourceText, TestMode.TranslationText);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI APIキーがありません。設定画面で入力するか、OPENAI_API_KEY環境変数を設定してください。");
        }

        var historyText = history.Count == 0
            ? "（まだありません）"
            : string.Join("\n", history.Select((item, index) =>
                $"{index + 1}. 原文: {item.SourceText}\n   訳文: {item.Translation}"));

        var userText = $$"""
            翻訳先: {{settings.TargetLanguage}}

            ゲーム・人物・用語の補足情報:
            {{(string.IsNullOrWhiteSpace(settings.GameContext) ? "（指定なし）" : settings.GameContext)}}

            次の翻訳に使う過去の翻訳:
            {{historyText}}

            添付画像の透明枠内に表示されていた、翻訳対象の台詞または文章を読み取ってください。
            次のJSONだけを返してください。Markdownのコードフェンスは付けないでください。
            {"source_text":"読み取った原文","translation":"文脈を反映した翻訳"}
            """;

        var dataUrl = $"data:image/png;base64,{Convert.ToBase64String(image)}";
        var payload = new
        {
            model = settings.Model,
            store = false,
            instructions = settings.BasePrompt,
            max_output_tokens = 800,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = userText },
                        new { type = "input_image", image_url = dataUrl, detail = "high" }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(CreateFriendlyApiError((int)response.StatusCode, body));
        }

        var outputText = ExtractOutputText(body);
        return ParseTranslation(outputText);
    }

    private static string ExtractOutputText(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("output", out var output))
        {
            throw new InvalidOperationException("API応答に翻訳結果がありませんでした。");
        }

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) &&
                    type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text))
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
            }
        }

        var result = string.Join("", parts).Trim();
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException("API応答の翻訳テキストが空でした。");
        }

        return result;
    }

    private static TranslationHistoryItem ParseTranslation(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        try
        {
            using var document = JsonDocument.Parse(cleaned);
            var root = document.RootElement;
            var source = root.TryGetProperty("source_text", out var sourceElement)
                ? sourceElement.GetString() ?? string.Empty
                : string.Empty;
            var translation = root.TryGetProperty("translation", out var translationElement)
                ? translationElement.GetString() ?? string.Empty
                : cleaned;
            return new TranslationHistoryItem(source, translation);
        }
        catch (JsonException)
        {
            return new TranslationHistoryItem(string.Empty, cleaned);
        }
    }

    private static string CreateFriendlyApiError(int statusCode, string body)
    {
        var message = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var messageElement))
            {
                message = messageElement.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // The HTTP status still gives the user a useful error if the body was not JSON.
        }

        var prefix = statusCode switch
        {
            401 => "APIキーが認証されませんでした。",
            429 => "APIの利用上限またはリクエスト制限に達しました。",
            _ => $"OpenAI APIエラー ({statusCode})。"
        };

        return string.IsNullOrWhiteSpace(message) ? prefix : $"{prefix}\n{message}";
    }
}
