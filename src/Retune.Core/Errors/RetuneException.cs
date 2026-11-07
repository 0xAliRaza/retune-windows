namespace Retune.Core.Errors;

/// <summary>Every failure the user can see, with the message they see. Spec: docs/spec/core.md §12.1.</summary>
public enum ProblemKind
{
    InvalidServer,
    InvalidCredentials,
    AccountInactive,
    Http,
    EmptyResponse,
    UnreadableResponse,
    EmptyPlaylist,
    MissingPassword,
    SecretStore,
    ResponseTooLarge,
    Timeout,
    ConnectionLost,
    Network,
}

/// <summary>A user-facing failure. <see cref="Exception.Message"/> is ready to show and never contains URLs or credentials.</summary>
public sealed class RetuneException : Exception
{
    public RetuneException()
        : this(ProblemKind.Network, "Something went wrong.")
    {
    }

    public RetuneException(string message)
        : this(ProblemKind.Network, message)
    {
    }

    public RetuneException(string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = ProblemKind.Network;
    }

    private RetuneException(ProblemKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public ProblemKind Kind { get; }

    /// <summary>The HTTP status for <see cref="ProblemKind.Http"/>.</summary>
    public int? HttpStatus { get; private init; }

    public static RetuneException InvalidServer() =>
        new(ProblemKind.InvalidServer, "That server address doesn't look right. Use something like http://example.com:8080.");

    public static RetuneException InvalidCredentials() =>
        new(ProblemKind.InvalidCredentials, "The server rejected that username or password.");

    public static RetuneException AccountInactive(string status) =>
        new(ProblemKind.AccountInactive, $"This account is {status.ToLowerInvariant()}.");

    public static RetuneException Http(int status) =>
        new(ProblemKind.Http, $"The server responded with an error (HTTP {status}).") { HttpStatus = status };

    public static RetuneException EmptyResponse() =>
        new(ProblemKind.EmptyResponse, "The server returned an empty response. It may be rate limiting — try again in a moment.");

    public static RetuneException UnreadableResponse() =>
        new(ProblemKind.UnreadableResponse, "The server returned data Retune couldn't read.");

    public static RetuneException EmptyPlaylist() =>
        new(ProblemKind.EmptyPlaylist, "No playable entries were found in this playlist.");

    public static RetuneException MissingPassword() =>
        new(ProblemKind.MissingPassword, "The saved password for this playlist is missing. Edit the playlist to re-enter it.");

    public static RetuneException SecretStore(int code) =>
        new(ProblemKind.SecretStore, $"The password couldn't be saved to Windows Credential Manager (error {code}).");

    public static RetuneException ResponseTooLarge() =>
        new(ProblemKind.ResponseTooLarge, "The server sent far more data than a playlist should contain.");

    public static RetuneException Timeout(Exception? inner = null) =>
        new(ProblemKind.Timeout, "The server took too long to respond.", inner);

    public static RetuneException ConnectionLost(Exception? inner = null) =>
        new(ProblemKind.ConnectionLost, "The connection to the server was lost.", inner);

    /// <param name="message">A platform message that has been checked to contain no URL.</param>
    public static RetuneException Network(string message, Exception? inner = null) => new(ProblemKind.Network, message, inner);
}
