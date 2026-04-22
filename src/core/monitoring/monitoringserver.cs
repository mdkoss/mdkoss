using System.Net;
using System.Text;
using System.Text.Json;

namespace MDKOSS.Core.Monitoring;

public sealed class MonitoringServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly MdkRuntime _runtime;
    private readonly string _prefix;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public MonitoringServer(MdkRuntime runtime, string prefix = "http://localhost:5080/")
    {
        _runtime = runtime;
        _prefix = prefix;
        _listener.Prefixes.Add(prefix);
    }

    public string Prefix => _prefix;

    public void Start()
    {
        if (_cts is not null)
        {
            throw new InvalidOperationException("Monitoring server has already started.");
        }

        _cts = new CancellationTokenSource();
        _listener.Start();
        _loopTask = ListenLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        _listener.Stop();
        if (_loopTask is not null)
        {
            await _loopTask.ConfigureAwait(false);
        }

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException)
            {
                // Listener is likely stopped.
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var path = context.Request.Url?.AbsolutePath?.TrimEnd('/') ?? "/";
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
        {
            var json = JsonSerializer.Serialize(_runtime.GetSnapshot(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await WriteResponseAsync(context.Response, "application/json; charset=utf-8", json, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        

        if (path.Equals("/", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(context.Response, "text/html; charset=utf-8", MonitoringPage.Html, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        await WriteResponseAsync(context.Response, "text/plain; charset=utf-8", "Not Found", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(
        HttpListenerResponse response,
        string contentType,
        string body,
        CancellationToken cancellationToken)
    {
        response.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private static Task WriteTaskOperationResultAsync(
        HttpListenerResponse response,
        bool success,
        string action,
        CancellationToken cancellationToken)
    {
        response.StatusCode = success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        var payload = JsonSerializer.Serialize(new
        {
            success,
            action,
            timestampUtc = DateTime.UtcNow
        });
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    public void Dispose()
    {
        _listener.Close();
        _cts?.Dispose();
    }
}
