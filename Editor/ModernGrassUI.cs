using UnityEditor;
using UnityEngine;

namespace ModernGrassTool.Editor
{
    public static class ModernGrassUI
    {
        // Category Accent Colors (Vibrant AAA theme palette)
        public static readonly Color ColorBladeShape    = new Color(0.20f, 0.85f, 0.48f, 1.0f); // Vivid Emerald Green
        public static readonly Color ColorClumping      = new Color(0.98f, 0.68f, 0.18f, 1.0f); // Warm Amber Gold
        public static readonly Color ColorColors        = new Color(0.96f, 0.38f, 0.38f, 1.0f); // Coral Rose
        public static readonly Color ColorHeightNoise   = new Color(0.18f, 0.82f, 0.95f, 1.0f); // Sky Cyan
        public static readonly Color ColorWind          = new Color(0.28f, 0.68f, 1.00f, 1.0f); // Aerodynamic Electric Blue
        public static readonly Color ColorShadows       = new Color(0.68f, 0.48f, 0.95f, 1.0f); // Lavender Purple
        public static readonly Color ColorCutVFX        = new Color(0.38f, 0.88f, 0.40f, 1.0f); // Mint Green
        public static readonly Color ColorGroundBlend   = new Color(0.92f, 0.58f, 0.22f, 1.0f); // Earth Ochre
        public static readonly Color ColorInteraction   = new Color(0.15f, 0.75f, 0.95f, 1.0f); // Neon Cyan / Motion Blue
        public static readonly Color ColorMesh          = new Color(0.95f, 0.48f, 0.78f, 1.0f); // Blossom Pink
        public static readonly Color ColorTexture       = new Color(0.98f, 0.72f, 0.22f, 1.0f); // Golden Amber Wheat
        public static readonly Color ColorPalette       = new Color(0.30f, 0.80f, 0.60f, 1.0f); // Sage Green
        public static readonly Color ColorBrush         = new Color(0.40f, 0.70f, 1.00f, 1.0f); // Cobalt Blue

        /// <summary>
        /// Draws a prominent Custom UI Section Header Divider with Title, Accent Stripe, Foldout Indicator, and Horizontal Divider Line.
        /// </summary>
        public static bool DrawSectionHeader(string title, bool isExpanded, string icon = "", Color? accent = null)
        {
            EditorGUILayout.Space(12f); // Clear vertical breathing room above the divider

            Rect bannerRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(30), GUILayout.ExpandWidth(true));
            Event e = Event.current;
            bool isHovered = bannerRect.Contains(e.mousePosition);

            Color accentCol = accent ?? ColorBladeShape;

            // Background & Border colors
            Color bgColor = EditorGUIUtility.isProSkin
                ? (isHovered ? new Color(0.22f, 0.24f, 0.27f, 1.0f) : new Color(0.16f, 0.17f, 0.19f, 1.0f))
                : (isHovered ? new Color(0.86f, 0.88f, 0.91f, 1.0f) : new Color(0.78f, 0.80f, 0.83f, 1.0f));

            Color borderColor = EditorGUIUtility.isProSkin
                ? new Color(0.10f, 0.11f, 0.12f, 1.0f)
                : new Color(0.60f, 0.62f, 0.65f, 1.0f);

            // 1. Draw solid background box
            EditorGUI.DrawRect(bannerRect, bgColor);

            // 2. Draw 1px perimeter border
            EditorGUI.DrawRect(new Rect(bannerRect.x, bannerRect.y, bannerRect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(bannerRect.x, bannerRect.yMax - 1, bannerRect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(bannerRect.x, bannerRect.y, 1, bannerRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(bannerRect.xMax - 1, bannerRect.y, 1, bannerRect.height), borderColor);

            // 3. Draw 5px Left Accent Bar
            EditorGUI.DrawRect(new Rect(bannerRect.x, bannerRect.y, 5, bannerRect.height), accentCol);

            // 4. Draw Foldout Arrow
            string arrow = isExpanded ? "▼" : "▶";
            Rect arrowRect = new Rect(bannerRect.x + 10, bannerRect.y + 6, 14, 18);
            GUIStyle arrowStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = isHovered ? Color.white : new Color(0.75f, 0.78f, 0.82f) }
            };
            GUI.Label(arrowRect, arrow, arrowStyle);

            // 5. Draw Icon + Title Label
            string displayTitle = string.IsNullOrEmpty(icon) ? title.ToUpperInvariant() : $"{icon}  {title.ToUpperInvariant()}";
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = isHovered ? Color.white : new Color(0.92f, 0.94f, 0.97f) }
            };
            GUIContent titleContent = new GUIContent(displayTitle);
            Vector2 titleSize = titleStyle.CalcSize(titleContent);
            Rect titleRect = new Rect(bannerRect.x + 28, bannerRect.y + 5, titleSize.x, 20);
            GUI.Label(titleRect, titleContent, titleStyle);

            // 6. Draw Thick Horizontal Divider Line extending from Title to right edge
            float lineStartX = titleRect.xMax + 12f;
            float lineEndX = bannerRect.xMax - 12f;
            if (lineEndX > lineStartX)
            {
                float lineY = bannerRect.y + bannerRect.height * 0.5f - 1f;
                Color lineCol = EditorGUIUtility.isProSkin 
                    ? new Color(0.32f, 0.36f, 0.42f, 0.85f) 
                    : new Color(0.60f, 0.63f, 0.68f, 0.85f);
                Color lineHighlight = EditorGUIUtility.isProSkin 
                    ? new Color(0.12f, 0.13f, 0.15f, 0.85f) 
                    : new Color(0.90f, 0.92f, 0.95f, 0.85f);

                // 2.5px thick groove line
                EditorGUI.DrawRect(new Rect(lineStartX, lineY, lineEndX - lineStartX, 1.5f), lineCol);
                EditorGUI.DrawRect(new Rect(lineStartX, lineY + 1.5f, lineEndX - lineStartX, 1f), lineHighlight);
            }

            // 7. Click interaction
            if (e.type == EventType.MouseDown && bannerRect.Contains(e.mousePosition) && e.button == 0)
            {
                isExpanded = !isExpanded;
                e.Use();
                GUI.changed = true;
            }

            EditorGUIUtility.AddCursorRect(bannerRect, MouseCursor.Link);

            EditorGUILayout.Space(4f); // Spacing below banner before content
            return isExpanded;
        }

        /// <summary>
        /// Draws a prominent Custom UI Section Title Divider without foldout toggle (Always Visible Divider).
        /// </summary>
        public static void DrawTitleDivider(string title, string icon = "", Color? accent = null)
        {
            EditorGUILayout.Space(12f);

            Rect bannerRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(30), GUILayout.ExpandWidth(true));
            Color accentCol = accent ?? ColorBladeShape;

            Color bgColor = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.17f, 0.19f, 1.0f)
                : new Color(0.78f, 0.80f, 0.83f, 1.0f);

            Color borderColor = EditorGUIUtility.isProSkin
                ? new Color(0.10f, 0.11f, 0.12f, 1.0f)
                : new Color(0.60f, 0.62f, 0.65f, 1.0f);

            EditorGUI.DrawRect(bannerRect, bgColor);
            EditorGUI.DrawRect(new Rect(bannerRect.x, bannerRect.y, bannerRect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(bannerRect.x, bannerRect.yMax - 1, bannerRect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(bannerRect.x, bannerRect.y, 1, bannerRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(bannerRect.xMax - 1, bannerRect.y, 1, bannerRect.height), borderColor);

            EditorGUI.DrawRect(new Rect(bannerRect.x, bannerRect.y, 5, bannerRect.height), accentCol);

            string displayTitle = string.IsNullOrEmpty(icon) ? title.ToUpperInvariant() : $"{icon}  {title.ToUpperInvariant()}";
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.92f, 0.94f, 0.97f) }
            };
            GUIContent titleContent = new GUIContent(displayTitle);
            Vector2 titleSize = titleStyle.CalcSize(titleContent);
            Rect titleRect = new Rect(bannerRect.x + 14, bannerRect.y + 5, titleSize.x, 20);
            GUI.Label(titleRect, titleContent, titleStyle);

            float lineStartX = titleRect.xMax + 12f;
            float lineEndX = bannerRect.xMax - 12f;
            if (lineEndX > lineStartX)
            {
                float lineY = bannerRect.y + bannerRect.height * 0.5f - 1f;
                Color lineCol = EditorGUIUtility.isProSkin 
                    ? new Color(0.32f, 0.36f, 0.42f, 0.85f) 
                    : new Color(0.60f, 0.63f, 0.68f, 0.85f);
                Color lineHighlight = EditorGUIUtility.isProSkin 
                    ? new Color(0.12f, 0.13f, 0.15f, 0.85f) 
                    : new Color(0.90f, 0.92f, 0.95f, 0.85f);

                EditorGUI.DrawRect(new Rect(lineStartX, lineY, lineEndX - lineStartX, 1.5f), lineCol);
                EditorGUI.DrawRect(new Rect(lineStartX, lineY + 1.5f, lineEndX - lineStartX, 1f), lineHighlight);
            }

            EditorGUILayout.Space(4f);
        }

        /// <summary>
        /// 1px border around live previews.
        /// </summary>
        public static void DrawPreviewBorder(Rect r)
        {
            Color borderColor = EditorGUIUtility.isProSkin ? new Color(0.12f, 0.12f, 0.12f, 0.85f) : new Color(0.5f, 0.5f, 0.5f, 0.85f);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), borderColor);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), borderColor);
        }
    }
}
