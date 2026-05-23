#pragma warning disable CA1416
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MemCls
{
    internal class Program
    {
        // --- Win32 APIs for Memory Status ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // --- Win32 APIs for Emptying Working Set ---
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        // --- Win32 APIs for System File Cache ---
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetSystemFileCacheSize(
            UIntPtr MinimumFileCacheSize,
            UIntPtr MaximumFileCacheSize,
            int Flags);

        // --- Win32 APIs for Adjusting Token Privileges ---
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x0002;

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle, 
            bool DisableAllPrivileges, 
            ref TOKEN_PRIVILEGES NewState, 
            uint BufferLength, 
            IntPtr PreviousState, 
            IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

        // --- Win32 APIs for Native System Info Settings ---
        public enum SYSTEM_MEMORY_LIST_COMMAND
        {
            MemoryCaptureState = 1,
            MemoryFailFreeHeaders = 2,
            MemoryFlushModifiedList = 3,
            MemoryPurgeStandbyList = 4,
            MemoryPurgeLowPriorityStandbyList = 5,
            MemoryCommandMax = 6
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_COMBINE_INFORMATION
        {
            public IntPtr Handle;
            public IntPtr PagesCombined;
        }

        [DllImport("ntdll.dll")]
        public static extern int NtSetSystemInformation(
            int SystemInformationClass, 
            IntPtr SystemInformation, 
            int SystemInformationLength);

        // --- Console Virtual Terminal APIs for True Color ---
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        private const int STD_OUTPUT_HANDLE = -11;
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        // --- True Color & Rendering Utilities ---
        private static bool _ansiEnabled = false;

        private static void EnableAnsiTrueColor()
        {
            try
            {
                IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle != IntPtr.Zero && GetConsoleMode(handle, out uint mode))
                {
                    _ansiEnabled = SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
                }
            }
            catch
            {
                _ansiEnabled = false;
            }
        }

        private static string Rgb(byte r, byte g, byte b, string text)
        {
            if (_ansiEnabled)
            {
                return $"\x1b[38;2;{r};{g};{b}m{text}\x1b[0m";
            }
            return text;
        }

        private static string Gradient(string text, (byte r, byte g, byte b) start, (byte r, byte g, byte b) end)
        {
            if (!_ansiEnabled || string.IsNullOrEmpty(text))
            {
                return text;
            }

            var sb = new System.Text.StringBuilder();
            int len = text.Length;
            for (int i = 0; i < len; i++)
            {
                float ratio = len > 1 ? (float)i / (len - 1) : 0f;
                byte r = (byte)(start.r + (end.r - start.r) * ratio);
                byte g = (byte)(start.g + (end.g - start.g) * ratio);
                byte b = (byte)(start.b + (end.b - start.b) * ratio);
                sb.Append($"\x1b[38;2;{r};{g};{b}m{text[i]}");
            }
            sb.Append("\x1b[0m");
            return sb.ToString();
        }

        // --- Structured Logger ---
        public enum LogLevel
        {
            Info,
            Warning,
            Error
        }

        private static readonly object _logLock = new object();

        public static void Log(LogLevel level, string message, Exception? ex = null)
        {
            // 1. Always write to File Sink (log/ directory relative to base directory)
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logDir = Path.Combine(baseDir, "log");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string logFilePath = Path.Combine(logDir, $"memcls_{DateTime.Now:yyyyMMdd}.log");
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpper()}] {message}";
                if (ex != null)
                {
                    logLine += $"{Environment.NewLine}{ex}";
                }

                lock (_logLock)
                {
                    File.AppendAllText(logFilePath, logLine + Environment.NewLine);
                }
            }
            catch
            {
                // Mute errors writing logs to disk to prevent crash
            }

            // 2. Output to Console ONLY if level is Error
            if (level == LogLevel.Error)
            {
                string prefix = Rgb(Red.r, Red.g, Red.b, "[-] ");
                string msg = Rgb(Red.r, Red.g, Red.b, message);
                Console.WriteLine($"{prefix}{msg}");
                if (ex != null)
                {
                    Console.WriteLine(Rgb(Gray.r, Gray.g, Gray.b, $"    Exception: {ex.Message}"));
                }
            }
        }

        // --- Colors ---
        private static readonly (byte r, byte g, byte b) ColorStart = (0, 242, 254);   // Ice Blue
        private static readonly (byte r, byte g, byte b) ColorEnd = (79, 172, 254);   // Bright Cyan/Blue
        private static readonly (byte r, byte g, byte b) Green = (46, 213, 115);      // Emerald Green
        private static readonly (byte r, byte g, byte b) Red = (255, 71, 87);         // Rose Red
        private static readonly (byte r, byte g, byte b) Orange = (255, 127, 80);      // Coral Orange
        private static readonly (byte r, byte g, byte b) Gray = (160, 160, 160);        // Muted Gray
        private static readonly (byte r, byte g, byte b) LightGray = (180, 189, 210);   // Gray Blue

        static void Main(string[] args)
        {
            // Initialize True Color Output
            EnableAnsiTrueColor();

            Console.Title = "Windows Memory Cleaner (MemCls)";
            
            string border = new string('=', 50);
            Console.WriteLine(Gradient(border, ColorStart, ColorEnd));
            Console.WriteLine(Gradient("          Windows Memory Cleaner (MemCls)        ", ColorStart, ColorEnd));
            Console.WriteLine(Gradient(border, ColorStart, ColorEnd));

            bool isAdmin = IsRunAsAdmin();
            if (!isAdmin)
            {
                Console.WriteLine(Rgb(LightGray.r, LightGray.g, LightGray.b, "[~] Requesting Administrator privileges for standard cleanup (UAC)..."));
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                try
                {
                    Process.Start(startInfo);
                    Log(LogLevel.Info, "Requested UAC elevation and restarted process.");
                    return;
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    string warnMsg = "[!] UAC elevation declined (User clicked No).\n" +
                                     "    Running in standard user mode. Only EmptyWorkingSet will be performed.";
                    Console.WriteLine(Rgb(Orange.r, Orange.g, Orange.b, warnMsg));
                    Console.WriteLine();
                    Log(LogLevel.Warning, "UAC elevation declined by user. Operating in fallback mode.", ex);
                }
            }
            else
            {
                Console.WriteLine(Rgb(Green.r, Green.g, Green.b, "[+] Running with Administrator privileges (Standard Cleanup)."));
                Console.WriteLine();
                Log(LogLevel.Info, "Started MemCls with Administrator privileges.");
            }

            // Enable privileges
            if (isAdmin)
            {
                TryEnablePrivilege("SeDebugPrivilege");
                TryEnablePrivilege("SeProfileSingleProcessPrivilege");
                TryEnablePrivilege("SeIncreaseQuotaPrivilege");
            }

            // Show initial memory
            MEMORYSTATUSEX beforeStatus = GetMemoryStatus();
            PrintMemoryStatus("Initial Memory Status", beforeStatus);

            Console.WriteLine(Rgb(LightGray.r, LightGray.g, LightGray.b, "\n[~] Beginning memory optimization..."));
            Log(LogLevel.Info, "Starting memory cleanup operations...");

            // 1. Clean processes working sets
            CleanWorkingSets();

            // Additional memory optimization features (require Admin)
            if (isAdmin)
            {
                // 2. Clear standby lists
                PurgeStandbyLists();

                // 3. Flush modified page list
                FlushModifiedPageList();

                // 4. Flush system file cache
                FlushSystemFileCache();

                // 5. Reconcile registry cache
                ReconcileRegistryCache();

                // 6. Combine physical memory
                CombinePhysicalMemoryPages();
            }

            // Wait a moment for OS to settle stats
            System.Threading.Thread.Sleep(1000);

            // Show final memory
            MEMORYSTATUSEX afterStatus = GetMemoryStatus();
            Console.WriteLine();
            PrintMemoryStatus("Final Memory Status", afterStatus);

            long freedBytes = (long)afterStatus.ullAvailPhys - (long)beforeStatus.ullAvailPhys;
            
            Console.WriteLine(Gradient(border, ColorStart, ColorEnd));
            if (freedBytes > 0)
            {
                string successMsg = $"[+] Success! Freed: {FormatBytes((ulong)freedBytes)} of physical memory.";
                Console.WriteLine(Rgb(Green.r, Green.g, Green.b, successMsg));
                Log(LogLevel.Info, $"Memory cleanup successful. Freed: {FormatBytes((ulong)freedBytes)}.");
            }
            else
            {
                string neutralMsg = "[*] Optimization complete (Memory was already optimized).";
                Console.WriteLine(Rgb(LightGray.r, LightGray.g, LightGray.b, neutralMsg));
                Log(LogLevel.Info, "Memory cleanup complete. No significant memory freed.");
            }
            Console.WriteLine(Gradient(border, ColorStart, ColorEnd));

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static bool IsRunAsAdmin()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, "Failed to verify administrator status.", ex);
                return false;
            }
        }

        private static bool TryEnablePrivilege(string privilegeName)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                {
                    Log(LogLevel.Warning, $"Failed to open process token for privilege: {privilegeName}");
                    return false;
                }

                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                {
                    Log(LogLevel.Warning, $"Failed to lookup privilege value: {privilegeName}");
                    return false;
                }

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                };

                if (!AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    Log(LogLevel.Warning, $"Failed to adjust token privileges for: {privilegeName}");
                    return false;
                }

                int lastError = Marshal.GetLastWin32Error();
                if (lastError == 0)
                {
                    Log(LogLevel.Info, $"Privilege adjusted successfully: {privilegeName}");
                    return true;
                }
                Log(LogLevel.Warning, $"AdjustTokenPrivileges returned last error: {lastError} for privilege: {privilegeName}");
                return false;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Exception adjusting token privilege: {privilegeName}", ex);
                return false;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    CloseHandle(tokenHandle);
                }
            }
        }

        private static MEMORYSTATUSEX GetMemoryStatus()
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref memStatus))
            {
                int err = Marshal.GetLastWin32Error();
                var ex = new System.ComponentModel.Win32Exception(err);
                Log(LogLevel.Error, "GlobalMemoryStatusEx failed.", ex);
                throw ex;
            }
            return memStatus;
        }

        private static void PrintMemoryStatus(string title, MEMORYSTATUSEX status)
        {
            Console.WriteLine(Rgb(255, 255, 255, $"--- {title} ---"));
            
            string labelColor(string label) => Rgb(LightGray.r, LightGray.g, LightGray.b, label);
            string valueColor(string val) => Rgb(ColorStart.r, ColorStart.g, ColorStart.b, val);

            Console.WriteLine($"  {labelColor("Memory Load (Usage):")}  {valueColor($"{status.dwMemoryLoad}%")}");
            Console.WriteLine($"  {labelColor("Total Physical RAM: ")}  {valueColor(FormatBytes(status.ullTotalPhys))}");
            Console.WriteLine($"  {labelColor("Available Physical: ")}  {valueColor(FormatBytes(status.ullAvailPhys))}");
            Console.WriteLine($"  {labelColor("Total Page File:    ")}  {valueColor(FormatBytes(status.ullTotalPageFile))}");
            Console.WriteLine($"  {labelColor("Available Page File:")}  {valueColor(FormatBytes(status.ullAvailPageFile))}");
        }

        private static string FormatBytes(ulong bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            double dblBytes = bytes;
            int i = 0;
            while (dblBytes >= 1024 && i < Suffix.Length - 1)
            {
                dblBytes /= 1024.0;
                i++;
            }
            return $"{dblBytes:F2} {Suffix[i]}";
        }

        private static void CleanWorkingSets()
        {
            int successCount = 0;
            int failCount = 0;
            Process[] processes = Process.GetProcesses();

            Log(LogLevel.Info, $"Beginning optimization of working sets for {processes.Length} processes.");

            // Standard console UI progress display: Since it's a progress output, we keep it in console
            Console.Write(Rgb(LightGray.r, LightGray.g, LightGray.b, "  [~] Optimizing process working sets: "));
            int currentLineCursor = Console.CursorLeft;

            for (int i = 0; i < processes.Length; i++)
            {
                Process proc = processes[i];
                try
                {
                    IntPtr handle = proc.Handle;
                    if (EmptyWorkingSet(handle))
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch
                {
                    failCount++;
                }
                finally
                {
                    proc.Dispose();
                }

                if (i % 10 == 0 || i == processes.Length - 1)
                {
                    Console.SetCursorPosition(currentLineCursor, Console.CursorTop);
                    Console.Write(Rgb(ColorStart.r, ColorStart.g, ColorStart.b, $"{i + 1}/{processes.Length} processed..."));
                }
            }

            Console.WriteLine();
            
            // This is a major summary message, so we write to log as Info, but also write to console
            string summaryMsg = $"Working sets optimized: {successCount} processes. (Skipped/Failed: {failCount})";
            Log(LogLevel.Info, summaryMsg);
            Console.WriteLine(Rgb(Green.r, Green.g, Green.b, $"  [+] {summaryMsg}"));
        }

        private static void PurgeStandbyLists()
        {
            Log(LogLevel.Info, "Purging system standby lists...");
            int SystemMemoryListInformation = 0x50; 

            // Purge Standby List
            int status = CallNtSetSystemInformation(SystemMemoryListInformation, SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeStandbyList);
            if (status == 0)
            {
                Log(LogLevel.Info, "Standby list purged successfully.");
            }
            else
            {
                Log(LogLevel.Error, $"Failed to purge standby list. NTSTATUS: 0x{status:X}");
            }

            // Purge Low Priority Standby List
            status = CallNtSetSystemInformation(SystemMemoryListInformation, SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeLowPriorityStandbyList);
            if (status == 0)
            {
                Log(LogLevel.Info, "Low-priority standby list purged successfully.");
            }
            else
            {
                Log(LogLevel.Warning, $"Low-priority standby list response/status: 0x{status:X}");
            }
        }

        private static void FlushModifiedPageList()
        {
            Log(LogLevel.Info, "Flushing system modified page list...");
            int SystemMemoryListInformation = 0x50; 

            int status = CallNtSetSystemInformation(SystemMemoryListInformation, SYSTEM_MEMORY_LIST_COMMAND.MemoryFlushModifiedList);
            if (status == 0)
            {
                Log(LogLevel.Info, "Modified page list flushed successfully.");
            }
            else
            {
                Log(LogLevel.Warning, $"Modified page list response/status: 0x{status:X}");
            }
        }

        private static void FlushSystemFileCache()
        {
            Log(LogLevel.Info, "Flushing system file cache...");
            try
            {
                UIntPtr purgeVal = (UIntPtr.Size == 8) 
                    ? new UIntPtr(0xFFFFFFFFFFFFFFFF) 
                    : new UIntPtr(0xFFFFFFFF);

                if (SetSystemFileCacheSize(purgeVal, purgeVal, 0))
                {
                    Log(LogLevel.Info, "System file cache flushed successfully.");
                }
                else
                {
                    int lastError = Marshal.GetLastWin32Error();
                    Log(LogLevel.Error, $"Failed to flush system file cache. Win32 Error: {lastError}");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "Exception flushing system file cache.", ex);
            }
        }

        private static void ReconcileRegistryCache()
        {
            Log(LogLevel.Info, "Reconciling registry cache...");
            int SystemRegistryReconciliationInformation = 155; 

            try
            {
                int status = NtSetSystemInformation(
                    SystemRegistryReconciliationInformation, 
                    IntPtr.Zero, 
                    0);

                if (status == 0)
                {
                    Log(LogLevel.Info, "Registry cache reconciled successfully.");
                }
                else
                {
                    Log(LogLevel.Warning, $"Registry cache reconciliation response/status: 0x{status:X}");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "Exception reconciling registry.", ex);
            }
        }

        private static void CombinePhysicalMemoryPages()
        {
            Log(LogLevel.Info, "Triggering physical memory page combining...");
            int SystemCombinePhysicalMemoryInformation = 130; 

            var info = new MEMORY_COMBINE_INFORMATION
            {
                Handle = IntPtr.Zero,
                PagesCombined = IntPtr.Zero
            };

            IntPtr pInfo = IntPtr.Zero;
            try
            {
                pInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MEMORY_COMBINE_INFORMATION)));
                Marshal.StructureToPtr(info, pInfo, false);

                int status = NtSetSystemInformation(
                    SystemCombinePhysicalMemoryInformation, 
                    pInfo, 
                    Marshal.SizeOf(typeof(MEMORY_COMBINE_INFORMATION)));

                if (status == 0)
                {
                    Log(LogLevel.Info, "Physical memory combining triggered successfully.");
                }
                else
                {
                    Log(LogLevel.Warning, $"Physical memory combining response/status: 0x{status:X}");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "Exception combining physical memory.", ex);
            }
            finally
            {
                if (pInfo != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pInfo);
                }
            }
        }

        private static int CallNtSetSystemInformation(int infoClass, SYSTEM_MEMORY_LIST_COMMAND command)
        {
            IntPtr pCommand = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(int)));
            Marshal.WriteInt32(pCommand, (int)command);

            try
            {
                int status = NtSetSystemInformation(
                    infoClass, 
                    pCommand, 
                    Marshal.SizeOf(typeof(int)));
                return status;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Exception calling NtSetSystemInformation for command {command}.", ex);
                return -1;
            }
            finally
            {
                Marshal.FreeHGlobal(pCommand);
            }
        }
    }
}
