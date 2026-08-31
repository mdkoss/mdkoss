using System.Runtime.InteropServices;

namespace MDKOSS.Extensions.Camera;

/// <summary>
/// 华睿科技 Huaray IMV（MVSDKmd.dll）。按序号或相机 Key 创建句柄，
/// 帧像素格式沿用 GenICam PFNC 编码，交由 <see cref="CameraPixel"/> 统一解码。
/// </summary>
internal sealed class NativeHuarayCamera : CameraBackend
{
    private const int CreateByIndex = 0;
    private const int CreateByCameraKey = 1;

    private IntPtr _handle = IntPtr.Zero;
    private bool _grabbing;
    private CameraTriggerMode _trigger = CameraTriggerMode.Continuous;

    public override string Vendor => "华睿科技 Huaray";

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
                    _ = Native.IMV_StopGrabbing(_handle);
                }

                _ = Native.IMV_Close(_handle);
                _ = Native.IMV_DestroyHandle(_handle);
            }
            catch (Exception)
            {
                // SDK already unloaded.
            }

            _handle = IntPtr.Zero;
            _grabbing = false;
        }
    }

    public override IReadOnlyList<CameraDeviceInfo> Enumerate()
    {
        var list = new List<CameraDeviceInfo>();
        CatchNative(
            () =>
            {
                var devices = new ImvDeviceList();
                if (Native.IMV_EnumDevices(ref devices, 0) != 0)
                {
                    return false;
                }

                for (var i = 0; i < (int)devices.DeviceNum; i++)
                {
                    list.Add(new CameraDeviceInfo(i, "", "", Vendor, "gige/usb3"));
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

            _ = Native.IMV_SetEnumFeatureSymbol(_handle, "ExposureAuto", "Off");
            return Native.IMV_SetDoubleFeatureValue(_handle, "ExposureTime", microseconds) == 0;
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

            _ = Native.IMV_SetEnumFeatureSymbol(_handle, "GainAuto", "Off");
            return Native.IMV_SetDoubleFeatureValue(_handle, "GainRaw", gain) == 0;
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
            _ = Native.IMV_SetEnumFeatureSymbol(_handle, "TriggerSelector", "FrameStart");
            if (mode == CameraTriggerMode.Continuous)
            {
                return Native.IMV_SetEnumFeatureSymbol(_handle, "TriggerMode", "Off") == 0;
            }

            var ok = Native.IMV_SetEnumFeatureSymbol(_handle, "TriggerMode", "On") == 0;
            ok &= Native.IMV_SetEnumFeatureSymbol(
                _handle,
                "TriggerSource",
                mode == CameraTriggerMode.Software ? "Software" : "Line1") == 0;
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

            _grabbing = Native.IMV_StartGrabbing(_handle) == 0;
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

            _ = Native.IMV_StopGrabbing(_handle);
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
                    if (_handle == IntPtr.Zero)
                    {
                        return null;
                    }

                    if (_trigger == CameraTriggerMode.Software)
                    {
                        _ = Native.IMV_ExecuteCommandFeature(_handle, "TriggerSoftware");
                    }

                    var native = new ImvFrame { Reserved = new byte[8] };
                    if (Native.IMV_GetFrame(_handle, ref native, (uint)Math.Max(1, timeoutMs)) != 0)
                    {
                        return null;
                    }

                    try
                    {
                        var info = native.Info;
                        var length = (int)info.Size;
                        if (native.Data == IntPtr.Zero || length <= 0)
                        {
                            return null;
                        }

                        var payload = new byte[length];
                        Marshal.Copy(native.Data, payload, 0, length);
                        return new CameraFrame(
                            (int)info.Width,
                            (int)info.Height,
                            unchecked((uint)info.PixelFormat),
                            payload,
                            (long)info.BlockId,
                            NowUnixMs());
                    }
                    finally
                    {
                        _ = Native.IMV_ReleaseFrame(_handle, ref native);
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
            var devices = new ImvDeviceList();
            if (Native.IMV_EnumDevices(ref devices, 0) != 0 || devices.DeviceNum == 0)
            {
                return false;
            }

            var handle = IntPtr.Zero;
            int rc;
            if (string.IsNullOrWhiteSpace(parameters.SerialNumber))
            {
                if (parameters.DeviceIndex >= (int)devices.DeviceNum)
                {
                    return false;
                }

                var index = Marshal.AllocHGlobal(sizeof(uint));
                try
                {
                    Marshal.WriteInt32(index, parameters.DeviceIndex);
                    rc = Native.IMV_CreateHandle(ref handle, CreateByIndex, index);
                }
                finally
                {
                    Marshal.FreeHGlobal(index);
                }
            }
            else
            {
                var key = Marshal.StringToHGlobalAnsi(parameters.SerialNumber);
                try
                {
                    rc = Native.IMV_CreateHandle(ref handle, CreateByCameraKey, key);
                }
                finally
                {
                    Marshal.FreeHGlobal(key);
                }
            }

            if (rc != 0 || handle == IntPtr.Zero)
            {
                return false;
            }

            if (Native.IMV_Open(handle) != 0)
            {
                _ = Native.IMV_DestroyHandle(handle);
                return false;
            }

            _handle = handle;
            ApplySettings(parameters);
            return true;
        }
    }

    private void ApplySettings(ExtCameraDeviceParameters parameters)
    {
        if (parameters.Width > 0)
        {
            _ = Native.IMV_SetIntFeatureValue(_handle, "Width", parameters.Width);
        }

        if (parameters.Height > 0)
        {
            _ = Native.IMV_SetIntFeatureValue(_handle, "Height", parameters.Height);
        }

        if (!string.IsNullOrWhiteSpace(parameters.PixelFormatName))
        {
            _ = Native.IMV_SetEnumFeatureSymbol(_handle, "PixelFormat", parameters.PixelFormatName);
        }

        TrySetTrigger(parameters.TriggerMode);
        TrySetExposure(parameters.ExposureUs);
        if (parameters.Gain > 0)
        {
            TrySetGain(parameters.Gain);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ImvDeviceList
    {
        public uint DeviceNum;
        public IntPtr DeviceInfo;
    }

    // Only the leading fields are read; the trailing pad keeps the struct at least as large as the SDK's.
    [StructLayout(LayoutKind.Sequential)]
    private struct ImvFrameInfo
    {
        public ulong BlockId;
        public uint Status;
        public uint Width;
        public uint Height;
        public uint Size;
        public int PixelFormat;
        public ulong TimeStamp;
        public uint ChunkCount;
        public uint PaddingX;
        public uint PaddingY;
        public uint RecvFrameTime;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ImvFrame
    {
        public IntPtr FrameHandle;
        public IntPtr Data;
        public ImvFrameInfo Info;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Reserved;
    }

    private static class Native
    {
        private const string Dll = "MVSDKmd.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_EnumDevices(ref ImvDeviceList deviceList, uint interfaceType);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_CreateHandle(ref IntPtr handle, int mode, IntPtr identifier);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_DestroyHandle(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_Open(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_Close(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_StartGrabbing(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_StopGrabbing(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_GetFrame(IntPtr handle, ref ImvFrame frame, uint timeoutMs);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_ReleaseFrame(IntPtr handle, ref ImvFrame frame);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_SetIntFeatureValue(IntPtr handle, string feature, long value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_SetDoubleFeatureValue(IntPtr handle, string feature, double value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_SetEnumFeatureSymbol(IntPtr handle, string feature, string symbol);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMV_ExecuteCommandFeature(IntPtr handle, string feature);
    }
}
