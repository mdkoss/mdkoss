namespace MDKOSS.Iec61131;

public enum IecType
{
    Bool,
    Real,
    String,
    Int,
    Time,
}

public enum IecPouKind
{
    Program,
    FunctionBlock,
}

public enum IecStepKind
{
    Goto,
    Assign,
    IfGoto,
    Delay,
    WriteIo,
    ReadIo,
    HostCall,
    Log,
    Halt,
    Complete,
}

public sealed class IecProject
{
    public string Name { get; set; } = "MDKOSS";
    public int CycleMs { get; set; } = 20;
    public List<IecVariable> Globals { get; set; } = [];
    public List<IecIoPoint> IoPoints { get; set; } = [];
    public List<IecPou> Pous { get; set; } = [];
    public List<IecNote> Notes { get; set; } = [];
    public List<IecNodeMap> NodeMaps { get; set; } = [];
}

public sealed class IecVariable
{
    public string Name { get; set; } = "v";
    public IecType Type { get; set; } = IecType.Real;
    public string? Init { get; set; }
    public string? Comment { get; set; }
    public string? SourceKey { get; set; }
}

public sealed class IecIoPoint
{
    public string Name { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string MdkAddress { get; set; } = string.Empty;
    public string AtAddress { get; set; } = string.Empty;
    public bool IsOutput { get; set; }
    public string? Label { get; set; }
}

public sealed class IecPou
{
    public string Name { get; set; } = "FB_Task";
    public IecPouKind Kind { get; set; } = IecPouKind.FunctionBlock;
    public string SourceName { get; set; } = string.Empty;
    public bool Cyclic { get; set; }
    public bool Loop { get; set; }
    public int StartStep { get; set; } = 10;
    public List<IecVariable> Locals { get; set; } = [];
    public List<IecInstance> Instances { get; set; } = [];
    public List<IecStep> Steps { get; set; } = [];
}

public sealed class IecInstance
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? Comment { get; set; }
}

public sealed class IecStep
{
    public int Number { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string FlowKind { get; set; } = string.Empty;
    public IecStepKind Kind { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Expression { get; set; }
    public int Next { get; set; }
    public int AltNext { get; set; }
    public int DelayMs { get; set; }
    public string? TimerName { get; set; }
    public string? IoName { get; set; }
    public string? HostType { get; set; }
    public string? HostInstance { get; set; }
    public List<IecHostArg> HostArgs { get; set; } = [];
    public List<IecHostArg> HostOutputs { get; set; } = [];
}

public sealed class IecHostArg
{
    public string Parameter { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class IecNote
{
    public string Severity { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
}

public sealed class IecNodeMap
{
    public string Pou { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int Step { get; set; }
}

public static class IecTypeMap
{
    public static IecType FromFlow(string? type) => (type ?? "number").Trim().ToLowerInvariant() switch
    {
        "bool" or "boolean" => IecType.Bool,
        "string" or "str" => IecType.String,
        "int" or "integer" => IecType.Int,
        _ => IecType.Real,
    };

    public static IecType FromObject(object? value) => value switch
    {
        bool => IecType.Bool,
        string => IecType.String,
        _ => IecType.Real,
    };

    public static string ToSt(IecType type) => type switch
    {
        IecType.Bool => "BOOL",
        IecType.Int => "DINT",
        IecType.String => "STRING[80]",
        IecType.Time => "TIME",
        _ => "REAL",
    };

    public static string DefaultInit(IecType type) => type switch
    {
        IecType.Bool => "FALSE",
        IecType.Int => "0",
        IecType.String => "''",
        IecType.Time => "T#0MS",
        _ => "0.0",
    };
}
