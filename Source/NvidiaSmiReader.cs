using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Polls nvidia-smi on a background thread to read GPU stats.
    /// Targets NVIDIA 4000/5000 series (RTX 40xx, RTX 50xx).
    /// nvidia-smi ships with every NVIDIA driver — no extra install needed.
    ///
    /// Queries:
    ///   GPU name, utilization, VRAM used/total, temperature, power draw/limit,
    ///   fan speed, clock speeds, driver version, per-process VRAM.
    /// </summary>
    internal static class NvidiaSmiReader
    {
        private static Thread _pollThread;
        private static volatile bool _shutdown;
        private static volatile bool _available;
        private static readonly object _lock = new object();

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
                Name = "RimSynapse-NvidiaSmi",
            };
            _pollThread.Start();
        }

        /// <summary>Stop background polling.</summary>
        internal static void Shutdown()
        {
            _shutdown = true;
        }

        private static void PollLoop()
        {
            // Initial check — is nvidia-smi available?
            if (!TestNvidiaSmi())
            {
                _available = false;
                LastError = "nvidia-smi not found. Ensure NVIDIA drivers are installed.";
                Verse.Log.Warning($"[RimSynapse NV] {LastError}");
                return;
            }

            _available = true;
            Verse.Log.Message($"[RimSynapse NV] nvidia-smi detected. GPU: {GpuName}, Driver: {DriverVersion}");

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
                    Verse.Log.Message($"[RimSynapse NV] nvidia-smi poll error: {ex.Message}");
                }

                Thread.Sleep(PollIntervalMs);
            }
        }

        /// <summary>
        /// Test whether nvidia-smi is available and grab static info.
        /// </summary>
        private static bool TestNvidiaSmi()
        {
            try
            {
                string output = RunNvidiaSmi(
                    "--query-gpu=name,driver_version --format=csv,noheader,nounits");

                if (string.IsNullOrEmpty(output))
                    return false;

                // Output: "NVIDIA GeForce RTX 5090, 572.83"
                var parts = output.Trim().Split(',');
                if (parts.Length >= 2)
                {
                    GpuName = parts[0].Trim();
                    DriverVersion = parts[1].Trim();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Poll core GPU metrics via nvidia-smi CSV output.
        /// </summary>
        private static void PollGpuStats()
        {
            string output = RunNvidiaSmi(
                "--query-gpu=" +
                "utilization.gpu," +
                "memory.used," +
                "memory.total," +
                "temperature.gpu," +
                "power.draw," +
                "power.limit," +
                "fan.speed," +
                "clocks.current.graphics," +
                "clocks.current.memory" +
                " --format=csv,noheader,nounits");

            if (string.IsNullOrEmpty(output))
                return;

            // Output: "85, 11234, 24576, 72, 320.45, 450.00, 55, 2520, 10501"
            var parts = output.Trim().Split(',');
            if (parts.Length >= 9)
            {
                lock (_lock)
                {
                    UtilizationPercent = ParseInt(parts[0]);
                    UsedVramMb = ParseFloat(parts[1]);
                    TotalVramMb = ParseFloat(parts[2]);
                    TemperatureC = ParseInt(parts[3]);
                    PowerDrawW = ParseFloat(parts[4]);
                    PowerLimitW = ParseFloat(parts[5]);
                    FanSpeedPercent = ParseInt(parts[6]);
                    GpuClockMhz = ParseInt(parts[7]);
                    MemClockMhz = ParseInt(parts[8]);
                    LastUpdated = DateTime.UtcNow;
                    LastError = null;
                }
            }
        }

        /// <summary>
        /// Poll per-process VRAM usage (identifies LM Studio, RimWorld, etc.).
        /// </summary>
        private static void PollProcesses()
        {
            string output = RunNvidiaSmi(
                "--query-compute-apps=pid,name,used_memory " +
                "--format=csv,noheader,nounits");

            var newProcesses = new List<GpuProcessInfo>();

            if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Split(new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 3)
                    {
                        newProcesses.Add(new GpuProcessInfo
                        {
                            Pid = ParseInt(parts[0]),
                            Name = parts[1].Trim(),
                            VramMb = ParseFloat(parts[2]),
                        });
                    }
                }
            }

            lock (_lock)
            {
                Processes = newProcesses;
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

                // Update per-process list
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

        /// <summary>
        /// Run nvidia-smi with arguments and capture stdout.
        /// </summary>
        private static string RunNvidiaSmi(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using (var process = Process.Start(psi))
            {
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000); // 5 second timeout

                if (process.ExitCode != 0)
                    return null;

                return output;
            }
        }

        // ── Parse helpers (tolerant of whitespace / [N/A] values) ──

        private static int ParseInt(string s)
        {
            s = s?.Trim();
            if (string.IsNullOrEmpty(s) || s.Contains("N/A"))
                return 0;
            return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture,
                out int v) ? v : 0;
        }

        private static float ParseFloat(string s)
        {
            s = s?.Trim();
            if (string.IsNullOrEmpty(s) || s.Contains("N/A"))
                return 0f;
            return float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture,
                out float v) ? v : 0f;
        }
    }

    /// <summary>
    /// Per-process GPU VRAM info from nvidia-smi.
    /// </summary>
    internal class GpuProcessInfo
    {
        public int Pid;
        public string Name;
        public float VramMb;

        /// <summary>Check if this looks like an LM Studio process.</summary>
        public bool IsLmStudio => Name != null && (
            Name.Contains("LM Studio") ||
            Name.Contains("lms") ||
            Name.Contains("llama") ||
            Name.Contains("server"));

        /// <summary>Check if this looks like a RimWorld process.</summary>
        public bool IsRimWorld => Name != null && (
            Name.Contains("RimWorld") ||
            Name.Contains("Unity"));
    }
}
