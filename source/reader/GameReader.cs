// External read-only reader for the installed Crimson Desert 1.0.0.2760 layout.
// No injection, remote execution, hooks, writes, or write-capable process handle.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

internal interface IMemoryReader
{
    byte[] Read(ulong address, int count);
    string CString(ulong address, int limit);
}

internal static class Values
{
    internal static Dictionary<string, object> D(params object[] pairs)
    {
        var result = new Dictionary<string, object>();
        for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
        return result;
    }
    internal static bool Pointer(ulong p) { return p >= 0x10000 && p <= 0x7FFFFFFFFFFF; }
    internal static ulong P(byte[] b, int at) { return BitConverter.ToUInt64(b, at); }
    internal static uint U32(byte[] b, int at) { return BitConverter.ToUInt32(b, at); }
    internal static ushort U16(byte[] b, int at) { return BitConverter.ToUInt16(b, at); }
    internal static long I64(byte[] b, int at) { return BitConverter.ToInt64(b, at); }
    internal static string Hex(ulong value) { return "0x" + value.ToString("x", CultureInfo.InvariantCulture); }
    internal static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    internal static int Bound(uint value, int minimum, int maximum, string label)
    {
        if (value < minimum || value > maximum) throw new InvalidDataException(label + " ist noch nicht lesbar.");
        return (int)value;
    }
    internal static string Long(long value) { return value.ToString(CultureInfo.InvariantCulture); }
}

internal sealed class ImageSpan
{
    internal readonly int Offset, Length;
    internal ImageSpan(int offset, int length) { Offset = offset; Length = length; }
}

internal static class NativeMethods
{
    // This is the ONLY process access mask requested by this executable.
    internal const uint ReadOnlyAccess = 0x0410; // VM_READ | QUERY_INFORMATION (VirtualQueryEx requires QUERY_INFORMATION)
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryInfo info, UIntPtr length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Module32FirstW(IntPtr snapshot, ref ModuleInfo module);
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryInfo
    {
        internal IntPtr BaseAddress, AllocationBase;
        internal uint AllocationProtect;
        internal UIntPtr RegionSize;
        internal uint State, Protect, Type;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct ModuleInfo
    {
        internal uint Size, ModuleId, ProcessId, GlobalUsage, ProcessUsage;
        internal IntPtr BaseAddress;
        internal uint BaseSize;
        internal IntPtr Module;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Path;
    }
}

internal sealed class NativeMemory : IMemoryReader, IDisposable
{
    private IntPtr handle;
    private readonly int pid;
    private readonly string expectedPath;
    private readonly long creationTime;
    internal ulong BaseAddress { get; private set; }
    internal uint ImageSize { get; private set; }
    internal int ProcessId { get { return pid; } }
    internal NativeMemory(int processId, string executable)
    {
        if (IntPtr.Size != 8) throw new InvalidOperationException("Der Leser muss als 64-Bit-Programm gestartet werden.");
        if (processId <= 0) throw new ArgumentException("Ungültige Prozessnummer.");
        pid = processId; expectedPath = Path.GetFullPath(executable);
        if (!String.Equals(Path.GetFileName(expectedPath), "CrimsonDesert.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Die ausgewählte Datei ist nicht CrimsonDesert.exe.");
        var version = FileVersionInfo.GetVersionInfo(expectedPath);
        if (version.FileMajorPart != 1 || version.FileMinorPart != 0 || version.FileBuildPart != 0 || version.FilePrivatePart != 2760) throw new InvalidDataException("Diese Spielversion wird vom externen Leser noch nicht unterstützt.");
        handle = NativeMethods.OpenProcess(NativeMethods.ReadOnlyAccess, false, pid);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Das Spiel konnte nicht zum Lesen geöffnet werden.");
        try
        {
            if (!String.Equals(ProcessPath(), expectedPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Prozess und ausgewählte Spieldatei stimmen nicht überein.");
            creationTime = StartTime();
            IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(0x18, pid);
            if (snapshot == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Das Hauptmodul konnte nicht gelesen werden.");
            try
            {
                var module = new NativeMethods.ModuleInfo(); module.Size = (uint)Marshal.SizeOf(typeof(NativeMethods.ModuleInfo));
                if (!NativeMethods.Module32FirstW(snapshot, ref module)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!String.Equals(Path.GetFullPath(module.Path), expectedPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Das Hauptmodul stimmt nicht mit dem Spiel überein.");
                BaseAddress = (ulong)module.BaseAddress.ToInt64(); ImageSize = module.BaseSize;
                if (!Values.Pointer(BaseAddress) || ImageSize != 0x16F1F000) throw new InvalidDataException("Die Spielstruktur passt nicht zum geprüften Profil.");
            }
            finally { NativeMethods.CloseHandle(snapshot); }
            byte[] live = Read(BaseAddress, 4096), disk = new byte[4096];
            using (var stream = File.OpenRead(expectedPath)) { int n = stream.Read(disk, 0, disk.Length); if (n != disk.Length) throw new InvalidDataException("Die Spieldatei ist unvollständig."); }
            int pe = BitConverter.ToInt32(live, 0x3C);
            if (pe < 64 || pe > 2048 || live[0] != 0x4D || live[1] != 0x5A || BitConverter.ToUInt32(live, pe) != 0x4550 || Values.U16(live, pe + 4) != 0x8664 || Values.U16(live, pe + 24) != 0x20B) throw new InvalidDataException("Ungültiges 64-Bit-Hauptmodul.");
            if (BitConverter.ToInt32(disk, 0x3C) != pe || Values.U32(live, pe + 8) != Values.U32(disk, pe + 8) || Values.U32(live, pe + 24 + 56) != ImageSize || Values.U32(disk, pe + 24 + 56) != ImageSize) throw new InvalidDataException("Geladenes Spiel und Datei haben unterschiedliche Versionen.");
            ValidateIdentity();
        }
        catch { Dispose(); throw; }
    }
    private string ProcessPath()
    {
        uint length = 32768; var path = new StringBuilder((int)length);
        if (!NativeMethods.QueryFullProcessImageName(handle, 0, path, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return Path.GetFullPath(path.ToString());
    }
    private long StartTime()
    {
        long created, exit, kernel, user;
        if (!NativeMethods.GetProcessTimes(handle, out created, out exit, out kernel, out user)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return created;
    }
    internal void ValidateIdentity()
    {
        uint code;
        if (handle == IntPtr.Zero || !NativeMethods.GetExitCodeProcess(handle, out code) || code != 259 || StartTime() != creationTime || !String.Equals(ProcessPath(), expectedPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Das Spiel wurde beendet oder neu gestartet. Erneut verbinden.");
    }
    private NativeMethods.MemoryInfo Query(ulong address)
    {
        NativeMethods.MemoryInfo info;
        if (NativeMethods.VirtualQueryEx(handle, new IntPtr((long)address), out info, (UIntPtr)Marshal.SizeOf(typeof(NativeMethods.MemoryInfo))) == UIntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Spielbereich ist nicht lesbar.");
        return info;
    }
    private static bool Readable(NativeMethods.MemoryInfo info)
    {
        uint p = info.Protect & 255;
        return info.State == 0x1000 && (info.Protect & 0x100) == 0 && (p == 2 || p == 4 || p == 8 || p == 0x20 || p == 0x40 || p == 0x80);
    }
    public byte[] Read(ulong address, int count)
    {
        if (handle == IntPtr.Zero) throw new ObjectDisposedException("Spielverbindung");
        if (!Values.Pointer(address) || count < 0 || count > 32 * 1024 * 1024 || address > 0x7FFFFFFFFFFFUL - (ulong)count) throw new InvalidDataException("Ungültiger Lesebereich.");
        byte[] result = new byte[count]; if (count == 0) return result;
        UIntPtr read;
        if (!NativeMethods.ReadProcessMemory(handle, new IntPtr((long)address), result, (UIntPtr)count, out read) || read.ToUInt64() != (ulong)count) throw new InvalidDataException("Spielbereich wird gerade geändert oder ist nicht lesbar.");
        return result;
    }
    public string CString(ulong address, int limit)
    {
        if (limit < 1 || limit > 4096) throw new InvalidDataException("Ungültige Zeichenkettengrenze.");
        var info = Query(address); if (!Readable(info)) throw new InvalidDataException("Text ist noch nicht lesbar.");
        ulong end = (ulong)info.BaseAddress.ToInt64() + info.RegionSize.ToUInt64();
        int count = (int)Math.Min((ulong)limit, end - address); byte[] bytes = Read(address, count);
        int zero = Array.IndexOf(bytes, (byte)0); if (zero < 0) throw new InvalidDataException("Spieltext ist unvollständig.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, zero);
    }
    internal byte[] ReadImage(out List<ImageSpan> spans)
    {
        ValidateIdentity(); spans = new List<ImageSpan>(); byte[] image = new byte[checked((int)ImageSize)];
        ulong at = BaseAddress, imageEnd = BaseAddress + ImageSize;
        while (at < imageEnd)
        {
            var info = Query(at); ulong regionStart = (ulong)info.BaseAddress.ToInt64(), end = regionStart + info.RegionSize.ToUInt64();
            if (end <= at || regionStart > at) throw new InvalidDataException("Die Modulbereiche konnten nicht vollständig geprüft werden.");
            ulong stop = Math.Min(end, imageEnd);
            if (Readable(info))
            {
                int offset = (int)(at - BaseAddress), length = (int)(stop - at);
                if (spans.Count > 0 && spans[spans.Count - 1].Offset + spans[spans.Count - 1].Length == offset)
                {
                    ImageSpan previous = spans[spans.Count - 1]; spans[spans.Count - 1] = new ImageSpan(previous.Offset, previous.Length + length);
                }
                else spans.Add(new ImageSpan(offset, length));
                for (ulong part = at; part < stop;)
                {
                    int size = (int)Math.Min(4UL * 1024 * 1024, stop - part); byte[] bytes = Read(part, size);
                    Buffer.BlockCopy(bytes, 0, image, (int)(part - BaseAddress), size); part += (ulong)size;
                }
            }
            else if (info.State == 0x1000 && (info.Protect & 0xF0) != 0) throw new InvalidDataException("Ein ausführbarer Spielbereich konnte nicht vollständig gelesen werden.");
            at = stop;
        }
        // No unreadable executable page may hide a second signature match.
        int pe = BitConverter.ToInt32(image, 0x3C), sections = Values.U16(image, pe + 6), sectionTable = pe + 24 + Values.U16(image, pe + 20);
        if (sections < 1 || sections > 96 || sectionTable > 4096 - sections * 40) throw new InvalidDataException("Die Modultabelle ist ungültig.");
        for (int i = 0; i < sections; i++)
        {
            int header = sectionTable + i * 40; if ((Values.U32(image, header + 36) & 0x20000000) == 0) continue;
            uint offset = Values.U32(image, header + 12), length = Values.U32(image, header + 8);
            if (length == 0) length = Values.U32(image, header + 16);
            if (offset > ImageSize || length > ImageSize - offset) throw new InvalidDataException("Ein Codebereich liegt außerhalb des Hauptmoduls.");
            bool covered = length == 0;
            foreach (ImageSpan span in spans) if (offset >= span.Offset && (ulong)offset + length <= (ulong)span.Offset + (ulong)span.Length) { covered = true; break; }
            if (!covered) throw new InvalidDataException("Ein Codebereich konnte nicht vollständig gelesen werden.");
        }
        ValidateIdentity(); return image;
    }
    public void Dispose() { if (handle != IntPtr.Zero) { NativeMethods.CloseHandle(handle); handle = IntPtr.Zero; } }
}

internal sealed class ScanResult
{
    internal ulong ManagerGlobal, ItemGlobal, GroupGlobal, StorageGlobal;
    internal readonly List<Dictionary<string, object>> Diagnostics = new List<Dictionary<string, object>>();
}

internal static class ProfileScanner
{
    internal const string Anchor90 = "44 8B 82 90 00 00 00 48 8D 54 24 58 48 8B 0D ?? ?? ?? ?? 48 8B 09 E8 ?? ?? ?? ?? 90 80 7C 24 68 00";
    internal const string Anchor180 = "40 53 48 83 EC 40 44 8B 81 80 01 00 00 48 8D 54 24 20 48 8B 0D ?? ?? ?? ?? 48 8B 09 E8 ?? ?? ?? ?? 90 80 7C 24 30 00 75 04 33 DB EB 1B 48 8B 44 24 28 48 8B 88 D0 00 00 00 48 8B 41 68 48 8B 88 38 01 00 00";
    internal const string TablePattern = "48 89 5C 24 10 48 89 6C 24 18 56 57 41 56 48 83 EC 50 0F B7 39 48 8B 1D";
    internal static List<int> Find(byte[] image, List<ImageSpan> spans, string pattern)
    {
        string[] tokens = pattern.Split(' '); int[] p = new int[tokens.Length];
        int anchorStart = 0, anchorLength = 0, start = 0, length = 0;
        for (int i = 0; i < p.Length; i++)
        {
            p[i] = tokens[i] == "??" ? -1 : Int32.Parse(tokens[i], NumberStyles.HexNumber);
            if (p[i] >= 0) { if (length == 0) start = i; length++; if (length > anchorLength) { anchorStart = start; anchorLength = length; } }
            else length = 0;
        }
        var results = new List<int>();
        foreach (var span in spans)
        {
            if (span.Offset < 0 || span.Length < 0 || span.Offset > image.Length - span.Length) throw new InvalidDataException("Ungültiger Scanbereich.");
            int last = span.Offset + span.Length - p.Length;
            for (int at = span.Offset; at <= last; at++)
            {
                if (image[at + anchorStart] != p[anchorStart]) continue;
                int j = 1; for (; j < anchorLength; j++) if (image[at + anchorStart + j] != p[anchorStart + j]) break;
                if (j != anchorLength) continue;
                for (j = 0; j < p.Length; j++) if (p[j] >= 0 && image[at + j] != p[j]) break;
                if (j == p.Length) results.Add(at);
            }
        }
        return results;
    }
    private static ulong Rip(byte[] image, int at, ulong moduleBase)
    {
        if (at < 0 || at > image.Length - 7) throw new InvalidDataException("Ungültige Signatur.");
        long target = (long)moduleBase + at + 7 + BitConverter.ToInt32(image, at + 3);
        if (target < (long)moduleBase || target >= (long)moduleBase + image.Length) throw new InvalidDataException("Signatur verweist außerhalb des Spiels.");
        return (ulong)target;
    }
    internal static ScanResult Scan(byte[] image, List<ImageSpan> spans, ulong moduleBase, IMemoryReader memory)
    {
        var result = new ScanResult(); var a = Find(image, spans, Anchor90); var b = Find(image, spans, Anchor180);
        result.Diagnostics.Add(Values.D("name", "Spielerkennung 1", "matches", a.Count)); result.Diagnostics.Add(Values.D("name", "Spielerkennung 2", "matches", b.Count));
        if (a.Count == 1 && b.Count == 1)
        {
            ulong first = Rip(image, a[0] + 12, moduleBase), second = Rip(image, b[0] + 18, moduleBase);
            if (first == second) result.ManagerGlobal = first;
        }
        var matches = Find(image, spans, TablePattern); var found = new Dictionary<string, List<ulong>>();
        found.Add("iteminfo", new List<ulong>()); found.Add("ItemGroupInfo", new List<ulong>()); found.Add("Inventory", new List<ulong>());
        foreach (int at in matches)
        {
            int nameAt = at + 0x52;
            if (nameAt > image.Length - 7 || image[nameAt] != 0x4C || image[nameAt + 2] != 5 || (image[nameAt + 1] != 0x8D && image[nameAt + 1] != 0x8B)) continue;
            ulong name = Rip(image, nameAt, moduleBase); bool indirect = image[nameAt + 1] == 0x8B;
            try
            {
                if (indirect) name = Values.P(memory.Read(name, 8), 0);
                string text = memory.CString(name, 64);
                if (found.ContainsKey(text) && (text == "Inventory") == indirect) found[text].Add(Rip(image, at + 0x15, moduleBase));
            }
            catch { throw new InvalidDataException("Tabellensignaturen konnten nicht vollständig geprüft werden."); }
        }
        foreach (var pair in found)
        {
            result.Diagnostics.Add(Values.D("name", pair.Key, "matches", pair.Value.Count));
            if (pair.Value.Count != 1) continue;
            foreach (ulong address in pair.Value)
            {
                if (pair.Key == "iteminfo") result.ItemGlobal = address;
                else if (pair.Key == "ItemGroupInfo") result.GroupGlobal = address;
                else result.StorageGlobal = address;
            }
        }
        return result;
    }
}

internal sealed class SnapshotReader
{
    private readonly IMemoryReader memory;
    private readonly ulong managerGlobal, itemGlobal, storageGlobal, moduleBase;
    private readonly uint moduleSize;
    private const string Reason = "Externe Leseverbindung: Änderungen am Spiel sind deaktiviert.";
    internal SnapshotReader(IMemoryReader memory, ulong managerGlobal, ulong itemGlobal, ulong storageGlobal, ulong moduleBase, uint moduleSize)
    { this.memory = memory; this.managerGlobal = managerGlobal; this.itemGlobal = itemGlobal; this.storageGlobal = storageGlobal; this.moduleBase = moduleBase; this.moduleSize = moduleSize; }
    private ulong Ptr(ulong address) { ulong p = Values.P(memory.Read(address, 8), 0); if (!Values.Pointer(p)) throw new InvalidDataException("Spielobjekt ist noch nicht verfügbar."); return p; }
    private static ulong PointerAt(byte[] bytes, int at) { ulong p = Values.P(bytes, at); if (!Values.Pointer(p)) throw new InvalidDataException("Spielobjekt ist noch nicht verfügbar."); return p; }
    private string EngineString(ulong address)
    {
        string key = memory.CString(Ptr(Ptr(address)), 512);
        if (String.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Gegenstandsschlüssel fehlt.");
        foreach (char c in key) if (Char.IsControl(c)) throw new InvalidDataException("Ungültiger Gegenstandsschlüssel.");
        return key;
    }
    private sealed class Identity { internal ulong Manager, Data, Owner, Actor, Marker, Root, Stats; internal uint Count, Capacity; internal byte TypeTag; internal byte[] StatBytes, OwnerBytes, Entries; }
    private Identity Resolve()
    {
        if (managerGlobal == 0) throw new InvalidDataException("Spielerstruktur wurde nicht eindeutig erkannt.");
        ulong manager = Ptr(Ptr(managerGlobal)); byte[] m = memory.Read(manager + 0xB8, 16);
        ulong data = PointerAt(m, 0); uint count = Values.U32(m, 8), capacity = Values.U32(m, 12);
        Values.Bound(count, 1, 8192, "Figurenliste"); if (capacity < count || capacity > 65536) throw new InvalidDataException("Figurenliste wird geändert.");
        byte[] entries = memory.Read(data, checked((int)count * 8)); var candidates = new List<ulong>(); var seen = new HashSet<ulong>();
        for (int i = 0; i < count; i++)
        {
            ulong owner = Values.P(entries, i * 8); if (owner == 0) continue;
            if (!Values.Pointer(owner) || !seen.Add(owner)) throw new InvalidDataException("Figurenliste ist nicht eindeutig.");
            byte[] obj = memory.Read(owner, 0xA8); ulong table = PointerAt(obj, 0);
            if (table < moduleBase || table >= moduleBase + moduleSize) throw new InvalidDataException("Figurentyp ist nicht bestätigt.");
            ulong descriptor = Values.P(obj, 0x88); if (descriptor == 0) continue;
            if (!Values.Pointer(descriptor)) throw new InvalidDataException("Figurentyp wird geändert.");
            byte tag = memory.Read(descriptor + 1, 1)[0]; if (tag != 1 && tag != 9) continue;
            ulong controller = Values.P(obj, 0xA0); if (controller == 0) continue;
            if (!Values.Pointer(controller)) throw new InvalidDataException("Spielfigur wird geändert.");
            if (Values.P(memory.Read(controller + 0xD0, 8), 0) == owner) candidates.Add(owner);
        }
        if (candidates.Count != 1) throw new InvalidDataException("Noch keine eindeutige geladene Spielfigur.");
        var id = new Identity(); id.Manager = manager; id.Data = data; id.Count = count; id.Capacity = capacity; id.Owner = candidates[0]; id.Entries = entries;
        id.OwnerBytes = memory.Read(id.Owner, 0xA8); id.TypeTag = memory.Read(PointerAt(id.OwnerBytes, 0x88) + 1, 1)[0];
        if (id.TypeTag != 1 && id.TypeTag != 9) throw new InvalidDataException("Spielfigur hat gewechselt.");
        id.Actor = PointerAt(id.OwnerBytes, 0x68); id.Marker = Ptr(id.Actor + 0x20); id.Root = Ptr(id.Marker + 0x18);
        if (Ptr(id.Root) != id.Marker) throw new InvalidDataException("Spieler-Rückverweis stimmt nicht überein.");
        id.Stats = Ptr(id.Root + 0x58); id.StatBytes = memory.Read(id.Stats, 16 * 0x90);
        if (BitConverter.ToInt32(id.StatBytes, 0) != 0) throw new InvalidDataException("Lebenswerte sind noch nicht bereit.");
        Stat(id.StatBytes, 0); Validate(id); return id;
    }
    private void Validate(Identity id)
    {
        if (Ptr(Ptr(managerGlobal)) != id.Manager) throw new InvalidDataException("Die Spielwelt wurde gewechselt.");
        byte[] m = memory.Read(id.Manager + 0xB8, 16), currentOwner = memory.Read(id.Owner, 0xA8);
        if (Values.P(currentOwner, 0) != Values.P(id.OwnerBytes, 0) || Values.U32(currentOwner, 0x60) != Values.U32(id.OwnerBytes, 0x60) || Values.P(currentOwner, 0x88) != Values.P(id.OwnerBytes, 0x88) || Values.P(currentOwner, 0xA0) != Values.P(id.OwnerBytes, 0xA0) || memory.Read(PointerAt(currentOwner, 0x88) + 1, 1)[0] != id.TypeTag) throw new InvalidDataException("Identität der Spielfigur wurde geändert.");
        if (Values.P(m, 0) != id.Data || Values.U32(m, 8) != id.Count || Values.U32(m, 12) != id.Capacity || !Values.Equal(id.Entries, memory.Read(id.Data, checked((int)id.Count * 8))) || Ptr(id.Owner + 0x68) != id.Actor || Ptr(id.Actor + 0x20) != id.Marker || Ptr(id.Marker + 0x18) != id.Root || Ptr(id.Root) != id.Marker || Ptr(id.Root + 0x58) != id.Stats || Ptr(Ptr(id.Owner + 0xA0) + 0xD0) != id.Owner) throw new InvalidDataException("Spielfigur hat gewechselt. Erneut lesen.");
    }
    private static long[] Stat(byte[] bytes, int at)
    {
        long value = Values.I64(bytes, at + 8), basis = Values.I64(bytes, at + 0x18), norm = Values.I64(bytes, at + 0x20), cap = Values.I64(bytes, at + 0x30), max = Math.Max(basis, cap);
        if (basis < 0 || cap < 0 || value < 0 || max <= 0 || max > 1000000000000L || value > max || norm != Math.Max(0L, value - basis)) throw new InvalidDataException("Spielerwerte werden gerade aktualisiert.");
        return new long[] { value, max };
    }
    internal Dictionary<string, object> Status()
    {
        var caps = Values.D("health", false, "stamina", false, "spirit", false, "inventory", false, "catalog", false, "writes", false, "addItem", false, "setQuantity", false, "teleport", false, "travel", false);
        var state = Values.D("connected", true, "accessMode", "readOnly", "playerReady", false, "layoutRecognized", managerGlobal != 0, "health", null, "maxHealth", null, "stamina", null, "maxStamina", null, "spirit", null, "maxSpirit", null, "capabilities", caps, "toggles", Values.D("health", false, "stamina", false, "spirit", false));
        try
        {
            Identity id = Resolve(); long[] health = Stat(id.StatBytes, 0); state["playerReady"] = true; state["health"] = health[0]; state["maxHealth"] = health[1];
            bool stamina = false, spirit = false;
            for (int i = 1; i < 16; i++)
            {
                int type = BitConverter.ToInt32(id.StatBytes, i * 0x90); if (type != 22 && type != 23) continue;
                long[] stat = Stat(id.StatBytes, i * 0x90);
                if ((type == 22 && stamina) || (type == 23 && spirit)) throw new InvalidDataException("Spielerwerte sind nicht eindeutig.");
                string name = type == 22 ? "stamina" : "spirit", maxName = type == 22 ? "maxStamina" : "maxSpirit";
                state[name] = stat[0]; state[maxName] = stat[1]; if (type == 22) stamina = true; else spirit = true;
            }
            Validate(id); caps["inventory"] = itemGlobal != 0 && storageGlobal != 0; caps["catalog"] = itemGlobal != 0;
            state["message"] = "Spielstand und Inventar erkannt. Externe Leseverbindung; Änderungen am Spiel sind noch nicht verfügbar.";
        }
        catch (Exception e)
        {
            state["playerReady"] = false; foreach (string key in new[] { "health", "maxHealth", "stamina", "maxStamina", "spirit", "maxSpirit" }) state[key] = null;
            state["message"] = e.Message;
        }
        return state;
    }
    private sealed class Table { internal ulong Global, Object, Definitions; internal int Count; internal byte[] Entries; }
    private Table ReadTable(ulong global)
    {
        if (global == 0) throw new InvalidDataException("Gegenstandstabelle wurde nicht erkannt.");
        var t = new Table(); t.Global = global; t.Object = Ptr(global); byte[] head = memory.Read(t.Object, 0x60);
        t.Count = Values.Bound(Values.U32(head, 8), 1, 65535, "Gegenstandstabelle"); t.Definitions = PointerAt(head, 0x58); t.Entries = memory.Read(t.Definitions, t.Count * 8); return t;
    }
    private void ValidateTable(Table t)
    {
        if (Ptr(t.Global) != t.Object) throw new InvalidDataException("Gegenstandstabelle wurde gewechselt.");
        byte[] h = memory.Read(t.Object, 0x60);
        if (Values.U32(h, 8) != t.Count || Values.P(h, 0x58) != t.Definitions || !Values.Equal(t.Entries, memory.Read(t.Definitions, t.Count * 8))) throw new InvalidDataException("Gegenstandstabelle wird gerade geändert.");
    }
    private ulong Definition(Table t, int index) { if (index < 0 || index >= t.Count) throw new InvalidDataException("Gegenstandsnummer passt nicht zur Tabelle."); return PointerAt(t.Entries, index * 8); }
    private sealed class Bucket { internal ulong Address, Slots; internal int Count; internal ushort Storage; internal byte[] Header, Data; }
    internal Dictionary<string, object> Inventory()
    {
        Identity identity = Resolve(); Table items = ReadTable(itemGlobal), storages = ReadTable(storageGlobal);
        ulong holder = Ptr(identity.Actor + 0xB8), canonical = Ptr(holder + 8);
        if (Ptr(Ptr(canonical + 0x68) + 0xB8) != holder) throw new InvalidDataException("Inventar-Rückverweis stimmt nicht überein.");
        byte[] holderHead = memory.Read(holder + 8, 0x20); ulong bucketArray = PointerAt(holderHead, 0x10); int count = Values.Bound(Values.U32(holderHead, 0x18), 1, 128, "Inventarbereiche");
        byte[] bucketPointers = memory.Read(bucketArray, count * 8); var buckets = new List<Bucket>(); var types = new HashSet<ushort>(); int totalSlots = 0;
        for (int i = 0; i < count; i++)
        {
            var b = new Bucket(); b.Address = PointerAt(bucketPointers, i * 8); b.Header = memory.Read(b.Address, 0x20); b.Storage = Values.U16(b.Header, 0x10);
            if (!types.Add(b.Storage) || b.Storage >= storages.Count) throw new InvalidDataException("Inventarbereiche sind nicht eindeutig.");
            b.Count = Values.Bound(Values.U32(b.Header, 8), 0, 8192, "Inventarplätze"); uint capacity = Values.U32(b.Header, 12);
            if (capacity < b.Count || capacity > 65536) throw new InvalidDataException("Inventargröße wird geändert.");
            totalSlots += b.Count; if (totalSlots > 100000) throw new InvalidDataException("Inventar ist außerhalb des geprüften Bereichs.");
            b.Slots = Values.P(b.Header, 0); b.Data = b.Count == 0 ? new byte[0] : memory.Read(b.Slots, b.Count * 0xC8); buckets.Add(b);
        }
        var output = new List<Dictionary<string, object>>(); var storageOutput = new List<Dictionary<string, object>>(); var descriptors = new Dictionary<int, Dictionary<string, object>>();
        foreach (Bucket b in buckets)
        {
            string storage = EngineString(Definition(storages, b.Storage) + 8); int occupied = 0;
            for (int i = 0; i < b.Count; i++)
            {
                int off = i * 0xC8, type = Values.U16(b.Data, off + 8); long quantity = Values.I64(b.Data, off + 0x10);
                if (type == 65535 || quantity <= 0) continue;
                Dictionary<string, object> descriptor;
                if (!descriptors.TryGetValue(type, out descriptor))
                {
                    ulong def = Definition(items, type); byte[] metadata = memory.Read(def, 0x42A); string key = EngineString(def + 8); long stack = Values.I64(metadata, 0x18);
                    if (stack < 1) throw new InvalidDataException("Gegenstandsgrenze ist ungültig.");
                    descriptor = Values.D("itemId", type, "itemKey", Values.U32(metadata, 0), "key", key, "name", key.Replace('_', ' '), "maxStack", Values.Long(stack), "category", "all", "addable", false, "editable", false, "reason", Reason, "defaultStorageType", Values.U16(metadata, 0x428)); descriptors.Add(type, descriptor);
                }
                var row = new Dictionary<string, object>(descriptor); row["slot"] = Values.Hex(b.Slots + (ulong)off); row["slotIndex"] = i; row["storageType"] = (int)b.Storage; row["storage"] = storage;
                row["instance"] = Values.Long(Values.I64(b.Data, off)); row["quantity"] = quantity <= 9007199254740991L ? (object)quantity : Values.Long(quantity); row["quantityRaw"] = Values.Long(quantity); output.Add(row); occupied++;
            }
            storageOutput.Add(Values.D("storageType", (int)b.Storage, "storage", storage, "slotCount", b.Count, "usedSlots", Values.U16(b.Header, 0x12), "maxSlots", Values.U16(b.Header, 0x14), "itemCount", occupied));
        }
        // A second complete sweep compares identity/type/quantity for EVERY slot,
        // including previously empty slots, not merely the returned occupied list.
        foreach (Bucket b in buckets)
        {
            byte[] next = b.Count == 0 ? new byte[0] : memory.Read(b.Slots, b.Count * 0xC8);
            for (int i = 0; i < b.Count; i++) { int at = i * 0xC8; if (Values.I64(next, at) != Values.I64(b.Data, at) || Values.U16(next, at + 8) != Values.U16(b.Data, at + 8) || Values.I64(next, at + 0x10) != Values.I64(b.Data, at + 0x10)) throw new InvalidDataException("Inventar wird gerade geändert. Erneut laden."); }
            if (!Values.Equal(b.Header, memory.Read(b.Address, 0x20))) throw new InvalidDataException("Inventarplätze wurden geändert. Erneut laden.");
        }
        if (!Values.Equal(holderHead, memory.Read(holder + 8, 0x20)) || !Values.Equal(bucketPointers, memory.Read(bucketArray, count * 8)) || Ptr(identity.Actor + 0xB8) != holder || Ptr(Ptr(canonical + 0x68) + 0xB8) != holder) throw new InvalidDataException("Inventar wurde gewechselt.");
        ValidateTable(items); ValidateTable(storages); Validate(identity);
        return Values.D("items", output, "storages", storageOutput, "total", output.Count, "source", "external-read-only", "accessMode", "readOnly");
    }
}

internal static class GameReaderProgram
{
    private static NativeMemory memory;
    private static SnapshotReader reader;
    private static ScanResult scan;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 64 };
    private static void Dispose() { reader = null; scan = null; if (memory != null) { memory.Dispose(); memory = null; } }
    private static object Dispatch(string command, Dictionary<string, object> args)
    {
        if (command == "dispose") { Dispose(); return Values.D("connected", false); }
        if (command == "init")
        {
            Dispose();
            if (!args.ContainsKey("pid") || !args.ContainsKey("exe")) throw new ArgumentException("Prozess und Spieldatei fehlen.");
            try
            {
                memory = new NativeMemory(Convert.ToInt32(args["pid"], CultureInfo.InvariantCulture), Convert.ToString(args["exe"], CultureInfo.InvariantCulture));
                List<ImageSpan> spans; byte[] image = memory.ReadImage(out spans); scan = ProfileScanner.Scan(image, spans, memory.BaseAddress, memory); image = null;
                reader = new SnapshotReader(memory, scan.ManagerGlobal, scan.ItemGlobal, scan.StorageGlobal, memory.BaseAddress, memory.ImageSize);
                memory.ValidateIdentity(); Dictionary<string, object> state = reader.Status(); state["pid"] = memory.ProcessId; return state;
            }
            catch { Dispose(); throw; }
        }
        if (memory == null || reader == null) throw new InvalidOperationException("Zuerst mit dem Spiel verbinden.");
        try { memory.ValidateIdentity(); }
        catch (Exception)
        {
            Dispose();
            if (command == "status") return Values.D("connected", false, "pid", null, "playerReady", false, "layoutRecognized", false, "accessMode", "readOnly", "health", null, "maxHealth", null, "stamina", null, "maxStamina", null, "spirit", null, "maxSpirit", null, "capabilities", Values.D("health", false, "stamina", false, "spirit", false, "inventory", false, "catalog", false, "writes", false, "addItem", false, "setQuantity", false, "teleport", false, "travel", false), "toggles", Values.D("health", false, "stamina", false, "spirit", false), "message", "Spiel beendet oder neu gestartet. Erneut verbinden.");
            throw new InvalidOperationException("Spiel beendet oder neu gestartet. Erneut verbinden.");
        }
        object result;
        if (command == "status") { var state = reader.Status(); state["pid"] = memory.ProcessId; result = state; }
        else if (command == "inventory") result = reader.Inventory();
        else if (command == "diagnostics") result = Values.D("pid", memory.ProcessId, "accessMode", "readOnly", "processAccess", "0x0410", "profile", "1.0.0.2760", "scans", scan.Diagnostics);
        else throw new ArgumentException("Unbekannter Lesebefehl.");
        memory.ValidateIdentity(); return result;
    }
    public static int Main()
    {
        Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                object id = null; string command = null;
                try
                {
                    if (line.Length > 1024 * 1024) throw new ArgumentException("Anfrage ist zu groß.");
                    var request = Json.Deserialize<Dictionary<string, object>>(line);
                    if (request == null || !request.TryGetValue("id", out id) || !request.ContainsKey("cmd")) throw new ArgumentException("Ungültige Leseanfrage.");
                    command = Convert.ToString(request["cmd"], CultureInfo.InvariantCulture);
                    var args = request.ContainsKey("args") ? request["args"] as Dictionary<string, object> : null;
                    Console.WriteLine(Json.Serialize(Values.D("id", id, "ok", true, "result", Dispatch(command, args ?? new Dictionary<string, object>()))));
                }
                catch (Exception error) { Console.WriteLine(Json.Serialize(Values.D("id", id, "ok", false, "error", error.Message))); }
                if (command == "dispose") break;
            }
        }
        finally { Dispose(); }
        return 0;
    }
}
