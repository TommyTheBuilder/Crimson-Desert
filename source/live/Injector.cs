using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class PywelInjector
{
    const uint PROCESS_CREATE_THREAD=0x0002, PROCESS_QUERY_INFORMATION=0x0400, PROCESS_VM_OPERATION=0x0008, PROCESS_VM_WRITE=0x0020, PROCESS_VM_READ=0x0010;
    const uint MEM_COMMIT=0x1000, MEM_RESERVE=0x2000, MEM_RELEASE=0x8000, PAGE_READWRITE=0x04, WAIT_OBJECT_0=0;
    const uint TH32CS_SNAPMODULE=0x00000008, TH32CS_SNAPMODULE32=0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct MODULEENTRY32 {
        public uint dwSize, th32ModuleID, th32ProcessID, GlblcntUsage, ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string szExePath;
    }

    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint allocation,uint protect);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool VirtualFreeEx(IntPtr process,IntPtr address,UIntPtr size,uint freeType);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] buffer,UIntPtr size,out UIntPtr written);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr CreateRemoteThread(IntPtr process,IntPtr attrs,UIntPtr stack,IntPtr start,IntPtr parameter,uint flags,out uint threadId);
    [DllImport("kernel32.dll", SetLastError=true)] static extern uint WaitForSingleObject(IntPtr h,uint ms);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref uint size);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi, SetLastError=true)] static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,int pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool Module32FirstW(IntPtr snapshot,ref MODULEENTRY32 module);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool Module32NextW(IntPtr snapshot,ref MODULEENTRY32 module);

    static string Full(string p){ return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant(); }
    static string ProcessPath(IntPtr process){ uint n=32768; var b=new StringBuilder((int)n); if(!QueryFullProcessImageName(process,0,b,ref n))throw new Win32Exception(Marshal.GetLastWin32Error()); return Path.GetFullPath(b.ToString()); }
    static IntPtr RemoteModule(int pid,string name,string fullPath){
        IntPtr snap=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE|TH32CS_SNAPMODULE32,pid); if(snap==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
        try{
            var m=new MODULEENTRY32(); m.dwSize=(uint)Marshal.SizeOf(typeof(MODULEENTRY32));
            if(!Module32FirstW(snap,ref m))return IntPtr.Zero;
            do{
                if(!String.IsNullOrEmpty(fullPath) && !String.IsNullOrEmpty(m.szExePath) && Full(m.szExePath)==Full(fullPath))return m.modBaseAddr;
                if(String.Equals(m.szModule,name,StringComparison.OrdinalIgnoreCase))return m.modBaseAddr;
                m.dwSize=(uint)Marshal.SizeOf(typeof(MODULEENTRY32));
            }while(Module32NextW(snap,ref m));
            return IntPtr.Zero;
        }finally{CloseHandle(snap);}
    }

    public static int Main(string[] args){
        try{
            if(args.Length<2){Console.WriteLine("ERR usage: PywelInjector.exe <pid> <dll> [expected CrimsonDesert.exe]");return 2;}
            int pid; if(!Int32.TryParse(args[0],out pid)||pid<1)throw new ArgumentException("Ungültige Prozessnummer.");
            string dll=Path.GetFullPath(args[1]); if(!File.Exists(dll))throw new FileNotFoundException("PywelLive.dll wurde nicht gefunden.",dll);
            string expected=args.Length>=3&&!String.IsNullOrWhiteSpace(args[2])?Path.GetFullPath(args[2]):null;
            IntPtr process=OpenProcess(PROCESS_CREATE_THREAD|PROCESS_QUERY_INFORMATION|PROCESS_VM_OPERATION|PROCESS_VM_WRITE|PROCESS_VM_READ,false,pid);
            if(process==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Crimson Desert konnte nicht für den Live-Mod geöffnet werden.");
            try{
                string actual=ProcessPath(process);
                if(!String.Equals(Path.GetFileName(actual),"CrimsonDesert.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Zielprozess ist nicht CrimsonDesert.exe: "+actual);
                if(expected!=null && !String.Equals(Full(actual),Full(expected),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Der laufende Spielprozess gehört zu einer anderen Installation: "+actual);
                if(RemoteModule(pid,Path.GetFileName(dll),dll)!=IntPtr.Zero){Console.WriteLine("OK already-loaded");return 0;}

                IntPtr localKernel=GetModuleHandle("kernel32.dll"); if(localKernel==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr localLoad=GetProcAddress(localKernel,"LoadLibraryW"); if(localLoad==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr remoteKernel=RemoteModule(pid,"kernel32.dll",null); if(remoteKernel==IntPtr.Zero)throw new InvalidDataException("kernel32.dll wurde im Zielprozess nicht gefunden.");
                long loadRva=localLoad.ToInt64()-localKernel.ToInt64(); IntPtr remoteLoad=new IntPtr(remoteKernel.ToInt64()+loadRva);

                byte[] text=Encoding.Unicode.GetBytes(dll+"\0");
                IntPtr remote=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)text.Length,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE); if(remote==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Speicher für den DLL-Pfad konnte nicht reserviert werden.");
                try{
                    UIntPtr written; if(!WriteProcessMemory(process,remote,text,(UIntPtr)text.Length,out written)||written.ToUInt64()!=(ulong)text.Length)throw new Win32Exception(Marshal.GetLastWin32Error(),"DLL-Pfad konnte nicht in den Zielprozess geschrieben werden.");
                    uint tid; IntPtr thread=CreateRemoteThread(process,IntPtr.Zero,UIntPtr.Zero,remoteLoad,remote,0,out tid); if(thread==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"LoadLibrary-Thread konnte nicht gestartet werden.");
                    try{ if(WaitForSingleObject(thread,15000)!=WAIT_OBJECT_0)throw new TimeoutException("Das Laden der Live-DLL hat zu lange gedauert."); }
                    finally{CloseHandle(thread);}
                }finally{VirtualFreeEx(process,remote,UIntPtr.Zero,MEM_RELEASE);}
                if(RemoteModule(pid,Path.GetFileName(dll),dll)==IntPtr.Zero)throw new InvalidOperationException("Windows hat PywelLive.dll nicht als geladenes Modul bestätigt.");
                Console.WriteLine("OK injected"); return 0;
            }finally{CloseHandle(process);}
        }catch(Exception e){Console.WriteLine("ERR "+e.Message);return 1;}
    }
}