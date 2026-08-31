namespace MDKOSS.Extensions.Camera;

/// <summary>Area-scan camera families selectable via the <c>backend</c> device parameter.</summary>
public readonly record struct CameraKind(
    string Type,
    string Name,
    string Vendor,
    string NativeDll,
    string Transport,
    bool NeedsVendorSdk,
    IReadOnlyList<string> Aliases);

/// <summary>
/// Area-scan cameras commonly paired with a PC HMI on domestic equipment lines.
/// Vendor SDKs are <b>not</b> redistributed — a camera whose runtime DLL is missing
/// falls back to <see cref="Sim"/> instead of faulting the runtime.
/// </summary>
public static class CameraCatalog
{
    public static readonly CameraKind Sim = new(
        "sim", "Simulator", "内置仿真", "", "software", false, ["simulate", "demo", "none"]);

    public static readonly CameraKind File = new(
        "file", "Image Folder", "本地图像回放", "", "file", false, ["folder", "image", "replay", "offline"]);

    public static readonly CameraKind Uvc = new(
        "uvc", "UVC / DirectShow", "通用 USB 相机", "", "usb", false, ["usb", "opencv", "directshow", "webcam"]);

    public static readonly CameraKind Hik = new(
        "hik", "MVS", "海康机器人 HikRobot", "MvCameraControl.dll", "gige/usb3", true, ["hikvision", "hikrobot", "mvs"]);

    public static readonly CameraKind Daheng = new(
        "daheng", "Galaxy", "大恒图像 Daheng", "GxIAPI.dll", "gige/usb3", true, ["galaxy", "gx", "daheng-galaxy"]);

    public static readonly CameraKind Huaray = new(
        "huaray", "IMV", "华睿科技 Huaray", "MVSDKmd.dll", "gige/usb3", true, ["dahua", "imv", "huaruay"]);

    public static readonly CameraKind MindVision = new(
        "mindvision", "MVCAM", "迈德威视 MindVision", "MVCAMSDK_X64.dll", "gige/usb", true, ["mindv", "mvcam"]);

    public static readonly CameraKind Basler = new(
        "basler", "pylon", "Basler", "PylonC.dll", "gige/usb3", true, ["pylon", "pylonc"]);

    public static readonly CameraKind Flir = new(
        "flir", "Spinnaker", "Teledyne FLIR", "SpinnakerC_v140.dll", "gige/usb3", true, ["spinnaker", "pointgrey", "teledyne"]);

    public static readonly CameraKind Tis = new(
        "tis", "IC Imaging Control", "映美精 The Imaging Source", "tisgrabber_x64.dll", "usb/gige", true, ["imagingsource", "ic"]);

    public static IReadOnlyList<CameraKind> All { get; } =
    [
        Sim, File, Uvc, Hik, Daheng, Huaray, MindVision, Basler, Flir, Tis,
    ];

    public static bool TryGet(string? type, out CameraKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        var key = type.Trim();
        foreach (var item in All)
        {
            if (string.Equals(item.Type, key, StringComparison.OrdinalIgnoreCase))
            {
                kind = item;
                return true;
            }
        }

        foreach (var item in All)
        {
            foreach (var alias in item.Aliases)
            {
                if (!string.Equals(alias, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kind = item;
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves a configured <c>backend</c> token, defaulting to <see cref="Sim"/>.</summary>
    public static CameraKind Resolve(string? type) => TryGet(type, out var kind) ? kind : Sim;
}
