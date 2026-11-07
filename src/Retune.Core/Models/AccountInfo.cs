namespace Retune.Core.Models;

/// <summary>Xtream account details from authentication. Held in memory only, never persisted.</summary>
public sealed record AccountInfo(
    string Status = "Unknown",
    string? Message = null,
    DateTimeOffset? ExpiresAt = null,
    bool IsTrial = false,
    int? ActiveConnections = null,
    int? MaxConnections = null,
    IReadOnlyList<string>? AllowedFormats = null,
    string? ServerTimezone = null)
{
    public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> Formats => AllowedFormats ?? [];
}
