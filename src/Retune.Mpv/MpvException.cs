namespace Retune.Mpv;

/// <summary>An mpv API call failed. <see cref="ErrorCode"/> is the mpv_error value.</summary>
public sealed class MpvException : Exception
{
    public MpvException()
    {
    }

    public MpvException(string message)
        : base(message)
    {
    }

    public MpvException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public MpvException(string operation, int errorCode, string errorMessage)
        : base($"{operation} failed: {errorMessage} ({errorCode})")
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }
}
