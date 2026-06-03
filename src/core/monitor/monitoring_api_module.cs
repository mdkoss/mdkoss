using System.Net;
using System.Text;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Base class for monitoring API modules. Each module handles a route prefix
/// (e.g. "/api/serial") and receives dispatched requests from <see cref="MonitoringServer"/>.
/// Follows the same abstract-base + sealed-concrete pattern as MTaskBase / MDeviceBase.
/// </summary>
public abstract class MonitoringApiModule
{
    /// <summary>CamelCase, indented serialization (for API responses).</summary>
    protected static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>CamelCase, case-insensitive deserialization (for request bodies).</summary>
    protected static readonly JsonSerializerOptions IoWriteJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    protected MonitoringApiModule(MdkRuntime runtime)
    {
        Runtime = runtime;
    }

    /// <summary>
    /// Route prefix this module handles (e.g. "/api/serial").
    /// Must be lowercase, no trailing slash.
    /// </summary>
    public abstract string RoutePrefix { get; }

    /// <summary>
    /// Handle a request that matched this module's <see cref="RoutePrefix"/>.
    /// </summary>
    /// <param name="context">The HTTP listener context.</param>
    /// <param name="remainingPath">
    ///   The path segment after <see cref="RoutePrefix"/>, with leading '/' preserved.
    ///   For "/api/serial/open" with prefix "/api/serial", this is "/open".
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the request was handled; <c>false</c> to fall through.</returns>
    public abstract Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken);

    /// <summary>Access to the runtime for device/driver/state queries.</summary>
    protected MdkRuntime Runtime { get; }

    // --- Shared response helpers ---

    protected static async Task WriteResponseAsync(
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

    protected static Task WriteErrorAsync(
        HttpListenerResponse response,
        string error,
        CancellationToken cancellationToken)
    {
        response.StatusCode = (int)HttpStatusCode.BadRequest;
        var payload = JsonSerializer.Serialize(new { success = false, error }, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    protected static Task WriteSuccessAsync(
        HttpListenerResponse response,
        string action,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { success = true, action }, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", payload, cancellationToken);
    }

    protected static async Task<string> ReadBodyAsync(
        HttpListenerRequest request,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    protected static T? Deserialize<T>(
        string json,
        JsonSerializerOptions? options = null)
        where T : class
    {
        return JsonSerializer.Deserialize<T>(json, options ?? IoWriteJsonOptions);
    }
}
