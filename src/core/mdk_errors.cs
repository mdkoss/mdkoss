namespace MDKOSS.Core;

/// <summary>Stable diagnostic codes for runtime configuration and bootstrap failures.</summary>
public enum MdkErrorCode
{
    UnsupportedDriverType = 4101,
    UnsupportedDeviceType = 4102,
    UnsupportedTaskType = 4103,
    DuplicateTaskName = 4104,
    GpioDriverScopeInvalid = 4105,
}

/// <summary>Exception carrying an <see cref="MdkErrorCode"/> for programmatic handling and logs.</summary>
public sealed class MdkException : Exception
{
    public MdkErrorCode Code { get; }

    public MdkException(MdkErrorCode code, string message)
        : base(Format(code, message))
    {
        Code = code;
    }

    public MdkException(MdkErrorCode code, string message, Exception innerException)
        : base(Format(code, message), innerException)
    {
        Code = code;
    }

    private static string Format(MdkErrorCode code, string message) => $"[{code:D}] {message}";
}
