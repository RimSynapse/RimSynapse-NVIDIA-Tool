using System;
using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// RimSynapse NVIDIA Tool — dashboard window for GPU stats and LLM performance.
    ///
    /// Displays:
    ///   - GPU status (name, utilization, VRAM, temp, power, clocks)
    ///   - VRAM breakdown by process (LM Studio, RimWorld, other)
    ///   - LM Studio connection status and active model
    ///   - Request queue stats (depth, throughput, throttle)
    ///   - Per-mod request and budget stats
    ///   - Token metrics and capacity estimation
    ///   - Context embedding diagnostics
    /// </summary>
    public class DevToolsWindow : Window
    {
        private Vector2 _scrollPos;

        // Column layout
        private const float LabelWidth = 180f;
        private const float ValueWidth = 200f;
        private const float BarWidth = 200f;
        private const float BarHeight = 18f;

        // Colors
        private static readonly Color ColorGreen = new Color(0.3f, 0.9f, 0.3f);
        private static readonly Color ColorYellow = new Color(0.9f, 0.9f, 0.2f);
        private static readonly Color ColorOrange = new Color(0.9f, 0.6f, 0.2f);
        private static readonly Color ColorRed = new Color(0.9f, 0.2f, 0.2f);
        private static readonly Color ColorCyan = new Color(0.3f, 0.85f, 0.9f);
        private static readonly Color ColorDimText = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color ColorBarBg = new Color(0.15f, 0.15f, 0.15f);
        private static readonly Color ColorVramBar = new Color(0.2f, 0.6f, 0.9f);
        private static readonly Color ColorGpuBar = new Color(0.3f, 0.85f, 0.3f);
        private static readonly Color ColorTempBar = new Color(0.9f, 0.4f, 0.2f);
        private static readonly Color ColorPowerBar = new Color(0.9f, 0.8f, 0.2f);

        public DevToolsWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            closeOnClickedOutside = false;
            preventCameraMotion = false;
            focusWhenOpened = false;
        }

        public override Vector2 InitialSize => new Vector2(520f, 680f);

        public override void DoWindowContents(Rect inRect)
        {
            // Scrollable content
            float contentHeight = 1200f;
            var viewRect = new Rect(0, 0, inRect.width - 20f, contentHeight);
            Widgets.BeginScrollView(inRect, ref _scrollPos, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // ── Header ──
            Text.Font = GameFont.Medium;
            listing.Label("RimSynapse NVIDIA Tool");
            Text.Font = GameFont.Small;
            listing.GapLine();
            listing.Gap(4f);

            // ── GPU Section ──
            DrawGpuSection(listing);
            listing.Gap(8f);

            // ── LM Studio Section ──
            DrawLmStudioSection(listing);
            listing.Gap(8f);

            // ── Request Queue Section ──
            DrawQueueSection(listing);
            listing.Gap(8f);

            // ── Token Metrics Section ──
            DrawTokenSection(listing);
            listing.Gap(8f);

            // ── Per-Mod Stats Section ──
            DrawModStatsSection(listing);
            listing.Gap(8f);

            // ── Context Embedding Section ──
            DrawContextSection(listing);

            listing.End();
            Widgets.EndScrollView();
        }

        // ────────────────────────────────────────────────────────
        //  GPU Stats
        // ────────────────────────────────────────────────────────

        private void DrawGpuSection(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "GPU Status");

            if (!NvidiaSmiReader.IsAvailable)
            {
                var prev = GUI.color;
                GUI.color = ColorRed;
                listing.Label("  ✗ NVIDIA GPU not detected");
                GUI.color = ColorDimText;
                listing.Label("    " + (NvidiaSmiReader.LastError ?? "nvidia-smi not found"));
                GUI.color = prev;
                return;
            }

            // GPU name and driver
            DrawLabelValue(listing, "GPU", NvidiaSmiReader.GpuName);
            DrawLabelValue(listing, "Driver", NvidiaSmiReader.DriverVersion);

            listing.Gap(4f);

            // Utilization bar
            float utilPct = NvidiaSmiReader.UtilizationPercent / 100f;
            DrawProgressBar(listing, "GPU Utilization",
                $"{NvidiaSmiReader.UtilizationPercent}%",
                utilPct, GetLoadColor(utilPct));

            // VRAM bar
            float vramPct = NvidiaSmiReader.TotalVramMb > 0
                ? NvidiaSmiReader.UsedVramMb / NvidiaSmiReader.TotalVramMb : 0f;
            string vramText = $"{NvidiaSmiReader.UsedVramMb / 1024f:F1} / " +
                              $"{NvidiaSmiReader.TotalVramMb / 1024f:F1} GB";
            DrawProgressBar(listing, "VRAM", vramText, vramPct, ColorVramBar);

            // Temperature bar
            float tempPct = Math.Min(1f, NvidiaSmiReader.TemperatureC / 100f);
            DrawProgressBar(listing, "Temperature",
                $"{NvidiaSmiReader.TemperatureC}°C",
                tempPct, GetTempColor(NvidiaSmiReader.TemperatureC));

            // Power bar
            float powerPct = NvidiaSmiReader.PowerLimitW > 0
                ? NvidiaSmiReader.PowerDrawW / NvidiaSmiReader.PowerLimitW : 0f;
            DrawProgressBar(listing, "Power Draw",
                $"{NvidiaSmiReader.PowerDrawW:F0} / {NvidiaSmiReader.PowerLimitW:F0} W",
                powerPct, ColorPowerBar);

            // Clocks
            DrawLabelValue(listing, "GPU Clock", $"{NvidiaSmiReader.GpuClockMhz} MHz");
            DrawLabelValue(listing, "Mem Clock", $"{NvidiaSmiReader.MemClockMhz} MHz");
            DrawLabelValue(listing, "Fan Speed", $"{NvidiaSmiReader.FanSpeedPercent}%");

            // Process VRAM breakdown
            var processes = NvidiaSmiReader.Processes;
            if (processes.Count > 0)
            {
                listing.Gap(4f);
                listing.Label("  VRAM by Process:");

                foreach (var p in processes)
                {
                    string icon = p.IsLmStudio ? "⚡" : p.IsRimWorld ? "🎮" : "  ";
                    DrawLabelValue(listing, $"    {icon} {p.Name}",
                        $"{p.VramMb:F0} MB");
                }
            }

            // Staleness warning
            if (NvidiaSmiReader.LastUpdated != DateTime.MinValue)
            {
                var age = DateTime.UtcNow - NvidiaSmiReader.LastUpdated;
                if (age.TotalSeconds > 10)
                {
                    var prev = GUI.color;
                    GUI.color = ColorYellow;
                    listing.Label($"  ⚠ Data is {age.TotalSeconds:F0}s old");
                    GUI.color = prev;
                }
            }
        }

        // ────────────────────────────────────────────────────────
        //  LM Studio Status
        // ────────────────────────────────────────────────────────

        private void DrawLmStudioSection(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "LM Studio");

            bool online = SynapseClient.IsOnline;
            var prev = GUI.color;
            GUI.color = online ? ColorGreen : ColorRed;
            listing.Label(online ? "  ✓ Connected" : "  ✗ Offline");
            GUI.color = prev;

            var settings = RimSynapseMod.Instance?.Settings;
            if (settings != null)
            {
                DrawLabelValue(listing, "Endpoint", settings.lmStudioUrl);
            }

            // We need to access internal ModelManager data
            // Use the public API surface available through SynapseClient
            var gpu = SynapseClient.Gpu;
            if (gpu != null && gpu.supported)
            {
                // GPU info already shown above
            }
        }

        // ────────────────────────────────────────────────────────
        //  Request Queue
        // ────────────────────────────────────────────────────────

        private void DrawQueueSection(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Request Queue");

            int depth = SynapseClient.TotalQueueDepth;
            float throttle = SynapseClient.ThrottleLevel;

            DrawLabelValue(listing, "Queue Depth", depth.ToString());

            // Throttle level bar
            Color throttleColor = throttle >= 0.9f ? ColorGreen
                : throttle >= 0.6f ? ColorYellow
                : throttle >= 0.3f ? ColorOrange : ColorRed;
            DrawProgressBar(listing, "Throttle Level",
                $"{throttle:P0}", throttle, throttleColor);

            DrawLabelValue(listing, "Requests/Min",
                $"{RequestMetrics.RequestsPerMinute:F1}");
            DrawLabelValue(listing, "Avg Response",
                $"{RequestMetrics.AvgDurationMs:F0} ms");
            DrawLabelValue(listing, "Throttled",
                $"{RequestMetrics.ThrottledPercent:F1}%");
        }

        // ────────────────────────────────────────────────────────
        //  Token Metrics
        // ────────────────────────────────────────────────────────

        private void DrawTokenSection(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Token Metrics");

            DrawLabelValue(listing, "Session Requests",
                $"{RequestMetrics.TotalRequests} ({RequestMetrics.FailedRequests} failed)");
            DrawLabelValue(listing, "Total Prompt Tokens",
                $"{RequestMetrics.TotalPromptTokens:N0}");
            DrawLabelValue(listing, "Total Completion Tokens",
                $"{RequestMetrics.TotalCompletionTokens:N0}");

            listing.Gap(4f);

            DrawLabelValue(listing, "Avg Prompt Tokens",
                $"{RequestMetrics.AvgPromptTokens:F0}");
            DrawLabelValue(listing, "Avg Completion Tokens",
                $"{RequestMetrics.AvgCompletionTokens:F0}");
            DrawLabelValue(listing, "Tokens/Second",
                $"{RequestMetrics.TokensPerSecond:F1} tok/s");

            listing.Gap(4f);

            // Capacity estimation
            int maxPawns = RequestMetrics.EstimateMaxPawns(4096, 1);
            DrawLabelValue(listing, "Est. Max Pawns (4K ctx)", maxPawns.ToString());
            maxPawns = RequestMetrics.EstimateMaxPawns(8192, 1);
            DrawLabelValue(listing, "Est. Max Pawns (8K ctx)", maxPawns.ToString());
        }

        // ────────────────────────────────────────────────────────
        //  Per-Mod Stats
        // ────────────────────────────────────────────────────────

        private void DrawModStatsSection(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Registered Mods");

            var mods = SynapseCore.RegisteredMods;
            if (mods == null || mods.Count == 0)
            {
                var prev = GUI.color;
                GUI.color = ColorDimText;
                listing.Label("  No companion mods registered.");
                GUI.color = prev;
                return;
            }

            foreach (var mod in mods)
            {
                listing.Label($"  {mod.DisplayName}");
                DrawLabelValue(listing, "    Requests", mod.RequestCount.ToString());
                DrawLabelValue(listing, "    Queued", mod.QueuedCount.ToString());
                DrawLabelValue(listing, "    Budget", $"{mod.QueryBudgetPercent:F0}%");
                listing.Gap(2f);
            }
        }

        // ────────────────────────────────────────────────────────
        //  Context Embedding
        // ────────────────────────────────────────────────────────

        private void DrawContextSection(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Context Embedding");

            bool enabled = SynapseCoreContext.IsEnabled();
            var prev = GUI.color;
            GUI.color = enabled ? ColorGreen : ColorDimText;
            listing.Label(enabled ? "  ✓ Enabled" : "  ✗ Disabled");
            GUI.color = prev;
        }

        // ────────────────────────────────────────────────────────
        //  Drawing helpers
        // ────────────────────────────────────────────────────────

        private void DrawSectionHeader(Listing_Standard listing, string title)
        {
            var prev = GUI.color;
            GUI.color = ColorCyan;
            Text.Font = GameFont.Small;
            listing.Label($"▸ {title}");
            GUI.color = prev;
            listing.GapLine();
        }

        private void DrawLabelValue(Listing_Standard listing, string label, string value)
        {
            var rect = listing.GetRect(Text.LineHeight);
            var labelRect = new Rect(rect.x + 8f, rect.y, LabelWidth, rect.height);
            var valueRect = new Rect(rect.x + LabelWidth + 12f, rect.y,
                rect.width - LabelWidth - 12f, rect.height);

            var prev = GUI.color;
            GUI.color = ColorDimText;
            Widgets.Label(labelRect, label);
            GUI.color = Color.white;
            Widgets.Label(valueRect, value);
            GUI.color = prev;
        }

        private void DrawProgressBar(Listing_Standard listing, string label,
            string valueText, float fillPercent, Color barColor)
        {
            var rect = listing.GetRect(BarHeight + 4f);
            var labelRect = new Rect(rect.x + 8f, rect.y, LabelWidth, rect.height);
            var barRect = new Rect(rect.x + LabelWidth + 12f, rect.y + 2f,
                BarWidth, BarHeight);
            var textRect = new Rect(barRect.xMax + 8f, rect.y,
                rect.width - barRect.xMax - 8f, rect.height);

            // Label
            var prev = GUI.color;
            GUI.color = ColorDimText;
            Widgets.Label(labelRect, label);

            // Bar background
            GUI.color = ColorBarBg;
            GUI.DrawTexture(barRect, BaseContent.WhiteTex);

            // Bar fill
            var fillRect = new Rect(barRect.x, barRect.y,
                barRect.width * Mathf.Clamp01(fillPercent), barRect.height);
            GUI.color = barColor;
            GUI.DrawTexture(fillRect, BaseContent.WhiteTex);

            // Value text
            GUI.color = Color.white;
            Widgets.Label(textRect, valueText);

            GUI.color = prev;
        }

        private Color GetLoadColor(float pct)
        {
            if (pct < 0.5f) return ColorGreen;
            if (pct < 0.75f) return ColorYellow;
            if (pct < 0.9f) return ColorOrange;
            return ColorRed;
        }

        private Color GetTempColor(int tempC)
        {
            if (tempC < 60) return ColorGreen;
            if (tempC < 75) return ColorYellow;
            if (tempC < 85) return ColorOrange;
            return ColorRed;
        }
    }
}
