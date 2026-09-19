using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ModernGrassTool.Editor
{
    public class ModernGrassPainterWindow : EditorWindow
    {
        public ModernGrassManager manager;
        public int selectedLayerIndex = 0;

        public enum BrushToolMode
        {
            Paint = 0,    // Left click to paint
            Erase = 1     // Left click to remove / erase
        }

        public BrushToolMode toolMode = BrushToolMode.Paint;
        public float brushRadius = 3.5f;
        public int brushDensity = 8;
        public float brushSpacing = 0.25f;
        public float minBladeDistance = 0.12f;
        public float maxSlopeAngle = 60f;
        public LayerMask hitLayers = ~0;
        public bool isPaintingEnabled = true;
        public bool enableStrokeUndo = false;

        private Vector3 _lastPaintPos = Vector3.positiveInfinity;
        private Vector3 _lastErasePos = Vector3.positiveInfinity;
        private bool _isStrokeActive = false;
        private HashSet<ModernGrassChunk> _touchedChunksThisStroke = new HashSet<ModernGrassChunk>();
        private bool _isShiftDown = false;
        private bool _isCtrlDown = false;
        private Vector2 _scrollPos;
        private SerializedObject _serializedObject;
        private SerializedProperty _hitLayersProp;

        // Foldout states
        private bool _showTextureSettings = true;
        private bool _showShapeSettings = true;
        private bool _showClumpSettings = true;
        private bool _showWindSettings = true;
        private bool _showColorSettings = true;
        private bool _showHeightNoiseSettings = true;
        private bool _showShadowLODSettings = false;
        private bool _showCutSettings = false;
        private bool _showGroundBlend = false;
        private bool _showInteractionSettings = false;

        // Noise preview cache
        private Texture2D _noisePreviewTex;
        private float _lastNoiseScale = -1f;
        private float _lastNoiseContrast = -1f;
        private Color _lastTopCol;
        private Color _lastNoiseTopCol;

        // Height noise preview cache
        private Texture2D _heightNoisePreviewTex;
        private float _lastHeightNoiseScale = -1f;
        private float _lastHeightNoiseContrast = -1f;
        private float _lastHeightNoiseIntensity = -1f;

        // Wind noise preview cache
        private Texture2D _windPreviewTex;
        private double _lastWindPreviewTime;

        [MenuItem("Tools/Easy Grass Painter/Grass Painter")]
        [MenuItem("Window/Easy Grass Painter/Grass Painter")]
        public static void OpenWindow()
        {
            var win = GetWindow<ModernGrassPainterWindow>("Grass Painter");
            win.minSize = new Vector2(360, 540);
            win.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            FindOrSelectManager();
            _serializedObject = new SerializedObject(this);
            _hitLayersProp = _serializedObject.FindProperty(nameof(hitLayers));
        }

        private void OnDisable()
        {
            ModernGrassManager.isPaintingActive = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            if (_noisePreviewTex != null)
            {
                DestroyImmediate(_noisePreviewTex);
                _noisePreviewTex = null;
            }
            if (_heightNoisePreviewTex != null)
            {
                DestroyImmediate(_heightNoisePreviewTex);
                _heightNoisePreviewTex = null;
            }
            if (_windPreviewTex != null)
            {
                DestroyImmediate(_windPreviewTex);
                _windPreviewTex = null;
            }
        }

        private void Update()
        {
            // Keep wind preview smoothly animated when wind settings are open
            if (_showWindSettings && EditorApplication.timeSinceStartup - _lastWindPreviewTime > 0.038)
            {
                Repaint();
            }
        }

        private void FindOrSelectManager()
        {
            if (manager == null)
            {
                manager = Object.FindFirstObjectByType<ModernGrassManager>();
            }
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // Header Banner
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("🌿", GUILayout.Width(28), GUILayout.Height(28));
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label("Modern Grass Painter", EditorStyles.boldLabel);
                    GUILayout.Label("ScriptableObject Palette & GPU Brush", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);

            // Manager link
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.BeginHorizontal();
                manager = (ModernGrassManager)EditorGUILayout.ObjectField("Manager", manager, typeof(ModernGrassManager), true);
                if (GUILayout.Button("Find", GUILayout.Width(50)))
                {
                    FindOrSelectManager();
                }
                EditorGUILayout.EndHorizontal();

                if (manager == null)
                {
                    EditorGUILayout.HelpBox("No ModernGrassManager found in current scene.", MessageType.Warning);
                    if (GUILayout.Button("Create Grass Manager in Scene", GUILayout.Height(26)))
                    {
                        GameObject go = new GameObject("ModernGrassManager");
                        manager = go.AddComponent<ModernGrassManager>();
                        manager.AddLayer("Lawn Grass");
                        EnsureTerrainBaker(manager);
                        Undo.RegisterCreatedObjectUndo(go, "Create Grass Manager");
                        Selection.activeGameObject = go;
                    }
                    EditorGUILayout.EndScrollView();
                    return;
                }
            }

            EditorGUILayout.Space(4);

            // 1. Brush Tools & Settings (Top Section)
            ModernGrassUI.DrawTitleDivider("Brush Tools & Settings", "🖌", ModernGrassUI.ColorBrush);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Prominent Large Painting Mode Toggle Button
                Color prevColor = GUI.backgroundColor;
                GUI.backgroundColor = isPaintingEnabled ? new Color(0.35f, 0.88f, 0.45f) : new Color(0.88f, 0.35f, 0.35f);
                string btnText = isPaintingEnabled ? "🟢 PAINTING MODE: ACTIVE (Click to Pause)" : "🔴 PAINTING MODE: OFF (Click to Activate)";
                if (GUILayout.Button(btnText, GUILayout.Height(38)))
                {
                    isPaintingEnabled = !isPaintingEnabled;
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = prevColor;

                EditorGUILayout.Space(4);

                EditorGUILayout.LabelField("Brush Tool Mode", EditorStyles.boldLabel);

                // Big Mode Toolbar
                int prevTool = (int)toolMode;
                int newTool = GUILayout.Toolbar(prevTool, new string[] { "🖌 Paint Brush", "🧹 Erase (Remover)" }, GUILayout.Height(30));
                if (newTool != prevTool)
                {
                    toolMode = (BrushToolMode)newTool;
                }

                EditorGUILayout.Space(2);
                string modeHelp = toolMode switch
                {
                    BrushToolMode.Paint => "Left-click & drag in Scene to paint grass. Hold [Shift] to quickly switch to Eraser.",
                    BrushToolMode.Erase => "Left-click & drag to erase grass blades within brush radius.",
                    _ => ""
                };
                EditorGUILayout.HelpBox(modeHelp, MessageType.None);

                EditorGUILayout.Space(4);
                brushRadius = EditorGUILayout.Slider("Brush Radius [ / ]", brushRadius, 0.2f, 15f);

                if (toolMode == BrushToolMode.Paint)
                {
                    brushDensity = EditorGUILayout.IntSlider("Blades Per Stamp", brushDensity, 1, 30);
                    brushSpacing = EditorGUILayout.Slider("Brush Spacing", brushSpacing, 0.05f, 2f);
                    string distLabel = minBladeDistance <= 0.001f ? "Min Blade Spacing (Off / Unlimited)" : $"Min Blade Spacing ({minBladeDistance:F2}m)";
                    minBladeDistance = EditorGUILayout.Slider(new GUIContent(distLabel, "Minimum distance between individual grass blades. Prevents overlapping blades and infinite point stacking when painting the same area repeatedly."), minBladeDistance, 0f, 0.5f);
                    maxSlopeAngle = EditorGUILayout.Slider(new GUIContent($"Brush Max Slope ({maxSlopeAngle:F0}°)", "Surfaces steeper than this angle in degrees will be skipped during painting."), maxSlopeAngle, 10f, 90f);
                }

                // Raycast Layer Mask Filter
                if (_hitLayersProp != null)
                {
                    _serializedObject.Update();
                    EditorGUILayout.PropertyField(_hitLayersProp, new GUIContent("Paintable Layers", "Raycast mask. Grass will only be painted on these surfaces."));
                    _serializedObject.ApplyModifiedProperties();
                }

                EditorGUILayout.Space(2);
                enableStrokeUndo = EditorGUILayout.Toggle(new GUIContent("Stroke Undo/Redo", "Record Unity Undo history for painting/erasing strokes. Recommended OFF on large scenes (>10,000 grass points) to prevent Unity Editor freezes during serialization (Application.Tick)."), enableStrokeUndo);

                EditorGUILayout.Space(4);
                // One-click Cleanup Floating Grass Button
                if (GUILayout.Button("🧹 Clean Floating Grass (Remove Air Points)", GUILayout.Height(24)))
                {
                    int removed = CleanFloatingPoints(manager, hitLayers);
                    if (removed > 0)
                    {
                        EditorUtility.DisplayDialog("Clean Floating Grass", $"Successfully purged {removed:N0} floating grass points hanging in the air!", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Clean Floating Grass", "No floating grass points detected. All grass points are planted on solid ground.", "OK");
                    }
                }
            }

            EditorGUILayout.Space(4);

            // 2. Grass Types Palette Shelf (Underneath Brush Settings)
            ModernGrassUI.DrawTitleDivider("Grass Types Palette", "🌾", ModernGrassUI.ColorPalette);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Grass Types Palette ({manager.layers.Count})", EditorStyles.boldLabel);
                if (GUILayout.Button("+ New Blank", GUILayout.Width(95)))
                {
                    CreateNewGrassType(sourceType: null);
                }

                bool canClone = manager.layers.Count > 0 && selectedLayerIndex >= 0 && selectedLayerIndex < manager.layers.Count && manager.layers[selectedLayerIndex].grassType != null;
                EditorGUI.BeginDisabledGroup(!canClone);
                if (GUILayout.Button(new GUIContent("📋 From Selected", "Create a new Grass Type cloned from the selected type, asking for a name first so you can tweak it slightly."), GUILayout.Width(110)))
                {
                    CreateNewGrassType(manager.layers[selectedLayerIndex].grassType);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                if (manager.layers.Count == 0)
                {
                    EditorGUILayout.HelpBox("No grass layers active. Click '+ New Type Asset' to begin.", MessageType.Info);
                }
                else
                {
                    int count = manager.layers.Count;
                    string[] typeLabels = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        var l = manager.layers[i];
                        typeLabels[i] = $"{l.FormatIcon} {l.DisplayName} ({l.PointCount})";
                    }

                    selectedLayerIndex = Mathf.Clamp(selectedLayerIndex, 0, count - 1);
                    selectedLayerIndex = GUILayout.SelectionGrid(selectedLayerIndex, typeLabels, Mathf.Min(count, 2), GUILayout.Height(Mathf.Ceil(count / 2f) * 28));

                    EditorGUILayout.Space(4);

                    // Drag & Drop Box for ModernGrassType assets
                    Rect dropRect = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
                    GUI.Box(dropRect, "⬇ Drag & Drop GrassType Asset here to add as Layer", EditorStyles.helpBox);
                    Event evt = Event.current;
                    if (dropRect.Contains(evt.mousePosition))
                    {
                        if (evt.type == EventType.DragUpdated)
                        {
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                            evt.Use();
                        }
                        else if (evt.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();
                            foreach (Object obj in DragAndDrop.objectReferences)
                            {
                                if (obj is ModernGrassType mgt)
                                {
                                    Undo.RecordObject(manager, "Add Grass Type Layer");
                                    manager.AddLayer(mgt);
                                    selectedLayerIndex = manager.layers.Count - 1;
                                    EditorUtility.SetDirty(manager);
                                }
                            }
                            evt.Use();
                        }
                    }
                }
            }

            EditorGUILayout.Space(4);

            // 3. Selected Layer Inspector & Visual Tweaks
            if (manager.layers.Count > 0 && selectedLayerIndex >= 0 && selectedLayerIndex < manager.layers.Count)
            {
                ModernGrassUI.DrawTitleDivider("Layer Settings & Blade Customization", "🌿", ModernGrassUI.ColorBladeShape);
                DrawLayerCard(manager.layers[selectedLayerIndex], selectedLayerIndex);
            }

            // Extra bottom scroll padding for comfortable viewing
            EditorGUILayout.Space(120);

            EditorGUILayout.EndScrollView();
        }

        private void DrawLayerCard(GrassLayer layer, int index)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Layer Title Bar
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                bool newVis = EditorGUILayout.Toggle(layer.isVisible, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(manager, "Toggle Layer Visibility");
                    layer.isVisible = newVis;
                    EditorUtility.SetDirty(manager);
                    if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                }
                EditorGUILayout.LabelField($"Layer {index + 1}: {layer.FormatIcon} {layer.DisplayName} [{layer.FormatName}]", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{layer.TotalWorldPoints:N0} pts ({layer.TotalChunkCount} chunks) / {layer.TotalBladeCount:N0} active blades", EditorStyles.miniLabel);

                Color prevClearBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.65f, 0.35f);
                if (GUILayout.Button("Clear All", GUILayout.Width(65), GUILayout.Height(18)))
                {
                    if (EditorUtility.DisplayDialog("Clear Layer", $"Erase all {layer.TotalWorldPoints:N0} points from layer '{layer.DisplayName}'?", "Clear All", "Cancel"))
                    {
                        Undo.RecordObject(manager, "Clear Layer Grass");
                        layer.points.Clear();
                        if (layer.chunks != null) layer.chunks.Clear();
                        layer.BuildChunkLookup();
                        layer.isDirty = true;
                        layer.treeDirty = true;
                        layer.cutDirty = true;
                        layer.EnsureBuffers();
                        EditorUtility.SetDirty(manager);
                        SceneView.RepaintAll();
                    }
                }
                GUI.backgroundColor = prevClearBg;

                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("🗑", GUILayout.Width(26), GUILayout.Height(18)))
                {
                    if (EditorUtility.DisplayDialog("Delete Layer", $"Delete layer '{layer.DisplayName}' ({layer.PointCount} points)?", "Delete", "Cancel"))
                    {
                        Undo.RecordObject(manager, "Delete Grass Layer");
                        manager.RemoveLayer(index);
                        selectedLayerIndex = Mathf.Clamp(selectedLayerIndex, 0, manager.layers.Count - 1);
                        EditorUtility.SetDirty(manager);
                        SceneView.RepaintAll();
                        GUIUtility.ExitGUI();
                    }
                }
                GUI.backgroundColor = prevBg;
                EditorGUILayout.EndHorizontal();

                // Grass Type ScriptableObject Slot
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var newType = (ModernGrassType)EditorGUILayout.ObjectField("Grass Type Asset", layer.grassType, typeof(ModernGrassType), false);
                if (layer.grassType != null)
                {
                    if (GUILayout.Button("📋 Clone As Layer", GUILayout.Width(115), GUILayout.Height(18)))
                    {
                        CreateNewGrassType(layer.grassType);
                    }
                    if (GUILayout.Button("💾 Save As Preset", GUILayout.Width(110), GUILayout.Height(18)))
                    {
                        SaveGrassTypeAsNew(layer.grassType);
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (newType != layer.grassType)
                {
                    Undo.RecordObject(manager, "Change Layer Grass Type");
                    layer.grassType = newType;
                    if (newType != null) layer.layerName = newType.typeName;
                    layer.EnsureBuffers();
                    EditorUtility.SetDirty(manager);
                }

                if (layer.grassType == null)
                {
                    EditorGUILayout.HelpBox("This layer has no GrassType ScriptableObject asset assigned. Convert it to save and reuse across scenes!", MessageType.Warning);
                    if (GUILayout.Button("✨ Convert & Save as GrassType Asset", GUILayout.Height(24)))
                    {
                        Undo.RecordObject(manager, "Convert Layer to GrassType");
                        ModernGrassManagerEditor.CreateAndAssignTypeAsset(layer);
                        EditorUtility.SetDirty(manager);
                    }
                }
                else
                {
                    // 3D GPU Preview
                    if (layer.RenderMode == FoliageRenderMode.CustomMesh)
                    {
                        EditorGUILayout.HelpBox($"📦 Custom 3D Model: {(layer.grassType.customMesh != null ? layer.grassType.customMesh.name : "None assigned")}", MessageType.Info);
                    }
                    else if (layer.RenderMode == FoliageRenderMode.ProceduralTexture)
                    {
                        EditorGUILayout.HelpBox($"🌾 Procedural Cutout Texture: {(layer.bladeTexture != null ? layer.bladeTexture.name : "None assigned (uses default blade)")}", MessageType.Info);
                    }

                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    string updatedName = EditorGUILayout.TextField("Type Name", layer.grassType.typeName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(layer.grassType, "Change Type Name");
                        layer.grassType.typeName = updatedName;
                        layer.layerName = updatedName;
                        EditorUtility.SetDirty(layer.grassType);
                        EditorUtility.SetDirty(manager);
                    }
                    if (GUILayout.Button("🏷 Rename Asset File", GUILayout.Width(130), GUILayout.Height(18)))
                    {
                        RenameGrassTypeAsset(layer.grassType);
                    }
                    EditorGUILayout.EndHorizontal();

                    if (layer.RenderMode != FoliageRenderMode.CustomMesh)
                    {
                        // 0. Blade Texture & Cutout (for ProceduralTexture mode)
                        if (layer.RenderMode == FoliageRenderMode.ProceduralTexture)
                        {
                            _showTextureSettings = ModernGrassUI.DrawSectionHeader("Blade Texture & Alpha Cutout", _showTextureSettings, "🌾", ModernGrassUI.ColorTexture);
                            if (_showTextureSettings)
                            {
                                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                                {
                                    EditorGUI.BeginChangeCheck();
                                    var newTex = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Blade Texture (RGBA)", "2D Cutout or Albedo texture for the blade (e.g. pampas grass fluff, wild weed silhouette)."), layer.grassType.bladeTexture, typeof(Texture2D), false);
                                    float newCutoff = EditorGUILayout.Slider(new GUIContent("Alpha Cutoff", "Alpha discard threshold for transparent pixels."), layer.grassType.alphaCutoff, 0.01f, 1f);
                                    bool newBlend = EditorGUILayout.Toggle(new GUIContent("Blend With Blade Tints", "Multiply texture with top/bottom blade colors (enable for tinting, disable to use pure texture color)."), layer.grassType.blendWithBladeColor);
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        Undo.RecordObject(layer.grassType, "Change Blade Texture Settings");
                                        layer.grassType.bladeTexture = newTex;
                                        layer.grassType.alphaCutoff = newCutoff;
                                        layer.grassType.blendWithBladeColor = newBlend;
                                        EditorUtility.SetDirty(layer.grassType);
                                        EditorUtility.SetDirty(manager);
                                    }
                                }
                            }
                        }

                        // 1. Blade Shape & Silhouette
                        _showShapeSettings = ModernGrassUI.DrawSectionHeader("Blade Shape & Silhouette", _showShapeSettings, "🌿", ModernGrassUI.ColorBladeShape);
                        if (_showShapeSettings)
                        {
                            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.PrefixLabel("Tip Shape");
                                int currentTip = Mathf.Clamp((int)layer.tipShape, 0, 1);
                                int newTip = GUILayout.Toolbar(currentTip, new string[] { "Pointed", "Blunt" }, GUILayout.Height(20));
                                if (newTip != (int)layer.tipShape) layer.tipShape = (GrassTipShape)newTip;
                                EditorGUILayout.EndHorizontal();

                                EditorGUILayout.BeginHorizontal();
                                layer.bladeSegments = EditorGUILayout.IntSlider("Blade Segments", layer.bladeSegments, 1, 5);
                                int tris = (layer.tipShape == GrassTipShape.Pointed) ? (layer.bladeSegments * 2 - 1) : (layer.bladeSegments * 2);
                                GUILayout.Label($"[{tris} Tris / {layer.bladeSegments * 6}v]", EditorStyles.miniBoldLabel, GUILayout.Width(105));
                                EditorGUILayout.EndHorizontal();

                                if (layer.tipShape != GrassTipShape.Pointed)
                                {
                                    layer.tipWidth = EditorGUILayout.Slider("Tip Cut Width", layer.tipWidth, 0.05f, 1.0f);
                                }
                                layer.bellyWidth = EditorGUILayout.Slider("Belly Fullness", layer.bellyWidth, 0.2f, 2.0f);
                                layer.defaultWidth = EditorGUILayout.Slider("Width", layer.defaultWidth, 0.02f, 0.4f);
                                layer.defaultHeight = EditorGUILayout.Slider("Height", layer.defaultHeight, 0.1f, 3.0f);
                                layer.bottomWidth = EditorGUILayout.Slider("Bottom Width (Root)", layer.bottomWidth, 0.0f, 2.0f);
                                layer.bottomWidthVariation = EditorGUILayout.Slider("Bottom Width Var", layer.bottomWidthVariation, 0f, 1f);
                                layer.widthVariation = EditorGUILayout.Slider("Width Variation", layer.widthVariation, 0f, 1f);
                                layer.heightVariation = EditorGUILayout.Slider("Height Variation", layer.heightVariation, 0f, 1f);
                                layer.bladeForward = EditorGUILayout.Slider("Blade Curvature", layer.bladeForward, 0f, 1.0f);
                                layer.bladeCurve = EditorGUILayout.Slider("Curve Power", layer.bladeCurve, 1f, 4f);
                                layer.uprightIntensity = EditorGUILayout.Slider(new GUIContent("Upright Intensity", "Blends between surface normal (0 = sticking out perpendicular to slope) and world up (1 = growing straight up towards the sky)."), layer.uprightIntensity, 0f, 1f);
                                float currentAngle = Mathf.Round(Mathf.Acos(Mathf.Clamp01(layer.normalLimit)) * Mathf.Rad2Deg);
                                float newAngle = EditorGUILayout.Slider(new GUIContent($"Max Slope Angle ({currentAngle:F0}°)", "Maximum surface slope angle in degrees where this grass species can grow."), currentAngle, 10f, 90f);
                                if (Mathf.Abs(newAngle - currentAngle) > 0.1f)
                                {
                                    layer.normalLimit = Mathf.Cos(newAngle * Mathf.Deg2Rad);
                                }
                            }
                        }

                        // 2. Clumping & Density
                        _showClumpSettings = ModernGrassUI.DrawSectionHeader("Clumping & Density", _showClumpSettings, "🌾", ModernGrassUI.ColorClumping);
                        if (_showClumpSettings)
                        {
                            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                layer.bladesPerClump = EditorGUILayout.IntSlider("Blades Per Clump", layer.bladesPerClump, 1, 8);
                                layer.clumpRadius = EditorGUILayout.Slider("Clump Radius", layer.clumpRadius, 0.01f, 0.4f);
                                layer.clumpTilt = EditorGUILayout.Slider("Clump Flare / Tilt", layer.clumpTilt, 0f, 1f);
                            }
                        }

                        // 3. Colors & Lighting
                        _showColorSettings = ModernGrassUI.DrawSectionHeader("Colors & Lighting", _showColorSettings, "🎨", ModernGrassUI.ColorColors);
                        if (_showColorSettings)
                        {
                            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                layer.topColor = EditorGUILayout.ColorField("Top Color", layer.topColor);
                                layer.bottomColor = EditorGUILayout.ColorField("Bottom Color", layer.bottomColor);
                                layer.normalUpBlend = EditorGUILayout.Slider("Normal Up Blend", layer.normalUpBlend, 0f, 1f);
                                layer.translucency = EditorGUILayout.Slider("Translucency", layer.translucency, 0f, 1f);
                                layer.edgeHighlight = EditorGUILayout.Slider("Edge Highlight", layer.edgeHighlight, 0f, 1f);

                                EditorGUILayout.Space(4);
                                EditorGUILayout.LabelField("World-Space Color Tint Noise (Ghibli / BOTW)", EditorStyles.miniBoldLabel);
                                layer.enableColorNoise = EditorGUILayout.Toggle("Enable Color Noise", layer.enableColorNoise);
                                if (layer.enableColorNoise)
                                {
                                    layer.noiseTopColor = EditorGUILayout.ColorField("Noise Tip Tint", layer.noiseTopColor);
                                    layer.noiseBottomColor = EditorGUILayout.ColorField("Noise Root Tint", layer.noiseBottomColor);
                                    layer.noiseScale = EditorGUILayout.Slider("Color Noise Scale", layer.noiseScale, 0.005f, 0.2f);
                                    layer.noiseContrast = EditorGUILayout.Slider("Color Noise Contrast", layer.noiseContrast, 0.1f, 3.0f);

                                    DrawNoisePreview(layer);
                                }
                            }
                        }

                        // 4. World-Space Height Variation (High / Low Meadows)
                        _showHeightNoiseSettings = ModernGrassUI.DrawSectionHeader("World-Space Height Noise", _showHeightNoiseSettings, "🏔", ModernGrassUI.ColorHeightNoise);
                        if (_showHeightNoiseSettings)
                        {
                            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                layer.enableHeightNoise = EditorGUILayout.Toggle("Enable Height Noise", layer.enableHeightNoise);
                                if (layer.enableHeightNoise)
                                {
                                    layer.heightNoiseScale = EditorGUILayout.Slider("Height Noise Scale", layer.heightNoiseScale, 0.005f, 0.2f);
                                    layer.heightNoiseContrast = EditorGUILayout.Slider("Height Noise Contrast", layer.heightNoiseContrast, 0.1f, 3.0f);
                                    layer.heightNoiseIntensity = EditorGUILayout.Slider("Variation Intensity", layer.heightNoiseIntensity, 0.0f, 1.0f);

                                    float minPct = (1.0f - layer.heightNoiseIntensity * 0.5f) * 100f;
                                    float maxPct = (1.0f + layer.heightNoiseIntensity * 0.5f) * 100f;
                                    EditorGUILayout.LabelField($"Height Range: {minPct:F0}% to {maxPct:F0}% ({(layer.defaultHeight * minPct / 100f):F2}m - {(layer.defaultHeight * maxPct / 100f):F2}m)", EditorStyles.miniLabel);

                                    DrawHeightNoisePreview(layer);
                                }
                            }
                        }

                        // 5. Wind, Sway & Gust Waves
                        _showWindSettings = ModernGrassUI.DrawSectionHeader("Wind, Sway & Gust Waves", _showWindSettings, "💨", ModernGrassUI.ColorWind);
                        if (_showWindSettings)
                        {
                            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                layer.windStrength = EditorGUILayout.Slider("Wind Strength", layer.windStrength, 0f, 2f);
                                layer.windSpeed = EditorGUILayout.Slider("Wind Speed", layer.windSpeed, 0f, 10f);
                                layer.windScale = EditorGUILayout.Slider("Wind Turbulence / Scale", layer.windScale, 0.01f, 2f);
                                layer.windElasticity = EditorGUILayout.Slider(new GUIContent("Wind Elasticity / Spring", "Controls spring recoil and organic bounce when blades return from swaying."), layer.windElasticity, 0f, 1f);
                                layer.windSmoothness = EditorGUILayout.Slider(new GUIContent("Wind Recovery Smoothness", "Softens the turnaround easing at rest pose, preventing stiffness and abrupt halts."), layer.windSmoothness, 0f, 1f);
                                if (manager != null)
                                {
                                    manager.windDirection = EditorGUILayout.Vector3Field("Global Wind Direction", manager.windDirection);
                                }

                                EditorGUILayout.Space(4);
                                EditorGUILayout.LabelField("Wind Gust Waves & Displacement", EditorStyles.miniBoldLabel);
                                layer.enableNoiseWindWaves = EditorGUILayout.Toggle(new GUIContent("Sweeping Gust Noise Waves", "Enables sweeping 2D noise wave bands that gust across the grass in the wind direction."), layer.enableNoiseWindWaves);
                                layer.enableHeightNoiseWaves = EditorGUILayout.Toggle(new GUIContent("Roll Height Waves with Wind", "Enables world-space height variations to roll continuously along with the wind direction."), layer.enableHeightNoiseWaves);
                                if (layer.enableHeightNoiseWaves)
                                {
                                    EditorGUI.indentLevel++;
                                    layer.heightWaveIntensity = EditorGUILayout.Slider(new GUIContent("Height Wave Intensity", "Controls how strongly blade heights dip and swell as rolling wind waves pass through."), layer.heightWaveIntensity, 0f, 1f);
                                    EditorGUI.indentLevel--;
                                }

                                DrawWindPreview(layer);
                            }
                        }

                        // 6. Shadows & Distance LOD
                        _showShadowLODSettings = ModernGrassUI.DrawSectionHeader("Shadows & Distance LOD", _showShadowLODSettings, "🌑", ModernGrassUI.ColorShadows);
                        if (_showShadowLODSettings)
                        {
                            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                layer.castShadows = EditorGUILayout.Toggle("Cast Shadows", layer.castShadows);
                                if (layer.castShadows)
                                {
                                    layer.shadowCastDensity = EditorGUILayout.Slider("Shadow Density", layer.shadowCastDensity, 0f, 1f);
                                }
                                layer.bladeShadowStrength = EditorGUILayout.Slider("Blade Self-Shadow", layer.bladeShadowStrength, 0f, 1f);
                                layer.minFadeDistance = EditorGUILayout.Slider("Min Fade Distance", layer.minFadeDistance, 5f, 100f);
                                layer.maxDrawDistance = EditorGUILayout.Slider("Max Draw Distance", layer.maxDrawDistance, 20f, 300f);

                                EditorGUILayout.Space(4);
                                EditorGUILayout.LabelField("Dynamic Segment LOD", EditorStyles.miniBoldLabel);
                                layer.enableSegmentLOD = EditorGUILayout.Toggle(new GUIContent("Enable Segment LOD", "Automatically steps down blade segment count at distance to save vertex processing while preserving near detail."), layer.enableSegmentLOD);
                                if (layer.enableSegmentLOD)
                                {
                                    EditorGUI.indentLevel++;
                                    layer.segmentLODStartDist = EditorGUILayout.Slider(new GUIContent("LOD Start Distance", "Distance where blade segments begin stepping down."), layer.segmentLODStartDist, 10f, 100f);
                                    layer.segmentLODEndDist = EditorGUILayout.Slider(new GUIContent("LOD End Distance", "Distance where blades reach minimum segment count."), layer.segmentLODEndDist, layer.segmentLODStartDist + 5f, 150f);
                                    layer.minSegmentCount = EditorGUILayout.IntSlider(new GUIContent("Min Far Segments", "Lowest segment count for distant blades (1 = flat quad, 2 = slight bend)."), layer.minSegmentCount, 1, 2);
                                    EditorGUI.indentLevel--;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Custom 3D Mesh Mode
                        _showShapeSettings = ModernGrassUI.DrawSectionHeader("Custom 3D Mesh Settings", _showShapeSettings, "🌸", ModernGrassUI.ColorMesh);
                        if (_showShapeSettings)
                        {
                            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                layer.grassType.customMesh = (Mesh)EditorGUILayout.ObjectField("3D Mesh (Model)", layer.grassType.customMesh, typeof(Mesh), false);
                                layer.grassType.customMaterial = (Material)EditorGUILayout.ObjectField("Material", layer.grassType.customMaterial, typeof(Material), false);
                                layer.grassType.scaleRange = EditorGUILayout.Vector2Field("Scale Min/Max", layer.grassType.scaleRange);
                                layer.grassType.alignToNormal = EditorGUILayout.Toggle("Align to Surface Normal", layer.grassType.alignToNormal);
                                layer.grassType.randomYRotation = EditorGUILayout.Toggle("Random Y Rotation", layer.grassType.randomYRotation);
                            }
                        }
                    }

                    // Cut VFX & Particles
                    _showCutSettings = ModernGrassUI.DrawSectionHeader("Cut VFX & Particles", _showCutSettings, "✂", ModernGrassUI.ColorCutVFX);
                    if (_showCutSettings)
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            layer.canBeCut = EditorGUILayout.Toggle(new GUIContent("Can Be Cut", "If enabled, this grass species can be cut down by weapons, lawnmowers, or ModernGrassCutter. If disabled, this species cannot be cut."), layer.canBeCut);
                            if (layer.canBeCut)
                            {
                                layer.cutParticlePrefab = (ParticleSystem)EditorGUILayout.ObjectField(new GUIContent("Cut Particle Prefab", "Optional custom Particle System spawned when this grass species is cut. If empty, a stylized procedural shred particle is used."), layer.cutParticlePrefab, typeof(ParticleSystem), false);
                            }
                        }
                    }

                    // Ground Blending
                    _showGroundBlend = ModernGrassUI.DrawSectionHeader("Ground Blending", _showGroundBlend, "🌍", ModernGrassUI.ColorGroundBlend);
                    if (_showGroundBlend)
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            var terrainMap = Object.FindAnyObjectByType<ModernRenderTerrainMap>();
                            if (terrainMap == null)
                            {
                                EditorGUILayout.HelpBox("ModernRenderTerrainMap is missing from the scene. Ground Blending requires this component to bake the ground diffuse texture.", MessageType.Warning);
                                if (GUILayout.Button("➕ Setup Ground Baker in Scene", GUILayout.Height(24)))
                                {
                                    EnsureTerrainBaker(manager);
                                }
                                EditorGUILayout.Space(2);
                            }
                            else
                            {
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    EditorGUILayout.LabelField("Ground Baker:", EditorStyles.miniBoldLabel, GUILayout.Width(85));
                                    EditorGUILayout.LabelField("Active in Scene", EditorStyles.miniLabel);
                                    if (GUILayout.Button("🔄 Re-Bake Ground", GUILayout.Width(125), GUILayout.Height(18)))
                                    {
                                        terrainMap.SetupAndBake();
                                    }
                                }
                                EditorGUILayout.Space(2);
                            }

                            layer.enableGroundBlend = EditorGUILayout.Toggle("Enable Ground Blend", layer.enableGroundBlend);
                            if (layer.enableGroundBlend)
                            {
                                layer.groundBlendFade = EditorGUILayout.Slider("Blend Fade", layer.groundBlendFade, -1f, 2f);
                                layer.groundBlendStretch = EditorGUILayout.Slider("Blend Stretch", layer.groundBlendStretch, 0.1f, 5f);
                                layer.groundBlendBrightness = EditorGUILayout.Slider("Brightness", layer.groundBlendBrightness, 0f, 2f);
                                layer.groundBlendSaturation = EditorGUILayout.Slider("Saturation", layer.groundBlendSaturation, 0f, 2f);
                                layer.ambientAdjustmentColor = EditorGUILayout.ColorField("Ambient Color", layer.ambientAdjustmentColor);
                            }
                        }
                    }

                    // Player & Object Interaction
                    _showInteractionSettings = ModernGrassUI.DrawSectionHeader("Player & Object Interaction", _showInteractionSettings, "🏃", ModernGrassUI.ColorInteraction);
                    if (_showInteractionSettings)
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            layer.enableInteraction = EditorGUILayout.Toggle(new GUIContent("Enable Interaction", "Enables real-time push and deformation when players or objects with ModernGrassInteractor touch this grass layer."), layer.enableInteraction);
                            if (layer.enableInteraction)
                            {
                                EditorGUI.indentLevel++;
                                layer.interactionStrength = EditorGUILayout.Slider(new GUIContent("Push Strength", "How strongly grass bends away from interactors."), layer.interactionStrength, 0.1f, 3.0f);
                                layer.interactionFlatten = EditorGUILayout.Slider(new GUIContent("Flatten Amount", "How much blades get pressed flat against the ground versus pushed sideways."), layer.interactionFlatten, 0.0f, 1.0f);
                                layer.elasticRecoverySpeed = EditorGUILayout.Slider(new GUIContent("Recovery Speed", "How fast blades spring back up to vertical orientation."), layer.elasticRecoverySpeed, 0.2f, 5.0f);
                                layer.elasticOscillation = EditorGUILayout.Slider(new GUIContent("Oscillation Bounce", "Elastic springiness/bounce back when releasing from an interactor."), layer.elasticOscillation, 0.0f, 1.0f);
                                EditorGUI.indentLevel--;

                                EditorGUILayout.Space(4);
                                EditorGUILayout.LabelField("Footprint / Walking Trail", EditorStyles.boldLabel);
                                layer.enableTrailPersistence = EditorGUILayout.Toggle(new GUIContent("Enable Walking Trail", "Keeps trampled footsteps and walking trails depressed before elastically recovering."), layer.enableTrailPersistence);
                                if (layer.enableTrailPersistence)
                                {
                                    EditorGUI.indentLevel++;
                                    layer.trailDuration = EditorGUILayout.Slider(new GUIContent("Trail Duration (sec)", "Time in seconds before footprints fully recover."), layer.trailDuration, 0.5f, 10.0f);
                                    layer.trailDepression = EditorGUILayout.Slider(new GUIContent("Trail Depression", "Downward flatten depth of footsteps in the grass."), layer.trailDepression, 0.0f, 1.0f);
                                    EditorGUI.indentLevel--;
                                }
                            }
                        }
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    if (layer.grassType != null) EditorUtility.SetDirty(layer.grassType);
                    EditorUtility.SetDirty(manager);
                    SceneView.RepaintAll();
                }
            }
        }

        private static string GetOrCreatePresetsFolder()
        {
            if (AssetDatabase.IsValidFolder("Assets/EasyGrassPresets"))
            {
                return "Assets/EasyGrassPresets";
            }
            if (AssetDatabase.IsValidFolder("Assets/ModernGrassPresets"))
            {
                return "Assets/ModernGrassPresets";
            }
            if (AssetDatabase.IsValidFolder("Assets/ModernGrassTool/Presets"))
            {
                return "Assets/ModernGrassTool/Presets";
            }
            AssetDatabase.CreateFolder("Assets", "EasyGrassPresets");
            return "Assets/EasyGrassPresets";
        }

        private void CreateNewGrassType(ModernGrassType sourceType)
        {
            string presetsFolder = GetOrCreatePresetsFolder();

            string defaultName = sourceType != null
                ? $"{sourceType.typeName.Replace(" ", "_")}_Variant"
                : "New_Grass_Type";

            string title = sourceType != null
                ? $"Create New Grass Type (Cloned from '{sourceType.typeName}')"
                : "Create New Grass Type Asset";

            string path = EditorUtility.SaveFilePanelInProject(
                title,
                defaultName,
                "asset",
                "Choose a name and location for your new Grass Type asset.",
                presetsFolder
            );

            if (string.IsNullOrEmpty(path)) return; // User canceled dialog

            string chosenFileName = System.IO.Path.GetFileNameWithoutExtension(path);

            ModernGrassType gt;
            if (sourceType != null)
            {
                gt = Instantiate(sourceType);
            }
            else
            {
                gt = ScriptableObject.CreateInstance<ModernGrassType>();
            }

            gt.typeName = chosenFileName.Replace("_", " ");
            AssetDatabase.CreateAsset(gt, path);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(manager, "Add Grass Type Layer");
            manager.AddLayer(gt);
            selectedLayerIndex = manager.layers.Count - 1;
            EditorUtility.SetDirty(manager);
            Selection.activeObject = gt;
            EditorGUIUtility.PingObject(gt);

            EditorUtility.DisplayDialog("Grass Type Created",
                $"Successfully created '{gt.typeName}' at:\n{path}\n\nIt has been added to your palette and is ready to paint!",
                "OK"
            );
        }

        private void RenameGrassTypeAsset(ModernGrassType gt)
        {
            if (gt == null) return;
            string assetPath = AssetDatabase.GetAssetPath(gt);
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog("Rename Error", "Cannot find asset file on disk.", "OK");
                return;
            }

            string currentFileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            string folder = System.IO.Path.GetDirectoryName(assetPath).Replace("\\", "/");
            string newPath = EditorUtility.SaveFilePanelInProject("Rename Grass Type Asset File", currentFileName, "asset", "Enter new file name for this Grass Type asset.", folder);
            if (string.IsNullOrEmpty(newPath)) return; // Canceled

            string newFileName = System.IO.Path.GetFileNameWithoutExtension(newPath);
            if (newFileName == currentFileName) return;

            string error = AssetDatabase.RenameAsset(assetPath, newFileName);
            if (string.IsNullOrEmpty(error))
            {
                gt.typeName = newFileName.Replace("_", " ");
                if (manager != null && selectedLayerIndex >= 0 && selectedLayerIndex < manager.layers.Count)
                {
                    manager.layers[selectedLayerIndex].layerName = gt.typeName;
                    EditorUtility.SetDirty(manager);
                }
                EditorUtility.SetDirty(gt);
                AssetDatabase.SaveAssets();
                Selection.activeObject = gt;
                EditorGUIUtility.PingObject(gt);
                EditorUtility.DisplayDialog("Rename Successful", $"Grass Type Asset renamed to '{newFileName}.asset'!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Rename Failed", $"Could not rename asset:\n{error}", "OK");
            }
        }

        private void SaveGrassTypeAsNew(ModernGrassType original)
        {
            if (original == null) return;
            string presetsFolder = GetOrCreatePresetsFolder();

            string defaultName = $"{original.typeName.Replace(" ", "_")}_Preset";
            string path = EditorUtility.SaveFilePanelInProject("Save Grass Type Preset", defaultName, "asset", "Save current grass settings as a reusable ScriptableObject preset asset.", presetsFolder);
            if (!string.IsNullOrEmpty(path))
            {
                ModernGrassType clone = Instantiate(original);
                clone.typeName = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(clone, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = clone;
                EditorGUIUtility.PingObject(clone);
                EditorUtility.DisplayDialog("Grass Preset Saved", $"Successfully saved preset to:\n{path}\n\nThis asset only contains the blade visual settings (no painted points). You can drag and drop it into any scene or project!", "OK");
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (manager == null || !isPaintingEnabled) return;
            if (manager.layers.Count == 0) return;
            if (selectedLayerIndex < 0 || selectedLayerIndex >= manager.layers.Count) return;

            Event e = Event.current;
            _isShiftDown = e.shift;
            _isCtrlDown = e.control;

            // Handle hotkeys [ and ] for brush resizing
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.LeftBracket)
                {
                    brushRadius = Mathf.Max(0.2f, brushRadius - 0.25f);
                    Repaint();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.RightBracket)
                {
                    brushRadius = Mathf.Min(15f, brushRadius + 0.25f);
                    Repaint();
                    e.Use();
                }
            }

            // Raycast into scene
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, hitLayers))
            {
                return;
            }

            bool shouldErase = (toolMode == BrushToolMode.Erase) || _isShiftDown;

            Color discColor = shouldErase ? new Color(1.0f, 0.25f, 0.2f, 0.8f) : new Color(0.2f, 0.9f, 0.3f, 0.7f);

            Handles.color = discColor;
            Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
            Handles.color = new Color(discColor.r, discColor.g, discColor.b, 0.15f);
            Handles.DrawSolidDisc(hit.point, hit.normal, brushRadius);

            if (e.type == EventType.MouseMove)
            {
                sceneView.Repaint();
            }

            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlID);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                ModernGrassManager.isPaintingActive = true;
                if (enableStrokeUndo)
                {
                    Undo.RecordObject(manager, shouldErase ? "Erase Grass" : "Paint Grass Points");
                }
                _isStrokeActive = true;
                _touchedChunksThisStroke.Clear();
                _lastPaintPos = Vector3.positiveInfinity;
                _lastErasePos = Vector3.positiveInfinity;

                GrassLayer currentLayer = manager.layers[selectedLayerIndex];
                float cSize = manager.chunkSize > 0f ? manager.chunkSize : 64f;
                currentLayer?.AutoMigratePointsToChunks(cSize);
            }

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
            {
                GrassLayer currentLayer = manager.layers[selectedLayerIndex];

                if (shouldErase)
                {
                    float eraseSpacing = Mathf.Max(0.1f, brushRadius * 0.12f);
                    if (e.type == EventType.MouseDown || Vector3.Distance(_lastErasePos, hit.point) >= eraseSpacing)
                    {
                        _lastErasePos = hit.point;
                        ErasePoints(currentLayer, hit.point, brushRadius);
                        e.Use();
                        sceneView.Repaint();
                    }
                }
                else
                {
                    if (Vector3.Distance(_lastPaintPos, hit.point) >= brushSpacing)
                    {
                        _lastPaintPos = hit.point;
                        PaintPoints(currentLayer, hit.point, hit.normal, brushRadius, brushDensity);
                        e.Use();
                        sceneView.Repaint();
                    }
                }
            }
            else if ((e.type == EventType.MouseUp || e.rawType == EventType.MouseUp) && e.button == 0)
            {
                ModernGrassManager.isPaintingActive = false;
                _lastPaintPos = Vector3.positiveInfinity;
                _lastErasePos = Vector3.positiveInfinity;
                if (_isStrokeActive)
                {
                    float cSize = manager.chunkSize > 0f ? manager.chunkSize : 64f;
                    foreach (var chk in _touchedChunksThisStroke)
                    {
                        if (chk != null) chk.RecalculateBounds(cSize);
                    }
                    _touchedChunksThisStroke.Clear();

                    if (selectedLayerIndex >= 0 && selectedLayerIndex < manager.layers.Count)
                    {
                        GrassLayer currentLayer = manager.layers[selectedLayerIndex];
                        if (currentLayer != null)
                        {
                            currentLayer.boundsDirty = true;
                            currentLayer.treeDirty = true;
                            currentLayer.EnsureBuffers();
                        }
                    }

                    EditorUtility.SetDirty(manager);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
                    _isStrokeActive = false;
                }
            }

            if (!_isStrokeActive && ModernGrassManager.isPaintingActive)
            {
                ModernGrassManager.isPaintingActive = false;
            }
        }

        private void PaintPoints(GrassLayer layer, Vector3 center, Vector3 normal, float radius, int count)
        {
            float cSize = manager.chunkSize > 0f ? manager.chunkSize : 64f;
            int pointsAdded = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2 randCircle = Random.insideUnitCircle * radius;
                Vector3 tangent = Vector3.Cross(normal, Vector3.up);
                if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(normal, Vector3.right);
                tangent.Normalize();
                Vector3 bitangent = Vector3.Cross(normal, tangent);

                Vector3 p = center + tangent * randCircle.x + bitangent * randCircle.y;

                // Strict ground check: raycast along surface normal and vertically down.
                // If the brush overlaps an edge into void/air, do NOT spawn floating grass points!
                bool groundFound = false;
                Ray ray = new Ray(p + normal * 2.5f, -normal);
                if (Physics.Raycast(ray, out RaycastHit hit, 5f, hitLayers) && Mathf.Abs(hit.point.y - center.y) <= radius * 0.75f + 2.0f)
                {
                    p = hit.point;
                    normal = hit.normal;
                    groundFound = true;
                }
                else
                {
                    Ray downRay = new Ray(p + Vector3.up * 2.5f, Vector3.down);
                    if (Physics.Raycast(downRay, out RaycastHit downHit, 5f, hitLayers) && Mathf.Abs(downHit.point.y - center.y) <= radius * 0.75f + 2.0f)
                    {
                        p = downHit.point;
                        normal = downHit.normal;
                        groundFound = true;
                    }
                }

                if (!groundFound)
                {
                    // No valid ground collider found underneath (hanging over edge into mid-air) -> Skip!
                    continue;
                }

                // Slope angle filter: prevent planting grass on steep cliffs or walls
                float surfaceSlope = Vector3.Angle(normal, Vector3.up);
                float layerMaxSlope = layer.normalLimit > 0.001f ? Mathf.Acos(Mathf.Clamp01(layer.normalLimit)) * Mathf.Rad2Deg : 90f;
                float effectiveMaxSlope = Mathf.Min(maxSlopeAngle, layerMaxSlope);
                if (surfaceSlope > effectiveMaxSlope)
                {
                    continue;
                }

                // Density / Minimum Distance check:
                // Prevents infinite point stacking when painting repeatedly over the same area
                if (minBladeDistance > 0.001f)
                {
                    float sqrMinDist = minBladeDistance * minBladeDistance;
                    bool tooClose = false;
                    var nearbyChunks = layer.GetChunksInRadius(p, minBladeDistance, cSize);
                    for (int c = 0; c < nearbyChunks.Count && !tooClose; c++)
                    {
                        var chk = nearbyChunks[c];
                        if (chk == null || chk.points == null) continue;
                        var pts = chk.points;
                        int pCount = pts.Count;
                        for (int j = 0; j < pCount; j++)
                        {
                            Vector3 exPos = pts[j].position;
                            float dx = exPos.x - p.x;
                            if (dx > minBladeDistance || dx < -minBladeDistance) continue;
                            float dz = exPos.z - p.z;
                            if (dz > minBladeDistance || dz < -minBladeDistance) continue;
                            float dy = exPos.y - p.y;
                            if (dy > minBladeDistance || dy < -minBladeDistance) continue;

                            if (dx * dx + dy * dy + dz * dz < sqrMinDist)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                    }

                    if (tooClose)
                    {
                        // An existing blade is already planted within minBladeDistance!
                        // Skip to prevent over-stacking and infinite density.
                        continue;
                    }
                }

                GrassPoint pt = new GrassPoint
                {
                    position = p,
                    normal = normal,
                    size = Vector2.one,
                    color = Color.white
                };

                // Route point into spatial chunk
                Vector2Int coord = GrassLayer.PositionToChunkCoord(p, cSize);
                ModernGrassChunk chunk = layer.GetOrCreateChunk(coord, cSize);
                chunk.points.Add(pt);
                chunk.isDirty = true;
                _touchedChunksThisStroke.Add(chunk);

                // Also keep in active display points
                layer.points.Add(pt);
                pointsAdded++;
            }

            if (pointsAdded > 0)
            {
                layer.isDirty = true;
                layer.cutDirty = true;
                layer.EnsureBuffers();
            }
        }

        private void ErasePoints(GrassLayer layer, Vector3 center, float radius)
        {
            float sqrRadius = radius * radius;
            float cSize = manager.chunkSize > 0f ? manager.chunkSize : 64f;
            int beforeActive = layer.points.Count;

            // Fast AABB bounding box early rejection
            float minX = center.x - radius;
            float maxX = center.x + radius;
            float minZ = center.z - radius;
            float maxZ = center.z + radius;
            float minY = center.y - 3.0f;
            float maxY = center.y + 3.0f;

            // 1. Remove from active display buffer with fast AABB early-out
            layer.points.RemoveAll(pt =>
            {
                Vector3 p = pt.position;
                if (p.x < minX || p.x > maxX || p.z < minZ || p.z > maxZ || p.y < minY || p.y > maxY) return false;
                float sqrDist = (p.x - center.x) * (p.x - center.x) + (p.z - center.z) * (p.z - center.z);
                return sqrDist <= sqrRadius;
            });

            // 2. Remove from spatial chunks
            var chunksInRadius = layer.GetChunksInRadius(center, radius, cSize);
            bool chunkModified = false;
            for (int c = 0; c < chunksInRadius.Count; c++)
            {
                var chk = chunksInRadius[c];
                int chkBefore = chk.points.Count;
                chk.points.RemoveAll(pt =>
                {
                    Vector3 p = pt.position;
                    if (p.x < minX || p.x > maxX || p.z < minZ || p.z > maxZ || p.y < minY || p.y > maxY) return false;
                    float sqrDist = (p.x - center.x) * (p.x - center.x) + (p.z - center.z) * (p.z - center.z);
                    return sqrDist <= sqrRadius;
                });
                if (chk.points.Count != chkBefore)
                {
                    chk.isDirty = true;
                    _touchedChunksThisStroke.Add(chk);
                    chunkModified = true;
                }
            }

            if (layer.points.Count != beforeActive || chunkModified)
            {
                layer.isDirty = true;
                layer.boundsDirty = true;
                layer.EnsureBuffers();
            }
        }

        public static void DrawHorizontalDivider(float height = 1f, float verticalSpacing = 8f)
        {
            EditorGUILayout.Space(verticalSpacing * 0.5f);
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(height), GUILayout.ExpandWidth(true));
            Color dividerColor = EditorGUIUtility.isProSkin 
                ? new Color(0.24f, 0.24f, 0.24f, 1.0f) 
                : new Color(0.68f, 0.68f, 0.68f, 1.0f);
            EditorGUI.DrawRect(r, dividerColor);
            EditorGUILayout.Space(verticalSpacing * 0.5f);
        }

        private static void DrawPreviewBorder(Rect r)
        {
            Color borderColor = EditorGUIUtility.isProSkin ? new Color(0.12f, 0.12f, 0.12f, 0.85f) : new Color(0.5f, 0.5f, 0.5f, 0.85f);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), borderColor);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), borderColor);
        }

        private void DrawNoisePreview(GrassLayer layer)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Live World-Space Color Noise (Grayscale Preview)", EditorStyles.miniBoldLabel);

            if (_noisePreviewTex == null)
            {
                _noisePreviewTex = new Texture2D(128, 70, TextureFormat.RGBA32, false);
                _noisePreviewTex.filterMode = FilterMode.Bilinear;
                _noisePreviewTex.wrapMode = TextureWrapMode.Repeat;
                _lastNoiseScale = -999f;
            }

            if (_lastNoiseScale != layer.noiseScale || _lastNoiseContrast != layer.noiseContrast)
            {
                _lastNoiseScale = layer.noiseScale;
                _lastNoiseContrast = layer.noiseContrast;

                Color[] pixels = new Color[128 * 70];
                float freq = layer.noiseScale * 80f;
                for (int y = 0; y < 70; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        float nx = (x / 128f) * freq;
                        float ny = (y / 70f) * freq;
                        float n = Mathf.PerlinNoise(nx, ny);
                        float contrasted = Mathf.Clamp01((n - 0.5f) * layer.noiseContrast + 0.5f);
                        pixels[y * 128 + x] = new Color(contrasted, contrasted, contrasted, 1f);
                    }
                }
                _noisePreviewTex.SetPixels(pixels);
                _noisePreviewTex.Apply();
            }

            Rect r = GUILayoutUtility.GetRect(128, 70, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(r, _noisePreviewTex, ScaleMode.ScaleAndCrop);
            DrawPreviewBorder(r);
        }

        private void DrawHeightNoisePreview(GrassLayer layer)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Live World-Space Height Noise (Dark=Short, Bright=Tall)", EditorStyles.miniBoldLabel);

            if (_heightNoisePreviewTex == null)
            {
                _heightNoisePreviewTex = new Texture2D(128, 70, TextureFormat.RGBA32, false);
                _heightNoisePreviewTex.filterMode = FilterMode.Bilinear;
                _heightNoisePreviewTex.wrapMode = TextureWrapMode.Repeat;
                _lastHeightNoiseScale = -999f;
            }

            if (_lastHeightNoiseScale != layer.heightNoiseScale || _lastHeightNoiseContrast != layer.heightNoiseContrast ||
                _lastHeightNoiseIntensity != layer.heightNoiseIntensity)
            {
                _lastHeightNoiseScale = layer.heightNoiseScale;
                _lastHeightNoiseContrast = layer.heightNoiseContrast;
                _lastHeightNoiseIntensity = layer.heightNoiseIntensity;

                Color[] pixels = new Color[128 * 70];
                float freq = layer.heightNoiseScale * 80f;
                for (int y = 0; y < 70; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        float nx = (x / 128f) * freq + 47.13f;
                        float ny = (y / 70f) * freq + 89.27f;
                        float n = Mathf.PerlinNoise(nx, ny);
                        float contrasted = Mathf.Clamp01((n - 0.5f) * layer.heightNoiseContrast + 0.5f);
                        float val = Mathf.Clamp01(Mathf.Lerp(0.5f - layer.heightNoiseIntensity * 0.5f, 0.5f + layer.heightNoiseIntensity * 0.5f, contrasted));
                        pixels[y * 128 + x] = new Color(val, val, val, 1f);
                    }
                }
                _heightNoisePreviewTex.SetPixels(pixels);
                _heightNoisePreviewTex.Apply();
            }

            Rect r = GUILayoutUtility.GetRect(128, 70, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(r, _heightNoisePreviewTex, ScaleMode.ScaleAndCrop);
            DrawPreviewBorder(r);
        }

        private void DrawWindPreview(GrassLayer layer)
        {
            EditorGUILayout.Space(2);
            Vector3 windDir = (manager != null && manager.windDirection.sqrMagnitude > 0.001f) 
                ? manager.windDirection.normalized 
                : new Vector3(1f, 0f, 0.2f).normalized;

            EditorGUILayout.LabelField($"Live Wind & Gusts Wave Preview (Direction: [{windDir.x:F1}, {windDir.z:F1}] | Speed: {layer.windSpeed:F1} m/s)", EditorStyles.miniBoldLabel);

            if (_windPreviewTex == null)
            {
                _windPreviewTex = new Texture2D(128, 70, TextureFormat.RGBA32, false);
                _windPreviewTex.filterMode = FilterMode.Bilinear;
                _windPreviewTex.wrapMode = TextureWrapMode.Repeat;
            }

            float time = (float)EditorApplication.timeSinceStartup;
            float speed = layer.windSpeed;
            float scale = layer.windScale;
            bool gust = layer.enableNoiseWindWaves;

            Color[] pixels = new Color[128 * 70];
            Color calmColor = new Color(0.10f, 0.15f, 0.22f, 1.0f);
            Color breezeColor = new Color(0.20f, 0.65f, 0.72f, 1.0f);
            Color gustColor = new Color(0.90f, 0.98f, 1.0f, 1.0f);

            for (int y = 0; y < 70; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float u = (x / 128f) * 12f;
                    float v = (y / 70f) * 6f;

                    // Directional wind wave displacement with spring recoil and recovery smoothness
                    float dotCoord = u * windDir.x + v * windDir.z;
                    float basePhase = time * speed * 1.2f + dotCoord * scale * 1.5f;
                    float rawSway = Mathf.Sin(basePhase);
                    float springBounce = Mathf.Sin(basePhase * 2.0f - 1.2f) * (0.22f * layer.windElasticity);
                    float combinedSway = rawSway + springBounce;
                    float sway01 = Mathf.Clamp01((combinedSway + 1.22f) / 2.44f);
                    float easedSway = Mathf.Lerp(sway01, Mathf.SmoothStep(0f, 1f, sway01), layer.windSmoothness);
                    float baseDisplacement = Mathf.Lerp(0.12f, 0.85f, easedSway);

                    float gustPush = 0f;
                    if (gust)
                    {
                        float waveCoordX = u * (scale * 0.25f) - windDir.x * (time * speed * 0.45f);
                        float waveCoordY = v * (scale * 0.25f) - windDir.z * (time * speed * 0.45f);
                        float noise = Mathf.PerlinNoise(waveCoordX, waveCoordY);
                        gustPush = Mathf.Pow(noise, 2.4f) * 1.5f;
                    }

                    float displacement = baseDisplacement + gustPush;
                    float t = Mathf.Clamp01(displacement / 2.3f);
                    Color col = (t < 0.5f) 
                        ? Color.Lerp(calmColor, breezeColor, t * 2f) 
                        : Color.Lerp(breezeColor, gustColor, (t - 0.5f) * 2f);

                    pixels[y * 128 + x] = col;
                }
            }

            _windPreviewTex.SetPixels(pixels);
            _windPreviewTex.Apply();

            Rect r = GUILayoutUtility.GetRect(128, 70, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(r, _windPreviewTex, ScaleMode.ScaleAndCrop);
            DrawPreviewBorder(r);
        }

        public static int CleanFloatingPoints(ModernGrassManager mgr, LayerMask mask)
        {
            if (mgr == null || mgr.layers == null) return 0;
            int totalRemoved = 0;
            float cSize = mgr.chunkSize > 0f ? mgr.chunkSize : 64f;

            Undo.RecordObject(mgr, "Clean Floating Grass Points");

            foreach (var layer in mgr.layers)
            {
                if (layer == null) continue;

                // 1. Filter chunks
                if (layer.chunks != null)
                {
                    foreach (var chunk in layer.chunks)
                    {
                        if (chunk == null || chunk.points == null) continue;
                        int before = chunk.points.Count;
                        chunk.points.RemoveAll(pt =>
                        {
                            Ray downRay = new Ray(pt.position + Vector3.up * 1.5f, Vector3.down);
                            bool hit = Physics.Raycast(downRay, 3.0f, mask);
                            if (!hit)
                            {
                                Ray normRay = new Ray(pt.position + pt.normal * 1.5f, -pt.normal);
                                hit = Physics.Raycast(normRay, 3.0f, mask);
                            }
                            return !hit;
                        });
                        int removed = before - chunk.points.Count;
                        if (removed > 0)
                        {
                            totalRemoved += removed;
                            chunk.isDirty = true;
                            chunk.RecalculateBounds(cSize);
                        }
                    }
                }

                // 2. Filter active display points
                if (layer.points != null)
                {
                    layer.points.RemoveAll(pt =>
                    {
                        Ray downRay = new Ray(pt.position + Vector3.up * 1.5f, Vector3.down);
                        bool hit = Physics.Raycast(downRay, 3.0f, mask);
                        if (!hit)
                        {
                            Ray normRay = new Ray(pt.position + pt.normal * 1.5f, -pt.normal);
                            hit = Physics.Raycast(normRay, 3.0f, mask);
                        }
                        return !hit;
                    });
                }

                layer.isDirty = true;
                layer.cutDirty = true;
                layer.treeDirty = true;
                layer.EnsureBuffers();
            }

            if (totalRemoved > 0)
            {
                EditorUtility.SetDirty(mgr);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);
            }

            return totalRemoved;
        }

        public static ModernRenderTerrainMap EnsureTerrainBaker(ModernGrassManager mgr)
        {
            var existing = Object.FindAnyObjectByType<ModernRenderTerrainMap>();
            if (existing != null) return existing;

            Transform parent = mgr != null ? mgr.transform : null;
            GameObject mapGo = new GameObject("ModernRenderTerrainMap");
            if (parent != null)
            {
                mapGo.transform.SetParent(parent);
                mapGo.transform.localPosition = Vector3.zero;
            }
            var baker = mapGo.AddComponent<ModernRenderTerrainMap>();
            baker.SetupAndBake();
            Undo.RegisterCreatedObjectUndo(mapGo, "Create Ground Baker");
            return baker;
        }
    }
}
