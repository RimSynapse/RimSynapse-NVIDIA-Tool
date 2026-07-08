using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Mod entry point for RimSynapse NVIDIA Tool.
    /// Registers with Core, starts GPU polling, and adds the toolbar button.
    /// </summary>
    public class DevToolsMod : Mod
    {
        public static DevToolsMod Instance { get; private set; }

        private SynapseModHandle _handle;
        private DevToolsWindow _window;

        public DevToolsMod(ModContentPack content) : base(content)
        {
            Instance = this;

            // Register with Core (no system prompt — this mod doesn't make LLM calls)
            _handle = SynapseCore.Register(
                "rimsynapse.nvtool",
                "RimSynapse NVIDIA Tool");

            // Start GPU polling
            NvidiaSmiReader.Start();

            Verse.Log.Message("[RimSynapse NV] RimSynapse NVIDIA Tool loaded.");
        }

        public override string SettingsCategory() => "RimSynapse NVIDIA Tool";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("RimSynapse NVIDIA Tool",
                tooltip: "GPU monitoring and performance dashboard.");
            listing.GapLine();

            if (listing.ButtonText("Open Dashboard"))
            {
                OpenDashboard();
            }

            listing.Gap(8f);

            // GPU status summary
            if (NvidiaSmiReader.IsAvailable)
            {
                listing.Label($"GPU: {NvidiaSmiReader.GpuName}");
                listing.Label($"Driver: {NvidiaSmiReader.DriverVersion}");
                listing.Label($"Utilization: {NvidiaSmiReader.UtilizationPercent}%");
                listing.Label($"VRAM: {NvidiaSmiReader.UsedVramMb / 1024f:F1} / " +
                              $"{NvidiaSmiReader.TotalVramMb / 1024f:F1} GB");
                listing.Label($"Temperature: {NvidiaSmiReader.TemperatureC}°C");
            }
            else
            {
                var prev = GUI.color;
                GUI.color = Color.red;
                listing.Label("NVIDIA GPU not detected.");
                GUI.color = prev;

                if (!string.IsNullOrEmpty(NvidiaSmiReader.LastError))
                {
                    GUI.color = Color.yellow;
                    listing.Label(NvidiaSmiReader.LastError);
                    GUI.color = prev;
                }
            }

            listing.Gap(8f);

            // Quick stats
            listing.Label($"Session Requests: {RequestMetrics.TotalRequests}");
            listing.Label($"Avg Response: {RequestMetrics.AvgDurationMs:F0} ms");
            listing.Label($"Tokens/sec: {RequestMetrics.TokensPerSecond:F1}");

            listing.End();
        }

        /// <summary>
        /// Open or focus the DevTools dashboard window.
        /// </summary>
        public void OpenDashboard()
        {
            if (_window != null && Find.WindowStack.IsOpen(_window))
            {
                Find.WindowStack.TryRemove(_window);
                return;
            }

            _window = new DevToolsWindow();
            Find.WindowStack.Add(_window);
        }
    }

    /// <summary>
    /// Adds the DevTools debug toolbar button when in dev mode.
    /// Also adds a main menu button so it's always accessible.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DevToolsStartup
    {
        static DevToolsStartup()
        {
            // The mod is already initialized via DevToolsMod constructor.
            // This class exists for any post-load initialization.
            Verse.Log.Message("[RimSynapse NV] DevTools startup hook executed.");
        }
    }
}
