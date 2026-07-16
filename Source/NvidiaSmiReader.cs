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
            RimSynapse.SynapseLogger.Message($"[RimSynapse NV] NVML initialized. GPU: {GpuName}, Driver: {DriverVersion}");

            while (!_shutdown)
            {
                try
                {
                    PollGpuStats();
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
                    RimSynapse.SynapseLogger.Warning($"[RimSynapse NV] {LastError}");
                    return false;
                }

                // Get first GPU device
                result = Nvml.DeviceGetHandleByIndex(0, out _device);
                if (result != Nvml.SUCCESS || _device == IntPtr.Zero)
                {
                    LastError = $"No NVIDIA GPU found (code {result}).";
                    RimSynapse.SynapseLogger.Warning($"[RimSynapse NV] {LastError}");
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
                RimSynapse.SynapseLogger.Warning($"[RimSynapse NV] {LastError}");
                return false;
            }
            catch (Exception ex)
            {
                LastError = $"NVML init error: {ex.Message}";
                RimSynapse.SynapseLogger.Warning($"[RimSynapse NV] {LastError}");
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
                    float vramMb = infos[i].usedGpuMemory / (1024f * 1024f);

                    // Sanity clamp: process can't use more VRAM than the GPU has
                    if (vramMb > TotalVramMb) vramMb = 0f;
                    if (vramMb < 0f) vramMb = 0f;

                    list.Add(new GpuProcessInfo
                    {
                        Pid = pid,
                        Name = procName,
                        VramMb = vramMb,
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
}

