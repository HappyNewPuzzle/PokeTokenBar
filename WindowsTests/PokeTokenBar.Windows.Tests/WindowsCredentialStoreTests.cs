using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using PokeTokenBar.Windows.Infrastructure;

namespace PokeTokenBar.Windows.Tests;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void TemporaryGenericCredentialCanBeCreatedReadAndDeleted()
    {
        var target = $"PokeTokenBar.Tests.{Guid.NewGuid():N}";
        var value = "fixture-secret-never-logged";
        var bytes = Encoding.UTF8.GetBytes(value);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var credential = new NativeCredential
            {
                Type = 1,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = handle.AddrOfPinnedObject(),
                Persist = 1,
                UserName = "PokeTokenBar.Tests",
            };
            Assert.True(CredWrite(ref credential, 0),
                new Win32Exception(Marshal.GetLastWin32Error()).Message);
            Assert.Equal(value, WindowsCredentialStore.Read(target));
        }
        finally
        {
            CredDelete(target, 1, 0);
            handle.Free();
        }

        Assert.Null(WindowsCredentialStore.Read(target));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
}
