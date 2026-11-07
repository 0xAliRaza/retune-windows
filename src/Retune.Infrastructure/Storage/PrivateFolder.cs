using System.Security.AccessControl;
using System.Security.Principal;

namespace Retune.Infrastructure.Storage;

/// <summary>
/// Creates folders readable only by the current user (plus SYSTEM, which Windows needs for backups and search),
/// with inheritance so every file written inside is private too. Mirrors the macOS 0700/0600 rule.
/// </summary>
internal static class PrivateFolder
{
    public static void Ensure(string path)
    {
        var info = Directory.CreateDirectory(path);
        Restrict(info);
    }

    public static void Restrict(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No current Windows user.");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    /// <summary>True when only the current user and SYSTEM have access rules (for tests and self-checks).</summary>
    public static bool IsPrivate(string path)
    {
        var user = WindowsIdentity.GetCurrent().User;
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var rules = new DirectoryInfo(path).GetAccessControl().GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        return rules.Cast<FileSystemAccessRule>().All(r => r.IdentityReference.Equals(user) || r.IdentityReference.Equals(system));
    }
}
