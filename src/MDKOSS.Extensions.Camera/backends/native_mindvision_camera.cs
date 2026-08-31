using System.Runtime.InteropServices;

namespace MDKOSS.Extensions.Camera;

/// <summary>
/// 迈德威视 MindVision（MVCAMSDK_X64.dll）。相机输出经 ISP（CameraImageProcess）统一转为
/// MONO8 / BGR8 后再交给上层，因此不需要在本进程做 Bayer 解码。
/// <c>serialNumber</c> 对应 SDK 的相机友好名（CameraInitEx2）。
/// </summary>
internal sealed class NativeMindVisionCamera : CameraBackend
{
    private const uint MediaMono8 = CameraPixel.Mono8;
    private const uint MediaBgr8 = CameraPixel.Bgr8;

    private int _handle = -1;
    private byte[] _rgb = [];
    private GCHandle _pinned;
    private uint _outFormat = MediaBgr8;
    private CameraTriggerMode _trigger = CameraTriggerMode.Continuous;
    private bool _playing;
    private long _frameId;

    public override string Vendor => "迈德威视 MindVision";

    public override bool TryOpen(ExtCameraDeviceParameters parameters, out string error)
    {
        BindNativeDll(parameters);
        return TryOpenNative(() => OpenCore(parameters), out error);
    }

    public override void Close()
    {
        lock (Gate)
        {
            if (_handle >= 0)
            {
                try
                {
                    if (_playing)
                    {
                        _ = Native.CameraStop(_handle);
                    }

                    _ = Native.CameraUnInit(_handle);
                }
                catch (Exception)
                {
                    // SDK already unloaded.
                }

                _handle = -1;
            }

            _playing = false;
            if (_pinned.IsAllocated)
            {
                _pinned.Free();
            }

            _rgb = [];
        }
    }

    public override IReadOnlyList<CameraDeviceInfo> Enumerate()
    {
        var list = new List<CameraDeviceInfo>();
        CatchNative(
            () =>
            {
                _ = Native.CameraSdkInit(1);

                // tSdkCameraDevInfo has no published size guarantee, so only the count is read here.
                const int slots = 16;
                const int slotBytes = 8192;
                var buffer = Marshal.AllocHGlobal(slots * slotBytes);
                try
                {
                    var count = slots;
                    if (Native.CameraEnumerateDevice(buffer, ref count) != 0)
                    {
                        return false;
                    }

                    for (var i = 0; i < count; i++)
                    {
                        list.Add(new CameraDeviceInfo(i, "", "", Vendor, "gige/usb"));
                    }

                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            },
            out _);

        return list;
    }

    public override bool TrySetExposure(double microseconds)
    {
        lock (Gate)
        {
            if (_handle < 0 || microseconds <= 0)
            {
                return false;
            }

            _ = Native.CameraSetAeState(_handle, 0);
            return Native.CameraSetExposureTime(_handle, microseconds) == 0;
        }
    }

    public override bool TrySetGain(double gain)
    {
        lock (Gate)
        {
            return _handle >= 0 && Native.CameraSetAnalogGain(_handle, (int)Math.Round(gain)) == 0;
        }
    }

    public override bool TrySetTrigger(CameraTriggerMode mode)
    {
        lock (Gate)
        {
            if (_handle < 0)
            {
                return false;
            }

            _trigger = mode;
            var sdkMode = mode switch
            {
                CameraTriggerMode.Software => 1,
                CameraTriggerMode.Hardware => 2,
                _ => 0,
            };
            return Native.CameraSetTriggerMode(_handle, sdkMode) == 0;
        }
    }

    public override bool StartGrab()
    {
        lock (Gate)
        {
            if (_handle < 0 || _playing)
            {
                return _playing;
            }

            _playing = Native.CameraPlay(_handle) == 0;
            return _playing;
        }
    }

    public override void StopGrab()
    {
        lock (Gate)
        {
            if (_handle < 0 || !_playing)
            {
                return;
            }

            _ = Native.CameraStop(_handle);
            _playing = false;
        }
    }

    public override bool TryGrab(int timeoutMs, out CameraFrame? frame, out string error)
    {
        return TryGrabNative(
            () =>
            {
                lock (Gate)
                {
                    if (_handle < 0)
                    {
                        return null;
                    }

                    if (_trigger == CameraTriggerMode.Software)
                    {
                        _ = Native.CameraSoftTrigger(_handle);
                    }

                    var head = SdkFrameHead.Create();
                    if (Native.CameraGetImageBuffer(_handle, ref head, out var raw, (uint)Math.Max(1, timeoutMs)) != 0)
                    {
                        return null;
                    }

                    try
                    {
                        var channels = _outFormat == MediaMono8 ? 1 : 3;
                        EnsureBuffer(head.Width * head.Height * channels);
                        if (Native.CameraImageProcess(_handle, raw, _pinned.AddrOfPinnedObject(), ref head) != 0)
                        {
                            return null;
                        }

                        var length = head.Width * head.Height * channels;
                        var payload = new byte[length];
                        Array.Copy(_rgb, payload, length);
                        return new CameraFrame(head.Width, head.Height, _outFormat, payload, ++_frameId, NowUnixMs());
                    }
                    finally
                    {
                        _ = Native.CameraReleaseImageBuffer(_handle, raw);
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
            _ = Native.CameraSdkInit(1);

            var handle = -1;
            var rc = string.IsNullOrWhiteSpace(parameters.SerialNumber)
                ? Native.CameraInitEx(parameters.DeviceIndex, -1, -1, ref handle)
                : Native.CameraInitEx2(parameters.SerialNumber, ref handle);
            if (rc != 0 || handle < 0)
            {
                return false;
            }

            _handle = handle;
            _outFormat = string.Equals(parameters.PixelFormatName, "Mono8", StringComparison.OrdinalIgnoreCase)
                ? MediaMono8
                : MediaBgr8;
            _ = Native.CameraSetIspOutFormat(_handle, _outFormat);

            TrySetTrigger(parameters.TriggerMode);
            TrySetExposure(parameters.ExposureUs);
            if (parameters.Gain > 0)
            {
                TrySetGain(parameters.Gain);
            }

            return true;
        }
    }

    private void EnsureBuffer(int bytes)
    {
        if (_rgb.Length >= bytes && _pinned.IsAllocated)
        {
            return;
        }

        if (_pinned.IsAllocated)
        {
            _pinned.Free();
        }

        _rgb = new byte[Math.Max(bytes, 1)];
        _pinned = GCHandle.Alloc(_rgb, GCHandleType.Pinned);
    }

    // Only the leading fields are read; the trailing pad keeps the struct at least as large as the SDK's.
    [StructLayout(LayoutKind.Sequential)]
    private struct SdkFrameHead
    {
        public uint MediaType;
        public uint Bytes;
        public int Width;
        public int Height;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public byte[] Pad;

        public static SdkFrameHead Create() => new() { Pad = new byte[256] };
    }

    private static class Native
    {
        private const string Dll = "MVCAMSDK_X64.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraSdkInit(int languageSelect);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraEnumerateDevice(IntPtr cameraList, ref int count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraInitEx(int deviceIndex, int paramLoadMode, int parameterTeam, ref int handle);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraInitEx2(string cameraName, ref int handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraUnInit(int handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraPlay(int handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraStop(int handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraSetIspOutFormat(int handle, uint mediaType);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraSetTriggerMode(int handle, int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraSoftTrigger(int handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraSetAeState(int handle, int state);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraSetExposureTime(int handle, double microseconds);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraSetAnalogGain(int handle, int gain);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraGetImageBuffer(int handle, ref SdkFrameHead head, out IntPtr buffer, uint timeout);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraImageProcess(int handle, IntPtr input, IntPtr output, ref SdkFrameHead head);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int CameraReleaseImageBuffer(int handle, IntPtr buffer);
    }
}
