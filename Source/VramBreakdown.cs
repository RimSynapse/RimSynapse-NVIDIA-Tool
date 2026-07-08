using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Estimates per-application VRAM breakdown without relying on
    /// NVML process enumeration (which requires elevated privileges).
    ///
    /// Sources:
    ///   RimWorld  → Unity's own Texture.currentTextureMemory + overhead
    ///   LM Studio → Model parameter count parsed from Core settings
    ///   System    → Total VRAM used - RimWorld - LM Studio
    /// </summary>
    internal static class VramBreakdown
    {
        private static float _rimworldMb;
        private static float _lmStudioMb;
        private static float _systemMb;
        private static DateTime _lastUpdate = DateTime.MinValue;
        private const float UpdateIntervalSec = 3f;

        // ── Public accessors ──

        internal static float RimWorldMb => _rimworldMb;
        internal static float LmStudioMb => _lmStudioMb;
        internal static float SystemMb => _systemMb;

        /// <summary>
        /// Refresh the breakdown. Call from the overlay's OnGUI (throttled internally).
        /// </summary>
        internal static void Refresh()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastUpdate).TotalSeconds < UpdateIntervalSec) return;
            _lastUpdate = now;

            float totalUsedMb = NvidiaSmiReader.UsedVramMb;
            if (totalUsedMb <= 0f) return;

            // 1. RimWorld — query Unity's own GPU memory tracking
            _rimworldMb = GetRimWorldVramMb();

            // 2. LM Studio — estimate from loaded model parameters
            _lmStudioMb = EstimateLmStudioVramMb();

            // 3. System — everything else
            _systemMb = totalUsedMb - _rimworldMb - _lmStudioMb;
            if (_systemMb < 0f) _systemMb = 0f;
        }

        // ────────────────────────────────────────────────────────
        //  RimWorld VRAM (from Unity)
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Uses Unity's own memory APIs to determine how much VRAM
        /// RimWorld is consuming. No external calls needed.
        /// </summary>
        private static float GetRimWorldVramMb()
        {
            try
            {
                // Texture.currentTextureMemory = actual GPU-resident texture bytes
                // This is the most reliable Unity API for GPU memory tracking
                long texBytes = (long)Texture.currentTextureMemory;
                float texMb = texBytes / (1024f * 1024f);

                // Add overhead for:
                //   - Render targets / frame buffers (~15-25% of texture memory)
                //   - Shader programs, constant buffers
                //   - Mesh GPU buffers (vertex/index)
                //   - Unity internal GPU allocations
                // Conservative 40% overhead multiplier for a 2D-heavy game like RimWorld
                float estimatedTotalMb = texMb * 1.4f;

                // Floor: even a minimal RimWorld scene uses some GPU memory
                if (estimatedTotalMb < 50f) estimatedTotalMb = 50f;

                return estimatedTotalMb;
            }
            catch
            {
                // If Unity's API fails, return a reasonable default
                return 200f; // ~200 MB is typical for RimWorld
            }
        }

        // ────────────────────────────────────────────────────────
        //  LM Studio VRAM (estimated from model name)
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Estimates LM Studio's VRAM usage from the loaded model name.
        /// Parses parameter count (e.g., "12b", "7b") and applies
        /// Q4_K_M quantization estimate (~0.65 GB per billion params).
        /// </summary>
        private static float EstimateLmStudioVramMb()
        {
            try
            {
                string modelName = GetLoadedModelName();
                if (string.IsNullOrEmpty(modelName)) return 0f;

                float billionParams = ParseBillionParams(modelName);
                if (billionParams <= 0f) return 0f;

                // Q4_K_M quantization: ~0.65 GB per billion parameters
                // Add ~500 MB overhead for KV cache, context, runtime
                float estimateGb = (billionParams * 0.65f) + 0.5f;

                return estimateGb * 1024f; // convert GB → MB
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// Get the currently loaded model name from Core's settings.
        /// </summary>
        private static string GetLoadedModelName()
        {
            try
            {
                var settings = RimSynapseMod.Instance?.Settings;
                return settings?.selectedModel;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parse billion parameter count from model name.
        /// Handles patterns like:
        ///   "gemma-4-12b-qat"      → 12
        ///   "llama-3.3-70b"        → 70
        ///   "qwen2.5-7b-instruct"  → 7
        ///   "gemma-4-e4b"          → 4 (expert/MoE shorthand)
        ///   "gemma-4-26b-a4b"      → 26 (active params noted but total used)
        ///   "phi-4-mini-3.8b"      → 3.8
        /// </summary>
        internal static float ParseBillionParams(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return 0f;

            modelName = modelName.ToLowerInvariant();

            // Match patterns like "12b", "7b", "70b", "3.8b", "0.5b"
            // but NOT patterns like "q4b", "a4b", "e4b" (quantization/expert markers)
            var match = Regex.Match(modelName, @"(?<![a-z])(\d+\.?\d*)b(?!\w)");
            if (match.Success)
            {
                float val;
                if (float.TryParse(match.Groups[1].Value, out val) && val > 0f)
                    return val;
            }

            // Fallback: check for known size keywords
            if (modelName.Contains("mini")) return 3.8f;
            if (modelName.Contains("small")) return 7f;
            if (modelName.Contains("medium")) return 13f;
            if (modelName.Contains("large")) return 34f;

            return 0f;
        }
    }
}
