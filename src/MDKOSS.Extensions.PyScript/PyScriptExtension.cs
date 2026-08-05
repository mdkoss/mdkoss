using System.Globalization;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Monitor;
using MDKOSS.Extensions;

namespace MDKOSS.Extensions.PyScript;

/// <summary>
/// Python script device extension package (config type <c>devpyscript</c>).
/// Register via <see cref="MdkExtensionHost"/> or <see cref="PyScriptExtensionBootstrap"/>.
/// </summary>
public sealed class PyScriptExtension : IMdkExtension
{
    public string Id => "pyscript";

    public string DisplayName => "Python script device";

    public void Register(IExtensionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Device("devpyscript", (cfg, name, vars, _) =>
        {
            var parameters = PyScriptDeviceParameters.ParseConfig(cfg.Parameters);
            return new PyScriptDevice(cfg.Id, name, parameters, vars);
        });

        registration.Action(
            device => device is PyScriptDevice,
            (device, action, parameters) =>
                PyScriptDeviceActions.Execute((PyScriptDevice)device, action, parameters));

        registration.MonitoringModule(runtime => new PyScriptApiModule(runtime));
    }
}

/// <summary>Call once before creating <see cref="MdkRuntime"/>.</summary>
public static class PyScriptExtensionBootstrap
{
    public static void Register() => MdkExtensionHost.Register(new PyScriptExtension());
}

/// <summary>Unified action handlers for <see cref="PyScriptDevice"/>.</summary>
internal static class PyScriptDeviceActions
{
    internal static DeviceActionResult Execute(
        PyScriptDevice device,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.ToLowerInvariant() switch
        {
            "run" or "execute" => HandleRun(device, parameters),
            "kill" or "cancel" => device.Kill()
                ? DeviceActionResult.Ok(new { killed = true, isRunning = device.IsRunning })
                : DeviceActionResult.Fail("not_running"),
            "status" or "result" => Status(device),
            _ => DeviceActionResult.Fail("unknown_action")
        };
    }

    private static DeviceActionResult HandleRun(
        PyScriptDevice device,
        Dictionary<string, JsonElement>? parameters)
    {
        string? scriptPath = null;
        string? arguments = null;
        int? timeoutMs = null;

        if (parameters is not null)
        {
            if (parameters.TryGetValue("scriptPath", out var scriptEl)
                && scriptEl.ValueKind == JsonValueKind.String)
            {
                scriptPath = scriptEl.GetString();
            }

            if (parameters.TryGetValue("arguments", out var argsEl)
                && argsEl.ValueKind == JsonValueKind.String)
            {
                arguments = argsEl.GetString();
            }

            if (parameters.TryGetValue("timeoutMs", out var timeoutEl))
            {
                timeoutMs = timeoutEl.ValueKind switch
                {
                    JsonValueKind.Number => timeoutEl.TryGetInt32(out var n) ? n : null,
                    JsonValueKind.String when int.TryParse(
                        timeoutEl.GetString(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed) => parsed,
                    _ => null
                };
            }
        }

        var result = device.Run(scriptPath, arguments, timeoutMs);
        // Null = could not start (missing script / already running / spawn error).
        // Non-null always returns the run payload (including non-zero exit / timeout).
        return result is null
            ? DeviceActionResult.Fail(device.LastError ?? "run_failed")
            : DeviceActionResult.Ok(result);
    }

    private static DeviceActionResult Status(PyScriptDevice device)
    {
        return DeviceActionResult.Ok(new
        {
            device.Id,
            isRunning = device.IsRunning,
            pythonPath = device.Parameters.PythonPath,
            scriptPath = device.Parameters.ScriptPath,
            workingDirectory = device.Parameters.WorkingDirectory,
            arguments = device.Parameters.Arguments,
            timeoutMs = device.Parameters.TimeoutMs,
            runCount = device.RunCount,
            lastError = device.LastError,
            lastResult = device.LastResult
        });
    }
}
