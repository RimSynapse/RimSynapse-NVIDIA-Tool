using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RimSynapse.NvidiaTool
{
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

        // Must match NVML C header exactly. The C struct has:
        //   uint pid (4 bytes) + 4 bytes padding + ulong usedGpuMemory (8 bytes)
        //   + uint gpuInstanceId (4) + uint computeInstanceId (4) = 24 bytes total
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        internal struct ProcessInfo
        {
            [FieldOffset(0)]  public uint pid;
            [FieldOffset(8)]  public ulong usedGpuMemory; // bytes
            [FieldOffset(16)] public uint gpuInstanceId;
            [FieldOffset(20)] public uint computeInstanceId;
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
