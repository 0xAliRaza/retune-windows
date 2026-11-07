namespace Retune.Core.Models;

/// <summary>Where a playlist's content comes from. The Xtream password is never part of it.</summary>
public abstract record PlaylistSource;

/// <param name="Server">A normalised server base URL (see <c>XtreamServer.Normalize</c>).</param>
public sealed record XtreamSource(string Server, string Username) : PlaylistSource;

/// <param name="Location">An http(s) URL, or a local file path for an M3U file.</param>
public sealed record M3uSource(string Location) : PlaylistSource
{
    public bool IsLocalFile => !Policies.UrlPolicy.HasHttpScheme(Location);
}

/// <summary>A user-configured source.</summary>
public sealed record Playlist(Guid Id, string Name, PlaylistSource Source, DateTimeOffset CreatedAt)
{
    public string KindLabel => Source switch
    {
        XtreamSource => "Xtream Codes",
        M3uSource { IsLocalFile: true } => "M3U File",
        _ => "M3U URL",
    };

    /// <summary>Series, EPG and account info exist only for Xtream.</summary>
    public bool SupportsSeries => Source is XtreamSource;
}

/// <summary>Full Xtream login. <see cref="ToString"/> never reveals the password.</summary>
public sealed record XtreamCredentials(string Server, string Username, string Password)
{
    public override string ToString() => $"XtreamCredentials {{ Server = {Server}, Username = {Username}, Password = *** }}";
}
