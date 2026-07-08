using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Adds a GPU overlay toggle button to RimWorld's play settings toolbar
    /// (the row of icons at the bottom-right of the screen).
    ///
    /// Uses Harmony to patch PlaySettings.DoPlaySettingsGlobalControls.
    /// The icon is generated programmatically — no texture files needed.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class ToolbarToggle
    {
        /// <summary>The toolbar icon texture (generated at startup).</summary>
        private static readonly Texture2D GpuIcon;

        static ToolbarToggle()
        {
            // Generate a 24x24 GPU chip icon programmatically
            GpuIcon = GenerateGpuIcon();

            // Apply Harmony patch
            var harmony = new Harmony("rimsynapse.nvtool.toolbar");
            harmony.PatchAll(typeof(ToolbarToggle).Assembly);
        }

        /// <summary>
        /// Generates a 24x24 GPU chip icon in NVIDIA green.
        /// Design: simple chip/processor outline with pin traces.
        /// </summary>
        private static Texture2D GenerateGpuIcon()
        {
            const int size = 24;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;

            // Match RimWorld's toolbar icon style: white/light gray
            var body = new Color32(220, 220, 220, 255);     // light gray
            var pins = new Color32(180, 180, 180, 180);     // dimmer gray
            var clear = new Color32(0, 0, 0, 0);

            // Fill transparent
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            // Draw chip body (8x8 centered rectangle, offset slightly)
            int chipX = 8, chipY = 8, chipW = 8, chipH = 8;
            for (int y = chipY; y < chipY + chipH; y++)
                for (int x = chipX; x < chipX + chipW; x++)
                    pixels[y * size + x] = body;

            // Draw chip outline (1px border around the chip)
            for (int x = chipX - 1; x <= chipX + chipW; x++)
            {
                pixels[(chipY - 1) * size + x] = body;           // top
                pixels[(chipY + chipH) * size + x] = body;        // bottom
            }
            for (int y = chipY - 1; y <= chipY + chipH; y++)
            {
                pixels[y * size + (chipX - 1)] = body;            // left
                pixels[y * size + (chipX + chipW)] = body;         // right
            }

            // Draw pin traces (extending from chip edges)
            // Top pins
            for (int i = 0; i < 4; i++)
            {
                int px = chipX + i * 2;
                pixels[(chipY - 2) * size + px] = pins;
                pixels[(chipY - 3) * size + px] = pins;
            }
            // Bottom pins
            for (int i = 0; i < 4; i++)
            {
                int px = chipX + i * 2;
                pixels[(chipY + chipH + 1) * size + px] = pins;
                pixels[(chipY + chipH + 2) * size + px] = pins;
            }
            // Left pins
            for (int i = 0; i < 4; i++)
            {
                int py = chipY + i * 2;
                pixels[py * size + (chipX - 2)] = pins;
                pixels[py * size + (chipX - 3)] = pins;
            }
            // Right pins
            for (int i = 0; i < 4; i++)
            {
                int py = chipY + i * 2;
                pixels[py * size + (chipX + chipW + 1)] = pins;
                pixels[py * size + (chipX + chipW + 2)] = pins;
            }

            // Small dot in center of chip (the GPU "core")
            var bright = new Color32(255, 255, 255, 255);
            pixels[12 * size + 12] = bright;
            pixels[11 * size + 12] = bright;
            pixels[12 * size + 11] = bright;
            pixels[11 * size + 11] = bright;

            tex.SetPixels32(pixels);
            tex.Apply(false, true); // makeNoLongerReadable = true for perf
            return tex;
        }
    }

    /// <summary>
    /// Harmony postfix on PlaySettings.DoPlaySettingsGlobalControls.
    /// Adds our GPU toggle icon to the toolbar row.
    /// </summary>
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    [StaticConstructorOnStartup]
    internal static class PlaySettingsPatch
    {
        [HarmonyPostfix]
        static void Postfix(WidgetRow row, bool worldView)
        {
            // Only show in colony view, not world map
            if (worldView) return;
            if (row == null) return;

            bool isOn = OverlayHud.CurrentMode != OverlayMode.Off;
            bool wasOn = isOn;

            row.ToggleableIcon(ref isOn, ToolbarToggle_GetIcon(),
                "Toggle RimSynapse GPU Overlay\n\n" +
                "Shows real-time VRAM usage breakdown.\n" +
                "Click to toggle. Use mod settings\nto switch Basic/Advanced.",
                SoundDefOf.Mouseover_ButtonToggle);

            // Handle toggle change
            if (isOn != wasOn)
            {
                if (isOn)
                    OverlayHud.SetMode(OverlayMode.Basic);
                else
                    OverlayHud.SetMode(OverlayMode.Off);
            }
        }

        /// <summary>Access the icon texture from the static constructor class.</summary>
        private static Texture2D ToolbarToggle_GetIcon()
        {
            // Use reflection-free access via a static field
            return _cachedIcon;
        }

        private static readonly Texture2D _cachedIcon = GenerateIconForPatch();

        private static Texture2D GenerateIconForPatch()
        {
            const int size = 24;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;

            var body = new Color32(220, 220, 220, 255);
            var pins = new Color32(180, 180, 180, 180);
            var bright = new Color32(255, 255, 255, 255);
            var clear = new Color32(0, 0, 0, 0);

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            // Chip body
            for (int y = 8; y < 16; y++)
                for (int x = 8; x < 16; x++)
                    pixels[y * size + x] = body;

            // Chip border
            for (int x = 7; x <= 16; x++)
            {
                pixels[7 * size + x] = body;
                pixels[16 * size + x] = body;
            }
            for (int y = 7; y <= 16; y++)
            {
                pixels[y * size + 7] = body;
                pixels[y * size + 16] = body;
            }

            // Pins (top, bottom, left, right)
            for (int i = 0; i < 4; i++)
            {
                int px = 8 + i * 2;
                pixels[6 * size + px] = pins;
                pixels[5 * size + px] = pins;
                pixels[17 * size + px] = pins;
                pixels[18 * size + px] = pins;
            }
            for (int i = 0; i < 4; i++)
            {
                int py = 8 + i * 2;
                pixels[py * size + 6] = pins;
                pixels[py * size + 5] = pins;
                pixels[py * size + 17] = pins;
                pixels[py * size + 18] = pins;
            }

            // Center dot
            pixels[12 * size + 12] = bright;
            pixels[11 * size + 12] = bright;
            pixels[12 * size + 11] = bright;
            pixels[11 * size + 11] = bright;

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
