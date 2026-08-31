using System.Runtime.InteropServices;
using OpenCvSharp;

namespace MDKOSS.Extensions.Camera;

/// <summary>
/// 映美精 The Imaging Source（tisgrabber_x64.dll）。IC Imaging Control 的返回值 1 表示成功，
/// 且 snap 得到的 RGB24 缓冲是自下而上的，取图时统一翻转为常规顶行在前的 BGR8。
/// </summary>
internal sealed class NativeTisCamera : CameraBackend
{
    private const int IcSuccess = 1;

    private IntPtr _grabber = IntPtr.Zero;
    private bool _live;
    private long _frameId;

    public override string Vendor => "映美精 The Imaging Source";

    public override bool TryOpen(ExtCameraDeviceParameters parameters, out string error)
    {
        BindNativeDll(parameters);
        return TryOpenNative(() => OpenCore(parameters), out error);
    }

    public override void Close()
    {
        lock (Gate)
        {
            if (_grabber == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (_live)
                {
                    _ = Native.IC_StopLive(_grabber);
                }

                _ = Native.IC_CloseVideoCaptureDevice(_grabber);
                var handle = _grabber;
                Native.IC_ReleaseGrabber(ref handle);
            }
            catch (Exception)
            {
                // SDK already unloaded.
            }

            _grabber = IntPtr.Zero;
            _live = false;
        }
    }

    public override IReadOnlyList<CameraDeviceInfo> Enumerate()
    {
        var list = new List<CameraDeviceInfo>();
        CatchNative(
            () =>
            {
                _ = Native.IC_InitLibrary(null);
                var count = Native.IC_GetDeviceCount();
                for (var i = 0; i < count; i++)
                {
                    var name = Marshal.PtrToStringAnsi(Native.IC_GetDevice(i)) ?? "";
                    var unique = Marshal.PtrToStringAnsi(Native.IC_GetUniqueNamefromList(i)) ?? "";
                    list.Add(new CameraDeviceInfo(i, name, unique, Vendor, "usb/gige"));
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
            if (_grabber == IntPtr.Zero || microseconds <= 0)
            {
                return false;
            }

            _ = Native.IC_SetPropertySwitch(_grabber, "Exposure", "Auto", 0);
            return Native.IC_SetPropertyAbsoluteValue(
                _grabber,
                "Exposure",
                "Value",
                (float)(microseconds / 1_000_000.0)) == IcSuccess;
        }
    }

    public override bool TrySetGain(double gain)
    {
        lock (Gate)
        {
            if (_grabber == IntPtr.Zero)
            {
                return false;
            }

            _ = Native.IC_SetPropertySwitch(_grabber, "Gain", "Auto", 0);
            return Native.IC_SetPropertyAbsoluteValue(_grabber, "Gain", "Value", (float)gain) == IcSuccess;
        }
    }

    public override bool StartGrab()
    {
        lock (Gate)
        {
            if (_grabber == IntPtr.Zero || _live)
            {
                return _live;
            }

            _live = Native.IC_StartLive(_grabber, 0) == IcSuccess;
            return _live;
        }
    }

    public override void StopGrab()
    {
        lock (Gate)
        {
            if (_grabber == IntPtr.Zero || !_live)
            {
                return;
            }

            _ = Native.IC_StopLive(_grabber);
            _live = false;
        }
    }

    public override bool TryGrab(int timeoutMs, out CameraFrame? frame, out string error)
    {
        return TryGrabNative(
            () =>
            {
                lock (Gate)
                {
                    if (_grabber == IntPtr.Zero)
                    {
                        return null;
                    }

                    if (Native.IC_SnapImage(_grabber, Math.Max(1, timeoutMs)) != IcSuccess)
                    {
                        return null;
                    }

                    if (Native.IC_GetImageDescription(_grabber, out var width, out var height, out var bits, out _)
                        != IcSuccess)
                    {
                        return null;
                    }

                    var data = Native.IC_GetImagePtr(_grabber);
                    if (data == IntPtr.Zero || width <= 0 || height <= 0)
                    {
                        return null;
                    }

                    var channels = Math.Max(1, bits / 8);
                    var length = width * height * channels;
                    var raw = new byte[length];
                    Marshal.Copy(data, raw, 0, length);

                    var type = channels == 1 ? MatType.CV_8UC1 : MatType.CV_8UC3;
                    using var bottomUp = Mat.FromPixelData(height, width, type, raw);
                    using var topDown = new Mat();
                    Cv2.Flip(bottomUp, topDown, FlipMode.X);
                    return MatFrame.FromMat(topDown, ++_frameId);
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
            if (Native.IC_InitLibrary(null) != IcSuccess)
            {
                return false;
            }

            var grabber = Native.IC_CreateGrabber();
            if (grabber == IntPtr.Zero)
            {
                return false;
            }

            var name = parameters.SerialNumber;
            if (string.IsNullOrWhiteSpace(name))
            {
                if (parameters.DeviceIndex >= Native.IC_GetDeviceCount())
                {
                    Native.IC_ReleaseGrabber(ref grabber);
                    return false;
                }

                name = Marshal.PtrToStringAnsi(Native.IC_GetDevice(parameters.DeviceIndex)) ?? "";
            }

            if (Native.IC_OpenVideoCaptureDevice(grabber, name) != IcSuccess)
            {
                Native.IC_ReleaseGrabber(ref grabber);
                return false;
            }

            _grabber = grabber;
            if (parameters.Width > 0 && parameters.Height > 0)
            {
                var format = string.Equals(parameters.PixelFormatName, "Mono8", StringComparison.OrdinalIgnoreCase)
                    ? $"Y800 ({parameters.Width}x{parameters.Height})"
                    : $"RGB24 ({parameters.Width}x{parameters.Height})";
                _ = Native.IC_SetVideoFormat(_grabber, format);
            }

            TrySetExposure(parameters.ExposureUs);
            if (parameters.Gain > 0)
            {
                TrySetGain(parameters.Gain);
            }

            return true;
        }
    }

    private static class Native
    {
        private const string Dll = "tisgrabber_x64.dll";

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_InitLibrary(string? licenseKey);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr IC_CreateGrabber();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void IC_ReleaseGrabber(ref IntPtr grabber);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_GetDeviceCount();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr IC_GetDevice(int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr IC_GetUniqueNamefromList(int index);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_OpenVideoCaptureDevice(IntPtr grabber, string deviceName);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_CloseVideoCaptureDevice(IntPtr grabber);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_SetVideoFormat(IntPtr grabber, string format);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_StartLive(IntPtr grabber, int showDisplay);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_StopLive(IntPtr grabber);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_SnapImage(IntPtr grabber, int timeoutMs);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_GetImageDescription(
            IntPtr grabber,
            out int width,
            out int height,
            out int bitsPerPixel,
            out int colorFormat);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr IC_GetImagePtr(IntPtr grabber);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_SetPropertyAbsoluteValue(IntPtr grabber, string property, string element, float value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int IC_SetPropertySwitch(IntPtr grabber, string property, string element, int on);
    }
}
