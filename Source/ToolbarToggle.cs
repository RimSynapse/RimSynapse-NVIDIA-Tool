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
        private static Texture2D GenerateIconFromMask(string[] mask)
        {
            int size = 24;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            var body = new Color32(150, 150, 150, 255);
            var black = new Color32(0, 0, 0, 255);

            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            bool[,] isBody = new bool[size, size];
            for (int y = 0; y < size; y++)
            {
                int texY = size - 1 - y;
                if (y < mask.Length)
                {
                    for (int x = 0; x < size && x < mask[y].Length; x++)
                    {
                        if (mask[y][x] != ' ') isBody[x, texY] = true;
                    }
                }
            }

            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    if (isBody[x, y])
                    {
                        pixels[y * size + x] = body;
                    }
                    else
                    {
                        bool nearBody = false;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                                {
                                    if (isBody[nx, ny]) nearBody = true;
                                }
                            }
                        }
                        if (nearBody) pixels[y * size + x] = black;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D GenerateGpuIcon()
        {
            string[] gpuMask = new string[]
            {
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "    xxxx xxxx   xx  x   ",
                "   xx  x xx xx  xx  x   ",
                "   xx    xx  x  xx  x   ",
                "   xx    xx  x  xx  x   ",
                "   xx xx xxxx   xx  x   ",
                "   xx  x xx     xx  x   ",
                "   xx  x xx     xx  x   ",
                "    xxxx xx      xxxx   ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        ",
                "                        "
            };
            return GenerateIconFromMask(gpuMask);
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
