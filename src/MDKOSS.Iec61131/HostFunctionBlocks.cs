namespace MDKOSS.Iec61131;

/// <summary>Stub FBs that preserve motion / host handshake as Execute→Done one-shots.</summary>
public static class HostFunctionBlocks
{
    public const string AxisMoveTo = "FB_AxisMoveTo";
    public const string AxisEnable = "FB_AxisEnable";
    public const string AxisJog = "FB_AxisJog";
    public const string AxisStop = "FB_AxisStop";
    public const string PlatformSetMotion = "FB_PlatformSetMotion";
    public const string PlatformAxisMoveTo = "FB_PlatformAxisMoveTo";
    public const string PlatformAxisJog = "FB_PlatformAxisJog";
    public const string PlatformAxisStop = "FB_PlatformAxisStop";
    public const string DeviceSnapshot = "FB_DeviceSnapshot";
    public const string EnsureDriver = "FB_EnsureDriver";
    public const string DeviceAction = "FB_DeviceAction";

    public static IReadOnlyList<string> AllTypes { get; } =
    [
        AxisMoveTo, AxisEnable, AxisJog, AxisStop,
        PlatformSetMotion, PlatformAxisMoveTo, PlatformAxisJog, PlatformAxisStop,
        DeviceSnapshot, EnsureDriver, DeviceAction,
    ];

    public static string WriteAll() => """
(* MDKOSS host / motion stubs — Execute rising edge completes next scan.
   Replace body with S7-1500 TO_Axis, or keep as PC handshake via DB. *)

FUNCTION_BLOCK FB_AxisMoveTo
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
    Position : REAL;
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
    Error := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_AxisEnable
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
    Enabled : BOOL;
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_AxisJog
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
    Direction : REAL;
    Velocity : REAL;
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_AxisStop
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_PlatformSetMotion
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
    Enabled : BOOL;
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_PlatformAxisMoveTo
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
    Axis : STRING[8];
    Position : REAL;
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_PlatformAxisJog
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
    Axis : STRING[8];
    Direction : REAL;
    Velocity : REAL;
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_PlatformAxisStop
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
    Axis : STRING[8];
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_DeviceSnapshot
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
    DeviceType : STRING[80];
    State : STRING[80];
    DriverConnected : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
    DeviceType := 'axis';
    State := 'Running';
    DriverConnected := TRUE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_EnsureDriver
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
    Error := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_DeviceAction
VAR_INPUT
    Execute : BOOL;
    DeviceId : STRING[80];
    Action : STRING[80];
    ParametersJson : STRING[80];
END_VAR
VAR_OUTPUT
    Done : BOOL;
    Busy : BOOL;
    Error : BOOL;
END_VAR
VAR
    prev : BOOL;
END_VAR
IF Execute AND NOT prev THEN
    Busy := TRUE;
    Done := FALSE;
ELSIF Execute AND Busy THEN
    Busy := FALSE;
    Done := TRUE;
ELSIF NOT Execute THEN
    Busy := FALSE;
    Done := FALSE;
END_IF;
prev := Execute;
END_FUNCTION_BLOCK
""";
}
