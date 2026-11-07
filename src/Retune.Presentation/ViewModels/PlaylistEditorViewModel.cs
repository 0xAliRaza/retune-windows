using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Retune.Core.Errors;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Core.Policies;
using Retune.Core.Xtream;
using Retune.Presentation.Services;

namespace Retune.Presentation.ViewModels;

public enum PlaylistKind
{
    Xtream,
    M3uUrl,
    M3uFile,
}

/// <summary>Add / Edit Playlist. Xtream logins are verified against the server before anything is saved.</summary>
public sealed partial class PlaylistEditorViewModel : ObservableObject
{
    public const string PlainHttpWarning = "This address uses plain HTTP, so your login travels unencrypted. Use an https:// address if your provider offers one.";

    private readonly PlaylistController _controller;
    private readonly TimeProvider _time;
    private readonly Playlist? _existing;

    public PlaylistEditorViewModel(PlaylistController controller, TimeProvider time, Playlist? existing)
    {
        _controller = controller;
        _time = time;
        _existing = existing;
        switch (existing?.Source)
        {
            case XtreamSource x:
                Kind = PlaylistKind.Xtream;
                Server = x.Server;
                Username = x.Username;
                break;
            case M3uSource { IsLocalFile: true } f:
                Kind = PlaylistKind.M3uFile;
                FilePath = f.Location;
                break;
            case M3uSource u:
                Kind = PlaylistKind.M3uUrl;
                Url = u.Location;
                break;
            default:
                break;
        }

        Name = existing?.Name ?? string.Empty;
    }

    /// <summary>Raised after a successful save; the dialog closes.</summary>
    public event EventHandler? Saved;

    public bool IsEditing => _existing is not null;

    public string Heading => IsEditing ? "Edit Playlist" : "Add Playlist";

    public string SaveLabel => IsEditing ? "Save" : "Add";

    public string PasswordPlaceholder => IsEditing ? "Unchanged" : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPlainHttpWarning), nameof(SuggestsXtream), nameof(IsValid), nameof(IsXtream), nameof(IsM3uUrl), nameof(IsM3uFile))]
    public partial PlaylistKind Kind { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPlainHttpWarning), nameof(IsValid))]
    public partial string Server { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPlainHttpWarning), nameof(SuggestsXtream), nameof(IsValid))]
    public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid), nameof(FileName))]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    public bool IsXtream => Kind == PlaylistKind.Xtream;

    public bool IsM3uUrl => Kind == PlaylistKind.M3uUrl;

    public bool IsM3uFile => Kind == PlaylistKind.M3uFile;

    public string FileName => FilePath is null ? "No file chosen" : Path.GetFileName(FilePath);

    public bool ShowsPlainHttpWarning => Kind switch
    {
        PlaylistKind.Xtream => XtreamServer.UsesPlainHttp(Server),
        PlaylistKind.M3uUrl => LenientUrl.Parse(Url) is { Scheme: "http" },
        _ => false,
    };

    public bool SuggestsXtream => Kind == PlaylistKind.M3uUrl && XtreamServer.ParseLink(Url) is not null;

    public bool IsValid => !IsBusy && Kind switch
    {
        PlaylistKind.Xtream => XtreamServer.Normalize(Server) is not null && !string.IsNullOrWhiteSpace(Username) && (IsEditing || Password.Length > 0),
        PlaylistKind.M3uUrl => LenientUrl.Parse(Url) is not null,
        _ => FilePath is not null,
    };

    /// <summary>Pasting a full provider link into Server fills in the username and password too.</summary>
    partial void OnServerChanged(string value)
    {
        if (XtreamServer.ParseLink(value) is { } link)
        {
            Server = link.Server;
            Username = link.Username;
            Password = link.Password;
        }
    }

    /// <summary>A chosen file names the playlist when no name was typed.</summary>
    partial void OnFilePathChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(Name) && value is not null)
        {
            Name = Path.GetFileNameWithoutExtension(value);
        }
    }

    [RelayCommand]
    private void UseXtreamInstead()
    {
        var link = Url;
        Kind = PlaylistKind.Xtream;
        Server = link;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!IsValid)
        {
            return;
        }

        Error = null;
        IsBusy = true;
        try
        {
            var (playlist, password) = Build();
            if (_existing is null)
            {
                await _controller.AddAsync(playlist, password).ConfigureAwait(true);
            }
            else
            {
                await _controller.UpdateAsync(playlist, password).ConfigureAwait(true);
            }

            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (RetuneException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private (Playlist Playlist, string? Password) Build()
    {
        var id = _existing?.Id ?? Guid.NewGuid();
        var created = _existing?.CreatedAt ?? _time.GetUtcNow();
        switch (Kind)
        {
            case PlaylistKind.Xtream:
                var server = XtreamServer.Normalize(Server) ?? throw RetuneException.InvalidServer();
                var host = Uri.TryCreate(server, UriKind.Absolute, out var u) ? u.Host : null;
                return (new Playlist(id, NameOr(host ?? "Xtream"), new XtreamSource(server, Username.Trim()), created), Password.Length > 0 ? Password : null);
            case PlaylistKind.M3uUrl:
                var url = LenientUrl.Parse(Url)!;
                return (new Playlist(id, NameOr(url.Host), new M3uSource(url.AbsoluteUri), created), null);
            default:
                return (new Playlist(id, NameOr(Path.GetFileNameWithoutExtension(FilePath!)), new M3uSource(FilePath!), created), null);
        }
    }

    private string NameOr(string fallback) => string.IsNullOrWhiteSpace(Name) ? fallback : Name.Trim();
}

/// <summary>"Create a PIN": 4–8 digits, typed twice.</summary>
public sealed partial class CreatePinViewModel(LibraryService library) : ObservableObject
{
    [ObservableProperty]
    public partial string Pin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Confirm { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>Sets the PIN and returns true, or shows why not.</summary>
    public bool Submit()
    {
        if (!Core.Parental.PinHash.IsValidFormat(Pin))
        {
            Error = "Use 4 to 8 digits.";
            return false;
        }

        if (Pin != Confirm)
        {
            Error = "The PINs don't match.";
            return false;
        }

        library.Update(s => s.WithPinHash(Core.Parental.PinHash.Create(Pin)));
        return true;
    }
}

/// <summary>"Enter PIN": with the attempt limiter's lockout message.</summary>
public sealed partial class VerifyPinViewModel(LibraryService library, string reason) : ObservableObject
{
    public string Reason { get; } = reason;

    [ObservableProperty]
    public partial string Pin { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    public bool Submit()
    {
        if (!library.Pins.CanAttempt)
        {
            Error = library.Pins.RejectionMessage;
            return false;
        }

        if (library.CheckPin(Pin) != Core.Parental.PinCheck.Wrong)
        {
            return true;
        }

        Pin = string.Empty;
        Error = library.Pins.RejectionMessage;
        return false;
    }
}
