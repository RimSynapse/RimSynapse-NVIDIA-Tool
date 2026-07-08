using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Mod entry point for RimSynapse NVIDIA Tool.
    /// Registers with Core, starts GPU polling, and manages the overlay HUD.
    /// </summary>
    public class DevToolsMod : Mod
    {
        public static DevToolsMod Instance { get; private set; }

        public DevToolsSettings Settings { get; private set; }

        private SynapseModHandle _handle;
        private DevToolsWindow _window;

        public DevToolsMod(ModContentPack content) : base(content)
        {
            Instance = this;

            // Load persistent settings
            Settings = GetSettings<DevToolsSettings>();

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

            // ── Overlay controls ──
            listing.Label("In-Game Overlay");
            listing.Gap(4f);

            string modeText;
            switch (OverlayHud.CurrentMode)
            {
                case OverlayMode.Basic: modeText = "Basic"; break;
                case OverlayMode.Advanced: modeText = "Advanced"; break;
                case OverlayMode.Developer: modeText = "Developer"; break;
                default: modeText = "Off"; break;
            }

            if (listing.ButtonText($"Overlay: {modeText}  (click to cycle)"))
            {
                OverlayHud.CycleMode();
            }

            listing.Gap(4f);
            var prev1 = GUI.color;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            listing.Label("  Basic: VRAM breakdown (System, RimWorld, LM Studio)");
            listing.Label("  Advanced: + Model, tokens/s, API calls, throttle");
            listing.Label("  Developer: + GPU temp, power, clocks, fan, utilization");
            listing.Label("  Click the mode label on the overlay to cycle.");
            listing.Label("  Drag the overlay header to reposition.");
            GUI.color = prev1;

            listing.Gap(12f);
            listing.GapLine();

            // ── VRAM notification ──
            listing.Label("VRAM Notifications");
            listing.Gap(4f);

            listing.CheckboxLabeled(
                "Show VRAM status on game load",
                ref Settings.alwaysNotifyVram,
                "Shows your VRAM breakdown every time you load a colony.\n" +
                "Uncheck to disable notifications (still warns if critically low).");

            listing.Gap(4f);
            var prev2 = GUI.color;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            listing.Label("  Checked (default): VRAM status every colony load");
            listing.Label("  Unchecked: only warns when < 3 GB free");
            GUI.color = prev2;

            listing.Gap(12f);
            listing.GapLine();

            // ── Full dashboard ──
            if (listing.ButtonText("Open Full Dashboard"))
            {
                OpenDashboard();
            }

            listing.Gap(12f);
            listing.GapLine();

            // ── GPU status summary ──
            listing.Label("GPU Status");
            listing.Gap(4f);

            if (NvidiaSmiReader.IsAvailable)
            {
                listing.Label($"  GPU: {NvidiaSmiReader.GpuName}");
                listing.Label($"  Driver: {NvidiaSmiReader.DriverVersion}");
                listing.Label($"  Utilization: {NvidiaSmiReader.UtilizationPercent}%");
                listing.Label($"  VRAM: {NvidiaSmiReader.UsedVramMb / 1024f:F1} / " +
                              $"{NvidiaSmiReader.TotalVramMb / 1024f:F1} GB");
                listing.Label($"  Temperature: {NvidiaSmiReader.TemperatureC}°C");
                listing.Label($"  Power: {NvidiaSmiReader.PowerDrawW:F0} / " +
                              $"{NvidiaSmiReader.PowerLimitW:F0} W");
                listing.Label($"  GPU Clock: {NvidiaSmiReader.GpuClockMhz} MHz");
                listing.Label($"  Mem Clock: {NvidiaSmiReader.MemClockMhz} MHz");
                listing.Label($"  Fan: {NvidiaSmiReader.FanSpeedPercent}%");

                // Process breakdown
                var processes = NvidiaSmiReader.Processes;
                if (processes.Count > 0)
                {
                    listing.Gap(4f);
                    listing.Label("  VRAM by Process:");
                    foreach (var p in processes)
                    {
                        string icon = p.IsLmStudio ? "⚡" : p.IsRimWorld ? "🎮" : "  ";
                        listing.Label($"    {icon} {p.Name}: {p.VramMb:F0} MB");
                    }
                }
            }
            else
            {
                var prev = GUI.color;
                GUI.color = Color.red;
                listing.Label("  NVIDIA GPU not detected.");
                GUI.color = prev;

                if (!string.IsNullOrEmpty(NvidiaSmiReader.LastError))
                {
                    GUI.color = Color.yellow;
                    listing.Label($"  {NvidiaSmiReader.LastError}");
                    GUI.color = prev;
                }
            }

            listing.Gap(12f);
            listing.GapLine();

            // ── Request metrics ──
            listing.Label("Request Metrics");
            listing.Gap(4f);
            listing.Label($"  Session Requests: {RequestMetrics.TotalRequests}" +
                          $" ({RequestMetrics.FailedRequests} failed)");
            listing.Label($"  Avg Response: {RequestMetrics.AvgDurationMs:F0} ms");
            listing.Label($"  Avg Prompt Tokens: {RequestMetrics.AvgPromptTokens:F0}");
            listing.Label($"  Avg Completion Tokens: {RequestMetrics.AvgCompletionTokens:F0}");
            listing.Label($"  Tokens/sec: {RequestMetrics.TokensPerSecond:F1}");
            listing.Label($"  Requests/min: {RequestMetrics.RequestsPerMinute:F1}");
            listing.Label($"  Throttled: {RequestMetrics.ThrottledPercent:F1}%");

            listing.End();
        }

        /// <summary>
        /// Open or focus the DevTools full dashboard window.
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
    /// Post-load hook. Overlay starts Off — use the toolbar toggle to enable.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DevToolsStartup
    {
        static DevToolsStartup()
        {
            // Overlay starts off — toolbar toggle icon lets users enable it
            OverlayHud.SetMode(OverlayMode.Off);
            Verse.Log.Message("[RimSynapse NV] DevTools ready. Use toolbar icon to toggle GPU overlay.");
        }
    }
}
