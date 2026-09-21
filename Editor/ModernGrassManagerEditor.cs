using UnityEditor;
using UnityEngine;

namespace ModernGrassTool.Editor
{
    [CustomEditor(typeof(ModernGrassManager))]
    public class ModernGrassManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("🌿", GUILayout.Width(28), GUILayout.Height(28));
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label("Modern Grass Manager", EditorStyles.boldLabel);
                    GUILayout.Label("Global Engine & World Settings", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);
            Color prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
            if (GUILayout.Button("🖌 Open Grass Painter Window", GUILayout.Height(36)))
            {
                ModernGrassPainterWindow.OpenWindow();
            }
            GUI.backgroundColor = prevColor;

            EditorGUILayout.Space(6);
            DrawPropertiesExcluding(serializedObject, "layers", "m_Script");
            serializedObject.ApplyModifiedProperties();

            ModernGrassManager mgr = (ModernGrassManager)target;
            if (mgr != null && mgr.enableFireSimulation)
            {
                EditorGUILayout.Space(6);
                Color origBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1.0f, 0.65f, 0.3f);
                if (GUILayout.Button("🔥 Clear Burn Map", GUILayout.Height(28)))
                {
                    mgr.ClearAllBurnInternal();
                }
                GUI.backgroundColor = origBg;
            }

            EditorGUILayout.Space(20);
        }

        public static ModernGrassType CreateAndAssignTypeAsset(GrassLayer layer)
        {
            if (!AssetDatabase.IsValidFolder("Assets/ModernGrassTool/Presets"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/ModernGrassTool"))
                {
                    AssetDatabase.CreateFolder("Assets", "ModernGrassTool");
                }
                AssetDatabase.CreateFolder("Assets/ModernGrassTool", "Presets");
            }

            string safeName = string.IsNullOrEmpty(layer.layerName) ? "LawnGrass" : layer.layerName.Replace(" ", "_");
            string path = $"Assets/ModernGrassTool/Presets/{safeName}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            ModernGrassType gt = ScriptableObject.CreateInstance<ModernGrassType>();
            gt.typeName = string.IsNullOrEmpty(layer.layerName) ? "Lawn Grass" : layer.layerName;
            gt.topColor = layer.topColor;
            gt.bottomColor = layer.bottomColor;
            gt.bladeSegments = layer.bladeSegments;
            gt.tipShape = layer.tipShape;
            gt.tipWidth = layer.tipWidth;
            gt.bellyWidth = layer.bellyWidth;
            gt.defaultWidth = layer.defaultWidth;
            gt.defaultHeight = layer.defaultHeight;
            gt.bottomWidth = layer.bottomWidth;
            gt.bottomWidthVariation = layer.bottomWidthVariation;
            gt.widthVariation = layer.widthVariation;
            gt.heightVariation = layer.heightVariation;
            gt.bladeForward = layer.bladeForward;
            gt.bladeCurve = layer.bladeCurve;
            gt.uprightIntensity = layer.uprightIntensity;
            gt.bladesPerClump = layer.bladesPerClump;
            gt.clumpRadius = layer.clumpRadius;
            gt.clumpTilt = layer.clumpTilt;
            gt.normalLimit = layer.normalLimit;
            gt.normalUpBlend = layer.normalUpBlend;
            gt.translucency = layer.translucency;
            gt.edgeHighlight = layer.edgeHighlight;
            gt.castShadows = layer.castShadows;
            gt.shadowCastDensity = layer.shadowCastDensity;
            gt.bladeShadowStrength = layer.bladeShadowStrength;
            gt.canBeCut = layer.canBeCut;
            gt.cutParticlePrefab = layer.cutParticlePrefab;
            gt.canCatchFire = layer.canCatchFire;
            gt.burnDuration = layer.burnDuration;
            gt.fireSpreadRadius = layer.fireSpreadRadius;
            gt.fireSpreadSpeed = layer.fireSpreadSpeed;
            gt.fireMaxSpreadGap = layer.fireMaxSpreadGap;
            gt.charredColor = layer.charredColor;
            gt.fireParticlePrefab = layer.fireParticlePrefab;
            gt.fireParticleDensity = layer.fireParticleDensity;
            gt.windStrength = layer.windStrength;
            gt.windSpeed = layer.windSpeed;
            gt.windScale = layer.windScale;
            gt.minFadeDistance = layer.minFadeDistance;
            gt.maxDrawDistance = layer.maxDrawDistance;

            AssetDatabase.CreateAsset(gt, path);
            AssetDatabase.SaveAssets();

            layer.grassType = gt;
            Debug.Log($"[ModernGrassManager] Converted and created GrassType asset at {path}");
            return gt;
        }
    }
}