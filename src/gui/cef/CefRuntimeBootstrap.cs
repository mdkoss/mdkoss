using CefSharp;
using CefSharp.Enums;
using CefSharp.WinForms;

namespace MDKOSS.Gui.CefUi;

internal static class CefRuntimeBootstrap
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

            if (!Cef.Initialize(settings, performDependencyCheck: true, browserProcessHandler: null))
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

        Cef.Shutdown();
        _initialized = false;
    }
}
