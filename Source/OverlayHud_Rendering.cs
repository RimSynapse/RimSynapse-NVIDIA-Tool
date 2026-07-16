using UnityEngine;

namespace RimSynapse.NvidiaTool
{
    /// <summary>
    /// Drawing helper methods for the GPU overlay HUD:
    /// rows, process rows, mini bars, borders, drag handling, and texture management.
    /// </summary>
    public static partial class OverlayHud
    {
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
