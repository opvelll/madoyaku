using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace HonnyakuKun.Services;

public static class WindowsCredentialStore
{
    private const string TargetName = "HonnyakuKun/OpenAI";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static string? ReadApiKey()
    {
        if (!CredRead(TargetName, CredTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "Windows資格情報マネージャーからAPIキーを読み込めませんでした。");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char)));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public static void WriteApiKey(string apiKey)
    {
        var normalized = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            DeleteApiKey();
            return;
        }

        var secretBytes = Encoding.Unicode.GetBytes(normalized);
        var secretPointer = Marshal.AllocHGlobal(secretBytes.Length);

        try
        {
            Marshal.Copy(secretBytes, 0, secretPointer, secretBytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = TargetName,
                CredentialBlobSize = checked((uint)secretBytes.Length),
                CredentialBlob = secretPointer,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows資格情報マネージャーにAPIキーを保存できませんでした。");
            }
        }
        finally
        {
            Array.Clear(secretBytes);
            for (var index = 0; index < secretBytes.Length; index++)
            {
                Marshal.WriteByte(secretPointer, index, 0);
            }

            Marshal.FreeHGlobal(secretPointer);
        }
    }

    public static void DeleteApiKey()
    {
        if (CredDelete(TargetName, CredTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error, "Windows資格情報マネージャーからAPIキーを削除できませんでした。");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
