namespace MDKOSS.Drivers.Boards;

/// <summary>Market motion / IO cards registered by this plugin (config <c>type</c> keys).</summary>
public readonly record struct BoardKind(
    string Type,
    string Name,
    string Vendor,
    string NativeDll,
    short DefaultIoBitBase,
    string Family);

/// <summary>
/// Domestic equipment OEM cards commonly paired with a PC HMI (点胶 / PNP / 装配).
/// Native vendor SDKs are <b>not</b> redistributed; simulate mode is the default.
/// </summary>
public static class BoardCatalog
{
    public static readonly BoardKind Zmc = new("zmc", "ZMC", "正运动 Zmotion", "zauxdll.dll", 0, "pulse/ethercat");
    public static readonly BoardKind Zmotion = new("zmotion", "Zmotion", "正运动 Zmotion", "zauxdll.dll", 0, "pulse/ethercat");
    public static readonly BoardKind Adt = new("adt", "ADT", "众为兴 ADT", "adt8948a1.dll", 0, "pulse");
    public static readonly BoardKind Mpc = new("mpc", "MPC", "摩信 MPC", "MPC08.dll", 0, "pulse");
    public static readonly BoardKind Emc = new("emc", "EMC", "雷赛 EtherCAT", "LTDMC.dll", 0, "ethercat");
    public static readonly BoardKind Gtn = new("gtn", "GTN", "固高 EtherCAT / GLink", "gtn.dll", 1, "ethercat");
    public static readonly BoardKind Adlink = new("adlink", "ADLINK", "凌华 ADLINK", "APS168.dll", 0, "pulse");
    public static readonly BoardKind Advantech = new("advantech", "Advantech", "研华 Advantech", "ADVMOT.dll", 0, "pulse");
    public static readonly BoardKind Galil = new("galil", "Galil", "Galil Motion Control", "gclib.dll", 0, "ethernet");
    public static readonly BoardKind Inovance = new("inovance", "Inovance", "汇川 Inovance", "IMC_API_x64.dll", 0, "ethercat");

    public static IReadOnlyList<BoardKind> All { get; } =
    [
        Zmc, Zmotion, Adt, Mpc, Emc, Gtn, Adlink, Advantech, Galil, Inovance,
    ];

    public static bool TryGet(string? type, out BoardKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        foreach (var item in All)
        {
            if (string.Equals(item.Type, type.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                kind = item;
                return true;
            }
        }

        return false;
    }
}
