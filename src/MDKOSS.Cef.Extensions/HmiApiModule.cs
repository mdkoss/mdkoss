using System.Net;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Monitor;

namespace MDKOSS.Cef.Extensions;

/// <summary>Handles <c>/api/hmi/*</c> — layout catalog and persistence for main-HMI 组态.</summary>
public sealed class HmiApiModule : MonitoringApiModule
{
    public HmiApiModule(MdkRuntime runtime) : base(runtime)
    {
    }

    public override string RoutePrefix => "/api/hmi";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var action = remainingPath.Trim('/');
        var method = context.Request.HttpMethod ?? "GET";
        var isGet = method.Equals("GET", StringComparison.OrdinalIgnoreCase);
        var isPut = method.Equals("PUT", StringComparison.OrdinalIgnoreCase);
        var isPost = method.Equals("POST", StringComparison.OrdinalIgnoreCase);

        if ((action.Length == 0 || action.Equals("layout", StringComparison.OrdinalIgnoreCase)) && isGet)
        {
            var layout = HmiLayoutStore.LoadOrDefault(Runtime);
            await WriteJsonAsync(context.Response, new
            {
                success = true,
                path = HmiLayoutStore.ResolvePath(Runtime),
                layout,
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (action.Equals("layout", StringComparison.OrdinalIgnoreCase) && isPut)
        {
            var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            var layout = HmiLayoutStore.Parse(body);
            if (layout is null)
            {
                await WriteErrorAsync(context.Response, "invalid_layout", cancellationToken).ConfigureAwait(false);
                return true;
            }

            HmiLayoutStore.Save(Runtime, layout);
            await WriteJsonAsync(context.Response, new
            {
                success = true,
                action = "save",
                path = HmiLayoutStore.ResolvePath(Runtime),
                layout,
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (action.Equals("widgets", StringComparison.OrdinalIgnoreCase) && isGet)
        {
            await WriteJsonAsync(context.Response, new
            {
                success = true,
                widgets = HmiWidgetCatalog.All,
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (action.StartsWith("widget/", StringComparison.OrdinalIgnoreCase) && isGet)
        {
            var file = action["widget/".Length..];
            if (HmiWidgetRegistry.TryGetAsset(file, out var contentType, out var body))
            {
                await WriteResponseAsync(context.Response, contentType, body, cancellationToken).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        if (action.Equals("reset", StringComparison.OrdinalIgnoreCase) && isPost)
        {
            var layout = HmiLayout.CreateDefault();
            HmiLayoutStore.Save(Runtime, layout);
            await WriteJsonAsync(context.Response, new
            {
                success = true,
                action = "reset",
                path = HmiLayoutStore.ResolvePath(Runtime),
                layout,
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (action.Equals("default", StringComparison.OrdinalIgnoreCase) && isGet)
        {
            await WriteJsonAsync(context.Response, new
            {
                success = true,
                layout = HmiLayout.CreateDefault(),
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", json, cancellationToken);
    }
}
