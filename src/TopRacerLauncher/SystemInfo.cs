using System.Runtime.InteropServices;
using System.Windows;

namespace TopRacerLauncher;

/// <summary>Краткие характеристики компьютера для правой панели.</summary>
public static class SystemInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public static string CpuName
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");

                if (key?.GetValue("ProcessorNameString") is string name && !string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
            catch { /* нет доступа */ }

            return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Неизвестно";
        }
    }

    public static string RamText
    {
        get
        {
            try
            {
                var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (GlobalMemoryStatusEx(ref status))
                {
                    double gb = status.TotalPhysical / 1024.0 / 1024.0 / 1024.0;
                    return gb >= 1 ? Math.Round(gb) + " ГБ" : Math.Round(gb * 1024) + " МБ";
                }
            }
            catch { /* ignore */ }

            return "Неизвестно";
        }
    }

    public static string GpuName
    {
        get
        {
            try
            {
                using var baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");

                if (baseKey != null)
                {
                    foreach (var sub in baseKey.GetSubKeyNames())
                    {
                        if (sub.Length != 4 || !int.TryParse(sub, out _)) continue;

                        using var gpu = baseKey.OpenSubKey(sub);
                        if (gpu?.GetValue("DriverDesc") is string desc && !string.IsNullOrWhiteSpace(desc))
                            return desc.Trim();
                    }
                }
            }
            catch { /* нет доступа */ }

            return "Неизвестно";
        }
    }

    public static string OsText
    {
        get
        {
            try
            {
                var version = Environment.OSVersion.Version;
                string name = version.Major >= 10 ? "Windows 10/11" : "Windows " + version.Major;
                return name + "  •  " + Environment.OSVersion.VersionString.Replace("Microsoft ", "");
            }
            catch { return "Windows"; }
        }
    }

    public static string ScreenText
    {
        get
        {
            try
            {
                return (int)SystemParameters.PrimaryScreenWidth + "×" + (int)SystemParameters.PrimaryScreenHeight;
            }
            catch { return ""; }
        }
    }
}
