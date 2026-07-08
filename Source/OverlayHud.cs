using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Compact, always-on-screen GPU overlay HUD.
    ///
    /// IMPORTANT: This is NOT a GameComponent. Using a Harmony patch on
    /// GameComponentUtility.GameComponentOnGUI() instead ensures zero save-file
    /// footprint — the mod can be safely added or removed from any save game
    /// without errors.
    ///
    /// Three display modes (cycled via button):
    ///   Basic    → VRAM breakdown
    ///   Advanced → + Model, tokens/s, API calls, throttle
    ///   Developer → + GPU temp, power, clocks, fan, utilization
    /// </summary>
    public static class OverlayHud
    {
        // ── Display state ──
        private static OverlayMode _mode = OverlayMode.Off;
        private static Vector2 _position = new Vector2(10f, 80f);
        private static bool _dragging;
        private static Vector2 _dragOffset;

        // ── Layout constants ──
        private const float PanelWidth = 280f;
        private const float RowHeight = 20f;
        private const float Padding = 10f;
        private const float HeaderHeight = 24f;
        private const float BarHeight = 4f;

        // ── Colors ──
        private static readonly Color BgColor = new Color(0.08f, 0.08f, 0.10f, 0.88f);
        private static readonly Color HeaderBg = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        private static readonly Color BorderColor = new Color(0.25f, 0.55f, 0.90f, 0.6f);
        private static readonly Color TextLabel = new Color(0.70f, 0.70f, 0.70f);
        private static readonly Color TextValue = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color AccentGreen = new Color(0.35f, 0.85f, 0.35f);
        private static readonly Color AccentYellow = new Color(0.90f, 0.85f, 0.30f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.55f, 0.20f);
        private static readonly Color AccentRed = new Color(0.95f, 0.30f, 0.25f);
        private static readonly Color BarVram = new Color(0.30f, 0.60f, 0.95f, 0.8f);

        // Cached textures
        private static Texture2D _bgTex;
        private static Texture2D _headerTex;
        private static Texture2D _borderTex;
        private static Texture2D _barBgTex;
        private static Texture2D _barFillTex;
        private static Texture2D _whiteTex;

        /// <summary>Toggle overlay mode: Off → Basic → Advanced → Developer → Off.</summary>
        public static void CycleMode()
        {
            switch (_mode)
            {
                case OverlayMode.Off: _mode = OverlayMode.Basic; break;
                case OverlayMode.Basic: _mode = OverlayMode.Advanced; break;
                case OverlayMode.Advanced: _mode = OverlayMode.Developer; break;
                case OverlayMode.Developer: _mode = OverlayMode.Off; break;
            }
        }

        /// <summary>Set overlay to a specific mode.</summary>
        public static void SetMode(OverlayMode mode) => _mode = mode;
        public static OverlayMode CurrentMode => _mode;

        /// <summary>
        /// Called from Harmony patch every GUI frame.
        /// </summary>
        internal static void OnGUI()
        {
            if (_mode == OverlayMode.Off) return;
            if (!NvidiaSmiReader.IsAvailable) return;

            // Only show during active colony gameplay (not world map, not menus)
            if (Verse.Current.Game?.CurrentMap == null) return;

            EnsureTextures();

            // Calculate panel height based on mode
            int basicRows = 5;
            bool showAdvanced = _mode == OverlayMode.Advanced || _mode == OverlayMode.Developer;
            int advancedRows = showAdvanced ? 6 : 0;
            int devRows = _mode == OverlayMode.Developer ? 8 : 0;
            float panelHeight = Padding + HeaderHeight + Padding
                + (basicRows * RowHeight)
                + (advancedRows > 0 ? 4f + (advancedRows * RowHeight) : 0f)
                + (devRows > 0 ? 4f + (devRows * RowHeight) : 0f)
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

            // ── Header row ──
            var headerRect = new Rect(panelRect.x, panelRect.y, PanelWidth, HeaderHeight + Padding);
            GUI.DrawTexture(headerRect, _headerTex);

            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.4f, 0.7f, 1.0f) },
            };
            GUI.Label(new Rect(x, y, contentWidth * 0.6f, HeaderHeight), "▸ RimSynapse GPU", headerStyle);

            // Mode toggle button (entire header is clickable)
            var modeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.5f, 0.5f, 0.6f) },
            };
            string modeLabel;
            switch (_mode)
            {
                case OverlayMode.Developer: modeLabel = "[Dev]"; break;
                case OverlayMode.Advanced: modeLabel = "[Adv]"; break;
                default: modeLabel = "[Basic]"; break;
            }
            
            // Draw the mode label
            var modeBtnRect = new Rect(x + contentWidth - 60f, y, 60f, HeaderHeight - 4f);
            GUI.Label(modeBtnRect, modeLabel, modeStyle);

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

            // ── Per-application breakdown ──
            VramBreakdown.Refresh();
            float totalUsedMb = NvidiaSmiReader.UsedVramMb;

            DrawProcessRow(x, ref y, contentWidth, "System",
                VramBreakdown.SystemMb, totalUsedMb, NvidiaSmiReader.TotalVramMb);
            DrawProcessRow(x, ref y, contentWidth, "RimWorld",
                VramBreakdown.RimWorldMb, totalUsedMb, NvidiaSmiReader.TotalVramMb);
            DrawProcessRow(x, ref y, contentWidth, "LM Studio",
                VramBreakdown.LmStudioMb, totalUsedMb, NvidiaSmiReader.TotalVramMb);

            // ── Advanced section (shown in Advanced + Developer) ──
            if (_mode == OverlayMode.Advanced || _mode == OverlayMode.Developer)
            {
                y += 2f;
                var divRect = new Rect(x, y, contentWidth, 1f);
                GUI.DrawTexture(divRect, _barBgTex);
                y += 6f;

                string modelName = "—";
                try
                {
                    // Prefer live model from API, fall back to persisted setting
                    string active = SynapseClient.ActiveModelName;
                    if (!string.IsNullOrEmpty(active))
                        modelName = TruncateModel(active);
                    else
                    {
                        var settings = RimSynapseMod.Instance?.Settings;
                        if (settings != null && !string.IsNullOrEmpty(settings.selectedModel))
                            modelName = TruncateModel(settings.selectedModel);
                    }
                }
                catch { }
                DrawRow(x, ref y, contentWidth, "Model", modelName, TextValue);

                string ctxText = "—";
                try
                {
                    var gpu = SynapseClient.Gpu;
                    ctxText = "8192 tok";
                }
                catch { }
                DrawRow(x, ref y, contentWidth, "Context", ctxText, TextValue);

                float tokSec = RequestMetrics.TokensPerSecond;
                DrawRow(x, ref y, contentWidth, "Tokens/s",
                    tokSec > 0 ? $"{tokSec:F1} tok/s" : "—",
                    tokSec > 20 ? AccentGreen : tokSec > 10 ? AccentYellow : TextValue);

                DrawRow(x, ref y, contentWidth, "API Calls",
                    RequestMetrics.TotalRequests.ToString(), TextValue);

                float throttle = SynapseClient.ThrottleLevel;
                Color throttleColor = throttle >= 0.9f ? AccentGreen
                    : throttle >= 0.5f ? AccentYellow
                    : throttle >= 0.2f ? AccentOrange : AccentRed;
                DrawRow(x, ref y, contentWidth, "Throttle",
                    $"{throttle:P0}", throttleColor);

                int queueDepth = SynapseClient.TotalQueueDepth;
                if (queueDepth > 0)
                {
                    DrawRow(x, ref y, contentWidth, "Queue",
                        queueDepth.ToString(), AccentYellow);
                }
            }

            // ── Developer section (full GPU hardware stats) ──
            if (_mode == OverlayMode.Developer)
            {
                y += 2f;
                var divRect2 = new Rect(x, y, contentWidth, 1f);
                GUI.DrawTexture(divRect2, _barBgTex);
                y += 6f;

                DrawRow(x, ref y, contentWidth, "GPU",
                    NvidiaSmiReader.GpuName, TextValue);

                DrawRow(x, ref y, contentWidth, "Driver",
                    NvidiaSmiReader.DriverVersion, TextValue);

                int tempC = NvidiaSmiReader.TemperatureC;
                Color tempColor = tempC < 60 ? AccentGreen
                    : tempC < 75 ? AccentYellow
                    : tempC < 85 ? AccentOrange : AccentRed;
                DrawRow(x, ref y, contentWidth, "Temp",
                    $"{tempC}°C", tempColor);

                float pw = NvidiaSmiReader.PowerDrawW;
                float pwLim = NvidiaSmiReader.PowerLimitW;
                float pwPct = pwLim > 0 ? pw / pwLim : 0f;
                Color pwColor = pwPct < 0.7f ? AccentGreen
                    : pwPct < 0.9f ? AccentYellow : AccentOrange;
                DrawRow(x, ref y, contentWidth, "Power",
                    $"{pw:F0} / {pwLim:F0} W", pwColor);

                DrawRow(x, ref y, contentWidth, "GPU Clock",
                    $"{NvidiaSmiReader.GpuClockMhz} MHz", TextValue);

                DrawRow(x, ref y, contentWidth, "Mem Clock",
                    $"{NvidiaSmiReader.MemClockMhz} MHz", TextValue);

                int fan = NvidiaSmiReader.FanSpeedPercent;
                Color fanColor = fan < 50 ? AccentGreen
                    : fan < 75 ? AccentYellow : AccentOrange;
                DrawRow(x, ref y, contentWidth, "Fan",
                    $"{fan}%", fanColor);

                int util = NvidiaSmiReader.UtilizationPercent;
                Color utilColor = util < 50 ? AccentGreen
                    : util < 80 ? AccentYellow : AccentOrange;
                DrawRow(x, ref y, contentWidth, "GPU Util",
                    $"{util}%", utilColor);
            }
        }

        // ────────────────────────────────────────────────────────
        //  Drawing helpers
        // ────────────────────────────────────────────────────────

        private static void DrawRow(float x, ref float y, float width,
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

            float labelWidth = width * 0.45f;
            float valueWidth = width * 0.55f;

            GUI.Label(new Rect(x, y, labelWidth, RowHeight), label, labelStyle);
            GUI.Label(new Rect(x + labelWidth, y, valueWidth, RowHeight), value, valueStyle);
            y += RowHeight;
        }

        private static void DrawProcessRow(float x, ref float y, float width,
            string name, float usedMb, float totalUsedMb, float totalMb)
        {
            if (usedMb <= 0f)
            {
                DrawRow(x, ref y, width, name, "—", TextLabel);
                return;
            }

            float pct = totalMb > 0 ? (usedMb / totalMb) * 100f : 0f;
            float gb = usedMb / 1024f;

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = TextLabel },
            };
            var pctStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = TextValue },
            };
            var gbStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = TextValue },
            };

            float col1 = width * 0.40f;
            float col2 = width * 0.22f;
            float col3 = width * 0.38f;

            GUI.Label(new Rect(x, y, col1, RowHeight), name, labelStyle);
            GUI.Label(new Rect(x + col1, y, col2, RowHeight), $"{pct:F0}%", pctStyle);
            GUI.Label(new Rect(x + col1 + col2, y, col3, RowHeight), $"{gb:F1} GB", gbStyle);
            y += RowHeight;
        }

        private static void DrawMiniBar(float x, ref float y, float width,
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

            y += BarHeight;
        }

        private static bool _wasDragged;

        private static void HandleDrag(Rect panelRect)
        {
            var headerRect = new Rect(panelRect.x, panelRect.y, PanelWidth, HeaderHeight + Padding);
            var evt = Event.current;

            if (evt.type == EventType.MouseDown && headerRect.Contains(evt.mousePosition))
            {
                _dragging = true;
                _wasDragged = false;
                _dragOffset = evt.mousePosition - _position;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && _dragging)
            {
                _dragging = false;
                if (!_wasDragged)
                {
                    // It was a click, not a drag. Cycle the mode!
                    CycleMode();
                    if (_mode == OverlayMode.Off) _mode = OverlayMode.Basic;
                }
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && _dragging)
            {
                _wasDragged = true;
                _position = evt.mousePosition - _dragOffset;
                evt.Use();
            }
        }

        private static void DrawBorder(Rect rect)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), _borderTex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), _borderTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), _borderTex);
            GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), _borderTex);
        }

        private static Color VramColor(float pct)
        {
            if (pct < 0.5f) return AccentGreen;
            if (pct < 0.7f) return AccentYellow;
            if (pct < 0.85f) return AccentOrange;
            return AccentRed;
        }

        private static string TruncateModel(string model)
        {
            if (model.Length <= 20) return model;
            // Try to keep the meaningful part
            int slash = model.LastIndexOf('/');
            if (slash >= 0) model = model.Substring(slash + 1);
            if (model.Length > 20) model = model.Substring(0, 18) + "…";
            return model;
        }

        private static void EnsureTextures()
        {
            if (_bgTex != null) return;
            _bgTex = MakeTex(BgColor);
            _headerTex = MakeTex(HeaderBg);
            _borderTex = MakeTex(BorderColor);
            _barBgTex = MakeTex(new Color(0.15f, 0.15f, 0.18f, 0.9f));
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
        Developer,
    }
}
