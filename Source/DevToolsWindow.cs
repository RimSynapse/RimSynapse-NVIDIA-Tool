using System;
using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// RimSynapse NVIDIA Tool — dashboard window for GPU stats and LLM performance.
    /// Section drawing methods are in DevToolsWindow_Sections.cs (partial class).
    /// This file contains the window shell, DoWindowContents, and drawing helpers.
    /// </summary>
    public partial class DevToolsWindow : Window
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

            // ── Sections (in partial class) ──
            DrawGpuSection(listing);
            listing.Gap(8f);
            DrawLmStudioSection(listing);
            listing.Gap(8f);
            DrawQueueSection(listing);
            listing.Gap(8f);
            DrawTokenSection(listing);
            listing.Gap(8f);
            DrawModStatsSection(listing);
            listing.Gap(8f);
            DrawContextSection(listing);

            listing.End();
            Widgets.EndScrollView();
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

            var prev = GUI.color;
            GUI.color = ColorDimText;
            Widgets.Label(labelRect, label);

            GUI.color = ColorBarBg;
            GUI.DrawTexture(barRect, BaseContent.WhiteTex);

            var fillRect = new Rect(barRect.x, barRect.y,
                barRect.width * Mathf.Clamp01(fillPercent), barRect.height);
            GUI.color = barColor;
            GUI.DrawTexture(fillRect, BaseContent.WhiteTex);

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
