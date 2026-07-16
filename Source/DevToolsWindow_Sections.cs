using System;
using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Section drawing methods for the NVIDIA developer tools dashboard:
    /// GPU, LM Studio, Queue, Token, Mod Stats, and Context sections.
    /// </summary>
    public partial class DevToolsWindow
    {
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

            DrawLabelValue(listing, "GPU", NvidiaSmiReader.GpuName);
            DrawLabelValue(listing, "Driver", NvidiaSmiReader.DriverVersion);

            listing.Gap(4f);

            float utilPct = NvidiaSmiReader.UtilizationPercent / 100f;
            DrawProgressBar(listing, "GPU Utilization",
                $"{NvidiaSmiReader.UtilizationPercent}%",
                utilPct, GetLoadColor(utilPct));

            float vramPct = NvidiaSmiReader.TotalVramMb > 0
                ? NvidiaSmiReader.UsedVramMb / NvidiaSmiReader.TotalVramMb : 0f;
            string vramText = $"{NvidiaSmiReader.UsedVramMb / 1024f:F1} / " +
                              $"{NvidiaSmiReader.TotalVramMb / 1024f:F1} GB";
            DrawProgressBar(listing, "VRAM", vramText, vramPct, ColorVramBar);

            float tempPct = Math.Min(1f, NvidiaSmiReader.TemperatureC / 100f);
            DrawProgressBar(listing, "Temperature",
                $"{NvidiaSmiReader.TemperatureC}°C",
                tempPct, GetTempColor(NvidiaSmiReader.TemperatureC));

            float powerPct = NvidiaSmiReader.PowerLimitW > 0
                ? NvidiaSmiReader.PowerDrawW / NvidiaSmiReader.PowerLimitW : 0f;
            DrawProgressBar(listing, "Power Draw",
                $"{NvidiaSmiReader.PowerDrawW:F0} / {NvidiaSmiReader.PowerLimitW:F0} W",
                powerPct, ColorPowerBar);

            DrawLabelValue(listing, "GPU Clock", $"{NvidiaSmiReader.GpuClockMhz} MHz");
            DrawLabelValue(listing, "Mem Clock", $"{NvidiaSmiReader.MemClockMhz} MHz");
            DrawLabelValue(listing, "Fan Speed", $"{NvidiaSmiReader.FanSpeedPercent}%");

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
    }
}
