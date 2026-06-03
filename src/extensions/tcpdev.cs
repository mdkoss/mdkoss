using System.Net.Sockets;
using System.Text;

namespace MDKOSS.Core;

/// <summary>TCP connection parity settings.</summary>
public enum TcpErrorCode
{
    Ok,
    NotConnected,
    AlreadyConnected,
    ConnectionRefused,
    InvalidParameter,
    Timeout,
    IoError,
    OperationFailed
}

/// <summary>
/// TCP connection configuration parameters.
/// </summary>
public sealed class TcpPortConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5000;
    public int ConnectTimeout { get; set; } = 5000;
    public int ReadTimeout { get; set; } = 5000;
    public int WriteTimeout { get; set; } = 5000;
    public Encoding Encoding { get; set; } = Encoding.ASCII;
    public bool NoDelay { get; set; } = false;
    public bool KeepAlive { get; set; } = true;
}

/// <summary>
/// TCP/IP device: wraps network communication with logical device semantics.
/// Commands follow MDK TCP/IP API: OpenNet, CloseNet, ChkNet, SetNet, Print, Input, LineInput, Read, ReadBin, Write, WriteBin.
/// </summary>
public sealed class TcpDevice : MDeviceBase
{
    private TcpPortConfig _config;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly object _lock = new();

    public TcpDevice(string id, string name, TcpPortConfig config, MVarStore vars)
        : base(id, name, MDeviceType.TcpDev, new TcpDriver(), vars)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>Current TCP connection configuration.</summary>
    public TcpPortConfig Config => _config;

    /// <summary>Indicates whether the TCP connection is currently established.</summary>
    public bool IsConnected => _client?.Connected == true;

    /// <summary>Gets the number of bytes available to read from the network stream.</summary>
    public int BytesToRead
    {
        get
        {
            if (_client?.Connected != true || _stream is null)
            {
                return 0;
            }

            try
            {
                return _stream.DataAvailable ? _client.Available : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>Opens TCP connection with current configuration (OpenNet equivalent).</summary>
    public TcpErrorCode Connect()
    {
        lock (_lock)
        {
            if (_client?.Connected == true)
            {
                return TcpErrorCode.AlreadyConnected;
            }

            try
            {
                _client = new TcpClient
                {
                    NoDelay = _config.NoDelay,
                    ReceiveTimeout = _config.ReadTimeout,
                    SendTimeout = _config.WriteTimeout
                };

                var result = _client.BeginConnect(_config.Host, _config.Port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(_config.ConnectTimeout))
                {
                    _client.Close();
                    _client = null;
                    return TcpErrorCode.Timeout;
                }

                _client.EndConnect(result);
                _stream = _client.GetStream();
                _stream.ReadTimeout = _config.ReadTimeout;
                _stream.WriteTimeout = _config.WriteTimeout;

                if (_config.KeepAlive)
                {
                    _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }

                State = MDeviceState.Running;
                WriteState("connected");

                Vars.Set(BuildVarKey("host"), _config.Host);
                Vars.Set(BuildVarKey("port"), _config.Port);
                Vars.Set(BuildVarKey("isConnected"), true);

                return TcpErrorCode.Ok;
            }
            catch (SocketException)
            {
                CleanupConnection();
                return TcpErrorCode.ConnectionRefused;
            }
            catch (TimeoutException)
            {
                CleanupConnection();
                return TcpErrorCode.Timeout;
            }
            catch (Exception)
            {
                CleanupConnection();
                return TcpErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>Closes the TCP connection (CloseNet equivalent).</summary>
    public TcpErrorCode Disconnect()
    {
        lock (_lock)
        {
            if (_client is null || !_client.Connected)
            {
                return TcpErrorCode.NotConnected;
            }

            try
            {
                CleanupConnection();
                State = MDeviceState.Stopped;
                WriteState("disconnected");
                Vars.Set(BuildVarKey("isConnected"), false);
                return TcpErrorCode.Ok;
            }
            catch (Exception)
            {
                return TcpErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>
    /// Checks connection status and returns bytes waiting or error code (ChkNet equivalent).
    /// Returns negative value for error conditions, positive for bytes available.
    /// </summary>
    public int CheckConnection()
    {
        lock (_lock)
        {
            if (_client?.Connected != true || _stream is null)
            {
                Vars.Set(BuildVarKey("lastError"), (int)TcpErrorCode.NotConnected);
                return -(int)TcpErrorCode.NotConnected;
            }

            try
            {
                var bytesToRead = _client.Available;
                Vars.Set(BuildVarKey("bytesToRead"), bytesToRead);
                return bytesToRead;
            }
            catch (Exception)
            {
                var error = -(int)TcpErrorCode.IoError;
                Vars.Set(BuildVarKey("lastError"), error);
                return error;
            }
        }
    }

    /// <summary>
    /// Updates connection parameters at runtime (SetNet equivalent).
    /// </summary>
    public TcpErrorCode SetParameters(TcpPortConfig? config = null)
    {
        lock (_lock)
        {
            var newConfig = config ?? _config;

            try
            {
                var wasConnected = _client?.Connected == true;

                if (wasConnected)
                {
                    Disconnect();
                }

                _config = newConfig;

                if (wasConnected)
                {
                    return Connect();
                }

                Vars.Set(BuildVarKey("host"), _config.Host);
                Vars.Set(BuildVarKey("port"), _config.Port);

                return TcpErrorCode.Ok;
            }
            catch (Exception)
            {
                return TcpErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>
    /// Sends a string to the connection with newline (Print # equivalent).
    /// </summary>
    public TcpErrorCode Print(string data)
    {
        lock (_lock)
        {
            if (!EnsureConnected())
            {
                return TcpErrorCode.NotConnected;
            }

            try
            {
                var bytes = _config.Encoding.GetBytes(data + "\r\n");
                _stream!.Write(bytes, 0, bytes.Length);
                Vars.Set(BuildVarKey("lastWriteLength"), bytes.Length);
                WriteState(State.ToString().ToLowerInvariant());
                return TcpErrorCode.Ok;
            }
            catch (IOException)
            {
                return TcpErrorCode.IoError;
            }
            catch (Exception)
            {
                return TcpErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>
    /// Receives a single character from the connection (Input # equivalent).
    /// </summary>
    public (TcpErrorCode error, char? value) ReadChar()
    {
        lock (_lock)
        {
            if (!EnsureConnected())
            {
                return (TcpErrorCode.NotConnected, null);
            }

            try
            {
                var b = _stream!.ReadByte();
                if (b < 0)
                {
                    return (TcpErrorCode.NotConnected, null);
                }

                var value = _config.Encoding.GetChars(new[] { (byte)b })[0];
                Vars.Set(BuildVarKey("lastReadChar"), value);
                return (TcpErrorCode.Ok, value);
            }
            catch (IOException)
            {
                return (TcpErrorCode.IoError, null);
            }
            catch (Exception)
            {
                return (TcpErrorCode.OperationFailed, null);
            }
        }
    }

    /// <summary>
    /// Receives a line of text from the connection (Line Input # equivalent).
    /// </summary>
    public (TcpErrorCode error, string? value) ReadLine()
    {
        lock (_lock)
        {
            if (!EnsureConnected())
            {
                return (TcpErrorCode.NotConnected, null);
            }

            try
            {
                var buffer = new List<byte>();
                while (true)
                {
                    var b = _stream!.ReadByte();
                    if (b < 0)
                    {
                        break;
                    }

                    buffer.Add((byte)b);
                    if (b == '\n')
                    {
                        break;
                    }
                }

                if (buffer.Count == 0)
                {
                    return (TcpErrorCode.NotConnected, null);
                }

                var value = _config.Encoding.GetString(buffer.ToArray()).TrimEnd('\r', '\n');
                Vars.Set(BuildVarKey("lastReadLine"), value);
                Vars.Set(BuildVarKey("lastReadLength"), value.Length);
                return (TcpErrorCode.Ok, value);
            }
            catch (IOException)
            {
                return (TcpErrorCode.IoError, null);
            }
            catch (Exception)
            {
                return (TcpErrorCode.OperationFailed, null);
            }
        }
    }

    /// <summary>
    /// Reads all available characters from the connection (Read # equivalent).
    /// </summary>
    public (TcpErrorCode error, string? value) ReadAll()
    {
        lock (_lock)
        {
            if (!EnsureConnected())
            {
                return (TcpErrorCode.NotConnected, null);
            }

            try
            {
                if (!_stream!.DataAvailable)
                {
                    return (TcpErrorCode.Ok, "");
                }

                var buffer = new byte[_client!.Available];
                var bytesRead = _stream.Read(buffer, 0, buffer.Length);
                var value = _config.Encoding.GetString(buffer, 0, bytesRead);
                Vars.Set(BuildVarKey("lastReadText"), value);
                Vars.Set(BuildVarKey("lastReadLength"), bytesRead);
                return (TcpErrorCode.Ok, value);
            }
            catch (IOException)
            {
                return (TcpErrorCode.IoError, null);
            }
            catch (Exception)
            {
                return (TcpErrorCode.OperationFailed, null);
            }
        }
    }

    /// <summary>
    /// Reads binary data from the connection (ReadBin # equivalent).
    /// </summary>
    public (TcpErrorCode error, byte[]? value) ReadBinary(int count)
    {
        lock (_lock)
        {
            if (!EnsureConnected())
            {
                return (TcpErrorCode.NotConnected, null);
            }

            if (count <= 0)
            {
                return (TcpErrorCode.InvalidParameter, null);
            }

            try
            {
                var buffer = new byte[count];
                var bytesRead = _stream!.Read(buffer, 0, count);

                if (bytesRead < count)
                {
                    Array.Resize(ref buffer, bytesRead);
                }

                Vars.Set(BuildVarKey("lastReadBytes"), bytesRead);
                return (TcpErrorCode.Ok, buffer);
            }
            catch (IOException)
            {
                return (TcpErrorCode.IoError, null);
            }
            catch (Exception)
            {
                return (TcpErrorCode.OperationFailed, null);
            }
        }
    }

    /// <summary>
    /// Sends a string to the connection (Write # equivalent).
    /// </summary>
    public TcpErrorCode Write(string data)
    {
        lock (_lock)
        {
            if (!EnsureConnected())
            {
                return TcpErrorCode.NotConnected;
            }

            try
            {
                var bytes = _config.Encoding.GetBytes(data);
                _stream!.Write(bytes, 0, bytes.Length);
                Vars.Set(BuildVarKey("lastWriteLength"), bytes.Length);
                WriteState(State.ToString().ToLowerInvariant());
                return TcpErrorCode.Ok;
            }
            catch (IOException)
            {
                return TcpErrorCode.IoError;
            }
            catch (Exception)
            {
                return TcpErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>
    /// Sends binary data to the connection (WriteBin # equivalent).
    /// </summary>
    public TcpErrorCode WriteBinary(byte[] data)
    {
        lock (_lock)
        {
            if (!EnsureConnected())
            {
                return TcpErrorCode.NotConnected;
            }

            if (data is null || data.Length == 0)
            {
                return TcpErrorCode.InvalidParameter;
            }

            try
            {
                _stream!.Write(data, 0, data.Length);
                Vars.Set(BuildVarKey("lastWriteBytes"), data.Length);
                WriteState(State.ToString().ToLowerInvariant());
                return TcpErrorCode.Ok;
            }
            catch (IOException)
            {
                return TcpErrorCode.IoError;
            }
            catch (Exception)
            {
                return TcpErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>Discards buffer contents (clears read buffers).</summary>
    public TcpErrorCode DiscardBuffers()
    {
        lock (_lock)
        {
            if (!EnsureConnected())
            {
                return TcpErrorCode.NotConnected;
            }

            try
            {
                // Drain available data from the stream
                while (_stream!.DataAvailable)
                {
                    var buffer = new byte[4096];
                    _stream.Read(buffer, 0, buffer.Length);
                }

                return TcpErrorCode.Ok;
            }
            catch (Exception)
            {
                return TcpErrorCode.IoError;
            }
        }
    }

    public override void Dispose()
    {
        lock (_lock)
        {
            CleanupConnection();
        }

        base.Dispose();
    }

    public override DeviceSnapshot GetSnapshot()
    {
        var host = _config.Host;
        var port = _config.Port;
        var isConnected = IsConnected;
        var bytesToRead = BytesToRead;

        return new DeviceSnapshot(
            Id,
            Name,
            Type.ToString(),
            State.ToString(),
            "tcp",
            isConnected,
            null,
            null,
            null,
            new TcpConnectionSnapshot(host, port, isConnected, bytesToRead));
    }

    private void CleanupConnection()
    {
        try
        {
            _stream?.Close();
        }
        catch { }

        try
        {
            _client?.Close();
        }
        catch { }

        _stream = null;
        _client = null;
    }

    private bool EnsureConnected()
    {
        if (_client?.Connected == true && _stream is not null)
        {
            return true;
        }

        State = MDeviceState.Fault;
        WriteState("fault");
        return false;
    }
}

/// <summary>
/// Minimal driver stub for TcpDevice to satisfy IDriver contract.
/// TCP connections use native .NET APIs, not a traditional hardware driver.
/// </summary>
internal sealed class TcpDriver : Drivers.IDriver
{
    public string Name => "TCP";

    public bool IsConnected => true;

    public void Initialize(MdkSetting.DriverConfig config)
    {
        // TcpDevice handles its own initialization via TcpPortConfig
    }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        return false;
    }

    public bool Write(string address, object? value)
    {
        return false;
    }

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

    public bool WriteDo(short doType, int value)
    {
        return false;
    }

    public bool WriteDoBit(short doType, short doIndex, bool value)
    {
        return false;
    }

    public bool EnableAxis(short axis)
    {
        return false;
    }

    public bool DisableAxis(short axis)
    {
        return false;
    }

    public bool Stop(int axisMask, int option = 0)
    {
        return false;
    }

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
    {
        return false;
    }

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration) => false;

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration) => false;

    public void Dispose()
    {
    }
}

/// <summary>Snapshot data for TCP connection monitoring.</summary>
public sealed record TcpConnectionSnapshot(
    string Host,
    int Port,
    bool IsConnected,
    int BytesToRead);
