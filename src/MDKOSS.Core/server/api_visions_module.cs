using System.Net;
using System.Text.Json;
using MDKOSS.Core.Vision;

namespace MDKOSS.Core.Monitor;

/// <summary>Handles /api/visions — list pipelines, backends, and set the active vision id.</summary>
public sealed class VisionsApiModule : MonitoringApiModule
{
    public VisionsApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/visions";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var path = remainingPath.Trim('/');
        var method = context.Request.HttpMethod ?? "GET";
        var isGet = method.Equals("GET", StringComparison.OrdinalIgnoreCase);
        var isPost = method.Equals("POST", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(path) && isGet)
        {
            await WriteListAsync(context.Response, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (path.Equals("backends", StringComparison.OrdinalIgnoreCase) && isGet)
        {
            await WriteBackendsAsync(context.Response, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (path.Equals("apply", StringComparison.OrdinalIgnoreCase) && isPost)
        {
            await HandleApplyAsync(context, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!string.IsNullOrEmpty(path) && isGet)
        {
            await HandleGetByIdAsync(context.Response, path, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private Task WriteListAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var snap = Runtime.GetSnapshot();
        var visions = Runtime.Setting.Visions.Select(Summarize).ToList();
        var devices = snap.Devices
            .Where(kv => IsVisionDevice(kv.Value.Type))
            .Select(kv => new
            {
                id = kv.Key,
                name = kv.Value.Name,
                type = kv.Value.Type,
                state = kv.Value.State,
            })
            .ToList();

        return WriteJsonAsync(response, new
        {
            success = true,
            activeVisionId = Runtime.Setting.ActiveVisionId ?? "",
            defaultAlgorithm = VisionAlgorithmRegistry.DefaultId,
            backends = ListBackends(),
            visions,
            devices,
            timestampUtc = DateTime.UtcNow,
        }, cancellationToken);
    }

    private Task WriteBackendsAsync(HttpListenerResponse response, CancellationToken cancellationToken) =>
        WriteJsonAsync(response, new
        {
            success = true,
            defaultAlgorithm = VisionAlgorithmRegistry.DefaultId,
            backends = ListBackends(),
            timestampUtc = DateTime.UtcNow,
        }, cancellationToken);

    private async Task HandleGetByIdAsync(
        HttpListenerResponse response,
        string id,
        CancellationToken cancellationToken)
    {
        var vision = FindVision(id);
        if (vision is null)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorAsync(response, "vision_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(response, new
        {
            success = true,
            vision = ToDto(vision),
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleApplyAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var id = GetQueryValue(context.Request.Url?.Query, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var req = Deserialize<ApplyRequest>(body);
                id = req?.Id;
            }
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            await WriteErrorAsync(context.Response, "vision_id_required", cancellationToken).ConfigureAwait(false);
            return;
        }

        var vision = FindVision(id);
        if (vision is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorAsync(context.Response, "vision_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        Runtime.Setting.ActiveVisionId = vision.Id;
        Runtime.Vars.Set("vision.activeId", vision.Id);
        Runtime.Vars.Set("vision.activeName", vision.Name ?? vision.Id);
        await WriteJsonAsync(context.Response, new
        {
            success = true,
            activeVisionId = vision.Id,
            activeVisionName = vision.Name ?? vision.Id,
        }, cancellationToken).ConfigureAwait(false);
    }

    private MdkSetting.VisionConfig? FindVision(string id) =>
        Runtime.Setting.Visions.FirstOrDefault(v =>
            string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    private static object Summarize(MdkSetting.VisionConfig v)
    {
        var pipeline = v.EffectivePipeline;
        var algorithm = string.IsNullOrWhiteSpace(pipeline.Algorithm)
            ? VisionAlgorithmRegistry.DefaultId
            : pipeline.Algorithm.Trim();
        var backend = VisionAlgorithmRegistry.Resolve(algorithm);
        return new
        {
            id = v.Id,
            name = v.Name,
            description = v.Description,
            cameraDeviceId = v.CameraDeviceId,
            algorithm,
            algorithmAvailable = backend.IsAvailable,
            nodeCount = pipeline.Nodes.Count,
        };
    }

    private static object ToDto(MdkSetting.VisionConfig v)
    {
        var pipeline = v.EffectivePipeline;
        var algorithm = string.IsNullOrWhiteSpace(pipeline.Algorithm)
            ? VisionAlgorithmRegistry.DefaultId
            : pipeline.Algorithm.Trim();
        return new
        {
            id = v.Id,
            name = v.Name,
            description = v.Description,
            cameraDeviceId = v.CameraDeviceId,
            algorithm,
            algorithmAvailable = VisionAlgorithmRegistry.Resolve(algorithm).IsAvailable,
            pipeline,
        };
    }

    private static IReadOnlyList<object> ListBackends() =>
        VisionAlgorithmRegistry.List()
            .Select(b => (object)new
            {
                id = b.Id,
                displayName = b.DisplayName,
                available = b.IsAvailable,
            })
            .ToList();

    private static bool IsVisionDevice(string? type)
    {
        var t = (type ?? "").Trim().ToLowerInvariant();
        return t is "visiondev" or "vision";
    }

    private Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", json, cancellationToken);
    }

    private sealed class ApplyRequest
    {
        public string? Id { get; set; }
    }
}
