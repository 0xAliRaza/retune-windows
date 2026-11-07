using System.Runtime.InteropServices;
using System.Text;
using Retune.Core.Errors;
using Retune.Core.Persistence;

namespace Retune.Infrastructure.Secrets;

/// <summary>
/// Xtream passwords in Windows Credential Manager (generic credentials, per user, encrypted by Windows),
/// named <c>&lt;prefix&gt;&lt;playlist uuid&gt;</c>. Visible under Control Panel › Credential Manager › Windows Credentials.
/// </summary>
public sealed partial class CredentialManagerSecretStore(string targetPrefix = CredentialManagerSecretStore.DefaultPrefix) : ISecretStore
{
    public const string DefaultPrefix = "Retune/";

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? Find(Guid playlistId)
    {
        if (!CredRead(Target(playlistId), CredTypeGeneric, 0, out var handle))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            if (credential.CredentialBlob == 0 || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(handle);
        }
    }

    public void Store(Guid playlistId, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var blob = Encoding.Unicode.GetBytes(secret);
        var target = Marshal.StringToHGlobalUni(Target(playlistId));
        var user = Marshal.StringToHGlobalUni("Retune");
        var blobPointer = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                UserName = user,
                CredentialBlob = blobPointer,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CredPersistLocalMachine,
            };

            if (!CredWrite(ref credential, 0))
            {
                throw RetuneException.SecretStore(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            // Wipe the plaintext copy before releasing the memory.
            Marshal.Copy(new byte[blob.Length], 0, blobPointer, blob.Length);
            Marshal.FreeHGlobal(blobPointer);
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(user);
            Array.Clear(blob);
        }
    }

    public void Remove(Guid playlistId)
    {
        if (!CredDelete(Target(playlistId), CredTypeGeneric, 0) && Marshal.GetLastPInvokeError() != ErrorNotFound)
        {
            throw RetuneException.SecretStore(Marshal.GetLastPInvokeError());
        }
    }

    private string Target(Guid playlistId) => $"{targetPrefix}{playlistId:D}";

    /// <summary>CREDENTIALW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref Credential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(nint buffer);
}
