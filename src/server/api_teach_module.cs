using System.Net;
using System.Text.Json;
using MDKOSS.Core.Data;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles /api/teach — platform teach point files (点位).</summary>
public sealed class TeachApiModule : MonitoringApiModule
{
    public TeachApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/teach";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var actionPath = remainingPath.Trim('/').ToLowerInvariant();
        var method = context.Request.HttpMethod ?? "GET";

        if (actionPath == "files" && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var platformId = GetQueryValue(context.Request.Url?.Query ?? string.Empty, "platformId");
            var files = Runtime.DataStore.ListTeachFiles(platformId);
            var json = JsonSerializer.Serialize(files, SnapshotJsonOptions);
            await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        if (actionPath == "point")
        {
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
                var req = Deserialize<TeachPointRequest>(body);
                if (req is null || string.IsNullOrWhiteSpace(req.PlatformId) || string.IsNullOrWhiteSpace(req.PointId))
                {
                    await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                var ok = Runtime.DataStore.TryUpsertTeachPoint(
                    req.PlatformId,
                    req.FileName ?? "default",
                    req.PointId,
                    req.Name ?? string.Empty,
                    req.Axes ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                    req.Kind,
                    out var error);
                await WriteMutationResultAsync(context.Response, ok, error, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            {
                var platformId = GetQueryValue(context.Request.Url?.Query ?? string.Empty, "platformId");
                var fileName = GetQueryValue(context.Request.Url?.Query ?? string.Empty, "file") ?? "default";
                var pointId = GetQueryValue(context.Request.Url?.Query ?? string.Empty, "pointId");
                if (string.IsNullOrWhiteSpace(platformId) || string.IsNullOrWhiteSpace(pointId))
                {
                    await WriteErrorAsync(context.Response, "platform_or_point_id_required", cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }

                var ok = Runtime.DataStore.TryDeleteTeachPoint(platformId, fileName, pointId, out var error);
                await WriteMutationResultAsync(context.Response, ok, error, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        if (string.IsNullOrEmpty(actionPath) || actionPath == "/")
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                var platformId = GetQueryValue(context.Request.Url?.Query ?? string.Empty, "platformId");
                if (string.IsNullOrWhiteSpace(platformId))
                {
                    await WriteErrorAsync(context.Response, "platform_id_required", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                var fileName = GetQueryValue(context.Request.Url?.Query ?? string.Empty, "file") ?? "default";
                var snapshot = Runtime.DataStore.GetTeachFileSnapshot(platformId, fileName);
                if (snapshot is null)
                {
                    var empty = new TeachPointFileSnapshot
                    {
                        PlatformId = platformId.Trim(),
                        FileName = fileName,
                        Points = [],
                    };
                    var emptyJson = JsonSerializer.Serialize(empty, SnapshotJsonOptions);
                    await WriteResponseAsync(context.Response, "application/json; charset=utf-8", emptyJson, cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }

                var json = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
                await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
                var snapshot = Deserialize<TeachPointFileSnapshot>(body);
                if (snapshot is null)
                {
                    await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
                    return true;
                }

                var ok = Runtime.DataStore.TrySaveTeachFileSnapshot(snapshot, out var error);
                await WriteMutationResultAsync(context.Response, ok, error, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        return false;
    }

    private sealed class TeachPointRequest
    {
        public string? PlatformId { get; set; }
        public string? FileName { get; set; }
        public string? Kind { get; set; }
        public string? PointId { get; set; }
        public string? Name { get; set; }
        public Dictionary<string, double>? Axes { get; set; }
    }

    private static string? GetQueryValue(string query, string name)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        var q = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = eq >= 0 ? part[..eq] : part;
            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return eq >= 0 ? Uri.UnescapeDataString(part[(eq + 1)..]) : string.Empty;
        }

        return null;
    }

    private static Task WriteMutationResultAsync(
        HttpListenerResponse response,
        bool success,
        string? error,
        CancellationToken cancellationToken)
    {
        response.StatusCode = success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        var payload = JsonSerializer.Serialize(new { success, error }, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }
}
