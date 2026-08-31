using System.Runtime.InteropServices;

namespace MDKOSS.Extensions.Camera;

/// <summary>
/// 大恒图像 Galaxy（GxIAPI.dll）。使用字符串特性接口（GXSetFloatValue / GXSetEnumValueByString），
/// 避免依赖版本相关的 featureID 常量。
/// </summary>
internal sealed class NativeDahengCamera : CameraBackend
{
    private static readonly object LibGate = new();
    private static int _libRefs;

    private IntPtr _handle;
    private byte[] _buffer = [];
    private GCHandle _pinned;
    private bool _grabbing;
    private CameraTriggerMode _trigger = CameraTriggerMode.Continuous;

    public override string Vendor => "大恒图像 Daheng";

    public override bool TryOpen(ExtCameraDeviceParameters parameters, out string error)
    {
        BindNativeDll(parameters);
        return TryOpenNative(() => OpenCore(parameters), out error);
    }

    public override void Close()
    {
        lock (Gate)
        {
            if (_handle != IntPtr.Zero)
            {
                try
                {
                    if (_grabbing)
                    {
                        _ = Native.GXStreamOff(_handle);
                    }

                    _ = Native.GXCloseDevice(_handle);
                }
                catch (Exception)
                {
                    // SDK already unloaded.
                }

                _handle = IntPtr.Zero;
                ReleaseLib();
            }

            _grabbing = false;
            if (_pinned.IsAllocated)
            {
                _pinned.Free();
            }

            _buffer = [];
        }
    }

    public override IReadOnlyList<CameraDeviceInfo> Enumerate()
    {
        var list = new List<CameraDeviceInfo>();
        CatchNative(
            () =>
            {
                if (!AcquireLib())
                {
                    return false;
                }

                try
                {
                    foreach (var info in ReadBaseInfo())
                    {
                        list.Add(new CameraDeviceInfo(
                            list.Count,
                            info.ModelName ?? "",
                            info.SerialNumber ?? "",
                            Vendor,
                            "gige/usb3"));
                    }

                    return true;
                }
                finally
                {
                    ReleaseLib();
                }
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

            _ = Native.GXSetEnumValueByString(_handle, "ExposureAuto", "Off");
            return Native.GXSetFloatValue(_handle, "ExposureTime", microseconds) == 0;
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

            _ = Native.GXSetEnumValueByString(_handle, "GainAuto", "Off");
            _ = Native.GXSetEnumValueByString(_handle, "GainSelector", "AnalogAll");
            return Native.GXSetFloatValue(_handle, "Gain", gain) == 0;
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
                return Native.GXSetEnumValueByString(_handle, "TriggerMode", "Off") == 0;
            }

            var ok = Native.GXSetEnumValueByString(_handle, "TriggerMode", "On") == 0;
            ok &= Native.GXSetEnumValueByString(
                _handle,
                "TriggerSource",
                mode == CameraTriggerMode.Software ? "Software" : "Line0") == 0;
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

            _grabbing = Native.GXStreamOn(_handle) == 0;
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

            _ = Native.GXStreamOff(_handle);
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
                    if (_handle == IntPtr.Zero || !_pinned.IsAllocated)
                    {
                        return null;
                    }

                    if (_trigger == CameraTriggerMode.Software)
                    {
                        _ = Native.GXSetCommandValue(_handle, "TriggerSoftware");
                    }

                    var data = new GxFrameData
                    {
                        ImgBuf = _pinned.AddrOfPinnedObject(),
                        ImgSize = _buffer.Length,
                        Reserved = new int[8],
                    };

                    if (Native.GXGetImage(_handle, ref data, (uint)Math.Max(1, timeoutMs)) != 0 || data.Status != 0)
                    {
                        return null;
                    }

                    var length = data.ImgSize > 0 ? Math.Min(data.ImgSize, _buffer.Length) : _buffer.Length;
                    var payload = new byte[length];
                    Array.Copy(_buffer, payload, length);
                    return new CameraFrame(
                        data.Width,
                        data.Height,
                        unchecked((uint)data.PixelFormat),
                        payload,
                        (long)data.FrameId,
                        NowUnixMs());
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
            if (!AcquireLib())
            {
                return false;
            }

            if (Native.GXUpdateDeviceList(out var count, 1000) != 0 || count == 0)
            {
                ReleaseLib();
                return false;
            }

            var index = ResolveIndex(parameters, (int)count);
            if (index < 0)
            {
                ReleaseLib();
                return false;
            }

            // GXOpenDeviceByIndex is 1-based.
            if (Native.GXOpenDeviceByIndex((uint)(index + 1), out var handle) != 0 || handle == IntPtr.Zero)
            {
                ReleaseLib();
                return false;
            }

            _handle = handle;
            ApplySettings(parameters);

            if (Native.GXGetIntValue(_handle, "PayloadSize", out var payload) != 0 || payload <= 0)
            {
                Close();
                return false;
            }

            _buffer = new byte[payload];
            _pinned = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            return true;
        }
    }

    private void ApplySettings(ExtCameraDeviceParameters parameters)
    {
        if (parameters.Width > 0)
        {
            _ = Native.GXSetIntValue(_handle, "Width", parameters.Width);
        }

        if (parameters.Height > 0)
        {
            _ = Native.GXSetIntValue(_handle, "Height", parameters.Height);
        }

        if (!string.IsNullOrWhiteSpace(parameters.PixelFormatName))
        {
            _ = Native.GXSetEnumValueByString(_handle, "PixelFormat", parameters.PixelFormatName);
        }

        TrySetTrigger(parameters.TriggerMode);
        TrySetExposure(parameters.ExposureUs);
        if (parameters.Gain > 0)
        {
            TrySetGain(parameters.Gain);
        }
    }

    private int ResolveIndex(ExtCameraDeviceParameters parameters, int count)
    {
        if (string.IsNullOrWhiteSpace(parameters.SerialNumber))
        {
            return parameters.DeviceIndex < count ? parameters.DeviceIndex : -1;
        }

        var infos = ReadBaseInfo();
        for (var i = 0; i < infos.Count; i++)
        {
            if (string.Equals(infos[i].SerialNumber, parameters.SerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<GxDeviceBaseInfo> ReadBaseInfo()
    {
        if (Native.GXUpdateDeviceList(out var count, 1000) != 0 || count == 0)
        {
            return [];
        }

        var size = Marshal.SizeOf<GxDeviceBaseInfo>();
        var buffer = Marshal.AllocHGlobal(size * (int)count);
        try
        {
            var bytes = (nuint)(size * (int)count);
            if (Native.GXGetAllDeviceBaseInfo(buffer, ref bytes) != 0)
            {
                return [];
            }

            var list = new List<GxDeviceBaseInfo>((int)count);
            for (var i = 0; i < (int)count; i++)
            {
                list.Add(Marshal.PtrToStructure<GxDeviceBaseInfo>(buffer + (i * size)));
            }

            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool AcquireLib()
    {
        lock (LibGate)
        {
            if (_libRefs > 0)
            {
                _libRefs++;
                return true;
            }

            if (Native.GXInitLib() != 0)
            {
                return false;
            }

            _libRefs = 1;
            return true;
        }
    }

    private static void ReleaseLib()
    {
        lock (LibGate)
        {
            if (_libRefs <= 0)
            {
                return;
            }

            _libRefs--;
            if (_libRefs == 0)
            {
                try
                {
                    _ = Native.GXCloseLib();
                }
                catch (Exception)
                {
                    // SDK already unloaded.
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GxFrameData
    {
        public int Status;
        public IntPtr ImgBuf;
        public int Width;
        public int Height;
        public int PixelFormat;
        public int ImgSize;
        public ulong FrameId;
        public ulong Timestamp;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public int[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct GxDeviceBaseInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string VendorName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ModelName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string SerialNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 132)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string UserId;

        public int AccessStatus;
        public int DeviceClass;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 300)]
        public byte[] Reserved;
    }

    private static class Native
    {
        private const string Dll = "GxIAPI.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXInitLib();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXCloseLib();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXUpdateDeviceList(out uint deviceNum, uint timeout);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXGetAllDeviceBaseInfo(IntPtr deviceInfo, ref nuint bufferSize);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXOpenDeviceByIndex(uint index, out IntPtr device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXCloseDevice(IntPtr device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXStreamOn(IntPtr device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXStreamOff(IntPtr device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXGetImage(IntPtr device, ref GxFrameData frameData, uint timeout);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXSetIntValue(IntPtr device, string feature, long value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXGetIntValue(IntPtr device, string feature, out long value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXSetFloatValue(IntPtr device, string feature, double value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXSetEnumValueByString(IntPtr device, string feature, string value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int GXSetCommandValue(IntPtr device, string feature);
    }
}
