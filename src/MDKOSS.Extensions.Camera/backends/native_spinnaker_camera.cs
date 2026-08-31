using System.Runtime.InteropServices;

namespace MDKOSS.Extensions.Camera;

/// <summary>
/// Teledyne FLIR Spinnaker C（SpinnakerC_v140.dll）。参数通过 GenICam 节点图设置；
/// 像素格式在打开时固定为 Mono8 或 BGR8，抓图后无需回读枚举。
/// </summary>
internal sealed class NativeSpinnakerCamera : CameraBackend
{
    private IntPtr _system = IntPtr.Zero;
    private IntPtr _cameraList = IntPtr.Zero;
    private IntPtr _camera = IntPtr.Zero;
    private IntPtr _nodeMap = IntPtr.Zero;
    private uint _pixelFormat = CameraPixel.Mono8;
    private bool _acquiring;
    private CameraTriggerMode _trigger = CameraTriggerMode.Continuous;
    private long _frameId;

    public override string Vendor => "Teledyne FLIR";

    public override bool TryOpen(ExtCameraDeviceParameters parameters, out string error)
    {
        BindNativeDll(parameters);
        return TryOpenNative(() => OpenCore(parameters), out error);
    }

    public override void Close()
    {
        lock (Gate)
        {
            try
            {
                if (_camera != IntPtr.Zero)
                {
                    if (_acquiring)
                    {
                        _ = Native.spinCameraEndAcquisition(_camera);
                    }

                    _ = Native.spinCameraDeInit(_camera);
                    _ = Native.spinCameraRelease(_camera);
                }

                if (_cameraList != IntPtr.Zero)
                {
                    _ = Native.spinCameraListClear(_cameraList);
                    _ = Native.spinCameraListDestroy(_cameraList);
                }

                if (_system != IntPtr.Zero)
                {
                    _ = Native.spinSystemReleaseInstance(_system);
                }
            }
            catch (Exception)
            {
                // SDK already unloaded.
            }

            _camera = IntPtr.Zero;
            _cameraList = IntPtr.Zero;
            _system = IntPtr.Zero;
            _nodeMap = IntPtr.Zero;
            _acquiring = false;
        }
    }

    public override IReadOnlyList<CameraDeviceInfo> Enumerate()
    {
        var list = new List<CameraDeviceInfo>();
        CatchNative(
            () =>
            {
                if (Native.spinSystemGetInstance(out var system) != 0)
                {
                    return false;
                }

                try
                {
                    if (Native.spinCameraListCreateEmpty(out var cameras) != 0)
                    {
                        return false;
                    }

                    try
                    {
                        if (Native.spinSystemGetCameras(system, cameras) != 0
                            || Native.spinCameraListGetSize(cameras, out var size) != 0)
                        {
                            return false;
                        }

                        for (var i = 0; i < (int)size; i++)
                        {
                            list.Add(new CameraDeviceInfo(i, "", "", Vendor, "gige/usb3"));
                        }

                        return true;
                    }
                    finally
                    {
                        _ = Native.spinCameraListClear(cameras);
                        _ = Native.spinCameraListDestroy(cameras);
                    }
                }
                finally
                {
                    _ = Native.spinSystemReleaseInstance(system);
                }
            },
            out _);

        return list;
    }

    public override bool TrySetExposure(double microseconds)
    {
        lock (Gate)
        {
            if (_nodeMap == IntPtr.Zero || microseconds <= 0)
            {
                return false;
            }

            SetEnum("ExposureAuto", "Off");
            return SetFloat("ExposureTime", microseconds);
        }
    }

    public override bool TrySetGain(double gain)
    {
        lock (Gate)
        {
            if (_nodeMap == IntPtr.Zero)
            {
                return false;
            }

            SetEnum("GainAuto", "Off");
            return SetFloat("Gain", gain);
        }
    }

    public override bool TrySetTrigger(CameraTriggerMode mode)
    {
        lock (Gate)
        {
            if (_nodeMap == IntPtr.Zero)
            {
                return false;
            }

            _trigger = mode;
            SetEnum("TriggerSelector", "FrameStart");
            if (mode == CameraTriggerMode.Continuous)
            {
                return SetEnum("TriggerMode", "Off");
            }

            var ok = SetEnum("TriggerSource", mode == CameraTriggerMode.Software ? "Software" : "Line0");
            return SetEnum("TriggerMode", "On") && ok;
        }
    }

    public override bool StartGrab()
    {
        lock (Gate)
        {
            if (_camera == IntPtr.Zero || _acquiring)
            {
                return _acquiring;
            }

            _acquiring = Native.spinCameraBeginAcquisition(_camera) == 0;
            return _acquiring;
        }
    }

    public override void StopGrab()
    {
        lock (Gate)
        {
            if (_camera == IntPtr.Zero || !_acquiring)
            {
                return;
            }

            _ = Native.spinCameraEndAcquisition(_camera);
            _acquiring = false;
        }
    }

    public override bool TryGrab(int timeoutMs, out CameraFrame? frame, out string error)
    {
        return TryGrabNative(
            () =>
            {
                lock (Gate)
                {
                    if (_camera == IntPtr.Zero)
                    {
                        return null;
                    }

                    if (_trigger == CameraTriggerMode.Software)
                    {
                        ExecuteCommand("TriggerSoftware");
                    }

                    if (Native.spinCameraGetNextImageEx(_camera, (ulong)Math.Max(1, timeoutMs), out var image) != 0)
                    {
                        return null;
                    }

                    try
                    {
                        if (Native.spinImageIsIncomplete(image, out var incomplete) != 0 || incomplete != 0)
                        {
                            return null;
                        }

                        if (Native.spinImageGetWidth(image, out var width) != 0
                            || Native.spinImageGetHeight(image, out var height) != 0
                            || Native.spinImageGetBufferSize(image, out var size) != 0
                            || Native.spinImageGetData(image, out var data) != 0
                            || data == IntPtr.Zero)
                        {
                            return null;
                        }

                        var length = (int)size;
                        var payload = new byte[length];
                        Marshal.Copy(data, payload, 0, length);
                        return new CameraFrame(
                            (int)width,
                            (int)height,
                            _pixelFormat,
                            payload,
                            ++_frameId,
                            NowUnixMs());
                    }
                    finally
                    {
                        _ = Native.spinImageRelease(image);
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
            if (Native.spinSystemGetInstance(out _system) != 0)
            {
                return false;
            }

            if (Native.spinCameraListCreateEmpty(out _cameraList) != 0
                || Native.spinSystemGetCameras(_system, _cameraList) != 0
                || Native.spinCameraListGetSize(_cameraList, out var size) != 0
                || (int)size == 0
                || parameters.DeviceIndex >= (int)size)
            {
                Close();
                return false;
            }

            if (Native.spinCameraListGet(_cameraList, (nuint)parameters.DeviceIndex, out _camera) != 0
                || Native.spinCameraInit(_camera) != 0
                || Native.spinCameraGetNodeMap(_camera, out _nodeMap) != 0)
            {
                Close();
                return false;
            }

            ApplySettings(parameters);
            return true;
        }
    }

    private void ApplySettings(ExtCameraDeviceParameters parameters)
    {
        SetEnum("AcquisitionMode", "Continuous");
        var wantColor = string.Equals(parameters.PixelFormatName, "BGR8", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameters.PixelFormatName, "RGB8", StringComparison.OrdinalIgnoreCase);
        if (wantColor && SetEnum("PixelFormat", "BGR8"))
        {
            _pixelFormat = CameraPixel.Bgr8;
        }
        else
        {
            SetEnum("PixelFormat", "Mono8");
            _pixelFormat = CameraPixel.Mono8;
        }

        TrySetTrigger(parameters.TriggerMode);
        TrySetExposure(parameters.ExposureUs);
        if (parameters.Gain > 0)
        {
            TrySetGain(parameters.Gain);
        }
    }

    private bool SetFloat(string node, double value)
    {
        return Native.spinNodeMapGetNode(_nodeMap, node, out var handle) == 0
               && handle != IntPtr.Zero
               && Native.spinFloatSetValue(handle, value) == 0;
    }

    private bool SetEnum(string node, string entry)
    {
        if (Native.spinNodeMapGetNode(_nodeMap, node, out var nodeHandle) != 0 || nodeHandle == IntPtr.Zero)
        {
            return false;
        }

        if (Native.spinEnumerationGetEntryByName(nodeHandle, entry, out var entryHandle) != 0
            || entryHandle == IntPtr.Zero
            || Native.spinEnumerationEntryGetIntValue(entryHandle, out var value) != 0)
        {
            return false;
        }

        return Native.spinEnumerationSetIntValue(nodeHandle, value) == 0;
    }

    private bool ExecuteCommand(string node)
    {
        return Native.spinNodeMapGetNode(_nodeMap, node, out var handle) == 0
               && handle != IntPtr.Zero
               && Native.spinCommandExecute(handle) == 0;
    }

    private static class Native
    {
        private const string Dll = "SpinnakerC_v140.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinSystemGetInstance(out IntPtr system);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinSystemReleaseInstance(IntPtr system);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraListCreateEmpty(out IntPtr cameraList);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinSystemGetCameras(IntPtr system, IntPtr cameraList);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraListGetSize(IntPtr cameraList, out nuint size);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraListGet(IntPtr cameraList, nuint index, out IntPtr camera);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraListClear(IntPtr cameraList);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraListDestroy(IntPtr cameraList);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraInit(IntPtr camera);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraDeInit(IntPtr camera);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraRelease(IntPtr camera);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraGetNodeMap(IntPtr camera, out IntPtr nodeMap);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraBeginAcquisition(IntPtr camera);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraEndAcquisition(IntPtr camera);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCameraGetNextImageEx(IntPtr camera, ulong timeoutMs, out IntPtr image);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinImageIsIncomplete(IntPtr image, out byte incomplete);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinImageGetWidth(IntPtr image, out nuint width);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinImageGetHeight(IntPtr image, out nuint height);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinImageGetBufferSize(IntPtr image, out nuint size);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinImageGetData(IntPtr image, out IntPtr data);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinImageRelease(IntPtr image);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinNodeMapGetNode(IntPtr nodeMap, string name, out IntPtr node);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinFloatSetValue(IntPtr node, double value);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinEnumerationGetEntryByName(IntPtr node, string name, out IntPtr entry);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinEnumerationEntryGetIntValue(IntPtr entry, out long value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinEnumerationSetIntValue(IntPtr node, long value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int spinCommandExecute(IntPtr node);
    }
}
