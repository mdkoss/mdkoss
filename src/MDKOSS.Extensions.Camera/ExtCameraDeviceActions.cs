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
        return action.ToLowerInvariant() switch
        {
            "open" => camera.Open()
                ? DeviceActionResult.Ok(new { isOpen = camera.IsOpen })
                : DeviceActionResult.Fail("open_failed"),
            "close" => camera.Close()
                ? DeviceActionResult.Ok(new { isOpen = camera.IsOpen })
                : DeviceActionResult.Fail("close_failed"),
            "trigger" or "capture" => Trigger(camera, parameters),
            "result" or "status" => Status(camera),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    private static DeviceActionResult Trigger(
        ExtCameraDevice camera,
        Dictionary<string, JsonElement>? parameters)
    {
        var recipe = "default";
        if (parameters is not null
            && parameters.TryGetValue("recipe", out var recipeEl)
            && recipeEl.ValueKind == JsonValueKind.String)
        {
            recipe = recipeEl.GetString() ?? "default";
        }

        var result = camera.TriggerCapture(recipe);
        return result is null
            ? DeviceActionResult.Fail("camera_not_open")
            : DeviceActionResult.Ok(result);
    }

    private static DeviceActionResult Status(ExtCameraDevice camera)
    {
        return DeviceActionResult.Ok(new
        {
            camera.Id,
            isOpen = camera.IsOpen,
            backend = camera.Parameters.Backend,
            deviceIndex = camera.Parameters.DeviceIndex,
            width = camera.Parameters.Width,
            height = camera.Parameters.Height,
            exposureMs = camera.Parameters.ExposureMs,
            captureCount = camera.CaptureCount,
            lastResult = camera.LastResult
        });
    }
}
