using CefSharp;
using CefSharp.WinForms;

namespace MDKOSS.Gui.CefUi;

/// <summary>
/// CefSharp initialize / shutdown for desktop hosts that embed <see cref="CefMainForm"/>.
/// </summary>
public static class CefRuntimeBootstrap
{
    private static bool _initialized;

    public static bool TryInitialize(out string? error)
    {
        if (_initialized)
        {
            error = null;
            return true;
        }

        try
        {
            var cachePath = Path.Combine(AppContext.BaseDirectory, "cef_cache");
            Directory.CreateDirectory(cachePath);

            var settings = new CefSettings
            {
                CachePath = cachePath,
                LogSeverity = LogSeverity.Warning,
            };

            if (!global::CefSharp.Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
            {
                error = "Cef.Initialize returned false.";
                return false;
            }

            _initialized = true;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        global::CefSharp.Cef.Shutdown();
        _initialized = false;
    }
}
