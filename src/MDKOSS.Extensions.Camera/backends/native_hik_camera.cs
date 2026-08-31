using System.Runtime.InteropServices;

namespace MDKOSS.Extensions.Camera;

/// <summary>
/// 海康机器人 MVS（MvCameraControl.dll）。手册：MV_CC_EnumDevices / MV_CC_CreateHandle /
/// MV_CC_GetOneFrameTimeout。GigE 与 USB3 相机共用同一套接口。
/// </summary>
internal sealed class NativeHikCamera : CameraBackend
{
    private const uint LayerGigeAndUsb = 0x00000001 | 0x00000004;
    private const uint AccessExclusive = 1;

    private IntPtr _handle;
    private byte[] _buffer = [];
    private bool _grabbing;
    private CameraTriggerMode _trigger = CameraTriggerMode.Continuous;

    public override string Vendor => "海康机器人 HikRobot";

    public override bool TryOpen(ExtCameraDeviceParameters parameters, out string error)
    {
        BindNativeDll(parameters);
        return TryOpenNative(() => OpenCore(parameters), out error);
    }

    public override void Close()
    {
        lock (Gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (_grabbing)
                {
                    _ = Native.MV_CC_StopGrabbing(_handle);
                }

                _ = Native.MV_CC_CloseDevice(_handle);
                _ = Native.MV_CC_DestroyHandle(_handle);
            }
            catch (Exception)
            {
                // SDK already unloaded.
            }

            _handle = IntPtr.Zero;
            _grabbing = false;
            _buffer = [];
        }
    }

    public override IReadOnlyList<CameraDeviceInfo> Enumerate()
    {
        var list = new List<CameraDeviceInfo>();
        CatchNative(
            () =>
            {
                var devices = MvDeviceInfoList.Create();
                if (Native.MV_CC_EnumDevices(LayerGigeAndUsb, ref devices) != 0)
                {
                    return false;
                }

                for (var i = 0; i < devices.DeviceNum && i < devices.DeviceInfo.Length; i++)
                {
                    var handle = IntPtr.Zero;
                    if (Native.MV_CC_CreateHandle(ref handle, devices.DeviceInfo[i]) != 0)
                    {
                        continue;
                    }

                    var model = "";
                    var serial = "";
                    if (Native.MV_CC_OpenDevice(handle, AccessExclusive, 0) == 0)
                    {
                        model = ReadString(handle, "DeviceModelName");
                        serial = ReadString(handle, "DeviceSerialNumber");
                        _ = Native.MV_CC_CloseDevice(handle);
                    }

                    _ = Native.MV_CC_DestroyHandle(handle);
                    list.Add(new CameraDeviceInfo(i, model, serial, Vendor, "gige/usb3"));
                }

                return true;
            },
            out _);

        return list;
    }

    public override bool TrySetExposure(double microseconds)
    {
        lock (Gate)
        {
            if (_handle == IntPtr.Zero || microseconds <= 0)
            {
                return false;
            }

            _ = Native.MV_CC_SetEnumValue(_handle, "ExposureAuto", 0);
            return Native.MV_CC_SetFloatValue(_handle, "ExposureTime", (float)microseconds) == 0;
        }
    }

    public override bool TrySetGain(double gain)
    {
        lock (Gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return false;
            }

            _ = Native.MV_CC_SetEnumValue(_handle, "GainAuto", 0);
            return Native.MV_CC_SetFloatValue(_handle, "Gain", (float)gain) == 0;
        }
    }

    public override bool TrySetTrigger(CameraTriggerMode mode)
    {
        lock (Gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return false;
            }

            _trigger = mode;
            if (mode == CameraTriggerMode.Continuous)
            {
                return Native.MV_CC_SetEnumValue(_handle, "TriggerMode", 0) == 0;
            }

            var ok = Native.MV_CC_SetEnumValue(_handle, "TriggerMode", 1) == 0;
            // TriggerSource 7 = Software, 0 = Line0.
            ok &= Native.MV_CC_SetEnumValue(_handle, "TriggerSource", mode == CameraTriggerMode.Software ? 7u : 0u) == 0;
            return ok;
        }
    }

    public override bool StartGrab()
    {
        lock (Gate)
        {
            if (_handle == IntPtr.Zero || _grabbing)
            {
                return _grabbing;
            }

            _grabbing = Native.MV_CC_StartGrabbing(_handle) == 0;
            return _grabbing;
        }
    }

    public override void StopGrab()
    {
        lock (Gate)
        {
            if (_handle == IntPtr.Zero || !_grabbing)
            {
                return;
            }

            _ = Native.MV_CC_StopGrabbing(_handle);
            _grabbing = false;
        }
    }

    public override bool TryGrab(int timeoutMs, out CameraFrame? frame, out string error)
    {
        return TryGrabNative(
            () =>
            {
                lock (Gate)
                {
                    if (_handle == IntPtr.Zero || _buffer.Length == 0)
                    {
                        return null;
                    }

                    if (_trigger == CameraTriggerMode.Software)
                    {
                        _ = Native.MV_CC_SetCommandValue(_handle, "TriggerSoftware");
                    }

                    var info = MvFrameOutInfoEx.Create();
                    var pinned = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
                    try
                    {
                        var rc = Native.MV_CC_GetOneFrameTimeout(
                            _handle,
                            pinned.AddrOfPinnedObject(),
                            (uint)_buffer.Length,
                            ref info,
                            (uint)Math.Max(1, timeoutMs));
                        if (rc != 0)
                        {
                            return null;
                        }

                        var length = info.FrameLen > 0 ? (int)info.FrameLen : _buffer.Length;
                        var data = new byte[Math.Min(length, _buffer.Length)];
                        Array.Copy(_buffer, data, data.Length);
                        return new CameraFrame(
                            info.Width,
                            info.Height,
                            unchecked((uint)info.PixelType),
                            data,
                            info.FrameNum,
                            NowUnixMs());
                    }
                    finally
                    {
                        pinned.Free();
                    }
                }
            },
            out frame,
            out error);
    }

    private bool OpenCore(ExtCameraDeviceParameters parameters)
    {
        lock (Gate)
        {
            Close();
            var devices = MvDeviceInfoList.Create();
            if (Native.MV_CC_EnumDevices(LayerGigeAndUsb, ref devices) != 0 || devices.DeviceNum == 0)
            {
                return false;
            }

            var index = ResolveIndex(devices, parameters);
            if (index < 0)
            {
                return false;
            }

            var handle = IntPtr.Zero;
            if (Native.MV_CC_CreateHandle(ref handle, devices.DeviceInfo[index]) != 0)
            {
                return false;
            }

            if (Native.MV_CC_OpenDevice(handle, AccessExclusive, 0) != 0)
            {
                _ = Native.MV_CC_DestroyHandle(handle);
                return false;
            }

            _handle = handle;
            ApplySettings(parameters);

            var payload = MvccIntValue.Create();
            if (Native.MV_CC_GetIntValue(_handle, "PayloadSize", ref payload) != 0 || payload.CurValue == 0)
            {
                Close();
                return false;
            }

            _buffer = new byte[payload.CurValue];
            return true;
        }
    }

    private void ApplySettings(ExtCameraDeviceParameters parameters)
    {
        if (parameters.Width > 0)
        {
            _ = Native.MV_CC_SetIntValue(_handle, "Width", (uint)parameters.Width);
        }

        if (parameters.Height > 0)
        {
            _ = Native.MV_CC_SetIntValue(_handle, "Height", (uint)parameters.Height);
        }

        TrySetTrigger(parameters.TriggerMode);
        TrySetExposure(parameters.ExposureUs);
        if (parameters.Gain > 0)
        {
            TrySetGain(parameters.Gain);
        }
    }

    private int ResolveIndex(MvDeviceInfoList devices, ExtCameraDeviceParameters parameters)
    {
        var count = (int)Math.Min(devices.DeviceNum, (uint)devices.DeviceInfo.Length);
        if (string.IsNullOrWhiteSpace(parameters.SerialNumber))
        {
            return parameters.DeviceIndex < count ? parameters.DeviceIndex : -1;
        }

        for (var i = 0; i < count; i++)
        {
            var handle = IntPtr.Zero;
            if (Native.MV_CC_CreateHandle(ref handle, devices.DeviceInfo[i]) != 0)
            {
                continue;
            }

            var serial = "";
            if (Native.MV_CC_OpenDevice(handle, AccessExclusive, 0) == 0)
            {
                serial = ReadString(handle, "DeviceSerialNumber");
                _ = Native.MV_CC_CloseDevice(handle);
            }

            _ = Native.MV_CC_DestroyHandle(handle);
            if (string.Equals(serial, parameters.SerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string ReadString(IntPtr handle, string key)
    {
        var value = MvccStringValue.Create();
        return Native.MV_CC_GetStringValue(handle, key, ref value) == 0 ? value.CurValue ?? "" : "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MvDeviceInfoList
    {
        public uint DeviceNum;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public IntPtr[] DeviceInfo;

        public static MvDeviceInfoList Create() => new() { DeviceNum = 0, DeviceInfo = new IntPtr[256] };
    }

    // Only the leading fields are read; the trailing pad keeps the struct at least as large as the SDK's.
    [StructLayout(LayoutKind.Sequential)]
    private struct MvFrameOutInfoEx
    {
        public ushort Width;
        public ushort Height;
        public int PixelType;
        public uint FrameNum;
        public uint DevTimeStampHigh;
        public uint DevTimeStampLow;
        public uint Reserved0;
        public long HostTimeStamp;
        public uint FrameLen;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
        public byte[] Pad;

        public static MvFrameOutInfoEx Create() => new() { Pad = new byte[1024] };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MvccIntValue
    {
        public uint CurValue;
        public uint Max;
        public uint Min;
        public uint Inc;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] Reserved;

        public static MvccIntValue Create() => new() { Reserved = new uint[4] };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct MvccStringValue
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string CurValue;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public long[] Reserved;

        public static MvccStringValue Create() => new() { CurValue = "", Reserved = new long[2] };
    }

    private static class Native
    {
        private const string Dll = "MvCameraControl.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_EnumDevices(uint layerType, ref MvDeviceInfoList devList);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_CreateHandle(ref IntPtr handle, IntPtr deviceInfo);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_DestroyHandle(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_OpenDevice(IntPtr handle, uint accessMode, ushort switchoverKey);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_CloseDevice(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_StartGrabbing(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_StopGrabbing(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_GetOneFrameTimeout(
            IntPtr handle,
            IntPtr data,
            uint dataSize,
            ref MvFrameOutInfoEx frameInfo,
            uint milliseconds);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_GetIntValue(IntPtr handle, string key, ref MvccIntValue value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetIntValue(IntPtr handle, string key, uint value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetFloatValue(IntPtr handle, string key, float value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetEnumValue(IntPtr handle, string key, uint value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_SetCommandValue(IntPtr handle, string key);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int MV_CC_GetStringValue(IntPtr handle, string key, ref MvccStringValue value);
    }
}
