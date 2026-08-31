using System.Runtime.InteropServices;

namespace MDKOSS.Extensions.Camera;

/// <summary>
/// Basler pylon C（PylonC.dll）。像素格式在打开时被强制为 Mono8 或 BGR8，
/// 因此抓图后不必回读 PixelFormat 节点。手册：PylonDeviceGrabSingleFrame。
/// </summary>
internal sealed class NativeBaslerCamera : CameraBackend
{
    private const int AccessControlAndStream = 0x2 | 0x4;
    private const int GrabResultBytes = 512;

    private static readonly object LibGate = new();
    private static int _libRefs;

    private IntPtr _device = IntPtr.Zero;
    private byte[] _buffer = [];
    private uint _pixelFormat = CameraPixel.Mono8;
    private int _width;
    private int _height;
    private CameraTriggerMode _trigger = CameraTriggerMode.Continuous;
    private long _frameId;

    public override string Vendor => "Basler";

    public override bool TryOpen(ExtCameraDeviceParameters parameters, out string error)
    {
        BindNativeDll(parameters);
        return TryOpenNative(() => OpenCore(parameters), out error);
    }

    public override void Close()
    {
        lock (Gate)
        {
            if (_device != IntPtr.Zero)
            {
                try
                {
                    _ = Native.PylonDeviceClose(_device);
                    _ = Native.PylonDestroyDevice(_device);
                }
                catch (Exception)
                {
                    // SDK already unloaded.
                }

                _device = IntPtr.Zero;
                ReleaseLib();
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
                    if (Native.PylonEnumerateDevices(out var count) != 0)
                    {
                        return false;
                    }

                    for (var i = 0; i < (int)count; i++)
                    {
                        list.Add(new CameraDeviceInfo(i, "", "", Vendor, "gige/usb3"));
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
            if (_device == IntPtr.Zero || microseconds <= 0)
            {
                return false;
            }

            _ = Native.PylonDeviceFeatureFromString(_device, "ExposureAuto", "Off");

            // USB3 cameras expose ExposureTime, GigE (ace classic) exposes ExposureTimeAbs.
            return Native.PylonDeviceSetFloatFeature(_device, "ExposureTime", microseconds) == 0
                   || Native.PylonDeviceSetFloatFeature(_device, "ExposureTimeAbs", microseconds) == 0;
        }
    }

    public override bool TrySetGain(double gain)
    {
        lock (Gate)
        {
            if (_device == IntPtr.Zero)
            {
                return false;
            }

            _ = Native.PylonDeviceFeatureFromString(_device, "GainAuto", "Off");
            return Native.PylonDeviceSetFloatFeature(_device, "Gain", gain) == 0
                   || Native.PylonDeviceSetIntegerFeature(_device, "GainRaw", (long)Math.Round(gain)) == 0;
        }
    }

    public override bool TrySetTrigger(CameraTriggerMode mode)
    {
        lock (Gate)
        {
            if (_device == IntPtr.Zero)
            {
                return false;
            }

            _trigger = mode;
            _ = Native.PylonDeviceFeatureFromString(_device, "TriggerSelector", "FrameStart");
            if (mode == CameraTriggerMode.Continuous)
            {
                return Native.PylonDeviceFeatureFromString(_device, "TriggerMode", "Off") == 0;
            }

            var ok = Native.PylonDeviceFeatureFromString(_device, "TriggerMode", "On") == 0;
            ok &= Native.PylonDeviceFeatureFromString(
                _device,
                "TriggerSource",
                mode == CameraTriggerMode.Software ? "Software" : "Line1") == 0;
            return ok;
        }
    }

    public override bool TryGrab(int timeoutMs, out CameraFrame? frame, out string error)
    {
        return TryGrabNative(
            () =>
            {
                lock (Gate)
                {
                    if (_device == IntPtr.Zero || _buffer.Length == 0)
                    {
                        return null;
                    }

                    if (_trigger == CameraTriggerMode.Software)
                    {
                        _ = Native.PylonDeviceExecuteCommandFeature(_device, "TriggerSoftware");
                    }

                    var result = Marshal.AllocHGlobal(GrabResultBytes);
                    var pinned = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
                    try
                    {
                        // The grab result struct layout is version specific; only pReady is inspected.
                        var rc = Native.PylonDeviceGrabSingleFrame(
                            _device,
                            0,
                            pinned.AddrOfPinnedObject(),
                            (nuint)_buffer.Length,
                            result,
                            out var ready,
                            (uint)Math.Max(1, timeoutMs));
                        if (rc != 0 || !ready)
                        {
                            return null;
                        }

                        var length = _width * _height * CameraPixel.BytesPerPixel(_pixelFormat);
                        length = Math.Min(length, _buffer.Length);
                        var payload = new byte[length];
                        Array.Copy(_buffer, payload, length);
                        return new CameraFrame(_width, _height, _pixelFormat, payload, ++_frameId, NowUnixMs());
                    }
                    finally
                    {
                        pinned.Free();
                        Marshal.FreeHGlobal(result);
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
            if (!AcquireLib())
            {
                return false;
            }

            if (Native.PylonEnumerateDevices(out var count) != 0 || (int)count == 0)
            {
                ReleaseLib();
                return false;
            }

            if (parameters.DeviceIndex >= (int)count)
            {
                ReleaseLib();
                return false;
            }

            if (Native.PylonCreateDeviceByIndex((nuint)parameters.DeviceIndex, out var device) != 0)
            {
                ReleaseLib();
                return false;
            }

            if (Native.PylonDeviceOpen(device, AccessControlAndStream) != 0)
            {
                _ = Native.PylonDestroyDevice(device);
                ReleaseLib();
                return false;
            }

            _device = device;
            ApplySettings(parameters);

            if (Native.PylonDeviceGetIntegerFeature(_device, "PayloadSize", out var payload) != 0 || payload <= 0)
            {
                Close();
                return false;
            }

            _buffer = new byte[payload];
            return true;
        }
    }

    private void ApplySettings(ExtCameraDeviceParameters parameters)
    {
        if (parameters.Width > 0)
        {
            _ = Native.PylonDeviceSetIntegerFeature(_device, "Width", parameters.Width);
        }

        if (parameters.Height > 0)
        {
            _ = Native.PylonDeviceSetIntegerFeature(_device, "Height", parameters.Height);
        }

        var wantColor = string.Equals(parameters.PixelFormatName, "BGR8", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameters.PixelFormatName, "RGB8", StringComparison.OrdinalIgnoreCase);
        if (wantColor && Native.PylonDeviceFeatureFromString(_device, "PixelFormat", "BGR8") == 0)
        {
            _pixelFormat = CameraPixel.Bgr8;
        }
        else
        {
            _ = Native.PylonDeviceFeatureFromString(_device, "PixelFormat", "Mono8");
            _pixelFormat = CameraPixel.Mono8;
        }

        _width = Native.PylonDeviceGetIntegerFeature(_device, "Width", out var w) == 0 ? (int)w : parameters.Width;
        _height = Native.PylonDeviceGetIntegerFeature(_device, "Height", out var h) == 0 ? (int)h : parameters.Height;

        TrySetTrigger(parameters.TriggerMode);
        TrySetExposure(parameters.ExposureUs);
        if (parameters.Gain > 0)
        {
            TrySetGain(parameters.Gain);
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

            if (Native.PylonInitialize() != 0)
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
                    _ = Native.PylonTerminate(false);
                }
                catch (Exception)
                {
                    // SDK already unloaded.
                }
            }
        }
    }

    private static class Native
    {
        private const string Dll = "PylonC.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonInitialize();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonTerminate([MarshalAs(UnmanagedType.U1)] bool shutDownLogging);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonEnumerateDevices(out nuint deviceCount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonCreateDeviceByIndex(nuint index, out IntPtr device);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonDestroyDevice(IntPtr device);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonDeviceOpen(IntPtr device, int accessMode);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonDeviceClose(IntPtr device);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonDeviceFeatureFromString(IntPtr device, string name, string value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonDeviceExecuteCommandFeature(IntPtr device, string name);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonDeviceSetFloatFeature(IntPtr device, string name, double value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonDeviceSetIntegerFeature(IntPtr device, string name, long value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonDeviceGetIntegerFeature(IntPtr device, string name, out long value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PylonDeviceGrabSingleFrame(
            IntPtr device,
            nuint channel,
            IntPtr buffer,
            nuint bufferSize,
            IntPtr grabResult,
            [MarshalAs(UnmanagedType.U1)] out bool ready,
            uint timeout);
    }
}
