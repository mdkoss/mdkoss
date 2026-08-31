using System.Globalization;
using System.Text.Json;
using MDKOSS.Core;

namespace MDKOSS.Extensions.Camera;

/// <summary>Unified action handlers for <see cref="ExtCameraDevice"/>.</summary>
internal static class ExtCameraDeviceActions
{
    internal static DeviceActionResult Execute(
        ExtCameraDevice camera,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.Trim().ToLowerInvariant() switch
        {
            "open" => camera.Open()
                ? DeviceActionResult.Ok(new { isOpen = camera.IsOpen, backend = camera.EffectiveKind.Type })
                : DeviceActionResult.Fail(Reason(camera, "open_failed")),
            "close" => camera.Close()
                ? DeviceActionResult.Ok(new { isOpen = camera.IsOpen })
                : DeviceActionResult.Fail("close_failed"),
            "trigger" or "capture" => Trigger(camera, parameters),
            "result" or "status" => Status(camera),
            "list" or "enum" or "enumerate" => DeviceActionResult.Ok(new { devices = camera.Enumerate() }),
            "catalog" or "backends" => DeviceActionResult.Ok(new { backends = CameraCatalog.All }),
            "startgrab" or "start" => camera.StartGrab()
                ? DeviceActionResult.Ok(new { grabbing = camera.IsGrabbing })
                : DeviceActionResult.Fail(Reason(camera, "start_grab_failed")),
            "stopgrab" or "stop" => StopGrab(camera),
            "param" or "setparam" or "config" => SetParameters(camera, parameters),
            _ => DeviceActionResult.Fail("unknown_action"),
        };
    }

    private static DeviceActionResult Trigger(
        ExtCameraDevice camera,
        Dictionary<string, JsonElement>? parameters)
    {
        var recipe = ReadString(parameters, "recipe") is { Length: > 0 } r ? r : "default";
        var result = camera.TriggerCapture(recipe);
        return result is null
            ? DeviceActionResult.Fail(camera.IsOpen ? Reason(camera, "grab_failed") : "camera_not_open")
            : DeviceActionResult.Ok(result);
    }

    private static DeviceActionResult StopGrab(ExtCameraDevice camera)
    {
        camera.StopGrab();
        return DeviceActionResult.Ok(new { grabbing = camera.IsGrabbing });
    }

    private static DeviceActionResult SetParameters(
        ExtCameraDevice camera,
        Dictionary<string, JsonElement>? parameters)
    {
        if (!camera.IsOpen)
        {
            return DeviceActionResult.Fail("camera_not_open");
        }

        var applied = new List<string>();
        if (ReadDouble(parameters, "exposureUs") is { } exposureUs && camera.SetExposure(exposureUs))
        {
            applied.Add("exposureUs");
        }
        else if (ReadDouble(parameters, "exposureMs") is { } exposureMs && camera.SetExposure(exposureMs * 1000))
        {
            applied.Add("exposureMs");
        }

        if (ReadDouble(parameters, "gain") is { } gain && camera.SetGain(gain))
        {
            applied.Add("gain");
        }

        var trigger = ReadString(parameters, "triggerMode", "trigger");
        if (!string.IsNullOrWhiteSpace(trigger)
            && camera.SetTrigger(ExtCameraDeviceParameters.ParseTrigger(trigger)))
        {
            applied.Add("triggerMode");
        }

        return applied.Count == 0
            ? DeviceActionResult.Fail(Reason(camera, "no_parameter_applied"))
            : DeviceActionResult.Ok(new { applied });
    }

    private static DeviceActionResult Status(ExtCameraDevice camera) =>
        DeviceActionResult.Ok(Snapshot(camera));

    /// <summary>Status payload shared by the action handler and <c>/api/extcamera/status</c>.</summary>
    internal static object Snapshot(ExtCameraDevice camera) => new
    {
        deviceId = camera.Id,
        isOpen = camera.IsOpen,
        grabbing = camera.IsGrabbing,
        backend = camera.Parameters.Backend,
        effectiveBackend = camera.EffectiveKind.Type,
        vendor = camera.EffectiveKind.Vendor,
        nativeDll = camera.EffectiveKind.NativeDll,
        deviceIndex = camera.Parameters.DeviceIndex,
        serialNumber = camera.Parameters.SerialNumber,
        width = camera.Parameters.Width,
        height = camera.Parameters.Height,
        exposureUs = camera.Parameters.ExposureUs,
        exposureMs = camera.Parameters.ExposureMs,
        gain = camera.Parameters.Gain,
        triggerMode = camera.Parameters.TriggerMode.ToString().ToLowerInvariant(),
        captureCount = camera.CaptureCount,
        failCount = camera.FailCount,
        lastError = camera.LastError,
        lastResult = camera.LastResult,
    };

    private static string Reason(ExtCameraDevice camera, string fallback) =>
        string.IsNullOrWhiteSpace(camera.LastError) ? fallback : camera.LastError;

    private static string ReadString(Dictionary<string, JsonElement>? parameters, params string[] keys)
    {
        if (parameters is null)
        {
            return "";
        }

        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString()?.Trim() ?? "";
            }
        }

        return "";
    }

    private static double? ReadDouble(Dictionary<string, JsonElement>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String when double.TryParse(
                el.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null,
        };
    }
}
