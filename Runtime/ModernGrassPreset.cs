using UnityEngine;

namespace ModernGrassTool
{
    [CreateAssetMenu(fileName = "NewGrassPreset", menuName = "Modern Grass/Grass Preset")]
    public class ModernGrassPreset : ScriptableObject
    {
        [Header("Appearance & Tints")]
        public Color topColor = new Color(0.44f, 0.76f, 0.28f, 1.0f);
        public Color bottomColor = new Color(0.12f, 0.35f, 0.08f, 1.0f);

        [Header("Clump Settings")]
        [Range(1, 8)] public int bladesPerClump = 4;
        [Range(0.01f, 0.4f)] public float clumpRadius = 0.12f;
        [Range(0f, 1f)] public float clumpTilt = 0.35f;

        [Header("Blade Shape & Geometry")]
        [Range(1, 5)] public int bladeSegments = 3;
        public GrassTipShape tipShape = GrassTipShape.Pointed;
        [Range(0.0f, 1.0f)] public float tipWidth = 0.5f;
        [Range(0.2f, 2.0f)] public float bellyWidth = 1.0f;

        [Header("Blade Dimensions")]
        [Range(0.01f, 0.5f)] public float defaultWidth = 0.1f;
        [Range(0.1f, 3.0f)] public float defaultHeight = 0.8f;
        [Range(0.0f, 2.0f)] public float bottomWidth = 0.5f;
        [Range(0f, 1f)] public float bottomWidthVariation = 0.3f;
        [Range(0f, 1f)] public float widthVariation = 0.3f;
        [Range(0f, 1f)] public float heightVariation = 0.4f;

        [Header("Curvature & Slope")]
        [Range(0f, 1.5f)] public float bladeForward = 0.38f;
        [Range(1f, 5f)] public float bladeCurve = 2.0f;
        [Range(0f, 1f)] public float uprightIntensity = 0.8f;
        [Range(0f, 1f)] public float normalLimit = 0.45f;

        [Header("Shading & Lighting")]
        [Range(0f, 1f)] public float normalUpBlend = 0.75f;
        [Range(0f, 1f)] public float translucency = 0.5f;
        [Range(0f, 1f)] public float edgeHighlight = 0.4f;

        [Header("Shadows (Stylized Soft Shadows)")]
        public bool castShadows = true;
        [Range(0f, 1f)] public float shadowCastDensity = 0.45f;
        [Range(0f, 1f)] public float bladeShadowStrength = 0.5f;

        [Header("Cut VFX and Particles")]
        public bool canBeCut = true;
        public ParticleSystem cutParticlePrefab;

        [Header("Wind")]
        [Range(0f, 2f)] public float windStrength = 0.3f;
        [Range(0f, 10f)] public float windSpeed = 2.0f;
        [Range(0.01f, 2f)] public float windScale = 0.5f;
        [Range(0f, 1f)] public float windElasticity = 0.5f;
        [Range(0f, 1f)] public float windSmoothness = 0.5f;
        public bool enableNoiseWindWaves = true;
        public bool enableHeightNoiseWaves = false;
        [Range(0f, 1f)] public float heightWaveIntensity = 0.5f;

        [Header("Ground Blending")]
        public bool enableGroundBlend = false;
        [Range(-1f, 2f)] public float groundBlendFade = 0.0f;
        [Range(0.1f, 5f)] public float groundBlendStretch = 2.5f;
        [Range(0f, 2f)] public float groundBlendBrightness = 1.0f;
        [Range(0f, 2f)] public float groundBlendSaturation = 1.0f;
        public Color ambientAdjustmentColor = Color.white;

        [Header("Culling & LOD")]
        public float minFadeDistance = 40f;
        public float maxDrawDistance = 120f;
        public bool enableSegmentLOD = false;
        [Range(10f, 100f)] public float segmentLODStartDist = 30f;
        [Range(20f, 150f)] public float segmentLODEndDist = 70f;
        [Range(1, 2)] public int minSegmentCount = 1;

        [Header("Player & Object Interaction")]
        public bool enableInteraction = false;
        [Range(0.0f, 10.0f)] public float elasticOscillation = 1.0f;

        public void ApplyTo(GrassLayer layer)
        {
            if (layer == null) return;
            layer.topColor = topColor;
            layer.bottomColor = bottomColor;

            layer.bladeSegments = bladeSegments;
            layer.tipShape = tipShape;
            layer.tipWidth = tipWidth;
            layer.bellyWidth = bellyWidth;

            layer.bladesPerClump = bladesPerClump;
            layer.clumpRadius = clumpRadius;
            layer.clumpTilt = clumpTilt;

            layer.defaultWidth = defaultWidth;
            layer.defaultHeight = defaultHeight;
            layer.bottomWidth = bottomWidth;
            layer.bottomWidthVariation = bottomWidthVariation;
            layer.widthVariation = widthVariation;
            layer.heightVariation = heightVariation;

            layer.bladeForward = bladeForward;
            layer.bladeCurve = bladeCurve;
            layer.uprightIntensity = uprightIntensity;
            layer.normalLimit = normalLimit;

            layer.normalUpBlend = normalUpBlend;
            layer.translucency = translucency;
            layer.edgeHighlight = edgeHighlight;

            layer.castShadows = castShadows;
            layer.shadowCastDensity = shadowCastDensity;
            layer.bladeShadowStrength = bladeShadowStrength;

            layer.canBeCut = canBeCut;
            layer.cutParticlePrefab = cutParticlePrefab;

            layer.windStrength = windStrength;
            layer.windSpeed = windSpeed;
            layer.windScale = windScale;
            layer.windElasticity = windElasticity;
            layer.windSmoothness = windSmoothness;
            layer.enableNoiseWindWaves = enableNoiseWindWaves;
            layer.enableHeightNoiseWaves = enableHeightNoiseWaves;
            layer.heightWaveIntensity = heightWaveIntensity;

            layer.enableGroundBlend = enableGroundBlend;
            layer.groundBlendFade = groundBlendFade;
            layer.groundBlendStretch = groundBlendStretch;
            layer.groundBlendBrightness = groundBlendBrightness;
            layer.groundBlendSaturation = groundBlendSaturation;
            layer.ambientAdjustmentColor = ambientAdjustmentColor;

            layer.minFadeDistance = minFadeDistance;
            layer.maxDrawDistance = maxDrawDistance;
            layer.enableSegmentLOD = enableSegmentLOD;
            layer.segmentLODStartDist = segmentLODStartDist;
            layer.segmentLODEndDist = segmentLODEndDist;
            layer.minSegmentCount = minSegmentCount;

            layer.enableInteraction = enableInteraction;
            layer.elasticOscillation = elasticOscillation;

            layer.isDirty = true;
        }

        public void CopyFrom(GrassLayer layer)
        {
            if (layer == null) return;
            topColor = layer.topColor;
            bottomColor = layer.bottomColor;

            bladeSegments = layer.bladeSegments;
            tipShape = layer.tipShape;
            tipWidth = layer.tipWidth;
            bellyWidth = layer.bellyWidth;

            bladesPerClump = layer.bladesPerClump;
            clumpRadius = layer.clumpRadius;
            clumpTilt = layer.clumpTilt;

            defaultWidth = layer.defaultWidth;
            defaultHeight = layer.defaultHeight;
            bottomWidth = layer.bottomWidth;
            bottomWidthVariation = layer.bottomWidthVariation;
            widthVariation = layer.widthVariation;
            heightVariation = layer.heightVariation;

            bladeForward = layer.bladeForward;
            bladeCurve = layer.bladeCurve;
            uprightIntensity = layer.uprightIntensity;
            normalLimit = layer.normalLimit;

            normalUpBlend = layer.normalUpBlend;
            translucency = layer.translucency;
            edgeHighlight = layer.edgeHighlight;

            castShadows = layer.castShadows;
            shadowCastDensity = layer.shadowCastDensity;
            bladeShadowStrength = layer.bladeShadowStrength;

            canBeCut = layer.canBeCut;
            cutParticlePrefab = layer.cutParticlePrefab;

            windStrength = layer.windStrength;
            windSpeed = layer.windSpeed;
            windScale = layer.windScale;
            windElasticity = layer.windElasticity;
            windSmoothness = layer.windSmoothness;
            enableNoiseWindWaves = layer.enableNoiseWindWaves;
            enableHeightNoiseWaves = layer.enableHeightNoiseWaves;
            heightWaveIntensity = layer.heightWaveIntensity;

            enableGroundBlend = layer.enableGroundBlend;
            groundBlendFade = layer.groundBlendFade;
            groundBlendStretch = layer.groundBlendStretch;
            groundBlendBrightness = layer.groundBlendBrightness;
            groundBlendSaturation = layer.groundBlendSaturation;
            ambientAdjustmentColor = layer.ambientAdjustmentColor;

            minFadeDistance = layer.minFadeDistance;
            maxDrawDistance = layer.maxDrawDistance;
            enableSegmentLOD = layer.enableSegmentLOD;
            segmentLODStartDist = layer.segmentLODStartDist;
            segmentLODEndDist = layer.segmentLODEndDist;
            minSegmentCount = layer.minSegmentCount;

            enableInteraction = layer.enableInteraction;
            elasticOscillation = layer.elasticOscillation;
        }
    }
}
