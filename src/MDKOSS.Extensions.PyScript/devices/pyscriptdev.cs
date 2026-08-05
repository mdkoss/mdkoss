using System.Diagnostics;
using System.Text;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Extensions.PyScript;

/// <summary>Result of a Python script execution.</summary>
public sealed record PyScriptRunResult(
    string ScriptPath,
    string Arguments,
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    bool Killed,
    long DurationMs,
    bool Ok);

/// <summary>
/// Python script device (config type <c>devpyscript</c>).
/// Spawns an external Python process, captures stdout/stderr, and publishes results to vars.
/// </summary>
public sealed class PyScriptDevice : MDeviceBase
{
    private readonly object _sync = new();
    private Process? _process;
    private PyScriptRunResult? _lastResult;
    private int _runCount;
    private string? _lastError;
    private bool _killRequested;

    public PyScriptDevice(string id, string name, PyScriptDeviceParameters parameters, MVarStore vars)
        : base(id, name, MDeviceType.Generic, new PyScriptLogicalDriver(), vars)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        PublishStatusVars();
    }

    public PyScriptDeviceParameters Parameters { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public PyScriptRunResult? LastResult
    {
        get { lock (_sync) return _lastResult; }
    }

    public int RunCount
    {
        get { lock (_sync) return _runCount; }
    }

    public string? LastError
    {
        get { lock (_sync) return _lastError; }
    }

    /// <summary>
    /// Runs the configured (or overridden) Python script synchronously.
    /// Returns null and sets <see cref="LastError"/> on validation / spawn failure.
    /// </summary>
    public PyScriptRunResult? Run(string? scriptPath = null, string? arguments = null, int? timeoutMs = null)
    {
        Process process;
        string script;
        string args;
        int timeout;
        bool capture;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        lock (_sync)
        {
            if (_process is { HasExited: false })
            {
                _lastError = "already_running";
                PublishStatusVarsUnlocked();
                return null;
            }

            script = string.IsNullOrWhiteSpace(scriptPath) ? Parameters.ScriptPath : scriptPath.Trim();
            if (string.IsNullOrWhiteSpace(script))
            {
                _lastError = "missing_script_path";
                PublishStatusVarsUnlocked();
                return null;
            }

            script = ResolvePath(script);
            if (!File.Exists(script))
            {
                _lastError = "script_not_found";
                PublishStatusVarsUnlocked();
                return null;
            }

            args = arguments ?? Parameters.Arguments;
            timeout = timeoutMs ?? Parameters.TimeoutMs;
            if (timeout < 0)
            {
                timeout = 0;
            }

            var workDir = string.IsNullOrWhiteSpace(Parameters.WorkingDirectory)
                ? (Path.GetDirectoryName(script) ?? AppContext.BaseDirectory)
                : ResolvePath(Parameters.WorkingDirectory);

            var python = string.IsNullOrWhiteSpace(Parameters.PythonPath) ? "python" : Parameters.PythonPath.Trim();
            capture = Parameters.CaptureOutput;

            var psi = new ProcessStartInfo
            {
                FileName = python,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = capture,
                RedirectStandardError = capture,
                RedirectStandardInput = false,
                StandardOutputEncoding = capture ? Encoding.UTF8 : null,
                StandardErrorEncoding = capture ? Encoding.UTF8 : null,
            };

            // -u: unbuffered stdout/stderr so capture is timely.
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(script);
            foreach (var token in SplitArguments(args))
            {
                psi.ArgumentList.Add(token);
            }

            try
            {
                process = new Process { StartInfo = psi, EnableRaisingEvents = false };
                if (!process.Start())
                {
                    _lastError = "start_failed";
                    PublishStatusVarsUnlocked();
                    return null;
                }
            }
            catch (Exception ex)
            {
                _lastError = $"start_failed:{ex.GetType().Name}";
                PublishStatusVarsUnlocked();
                return null;
            }

            _process = process;
            _killRequested = false;
            _lastError = null;
            State = MDeviceState.Running;
            PublishStatusVarsUnlocked();

            if (capture)
            {
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        lock (stdout) stdout.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        lock (stderr) stderr.AppendLine(e.Data);
                    }
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
        }

        // Wait outside the lock so Kill() can interrupt.
        var sw = Stopwatch.StartNew();
        var timedOut = false;
        try
        {
            if (timeout > 0)
            {
                if (!process.WaitForExit(timeout))
                {
                    timedOut = true;
                    TryKillProcess(process);
                    process.WaitForExit(5_000);
                }
            }
            else
            {
                process.WaitForExit();
            }
        }
        finally
        {
            sw.Stop();
            if (capture)
            {
                try { process.CancelOutputRead(); } catch { /* ignore */ }
                try { process.CancelErrorRead(); } catch { /* ignore */ }
            }
        }

        string outText;
        string errText;
        lock (stdout) outText = stdout.ToString().TrimEnd();
        lock (stderr) errText = stderr.ToString().TrimEnd();

        lock (_sync)
        {
            var killed = _killRequested || timedOut;
            var exitCode = timedOut ? -1 : (process.HasExited ? process.ExitCode : -1);
            var result = new PyScriptRunResult(
                ScriptPath: script,
                Arguments: args ?? string.Empty,
                ExitCode: exitCode,
                StdOut: outText,
                StdErr: errText,
                TimedOut: timedOut,
                Killed: killed,
                DurationMs: sw.ElapsedMilliseconds,
                Ok: !timedOut && !_killRequested && exitCode == 0);

            _lastResult = result;
            _runCount++;
            _lastError = result.Ok
                ? null
                : timedOut ? "timeout" : _killRequested ? "killed" : $"exit_{exitCode}";
            State = result.Ok ? MDeviceState.Initialized : (_killRequested ? MDeviceState.Stopped : MDeviceState.Fault);
            PublishResultVarsUnlocked(result);
            PublishStatusVarsUnlocked();

            try { process.Dispose(); } catch { /* ignore */ }
            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }

            _killRequested = false;
            return result;
        }
    }

    /// <summary>Kills the currently running Python process, if any.</summary>
    public bool Kill()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            if (process is null || process.HasExited)
            {
                return false;
            }

            _killRequested = true;
            _lastError = "killed";
            State = MDeviceState.Stopped;
            PublishStatusVarsUnlocked();
        }

        TryKillProcess(process);
        return true;
    }

    /// <summary>Updates runtime parameters (does not interrupt a running process).</summary>
    public void SetParameters(PyScriptDeviceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (_sync)
        {
            Parameters = parameters;
            PublishStatusVarsUnlocked();
        }
    }

    public override void Start()
    {
        // Do not call EnsureConnected — process lifecycle is managed by Run/Kill.
        State = MDeviceState.Initialized;
        WriteState("initialized");
        PublishStatusVars();
    }

    public override void Stop()
    {
        Kill();
        base.Stop();
    }

    public override void Dispose()
    {
        Kill();
        base.Dispose();
    }

    public override DeviceSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new DeviceSnapshot(
                Id,
                Name,
                "devpyscript",
                State.ToString(),
                $"python:{Parameters.PythonPath}",
                IsRunning);
        }
    }

    private void PublishStatusVars()
    {
        lock (_sync)
        {
            PublishStatusVarsUnlocked();
        }
    }

    private void PublishStatusVarsUnlocked()
    {
        Vars.Set(BuildVarKey("pythonPath"), Parameters.PythonPath);
        Vars.Set(BuildVarKey("scriptPath"), Parameters.ScriptPath);
        Vars.Set(BuildVarKey("workingDirectory"), Parameters.WorkingDirectory);
        Vars.Set(BuildVarKey("arguments"), Parameters.Arguments);
        Vars.Set(BuildVarKey("timeoutMs"), Parameters.TimeoutMs);
        Vars.Set(BuildVarKey("isRunning"), _process is { HasExited: false });
        Vars.Set(BuildVarKey("runCount"), _runCount);
        Vars.Set(BuildVarKey("lastError"), _lastError ?? string.Empty);
        WriteState(State.ToString().ToLowerInvariant());
    }

    private void PublishResultVarsUnlocked(PyScriptRunResult result)
    {
        Vars.Set(BuildVarKey("lastScriptPath"), result.ScriptPath);
        Vars.Set(BuildVarKey("lastExitCode"), result.ExitCode);
        Vars.Set(BuildVarKey("lastStdOut"), result.StdOut);
        Vars.Set(BuildVarKey("lastStdErr"), result.StdErr);
        Vars.Set(BuildVarKey("lastTimedOut"), result.TimedOut);
        Vars.Set(BuildVarKey("lastDurationMs"), result.DurationMs);
        Vars.Set(BuildVarKey("lastOk"), result.Ok);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    /// <summary>Splits a command-line argument string, respecting simple double quotes.</summary>
    internal static IEnumerable<string> SplitArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            yield break;
        }

        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}

/// <summary>Minimal IDriver stub — script I/O lives on the device, not a motion card.</summary>
internal sealed class PyScriptLogicalDriver : IDriver
{
    public string Name => "PYSCRIPT";

    public bool IsConnected => true;

    public void Initialize(MdkSetting.DriverConfig config) { }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        return false;
    }

    public bool Write(string address, object? value) => false;

    public bool TryReadDi(short diType, out int value)
    {
        value = 0;
        return false;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        return false;
    }

    public bool WriteDo(short doType, int value) => false;

    public bool WriteDoBit(short doType, short doIndex, bool value) => false;

    public bool EnableAxis(short axis) => false;

    public bool DisableAxis(short axis) => false;

    public bool IsAxisEnabled(short axis) => false;

    public bool TryGetAxisStatus(short axis, out int status)
    {
        status = 0;
        return false;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        return false;
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        return false;
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        return false;
    }

    public bool SetAxisPosition(short axis, double position) => false;

    public bool SetAxisVelocity(short axis, double velocity) => false;

    public bool SetAxisAcceleration(short axis, double acceleration) => false;

    public bool SetAxisDeceleration(short axis, double deceleration) => false;

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
        => false;

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration) => false;

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
        => false;

    public bool Stop(int axisMask, int option = 0) => false;

    public void Dispose() { }
}
