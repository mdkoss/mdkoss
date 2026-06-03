using System.IO.Ports;
using System.Text;

namespace MDKOSS.Core;

/// <summary>Serial port parity settings.</summary>
public enum SerialParity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

/// <summary>Serial port stop bits settings.</summary>
public enum SerialStopBits
{
    None,
    One,
    Two,
    OnePointFive
}

/// <summary>Serial port handshake settings.</summary>
public enum SerialHandshake
{
    None,
    XOnXOff,
    RequestToSend,
    RequestToSendXOnXOff
}

/// <summary>
/// Serial port configuration parameters.
/// </summary>
public sealed class SerialPortConfig
{
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public SerialParity Parity { get; set; } = SerialParity.None;
    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;
    public SerialHandshake Handshake { get; set; } = SerialHandshake.None;
    public int ReadTimeout { get; set; } = 5000;
    public int WriteTimeout { get; set; } = 5000;
    public Encoding Encoding { get; set; } = Encoding.ASCII;
    public bool DtrEnable { get; set; } = false;
    public bool RtsEnable { get; set; } = false;
}

/// <summary>
/// Serial port error codes.
/// </summary>
public enum SerialErrorCode
{
    Ok,
    PortNotOpen,
    PortAlreadyOpen,
    PortNotFound,
    InvalidParameter,
    Timeout,
    IoError,
    OperationFailed
}

/// <summary>
/// Serial port device: wraps RS-232C communication with logical device semantics.
/// </summary>
public sealed class SerialDevice : MDeviceBase
{
    private SerialPortConfig _config;
    private SerialPort? _port;
    private readonly object _lock = new();
    private readonly StringBuilder _readBuffer = new();

    public SerialDevice(string id, string name, SerialPortConfig config, MVarStore vars)
        : base(id, name, MDeviceType.SerialDev, new SerialDriver(), vars)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>Current serial port configuration.</summary>
    public SerialPortConfig Config => _config;

    /// <summary>Indicates whether the serial port is currently open.</summary>
    public bool IsOpen => _port?.IsOpen == true;

    /// <summary>Gets the number of bytes available to read from the port.</summary>
    public int BytesToRead => _port?.BytesToRead ?? 0;

    /// <summary>Opens the serial port with current configuration (OpenCom equivalent).</summary>
    public SerialErrorCode Open()
    {
        lock (_lock)
        {
            if (_port?.IsOpen == true)
            {
                return SerialErrorCode.PortAlreadyOpen;
            }

            try
            {
                _port = new SerialPort
                {
                    PortName = _config.PortName,
                    BaudRate = _config.BaudRate,
                    DataBits = _config.DataBits,
                    Parity = ConvertToParity(_config.Parity),
                    StopBits = ConvertToStopBits(_config.StopBits),
                    Handshake = ConvertToHandshake(_config.Handshake),
                    ReadTimeout = _config.ReadTimeout,
                    WriteTimeout = _config.WriteTimeout,
                    Encoding = _config.Encoding,
                    DtrEnable = _config.DtrEnable,
                    RtsEnable = _config.RtsEnable
                };

                _port.Open();
                State = MDeviceState.Running;
                WriteState("open");

                Vars.Set(BuildVarKey("portName"), _config.PortName);
                Vars.Set(BuildVarKey("baudRate"), _config.BaudRate);
                Vars.Set(BuildVarKey("isOpen"), true);

                return SerialErrorCode.Ok;
            }
            catch (UnauthorizedAccessException)
            {
                return SerialErrorCode.PortNotFound;
            }
            catch (IOException)
            {
                return SerialErrorCode.IoError;
            }
            catch (Exception)
            {
                return SerialErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>Closes the serial port (CloseCom equivalent).</summary>
    public SerialErrorCode Close()
    {
        lock (_lock)
        {
            if (_port is null || !_port.IsOpen)
            {
                return SerialErrorCode.PortNotOpen;
            }

            try
            {
                _port.Close();
                _port.Dispose();
                _port = null;

                State = MDeviceState.Stopped;
                WriteState("closed");

                Vars.Set(BuildVarKey("isOpen"), false);

                return SerialErrorCode.Ok;
            }
            catch (Exception)
            {
                return SerialErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>
    /// Checks port status and returns bytes waiting or error code (ChkCom equivalent).
    /// Returns negative value for error conditions, positive for bytes available.
    /// </summary>
    public int CheckPort()
    {
        lock (_lock)
        {
            if (_port is null || !_port.IsOpen)
            {
                Vars.Set(BuildVarKey("lastError"), (int)SerialErrorCode.PortNotOpen);
                return -(int)SerialErrorCode.PortNotOpen;
            }

            try
            {
                var bytesToRead = _port.BytesToRead;
                Vars.Set(BuildVarKey("bytesToRead"), bytesToRead);
                return bytesToRead;
            }
            catch (Exception)
            {
                var error = -(int)SerialErrorCode.IoError;
                Vars.Set(BuildVarKey("lastError"), error);
                return error;
            }
        }
    }

    /// <summary>
    /// Updates port parameters at runtime (SetCom equivalent).
    /// </summary>
    public SerialErrorCode SetParameters(SerialPortConfig? config = null)
    {
        lock (_lock)
        {
            var newConfig = config ?? _config;

            try
            {
                var wasOpen = _port?.IsOpen == true;

                if (wasOpen)
                {
                    Close();
                }

                _config = newConfig;

                if (wasOpen)
                {
                    Open();
                }

                Vars.Set(BuildVarKey("baudRate"), _config.BaudRate);
                Vars.Set(BuildVarKey("dataBits"), _config.DataBits);
                Vars.Set(BuildVarKey("parity"), _config.Parity.ToString());

                return SerialErrorCode.Ok;
            }
            catch (Exception)
            {
                return SerialErrorCode.OperationFailed;
            }
        }
    }

    /// <summary>
    /// Sends a string to the port with newline (Print # equivalent).
    /// </summary>
    public SerialErrorCode Print(string data)
    {
        lock (_lock)
        {
            if (!EnsurePortOpen())
            {
                return SerialErrorCode.PortNotOpen;
            }

            try
            {
                _port!.WriteLine(data);
                Vars.Set(BuildVarKey("lastWriteLength"), data.Length + _port.NewLine.Length);
                WriteState(State.ToString().ToLowerInvariant());
                return SerialErrorCode.Ok;
            }
            catch (TimeoutException)
            {
                return SerialErrorCode.Timeout;
            }
            catch (Exception)
            {
                return SerialErrorCode.IoError;
            }
        }
    }

    /// <summary>
    /// Receives a single character from the port (Input # equivalent).
    /// </summary>
    public (SerialErrorCode error, char? value) ReadChar()
    {
        lock (_lock)
        {
            if (!EnsurePortOpen())
            {
                return (SerialErrorCode.PortNotOpen, null);
            }

            try
            {
                var value = (char)_port!.ReadChar();
                Vars.Set(BuildVarKey("lastReadChar"), value);
                return (SerialErrorCode.Ok, value);
            }
            catch (TimeoutException)
            {
                return (SerialErrorCode.Timeout, null);
            }
            catch (Exception)
            {
                return (SerialErrorCode.IoError, null);
            }
        }
    }

    /// <summary>
    /// Receives a line of text from the port (Line Input # equivalent).
    /// </summary>
    public (SerialErrorCode error, string? value) ReadLine()
    {
        lock (_lock)
        {
            if (!EnsurePortOpen())
            {
                return (SerialErrorCode.PortNotOpen, null);
            }

            try
            {
                var value = _port!.ReadLine();
                Vars.Set(BuildVarKey("lastReadLine"), value);
                Vars.Set(BuildVarKey("lastReadLength"), value.Length);
                return (SerialErrorCode.Ok, value);
            }
            catch (TimeoutException)
            {
                return (SerialErrorCode.Timeout, null);
            }
            catch (Exception)
            {
                return (SerialErrorCode.IoError, null);
            }
        }
    }

    /// <summary>
    /// Reads all available characters from the port (Read # equivalent).
    /// </summary>
    public (SerialErrorCode error, string? value) ReadAll()
    {
        lock (_lock)
        {
            if (!EnsurePortOpen())
            {
                return (SerialErrorCode.PortNotOpen, null);
            }

            try
            {
                var buffer = new byte[_port!.BytesToRead];
                var bytesRead = _port.Read(buffer, 0, buffer.Length);
                var value = _config.Encoding.GetString(buffer, 0, bytesRead);
                Vars.Set(BuildVarKey("lastReadText"), value);
                Vars.Set(BuildVarKey("lastReadLength"), bytesRead);
                return (SerialErrorCode.Ok, value);
            }
            catch (TimeoutException)
            {
                return (SerialErrorCode.Timeout, null);
            }
            catch (Exception)
            {
                return (SerialErrorCode.IoError, null);
            }
        }
    }

    /// <summary>
    /// Reads binary data from the port (ReadBin # equivalent).
    /// </summary>
    public (SerialErrorCode error, byte[]? value) ReadBinary(int count)
    {
        lock (_lock)
        {
            if (!EnsurePortOpen())
            {
                return (SerialErrorCode.PortNotOpen, null);
            }

            if (count <= 0)
            {
                return (SerialErrorCode.InvalidParameter, null);
            }

            try
            {
                var buffer = new byte[count];
                var bytesRead = _port!.Read(buffer, 0, count);

                if (bytesRead < count)
                {
                    Array.Resize(ref buffer, bytesRead);
                }

                Vars.Set(BuildVarKey("lastReadBytes"), bytesRead);
                return (SerialErrorCode.Ok, buffer);
            }
            catch (TimeoutException)
            {
                return (SerialErrorCode.Timeout, null);
            }
            catch (Exception)
            {
                return (SerialErrorCode.IoError, null);
            }
        }
    }

    /// <summary>
    /// Sends a string to the port (Write # equivalent).
    /// </summary>
    public SerialErrorCode Write(string data)
    {
        lock (_lock)
        {
            if (!EnsurePortOpen())
            {
                return SerialErrorCode.PortNotOpen;
            }

            try
            {
                _port!.Write(data);
                Vars.Set(BuildVarKey("lastWriteLength"), data.Length);
                WriteState(State.ToString().ToLowerInvariant());
                return SerialErrorCode.Ok;
            }
            catch (TimeoutException)
            {
                return SerialErrorCode.Timeout;
            }
            catch (Exception)
            {
                return SerialErrorCode.IoError;
            }
        }
    }

    /// <summary>
    /// Sends binary data to the port (WriteBin # equivalent).
    /// </summary>
    public SerialErrorCode WriteBinary(byte[] data)
    {
        lock (_lock)
        {
            if (!EnsurePortOpen())
            {
                return SerialErrorCode.PortNotOpen;
            }

            if (data is null || data.Length == 0)
            {
                return SerialErrorCode.InvalidParameter;
            }

            try
            {
                _port!.Write(data, 0, data.Length);
                Vars.Set(BuildVarKey("lastWriteBytes"), data.Length);
                WriteState(State.ToString().ToLowerInvariant());
                return SerialErrorCode.Ok;
            }
            catch (TimeoutException)
            {
                return SerialErrorCode.Timeout;
            }
            catch (Exception)
            {
                return SerialErrorCode.IoError;
            }
        }
    }

    /// <summary>Discards buffer contents (clears read/write buffers).</summary>
    public SerialErrorCode DiscardBuffers()
    {
        lock (_lock)
        {
            if (!EnsurePortOpen())
            {
                return SerialErrorCode.PortNotOpen;
            }

            try
            {
                _port!.DiscardInBuffer();
                _port.DiscardOutBuffer();
                _readBuffer.Clear();
                return SerialErrorCode.Ok;
            }
            catch (Exception)
            {
                return SerialErrorCode.IoError;
            }
        }
    }

    public override void Dispose()
    {
        lock (_lock)
        {
            if (_port is not null)
            {
                if (_port.IsOpen)
                {
                    try
                    {
                        _port.Close();
                    }
                    catch { }
                }
                _port.Dispose();
                _port = null;
            }
        }

        base.Dispose();
    }

    public override DeviceSnapshot GetSnapshot()
    {
        var portName = _config.PortName;
        var isOpen = IsOpen;
        var baudRate = _config.BaudRate;
        var bytesToRead = BytesToRead;

        return new DeviceSnapshot(
            Id,
            Name,
            Type.ToString(),
            State.ToString(),
            "serial",
            isOpen,
            null,
            null,
            new SerialPortSnapshot(portName, baudRate, isOpen, bytesToRead),
            null);
    }

    private bool EnsurePortOpen()
    {
        if (_port?.IsOpen == true)
        {
            return true;
        }

        State = MDeviceState.Fault;
        WriteState("fault");
        return false;
    }

    private static Parity ConvertToParity(SerialParity parity) => parity switch
    {
        SerialParity.Odd => Parity.Odd,
        SerialParity.Even => Parity.Even,
        SerialParity.Mark => Parity.Mark,
        SerialParity.Space => Parity.Space,
        _ => Parity.None
    };

    private static StopBits ConvertToStopBits(SerialStopBits stopBits) => stopBits switch
    {
        SerialStopBits.Two => StopBits.Two,
        SerialStopBits.OnePointFive => StopBits.OnePointFive,
        _ => StopBits.One
    };

    private static Handshake ConvertToHandshake(SerialHandshake handshake) => handshake switch
    {
        SerialHandshake.XOnXOff => Handshake.XOnXOff,
        SerialHandshake.RequestToSend => Handshake.RequestToSend,
        SerialHandshake.RequestToSendXOnXOff => Handshake.RequestToSendXOnXOff,
        _ => Handshake.None
    };
}

/// <summary>
/// Minimal driver stub for SerialDevice to satisfy IDriver contract.
/// Serial ports use native .NET APIs, not a traditional hardware driver.
/// </summary>
internal sealed class SerialDriver : Drivers.IDriver
{
    public string Name => "SERIAL";

    public bool IsConnected => true;

    public void Initialize(MdkSetting.DriverConfig config)
    {
        // SerialDevice handles its own initialization via SerialPortConfig
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

/// <summary>Snapshot data for serial port monitoring.</summary>
public sealed record SerialPortSnapshot(
    string PortName,
    int BaudRate,
    bool IsOpen,
    int BytesToRead,
    int DataBits = 8,
    string Parity = "None",
    string StopBits = "One");
