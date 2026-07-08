using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Reads GPU stats via NVML (NVIDIA Management Library) P/Invoke.
    /// 
    /// NVML ships with every NVIDIA driver as nvml.dll in System32.
    /// This approach calls native functions directly — no process spawning,
    /// no shell commands, fully Steam Workshop safe.
    ///
    /// Targets NVIDIA 4000/5000 series (RTX 40xx, RTX 50xx).
    /// </summary>
    internal static class NvidiaSmiReader
    {
        private static Thread _pollThread;
        private static volatile bool _shutdown;
        private static volatile bool _available;
        private static readonly object _lock = new object();
        private static IntPtr _device = IntPtr.Zero;

        /// <summary>Interval between polls in milliseconds.</summary>
        private const int PollIntervalMs = 3000;

        // ── Cached GPU data ──

        internal static bool IsAvailable => _available;
        internal static string GpuName { get; private set; } = "Unknown";
        internal static string DriverVersion { get; private set; } = "Unknown";
        internal static int UtilizationPercent { get; private set; }
        internal static float UsedVramMb { get; private set; }
        internal static float TotalVramMb { get; private set; }
        internal static int TemperatureC { get; private set; }
        internal static float PowerDrawW { get; private set; }
        internal static float PowerLimitW { get; private set; }
        internal static int FanSpeedPercent { get; private set; }
        internal static int GpuClockMhz { get; private set; }
        internal static int MemClockMhz { get; private set; }
        internal static DateTime LastUpdated { get; private set; } = DateTime.MinValue;
        internal static string LastError { get; private set; }

        /// <summary>Per-process VRAM breakdown.</summary>
        internal static List<GpuProcessInfo> Processes { get; private set; } = new List<GpuProcessInfo>();

        /// <summary>Start background polling.</summary>
        internal static void Start()
        {
            if (_pollThread != null) return;

            _shutdown = false;
            _pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "RimSynapse-NVML",
            };
            _pollThread.Start();
        }

        /// <summary>Stop background polling and shutdown NVML.</summary>
        internal static void Shutdown()
        {
            _shutdown = true;
        }

        private static void PollLoop()
        {
            // Initialize NVML
            if (!InitNvml())
            {
                _available = false;
                return;
            }

            _available = true;
            Verse.Log.Message($"[RimSynapse NV] NVML initialized. GPU: {GpuName}, Driver: {DriverVersion}");

            while (!_shutdown)
            {
                try
                {
                    PollGpuStats();
                    PollProcesses();
                    PushToCoreFramework();
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }

                Thread.Sleep(PollIntervalMs);
            }

            // Cleanup
            try { Nvml.Shutdown(); } catch { }
        }

        // ────────────────────────────────────────────────────────
        //  Initialization
        // ────────────────────────────────────────────────────────

        private static bool InitNvml()
        {
            try
            {
                int result = Nvml.Init();
                if (result != Nvml.SUCCESS)
                {
                    LastError = $"NVML init failed (code {result}).";
                    Verse.Log.Warning($"[RimSynapse NV] {LastError}");
                    return false;
                }

                // Get first GPU device
                result = Nvml.DeviceGetHandleByIndex(0, out _device);
                if (result != Nvml.SUCCESS || _device == IntPtr.Zero)
                {
                    LastError = $"No NVIDIA GPU found (code {result}).";
                    Verse.Log.Warning($"[RimSynapse NV] {LastError}");
                    Nvml.Shutdown();
                    return false;
                }

                // Read static info
                var nameBuf = new StringBuilder(256);
                if (Nvml.DeviceGetName(_device, nameBuf, 256) == Nvml.SUCCESS)
                    GpuName = nameBuf.ToString();

                var driverBuf = new StringBuilder(256);
                if (Nvml.SystemGetDriverVersion(driverBuf, 256) == Nvml.SUCCESS)
                    DriverVersion = driverBuf.ToString();

                return true;
            }
            catch (DllNotFoundException)
            {
                LastError = "nvml.dll not found. NVIDIA drivers may not be installed.";
                Verse.Log.Warning($"[RimSynapse NV] {LastError}");
                return false;
            }
            catch (Exception ex)
            {
                LastError = $"NVML init error: {ex.Message}";
                Verse.Log.Warning($"[RimSynapse NV] {LastError}");
                return false;
            }
        }

        // ────────────────────────────────────────────────────────
        //  Polling
        // ────────────────────────────────────────────────────────

        private static void PollGpuStats()
        {
            if (_device == IntPtr.Zero) return;

            // Utilization
            var util = new Nvml.Utilization();
            if (Nvml.DeviceGetUtilizationRates(_device, ref util) == Nvml.SUCCESS)
            {
                lock (_lock)
                {
                    UtilizationPercent = (int)util.gpu;
                }
            }

            // Memory
            var mem = new Nvml.Memory();
            if (Nvml.DeviceGetMemoryInfo(_device, ref mem) == Nvml.SUCCESS)
            {
                lock (_lock)
                {
                    UsedVramMb = mem.used / (1024f * 1024f);
                    TotalVramMb = mem.total / (1024f * 1024f);
                }
            }

            // Temperature
            uint temp;
            if (Nvml.DeviceGetTemperature(_device, Nvml.TEMPERATURE_GPU, out temp) == Nvml.SUCCESS)
            {
                lock (_lock) { TemperatureC = (int)temp; }
            }

            // Power
            uint powerMw, limitMw;
            if (Nvml.DeviceGetPowerUsage(_device, out powerMw) == Nvml.SUCCESS)
            {
                lock (_lock) { PowerDrawW = powerMw / 1000f; }
            }
            if (Nvml.DeviceGetEnforcedPowerLimit(_device, out limitMw) == Nvml.SUCCESS)
            {
                lock (_lock) { PowerLimitW = limitMw / 1000f; }
            }

            // Fan speed
            uint fan;
            if (Nvml.DeviceGetFanSpeed(_device, out fan) == Nvml.SUCCESS)
            {
                lock (_lock) { FanSpeedPercent = (int)fan; }
            }

            // Clocks
            uint gpuClock, memClock;
            if (Nvml.DeviceGetClockInfo(_device, Nvml.CLOCK_GRAPHICS, out gpuClock) == Nvml.SUCCESS)
            {
                lock (_lock) { GpuClockMhz = (int)gpuClock; }
            }
            if (Nvml.DeviceGetClockInfo(_device, Nvml.CLOCK_MEM, out memClock) == Nvml.SUCCESS)
            {
                lock (_lock) { MemClockMhz = (int)memClock; }
            }

            lock (_lock)
            {
                LastUpdated = DateTime.UtcNow;
                LastError = null;
            }
        }

        /// <summary>
        /// Poll per-process VRAM usage via NVML.
        /// Queries both compute (CUDA/LLM) and graphics (rendering/Unity) processes.
        /// </summary>
        private static void PollProcesses()
        {
            if (_device == IntPtr.Zero) return;

            var newProcesses = new List<GpuProcessInfo>();
            var seenPids = new HashSet<int>();

            // Compute processes (LM Studio / CUDA workloads)
            CollectProcesses(true, newProcesses, seenPids);

            // Graphics processes (RimWorld / Unity rendering)
            CollectProcesses(false, newProcesses, seenPids);

            lock (_lock)
            {
                Processes = newProcesses;
            }
        }

        private static void CollectProcesses(bool compute,
            List<GpuProcessInfo> list, HashSet<int> seenPids)
        {
            try
            {
                // First call: get count
                uint count = 0;
                int result;

                if (compute)
                    result = Nvml.DeviceGetComputeRunningProcesses(_device, ref count, null);
                else
                    result = Nvml.DeviceGetGraphicsRunningProcesses(_device, ref count, null);

                // INSUFFICIENT_SIZE means count was set to the required size
                if (result != Nvml.SUCCESS && result != Nvml.ERROR_INSUFFICIENT_SIZE)
                    return;
                if (count == 0) return;

                // Second call: get data
                var infos = new Nvml.ProcessInfo[count];
                if (compute)
                    result = Nvml.DeviceGetComputeRunningProcesses(_device, ref count, infos);
                else
                    result = Nvml.DeviceGetGraphicsRunningProcesses(_device, ref count, infos);

                if (result != Nvml.SUCCESS) return;

                for (int i = 0; i < count; i++)
                {
                    int pid = (int)infos[i].pid;
                    if (pid <= 0 || seenPids.Contains(pid)) continue;
                    seenPids.Add(pid);

                    // Resolve process name from PID (safe .NET API, no process spawning)
                    string procName = ResolveProcessName(pid);

                    list.Add(new GpuProcessInfo
                    {
                        Pid = pid,
                        Name = procName,
                        VramMb = infos[i].usedGpuMemory / (1024f * 1024f),
                    });
                }
            }
            catch { /* Process enumeration can fail on permission issues — skip */ }
        }

        /// <summary>
        /// Resolve a PID to a process name using .NET's Process API.
        /// Safe, no external process spawning.
        /// </summary>
        private static string ResolveProcessName(int pid)
        {
            try
            {
                using (var proc = Process.GetProcessById(pid))
                {
                    return proc.ProcessName;
                }
            }
            catch
            {
                return $"PID {pid}";
            }
        }

        /// <summary>
        /// Push stats to Core's GpuStats framework so other mods can read them.
        /// </summary>
        private static void PushToCoreFramework()
        {
            var gpu = SynapseClient.Gpu;
            if (gpu == null) return;

            lock (_lock)
            {
                gpu.supported = true;
                gpu.utilizationPercent = UtilizationPercent;
                gpu.usedVramGb = UsedVramMb / 1024f;
                gpu.totalVramGb = TotalVramMb / 1024f;
                gpu.lastUpdated = LastUpdated;

                gpu.processes.Clear();
                foreach (var p in Processes)
                {
                    gpu.processes.Add(new GpuProcess
                    {
                        pid = p.Pid,
                        name = p.Name,
                        vramMb = p.VramMb,
                    });
                }
            }
        }
    }

    // ────────────────────────────────────────────────────────
    //  NVML P/Invoke bindings
    // ────────────────────────────────────────────────────────

    /// <summary>
    /// Native P/Invoke declarations for NVML (nvml.dll).
    /// nvml.dll ships with every NVIDIA driver in System32.
    /// </summary>
    internal static class Nvml
    {
        internal const int SUCCESS = 0;
        internal const int ERROR_INSUFFICIENT_SIZE = 7;
        internal const int TEMPERATURE_GPU = 0;
        internal const int CLOCK_GRAPHICS = 0;
        internal const int CLOCK_SM = 1;
        internal const int CLOCK_MEM = 2;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Utilization
        {
            public uint gpu;    // % utilization
            public uint memory; // % utilization
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Memory
        {
            public ulong total; // bytes
            public ulong free;  // bytes
            public ulong used;  // bytes
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInfo
        {
            public uint pid;
            public ulong usedGpuMemory; // bytes
            public uint gpuInstanceId;
            public uint computeInstanceId;
        }

        // ── Init / Shutdown ──

        [DllImport("nvml", EntryPoint = "nvmlInit_v2", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Init();

        [DllImport("nvml", EntryPoint = "nvmlShutdown", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Shutdown();

        // ── Device ──

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetHandleByIndex_v2", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetName", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetName(IntPtr device, StringBuilder name, uint length);

        [DllImport("nvml", EntryPoint = "nvmlSystemGetDriverVersion", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SystemGetDriverVersion(StringBuilder version, uint length);

        // ── Stats ──

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetUtilizationRates", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetUtilizationRates(IntPtr device, ref Utilization utilization);

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetMemoryInfo", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetMemoryInfo(IntPtr device, ref Memory memory);

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetTemperature", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetPowerUsage", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetPowerUsage(IntPtr device, out uint power);

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetEnforcedPowerLimit", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetEnforcedPowerLimit(IntPtr device, out uint limit);

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetFanSpeed", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetFanSpeed(IntPtr device, out uint speed);

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetClockInfo", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetClockInfo(IntPtr device, int clockType, out uint clock);

        // ── Processes ──

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetComputeRunningProcesses_v3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetComputeRunningProcesses(
            IntPtr device, ref uint infoCount,
            [In, Out] ProcessInfo[] infos);

        [DllImport("nvml", EntryPoint = "nvmlDeviceGetGraphicsRunningProcesses_v3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DeviceGetGraphicsRunningProcesses(
            IntPtr device, ref uint infoCount,
            [In, Out] ProcessInfo[] infos);
    }

    /// <summary>
    /// Per-process GPU VRAM info.
    /// </summary>
    internal class GpuProcessInfo
    {
        public int Pid;
        public string Name;
        public float VramMb;

        /// <summary>Check if this looks like an LM Studio process.</summary>
        public bool IsLmStudio => Name != null && (
            Name.IndexOf("LM Studio", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("lms", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("llama", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.Equals("server", StringComparison.OrdinalIgnoreCase));

        /// <summary>Check if this looks like a RimWorld process.</summary>
        public bool IsRimWorld => Name != null && (
            Name.IndexOf("RimWorld", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Name.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
