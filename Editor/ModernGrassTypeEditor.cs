using UnityEditor;
using UnityEngine;

namespace ModernGrassTool.Editor
{
    [CustomEditor(typeof(ModernGrassType))]
    public class ModernGrassTypeEditor : UnityEditor.Editor
    {
        private GrassBladePreviewHelper _previewHelper = new GrassBladePreviewHelper();
        private Shader _grassShader;
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

        // Wind preview cache
        private Texture2D _windPreviewTex;
        private Color[] _windPixels;
        private float _lastWindPreviewTime = -999f;

        // Foldout states
        private static bool _showTextureSettings = true;
        private static bool _showShapeSettings = true;
        private static bool _showClumpSettings = true;
        private static bool _showColorSettings = true;
        private static bool _showHeightNoiseSettings = true;
        private static bool _showWindSettings = true;
        private static bool _showShadowLODSettings = true;
        private static bool _showCutSettings = true;
        private static bool _showInteractionSettings = false;

        private void OnEnable()
        {
            _grassShader = Shader.Find("ModernGrassTool/ModernGrassShader");
        }

        private void OnDisable()
        {
            _previewHelper?.Cleanup();
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

        public override void OnInspectorGUI()
        {
            ModernGrassType gt = (ModernGrassType)target;
            serializedObject.Update();

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                string formatIcon = gt.renderMode switch
                {
                    FoliageRenderMode.ProceduralBlade => "🌿",
                    FoliageRenderMode.ProceduralTexture => "🌾",
                    FoliageRenderMode.CustomMesh => "📦",
                    _ => "🌿"
                };
                string formatBadge = gt.renderMode switch
                {
                    FoliageRenderMode.ProceduralBlade => "Procedural Color Blade (GPU)",
                    FoliageRenderMode.ProceduralTexture => "Procedural Textured Blade (GPU)",
                    FoliageRenderMode.CustomMesh => "Custom 3D Mesh / Props (Instanced)",
                    _ => "Procedural Blade"
                };

                GUILayout.Label(formatIcon, GUILayout.Width(28), GUILayout.Height(28));
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label(string.IsNullOrEmpty(gt.typeName) ? gt.name : gt.typeName, EditorStyles.boldLabel);
                    GUILayout.Label(formatBadge, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(6);

            // Render Mode Selection
            EditorGUI.BeginChangeCheck();
            gt.typeName = EditorGUILayout.TextField("Type Name", gt.typeName);
            gt.renderMode = (FoliageRenderMode)EditorGUILayout.EnumPopup("Render Mode", gt.renderMode);

            if (gt.renderMode == FoliageRenderMode.CustomMesh)
            {
                ModernGrassUI.DrawTitleDivider("Custom 3D Mesh & Props Settings", "📦", ModernGrassUI.ColorMesh);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    gt.customMesh = (Mesh)EditorGUILayout.ObjectField("3D Mesh (Model)", gt.customMesh, typeof(Mesh), false);
                    gt.customMaterial = (Material)EditorGUILayout.ObjectField("Material", gt.customMaterial, typeof(Material), false);
                    gt.scaleRange = EditorGUILayout.Vector2Field("Random Scale Min/Max", gt.scaleRange);
                    gt.alignToNormal = EditorGUILayout.Toggle("Align to Surface Normal", gt.alignToNormal);
                    gt.randomYRotation = EditorGUILayout.Toggle("Random Y Rotation", gt.randomYRotation);
                }
            }
            else
            {
                // Procedural Texture & Cutout Settings (when in ProceduralTexture mode)
                if (gt.renderMode == FoliageRenderMode.ProceduralTexture)
                {
                    _showTextureSettings = ModernGrassUI.DrawSectionHeader("Blade Texture & Alpha Cutout", _showTextureSettings, "🌾", ModernGrassUI.ColorTexture);
                    if (_showTextureSettings)
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            gt.bladeTexture = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Blade Texture (RGBA)", "2D Cutout or Albedo texture for the blade (e.g. pampas grass fluff, wild weed silhouette)."), gt.bladeTexture, typeof(Texture2D), false);
                            gt.alphaCutoff = EditorGUILayout.Slider(new GUIContent("Alpha Cutoff", "Alpha discard threshold for transparent pixels."), gt.alphaCutoff, 0.01f, 1f);
                            gt.blendWithBladeColor = EditorGUILayout.Toggle(new GUIContent("Blend With Blade Tints", "Multiply texture with top/bottom blade colors (enable for tinting, disable to use pure texture color)."), gt.blendWithBladeColor);
                        }
                    }
                }

                // 3D Real-time GPU Blade Preview
                if (_grassShader != null)
                {
                    Rect previewRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(210), GUILayout.ExpandWidth(true));
                    GrassLayer dummyLayer = new GrassLayer { grassType = gt };
                    _previewHelper.DrawPreview(previewRect, dummyLayer, _grassShader);
                }

                // 1. Blade Shape & Silhouette
                _showShapeSettings = ModernGrassUI.DrawSectionHeader("Blade Shape & Silhouette", _showShapeSettings, "🌿", ModernGrassUI.ColorBladeShape);
                if (_showShapeSettings)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        // Tip Shape
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel(new GUIContent("Tip Shape", "Blade tip silhouette"));
                        int currentTip = Mathf.Clamp((int)gt.tipShape, 0, 1);
                        int newTip = GUILayout.Toolbar(currentTip, new string[] { "▲ Pointed", "▬ Blunt" }, GUILayout.Height(22));
                        if (newTip != (int)gt.tipShape) gt.tipShape = (GrassTipShape)newTip;
                        EditorGUILayout.EndHorizontal();

                        // Segments
                        EditorGUILayout.BeginHorizontal();
                        gt.bladeSegments = EditorGUILayout.IntSlider(new GUIContent("Blade Segments", "Number of vertical height divisions."), gt.bladeSegments, 1, 5);
                        int trisCount = (gt.tipShape == GrassTipShape.Pointed) ? (gt.bladeSegments * 2 - 1) : (gt.bladeSegments * 2);
                        GUILayout.Label($"[{trisCount} Tris / {gt.bladeSegments * 6}v]", EditorStyles.miniBoldLabel, GUILayout.Width(105));
                        EditorGUILayout.EndHorizontal();

                        if (gt.tipShape != GrassTipShape.Pointed)
                        {
                            gt.tipWidth = EditorGUILayout.Slider("Tip Cut Width", gt.tipWidth, 0.05f, 1.0f);
                        }
                        gt.bellyWidth = EditorGUILayout.Slider("Belly Fullness", gt.bellyWidth, 0.2f, 2.0f);

                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Dimensions and Curvature", EditorStyles.boldLabel);
                        gt.defaultWidth = EditorGUILayout.Slider("Width", gt.defaultWidth, 0.02f, 0.4f);
                        gt.defaultHeight = EditorGUILayout.Slider("Height", gt.defaultHeight, 0.1f, 3.0f);
                        gt.bottomWidth = EditorGUILayout.Slider("Bottom Width (Root)", gt.bottomWidth, 0.0f, 2.0f);
                        gt.bottomWidthVariation = EditorGUILayout.Slider("Bottom Width Var", gt.bottomWidthVariation, 0f, 1f);
                        gt.widthVariation = EditorGUILayout.Slider("Width Variation", gt.widthVariation, 0f, 1f);
                        gt.heightVariation = EditorGUILayout.Slider("Height Variation", gt.heightVariation, 0f, 1f);
                        gt.bladeForward = EditorGUILayout.Slider("Blade Curvature", gt.bladeForward, 0f, 1.0f);
                        gt.bladeCurve = EditorGUILayout.Slider("Curve Power", gt.bladeCurve, 1f, 4f);
                        gt.uprightIntensity = EditorGUILayout.Slider(new GUIContent("Upright Intensity", "Blends between surface normal (0 = sticking out perpendicular to slope) and world up (1 = growing straight up towards the sky)."), gt.uprightIntensity, 0f, 1f);
                        float currentAngle = Mathf.Round(Mathf.Acos(Mathf.Clamp01(gt.normalLimit)) * Mathf.Rad2Deg);
                        float newAngle = EditorGUILayout.Slider(new GUIContent($"Max Slope Angle ({currentAngle:F0}°)", "Maximum surface slope angle in degrees where this grass species can grow."), currentAngle, 10f, 90f);
                        if (Mathf.Abs(newAngle - currentAngle) > 0.1f)
                        {
                            gt.normalLimit = Mathf.Cos(newAngle * Mathf.Deg2Rad);
                        }
                    }
                }

                // 2. Clumping & Density
                _showClumpSettings = ModernGrassUI.DrawSectionHeader("Clumping & Density", _showClumpSettings, "🌾", ModernGrassUI.ColorClumping);
                if (_showClumpSettings)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        gt.bladesPerClump = EditorGUILayout.IntSlider("Blades Per Clump", gt.bladesPerClump, 1, 8);
                        gt.clumpRadius = EditorGUILayout.Slider("Clump Radius", gt.clumpRadius, 0.01f, 0.4f);
                        gt.clumpTilt = EditorGUILayout.Slider("Clump Flare / Tilt", gt.clumpTilt, 0f, 1f);
                    }
                }

                // 3. Colors & Lighting
                _showColorSettings = ModernGrassUI.DrawSectionHeader("Colors & Lighting", _showColorSettings, "🎨", ModernGrassUI.ColorColors);
                if (_showColorSettings)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        gt.topColor = EditorGUILayout.ColorField("Top Color", gt.topColor);
                        gt.bottomColor = EditorGUILayout.ColorField("Bottom Color", gt.bottomColor);
                        gt.normalUpBlend = EditorGUILayout.Slider("Normal Up Blend", gt.normalUpBlend, 0f, 1f);
                        gt.translucency = EditorGUILayout.Slider("Translucency", gt.translucency, 0f, 1f);
                        gt.edgeHighlight = EditorGUILayout.Slider("Edge Highlight", gt.edgeHighlight, 0f, 1f);

                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("World-Space Color Tint Noise (Ghibli / BOTW)", EditorStyles.miniBoldLabel);
                        gt.enableColorNoise = EditorGUILayout.Toggle("Enable Color Noise", gt.enableColorNoise);
                        if (gt.enableColorNoise)
                        {
                            gt.noiseTopColor = EditorGUILayout.ColorField("Noise Tip Tint", gt.noiseTopColor);
                            gt.noiseBottomColor = EditorGUILayout.ColorField("Noise Root Tint", gt.noiseBottomColor);
                            gt.noiseScale = EditorGUILayout.Slider("Color Noise Scale", gt.noiseScale, 0.005f, 0.2f);
                            gt.noiseContrast = EditorGUILayout.Slider("Color Noise Contrast", gt.noiseContrast, 0.1f, 3.0f);

                            DrawNoisePreview(gt);
                        }
                    }
                }

                // 4. World-Space Height Variation (High / Low Meadows)
                _showHeightNoiseSettings = ModernGrassUI.DrawSectionHeader("World-Space Height Noise", _showHeightNoiseSettings, "🏔", ModernGrassUI.ColorHeightNoise);
                if (_showHeightNoiseSettings)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        gt.enableHeightNoise = EditorGUILayout.Toggle("Enable Height Noise", gt.enableHeightNoise);
                        if (gt.enableHeightNoise)
                        {
                            gt.heightNoiseScale = EditorGUILayout.Slider("Height Noise Scale", gt.heightNoiseScale, 0.005f, 0.2f);
                            gt.heightNoiseContrast = EditorGUILayout.Slider("Height Noise Contrast", gt.heightNoiseContrast, 0.1f, 3.0f);
                            gt.heightNoiseIntensity = EditorGUILayout.Slider("Variation Intensity", gt.heightNoiseIntensity, 0.0f, 1.0f);

                            float minPct = (1.0f - gt.heightNoiseIntensity * 0.5f) * 100f;
                            float maxPct = (1.0f + gt.heightNoiseIntensity * 0.5f) * 100f;
                            EditorGUILayout.LabelField($"Height Range: {minPct:F0}% to {maxPct:F0}% ({(gt.defaultHeight * minPct / 100f):F2}m - {(gt.defaultHeight * maxPct / 100f):F2}m)", EditorStyles.miniLabel);

                            DrawHeightNoisePreview(gt);
                        }
                    }
                }

                // 5. Wind, Sway & Gust Waves
                _showWindSettings = ModernGrassUI.DrawSectionHeader("Wind, Sway & Gust Waves", _showWindSettings, "💨", ModernGrassUI.ColorWind);
                if (_showWindSettings)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        gt.windStrength = EditorGUILayout.Slider("Wind Strength", gt.windStrength, 0f, 2f);
                        gt.windSpeed = EditorGUILayout.Slider("Wind Speed", gt.windSpeed, 0f, 10f);
                        gt.windScale = EditorGUILayout.Slider("Wind Turbulence / Scale", gt.windScale, 0.01f, 2f);
                        gt.windElasticity = EditorGUILayout.Slider(new GUIContent("Wind Elasticity / Spring", "Controls spring recoil and organic bounce when blades return from swaying."), gt.windElasticity, 0f, 1f);
                        gt.windSmoothness = EditorGUILayout.Slider(new GUIContent("Wind Recovery Smoothness", "Softens the turnaround easing at rest pose, preventing stiffness and abrupt halts."), gt.windSmoothness, 0f, 1f);

                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Wind Gust Waves & Displacement", EditorStyles.miniBoldLabel);
                        gt.enableNoiseWindWaves = EditorGUILayout.Toggle(new GUIContent("Sweeping Gust Noise Waves", "Enables sweeping 2D noise wave bands that gust across the grass in the wind direction."), gt.enableNoiseWindWaves);
                        gt.enableHeightNoiseWaves = EditorGUILayout.Toggle(new GUIContent("Roll Height Waves with Wind", "Enables world-space height variations to roll continuously along with the wind direction."), gt.enableHeightNoiseWaves);
                        if (gt.enableHeightNoiseWaves)
                        {
                            EditorGUI.indentLevel++;
                            gt.heightWaveIntensity = EditorGUILayout.Slider(new GUIContent("Height Wave Intensity", "Controls how strongly blade heights dip and swell as rolling wind waves pass through."), gt.heightWaveIntensity, 0f, 1f);
                            EditorGUI.indentLevel--;
                        }

                        DrawWindPreview(gt);
                    }
                }
            }

            // 6. Shadows & Distance LOD
            _showShadowLODSettings = ModernGrassUI.DrawSectionHeader("Shadows & Distance LOD", _showShadowLODSettings, "🌑", ModernGrassUI.ColorShadows);
            if (_showShadowLODSettings)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    gt.minFadeDistance = EditorGUILayout.Slider("Min Fade Distance", gt.minFadeDistance, 5f, 100f);
                    gt.maxDrawDistance = EditorGUILayout.Slider("Max Draw Distance", gt.maxDrawDistance, 20f, 300f);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Dynamic Segment LOD", EditorStyles.miniBoldLabel);
                    gt.enableSegmentLOD = EditorGUILayout.Toggle(new GUIContent("Enable Segment LOD", "Automatically steps down blade segment count at distance to save vertex processing while preserving near detail."), gt.enableSegmentLOD);
                    if (gt.enableSegmentLOD)
                    {
                        EditorGUI.indentLevel++;
                        gt.segmentLODStartDist = EditorGUILayout.Slider(new GUIContent("LOD Start Distance", "Distance where blade segments begin stepping down."), gt.segmentLODStartDist, 10f, 100f);
                        gt.segmentLODEndDist = EditorGUILayout.Slider(new GUIContent("LOD End Distance", "Distance where blades reach minimum segment count."), gt.segmentLODEndDist, gt.segmentLODStartDist + 5f, 150f);
                        gt.minSegmentCount = EditorGUILayout.IntSlider(new GUIContent("Min Far Segments", "Lowest segment count for distant blades (1 = flat quad, 2 = slight bend)."), gt.minSegmentCount, 1, 2);
                        EditorGUI.indentLevel--;
                    }

                    gt.castShadows = EditorGUILayout.Toggle("Cast Shadows", gt.castShadows);
                    if (gt.castShadows)
                    {
                        gt.shadowCastDensity = EditorGUILayout.Slider("Shadow Cast Density", gt.shadowCastDensity, 0f, 1f);
                    }
                    gt.bladeShadowStrength = EditorGUILayout.Slider("Blade Shadow Strength", gt.bladeShadowStrength, 0f, 1f);
                }
            }

            // 7. Cut Effects & Particles
            _showCutSettings = ModernGrassUI.DrawSectionHeader("Cut Effects & Particles", _showCutSettings, "✂", ModernGrassUI.ColorCutVFX);
            if (_showCutSettings)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    gt.canBeCut = EditorGUILayout.Toggle(new GUIContent("Can Be Cut", "If enabled, this grass species can be cut down by weapons, lawnmowers, or ModernGrassCutter. If disabled, this species cannot be cut."), gt.canBeCut);
                    if (gt.canBeCut)
                    {
                        gt.cutParticlePrefab = (ParticleSystem)EditorGUILayout.ObjectField(new GUIContent("Cut Particle Prefab", "Optional custom Particle System spawned when this grass species is cut. If empty, a stylized procedural shred particle is used."), gt.cutParticlePrefab, typeof(ParticleSystem), false);
                    }
                }
            }

            // 8. Player & Object Interaction
            _showInteractionSettings = ModernGrassUI.DrawSectionHeader("Player & Object Interaction", _showInteractionSettings, "🏃", ModernGrassUI.ColorInteraction);
            if (_showInteractionSettings)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    gt.enableInteraction = EditorGUILayout.Toggle(new GUIContent("Enable Interaction", "Enables real-time push and deformation when players or objects with ModernGrassInteractor touch this grass layer."), gt.enableInteraction);
                    if (gt.enableInteraction)
                    {
                        EditorGUI.indentLevel++;
                        gt.elasticOscillation = EditorGUILayout.Slider(new GUIContent("Spring Wobble", "How much blades wobble and shake as they spring back to their rest position after being pushed."), gt.elasticOscillation, 0.0f, 1.0f);
                        EditorGUI.indentLevel--;
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(gt);
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(40);
        }

        public override bool RequiresConstantRepaint() => false;

        private void DrawNoisePreview(ModernGrassType gt)
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

            if (_lastNoiseScale != gt.noiseScale || _lastNoiseContrast != gt.noiseContrast)
            {
                _lastNoiseScale = gt.noiseScale;
                _lastNoiseContrast = gt.noiseContrast;

                Color[] pixels = new Color[128 * 70];
                float freq = gt.noiseScale * 80f;
                for (int y = 0; y < 70; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        float nx = (x / 128f) * freq;
                        float ny = (y / 70f) * freq;
                        float n = Mathf.PerlinNoise(nx, ny);
                        float contrasted = Mathf.Clamp01((n - 0.5f) * gt.noiseContrast + 0.5f);
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

        private void DrawHeightNoisePreview(ModernGrassType gt)
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

            if (_lastHeightNoiseScale != gt.heightNoiseScale || _lastHeightNoiseContrast != gt.heightNoiseContrast ||
                _lastHeightNoiseIntensity != gt.heightNoiseIntensity)
            {
                _lastHeightNoiseScale = gt.heightNoiseScale;
                _lastHeightNoiseContrast = gt.heightNoiseContrast;
                _lastHeightNoiseIntensity = gt.heightNoiseIntensity;

                Color[] pixels = new Color[128 * 70];
                float freq = gt.heightNoiseScale * 80f;

                for (int y = 0; y < 70; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        float nx = (x / 128f) * freq + 47.13f;
                        float ny = (y / 70f) * freq + 89.27f;
                        float n = Mathf.PerlinNoise(nx, ny);
                        float contrasted = Mathf.Clamp01((n - 0.5f) * gt.heightNoiseContrast + 0.5f);
                        float val = Mathf.Clamp01(Mathf.Lerp(0.5f - gt.heightNoiseIntensity * 0.5f, 0.5f + gt.heightNoiseIntensity * 0.5f, contrasted));
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

        private void DrawWindPreview(ModernGrassType gt)
        {
            EditorGUILayout.Space(2);
            var mgr = Object.FindFirstObjectByType<ModernGrassManager>();
            Vector3 windDir = (mgr != null && mgr.windDirection.sqrMagnitude > 0.001f) 
                ? mgr.windDirection.normalized 
                : new Vector3(1f, 0f, 0.2f).normalized;

            EditorGUILayout.LabelField($"Live Wind & Gusts Wave Preview (Direction: [{windDir.x:F1}, {windDir.z:F1}] | Speed: {gt.windSpeed:F1} m/s)", EditorStyles.miniBoldLabel);

            if (_windPreviewTex == null)
            {
                _windPreviewTex = new Texture2D(128, 70, TextureFormat.RGBA32, false);
                _windPreviewTex.filterMode = FilterMode.Bilinear;
                _windPreviewTex.wrapMode = TextureWrapMode.Repeat;
            }

            if (_windPixels == null || _windPixels.Length != 128 * 70)
                _windPixels = new Color[128 * 70];

            float time = (float)EditorApplication.timeSinceStartup;
            // Throttle to 30 FPS and reuse pixel array to avoid GC allocations and CPU lag
            if (time - _lastWindPreviewTime >= 0.033f)
            {
                _lastWindPreviewTime = time;
                float speed = gt.windSpeed;
                float scale = gt.windScale;
                bool gust = gt.enableNoiseWindWaves;

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
                        float springBounce = Mathf.Sin(basePhase * 2.0f - 1.2f) * (0.22f * gt.windElasticity);
                        float combinedSway = rawSway + springBounce;
                        float sway01 = Mathf.Clamp01((combinedSway + 1.22f) / 2.44f);
                        float easedSway = Mathf.Lerp(sway01, Mathf.SmoothStep(0f, 1f, sway01), gt.windSmoothness);
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

                        _windPixels[y * 128 + x] = col;
                    }
                }

                _windPreviewTex.SetPixels(_windPixels);
                _windPreviewTex.Apply();
            }

            Rect r = GUILayoutUtility.GetRect(128, 70, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(r, _windPreviewTex, ScaleMode.ScaleAndCrop);
            DrawPreviewBorder(r);
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
    }
}