using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Compact, always-on-screen GPU overlay HUD.
    ///
    /// Three display modes (cycled via button or keybind):
    ///   Off → Basic → Advanced → Off
    ///
    /// Basic shows:
    ///   Available VRAM (total)
    ///   System      %   mem
    ///   RimWorld    %   mem
    ///   LM Studio   %   mem
    ///
    /// Advanced adds:
    ///   Active model name
    ///   Context window size
    ///   Tokens/sec throughput
    ///   Total API calls
    ///   Throttle level
    /// </summary>
    public class OverlayHud : GameComponent
    {
        // ── Display state ──
        private static OverlayMode _mode = OverlayMode.Off;
        private static Vector2 _position = new Vector2(10f, 200f);
        private static bool _dragging;
        private static Vector2 _dragOffset;

        // ── Layout constants ──
        private const float PanelWidth = 260f;
        private const float RowHeight = 18f;
        private const float Padding = 8f;
        private const float HeaderHeight = 22f;
        private const float BarHeight = 4f;

        // ── Colors ──
        private static readonly Color BgColor = new Color(0.08f, 0.08f, 0.10f, 0.88f);
        private static readonly Color HeaderBg = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        private static readonly Color BorderColor = new Color(0.25f, 0.55f, 0.90f, 0.6f);
        private static readonly Color TextLabel = new Color(0.65f, 0.65f, 0.65f);
        private static readonly Color TextValue = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color TextTitle = new Color(0.4f, 0.8f, 1.0f);
        private static readonly Color AccentGreen = new Color(0.3f, 0.9f, 0.4f);
        private static readonly Color AccentYellow = new Color(0.95f, 0.85f, 0.2f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.55f, 0.2f);
        private static readonly Color AccentRed = new Color(0.95f, 0.25f, 0.25f);
        private static readonly Color BarBg = new Color(0.15f, 0.15f, 0.18f);
        private static readonly Color BarVram = new Color(0.25f, 0.6f, 0.95f, 0.9f);

        // Cached textures
        private static Texture2D _bgTex;
        private static Texture2D _headerTex;
        private static Texture2D _borderTex;
        private static Texture2D _barBgTex;
        private static Texture2D _barFillTex;
        private static Texture2D _whiteTex;

        public OverlayHud(Game game) : base() { }

        /// <summary>Toggle overlay mode: Off → Basic → Advanced → Off.</summary>
        public static void CycleMode()
        {
            switch (_mode)
            {
                case OverlayMode.Off: _mode = OverlayMode.Basic; break;
                case OverlayMode.Basic: _mode = OverlayMode.Advanced; break;
                case OverlayMode.Advanced: _mode = OverlayMode.Off; break;
            }
        }

        /// <summary>Set overlay to a specific mode.</summary>
        public static void SetMode(OverlayMode mode) => _mode = mode;
        public static OverlayMode CurrentMode => _mode;

        public override void GameComponentOnGUI()
        {
            if (_mode == OverlayMode.Off) return;
            if (!NvidiaSmiReader.IsAvailable) return;

            EnsureTextures();

            // Calculate panel height based on mode
            int basicRows = 5; // header + vram bar + system + rimworld + lmstudio
            int advancedRows = _mode == OverlayMode.Advanced ? 6 : 0; // divider + model + ctx + tok/s + calls + throttle
            float panelHeight = Padding + HeaderHeight + Padding
                + (basicRows * RowHeight)
                + (advancedRows > 0 ? 4f + (advancedRows * RowHeight) : 0f)
                + Padding;

            // Clamp position to screen
            _position.x = Mathf.Clamp(_position.x, 0, Screen.width - PanelWidth);
            _position.y = Mathf.Clamp(_position.y, 0, Screen.height - panelHeight);

            var panelRect = new Rect(_position.x, _position.y, PanelWidth, panelHeight);

            // ── Handle dragging ──
            HandleDrag(panelRect);

            // ── Draw panel background ──
            GUI.DrawTexture(panelRect, _bgTex);

            // Border (1px)
            DrawBorder(panelRect);

            float y = panelRect.y + Padding;
            float x = panelRect.x + Padding;
            float contentWidth = PanelWidth - Padding * 2;

            // ── Header ──
            var headerRect = new Rect(panelRect.x, y - 2f, PanelWidth, HeaderHeight);
            GUI.DrawTexture(headerRect, _headerTex);

            // Title
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = TextTitle },
            };
            GUI.Label(new Rect(x, y, contentWidth - 50f, HeaderHeight - 4f),
                "▸ RimSynapse GPU", titleStyle);

            // Mode toggle button (right side of header)
            var modeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.5f, 0.5f, 0.6f) },
            };
            string modeLabel = _mode == OverlayMode.Basic ? "[Basic]" : "[Advanced]";
            var modeBtnRect = new Rect(x + contentWidth - 60f, y, 60f, HeaderHeight - 4f);
            if (GUI.Button(modeBtnRect, modeLabel, modeStyle))
            {
                CycleMode();
                if (_mode == OverlayMode.Off) _mode = OverlayMode.Basic; // Skip Off when clicking
            }

            y += HeaderHeight + 2f;

            // ── VRAM total line ──
            float totalVramGb = NvidiaSmiReader.TotalVramMb / 1024f;
            float usedVramGb = NvidiaSmiReader.UsedVramMb / 1024f;
            float availableVramGb = totalVramGb - usedVramGb;
            float vramPct = totalVramGb > 0 ? usedVramGb / totalVramGb : 0f;

            DrawRow(x, ref y, contentWidth, "Available VRAM",
                $"{availableVramGb:F1} / {totalVramGb:F1} GB",
                VramColor(vramPct));

            // VRAM bar
            DrawMiniBar(x, ref y, contentWidth, vramPct, BarVram);
            y += 4f;

            // ── Per-process breakdown ──
            var processes = NvidiaSmiReader.Processes;

            // System (everything not RimWorld or LM Studio)
            float lmsMb = 0f, rwMb = 0f, otherMb = 0f;
            if (processes != null)
            {
                foreach (var p in processes)
                {
                    if (p.IsLmStudio) lmsMb += p.VramMb;
                    else if (p.IsRimWorld) rwMb += p.VramMb;
                    else otherMb += p.VramMb;
                }
            }
            // System = used - identified processes
            float identifiedMb = lmsMb + rwMb + otherMb;
            float systemMb = NvidiaSmiReader.UsedVramMb - identifiedMb;
            if (systemMb < 0) systemMb = 0;
            float totalUsedMb = NvidiaSmiReader.UsedVramMb;

            DrawProcessRow(x, ref y, contentWidth, "System",
                systemMb + otherMb, totalUsedMb, NvidiaSmiReader.TotalVramMb);
            DrawProcessRow(x, ref y, contentWidth, "RimWorld",
                rwMb, totalUsedMb, NvidiaSmiReader.TotalVramMb);
            DrawProcessRow(x, ref y, contentWidth, "LM Studio",
                lmsMb, totalUsedMb, NvidiaSmiReader.TotalVramMb);

            // ── Advanced section ──
            if (_mode == OverlayMode.Advanced)
            {
                y += 2f;
                // Divider line
                var divRect = new Rect(x, y, contentWidth, 1f);
                GUI.DrawTexture(divRect, _barBgTex);
                y += 6f;

                // Model name — read from Core's public API
                string modelName = "—";
                try
                {
                    // Try to get model from SynapseClient.Gpu or settings
                    var settings = RimSynapseMod.Instance?.Settings;
                    if (settings != null && !string.IsNullOrEmpty(settings.selectedModel))
                        modelName = TruncateModel(settings.selectedModel);
                }
                catch { /* Ignore */ }
                DrawRow(x, ref y, contentWidth, "Model", modelName, TextValue);

                // Context window
                string ctxText = "—";
                try
                {
                    var gpu = SynapseClient.Gpu;
                    // Context window is available through ModelManager indirectly
                    // For now, show what we know
                    ctxText = "8192 tok"; // Will be dynamic once we expose it
                }
                catch { /* Ignore */ }
                DrawRow(x, ref y, contentWidth, "Context", ctxText, TextValue);

                // Tokens/sec
                float tokSec = RequestMetrics.TokensPerSecond;
                DrawRow(x, ref y, contentWidth, "Tokens/s",
                    tokSec > 0 ? $"{tokSec:F1} tok/s" : "—",
                    tokSec > 20 ? AccentGreen : tokSec > 10 ? AccentYellow : TextValue);

                // API calls
                DrawRow(x, ref y, contentWidth, "API Calls",
                    RequestMetrics.TotalRequests.ToString(), TextValue);

                // Throttle
                float throttle = SynapseClient.ThrottleLevel;
                Color throttleColor = throttle >= 0.9f ? AccentGreen
                    : throttle >= 0.5f ? AccentYellow
                    : throttle >= 0.2f ? AccentOrange : AccentRed;
                DrawRow(x, ref y, contentWidth, "Throttle",
                    $"{throttle:P0}", throttleColor);

                // Queue depth (bonus)
                int queueDepth = SynapseClient.TotalQueueDepth;
                if (queueDepth > 0)
                {
                    DrawRow(x, ref y, contentWidth, "Queue",
                        queueDepth.ToString(), AccentYellow);
                }
            }
        }

        // ────────────────────────────────────────────────────────
        //  Drawing helpers
        // ────────────────────────────────────────────────────────

        private void DrawRow(float x, ref float y, float width,
            string label, string value, Color valueColor)
        {
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = TextLabel },
            };
            var valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = valueColor },
            };

            GUI.Label(new Rect(x, y, width * 0.45f, RowHeight), label, labelStyle);
            GUI.Label(new Rect(x + width * 0.45f, y, width * 0.55f, RowHeight),
                value, valueStyle);
            y += RowHeight;
        }

        private void DrawProcessRow(float x, ref float y, float width,
            string name, float usedMb, float totalUsedMb, float totalMb)
        {
            float pct = totalMb > 0 ? (usedMb / totalMb) * 100f : 0f;
            float gb = usedMb / 1024f;

            string valueStr = usedMb > 0
                ? $"{pct:F0}%   {gb:F1} GB"
                : "—";

            Color color = usedMb > 0 ? TextValue : TextLabel;
            DrawRow(x, ref y, width, name, valueStr, color);
        }

        private void DrawMiniBar(float x, ref float y, float width,
            float fill, Color barColor)
        {
            var bgRect = new Rect(x, y, width, BarHeight);
            GUI.DrawTexture(bgRect, _barBgTex);

            if (fill > 0f)
            {
                var fillRect = new Rect(x, y, width * Mathf.Clamp01(fill), BarHeight);
                var prevColor = GUI.color;
                GUI.color = barColor;
                GUI.DrawTexture(fillRect, _whiteTex);
                GUI.color = prevColor;
            }
            y += BarHeight + 2f;
        }

        private void DrawBorder(Rect rect)
        {
            var prev = GUI.color;
            GUI.color = BorderColor;
            // Top
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), _whiteTex);
            // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), _whiteTex);
            // Left
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1f, rect.height), _whiteTex);
            // Right
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), _whiteTex);
            GUI.color = prev;
        }

        // ────────────────────────────────────────────────────────
        //  Drag handling
        // ────────────────────────────────────────────────────────

        private void HandleDrag(Rect panelRect)
        {
            var e = Event.current;
            if (e == null) return;

            // Only drag from the header area
            var headerRect = new Rect(panelRect.x, panelRect.y,
                panelRect.width, HeaderHeight + Padding);

            if (e.type == EventType.MouseDown && e.button == 0
                && headerRect.Contains(e.mousePosition))
            {
                _dragging = true;
                _dragOffset = e.mousePosition - _position;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                _position = e.mousePosition - _dragOffset;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _dragging)
            {
                _dragging = false;
                e.Use();
            }
        }

        // ────────────────────────────────────────────────────────
        //  Utilities
        // ────────────────────────────────────────────────────────

        private static string TruncateModel(string model)
        {
            if (string.IsNullOrEmpty(model)) return "—";
            // Strip org prefix: "google/gemma-4-12b-qat" → "gemma-4-12b-qat"
            int slash = model.LastIndexOf('/');
            if (slash >= 0 && slash < model.Length - 1)
                model = model.Substring(slash + 1);
            // Truncate if too long
            if (model.Length > 22)
                model = model.Substring(0, 19) + "...";
            return model;
        }

        private static Color VramColor(float pct)
        {
            if (pct < 0.5f) return AccentGreen;
            if (pct < 0.75f) return AccentYellow;
            if (pct < 0.9f) return AccentOrange;
            return AccentRed;
        }

        private static void EnsureTextures()
        {
            if (_bgTex != null) return;

            _bgTex = MakeTex(BgColor);
            _headerTex = MakeTex(HeaderBg);
            _borderTex = MakeTex(BorderColor);
            _barBgTex = MakeTex(BarBg);
            _barFillTex = MakeTex(BarVram);
            _whiteTex = MakeTex(Color.white);
        }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }

    public enum OverlayMode
    {
        Off,
        Basic,
        Advanced,
    }
}
