using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Mindie.Services;

internal interface INvidiaKeyStore
{
    string? Read();
    void Save(string key);
    void Delete();
}

// The library and its exports contain only provider preferences, never credentials.
internal sealed class NvidiaCredentials : INvidiaKeyStore
{
    internal const string Target = "Mindie/NVIDIA";
    public string? Read()
    {
        if (!CredRead(Target,1,0,out var pointer)) {
            var error=Marshal.GetLastWin32Error();
            if(error==1168) return null;
            throw new Win32Exception(error,"No se pudo leer la clave de NVIDIA en el Administrador de credenciales de Windows.");
        }
        try {
            var credential=Marshal.PtrToStructure<Credential>(pointer);
            return credential.BlobSize==0?null:Marshal.PtrToStringUni(credential.Blob,credential.BlobSize/2);
        }
        finally { CredFree(pointer); }
    }
    public void Save(string key)
    {
        key=key.Trim();
        if(key.Length is < 10 or > 2000 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl)) throw new ArgumentException("Introduce una clave API válida, sin espacios ni el prefijo Bearer.");
        var pointer=Marshal.StringToCoTaskMemUni(key);
        try {
            var credential=new Credential { Type=1,TargetName=Target,BlobSize=Encoding.Unicode.GetByteCount(key),Blob=pointer,Persist=2,UserName="NVIDIA" };
            if(!CredWrite(ref credential,0)) throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows no pudo guardar la clave de NVIDIA. El cambio no se ha aplicado.");
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(pointer); }
    }
    public void Delete()
    {
        if(!CredDelete(Target,1,0) && Marshal.GetLastWin32Error()!=1168) throw new Win32Exception(Marshal.GetLastWin32Error(),"Windows no pudo eliminar la clave de NVIDIA.");
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags,Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int BlobSize;
        public IntPtr Blob;
        public uint Persist,AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target,uint type,uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr pointer);
}
