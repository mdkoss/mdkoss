using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Sample.SampleExt;

/// <summary>
/// Publishes a Sample run screenshot summary to a DingTalk custom robot webhook.
/// Webhook URL comes from the request or <c>MDK_DINGTALK_WEBHOOK</c>.
/// </summary>
public static class SampleDingTalkPublisher
{
    public const string WebhookEnvVar = "MDK_DINGTALK_WEBHOOK";

    public static string? ResolveWebhook(string? explicitWebhook)
    {
        if (!string.IsNullOrWhiteSpace(explicitWebhook))
        {
            return explicitWebhook.Trim();
        }

        var env = Environment.GetEnvironmentVariable(WebhookEnvVar);
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    public static async Task<PublishResult> PublishAsync(
        HttpClient http,
        string webhookUrl,
        RuntimeSnapshot snapshot,
        byte[] pngBytes,
        string? imageUploadUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookUrl);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pngBytes);

        string? imageUrl = null;
        if (!string.IsNullOrWhiteSpace(imageUploadUrl))
        {
            imageUrl = await TryUploadPngAsync(http, imageUploadUrl.Trim(), pngBytes, cancellationToken)
                .ConfigureAwait(false);
        }

        var markdown = SampleRunScreenshot.BuildMarkdown(snapshot, imageUrl);
        // Custom robots often require a security keyword; keep it at the very start of content.
        var keyword = Environment.GetEnvironmentVariable("MDK_DINGTALK_KEYWORD");
        if (string.IsNullOrWhiteSpace(keyword))
        {
            keyword = "[g]";
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        // Prefer text msgtype (same as scripts/send_dingding.py) for keyword matching reliability.
        var payload = JsonSerializer.Serialize(new
        {
            msgtype = "text",
            text = new
            {
                content = $"[{stamp}] {keyword} {markdown}",
            },
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(webhookUrl, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new PublishResult(false, $"http_{(int)response.StatusCode}", body, imageUrl, markdown);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var err = doc.RootElement.TryGetProperty("errcode", out var code) ? code.GetInt32() : -1;
            if (err != 0)
            {
                var msg = doc.RootElement.TryGetProperty("errmsg", out var em) ? em.GetString() : body;
                return new PublishResult(false, msg ?? "dingtalk_error", body, imageUrl, markdown);
            }
        }
        catch (JsonException)
        {
            return new PublishResult(false, "invalid_dingtalk_response", body, imageUrl, markdown);
        }

        return new PublishResult(true, null, body, imageUrl, markdown);
    }

    private static async Task<string?> TryUploadPngAsync(
        HttpClient http,
        string uploadUrl,
        byte[] pngBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(pngBytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "sample-run.png");
            using var response = await http.PostAsync(uploadUrl, form, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var text = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public sealed record PublishResult(
        bool Success,
        string? Error,
        string? ResponseBody,
        string? ImageUrl,
        string Markdown);
}
