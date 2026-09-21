using System;
using UnityEngine;

namespace ModernGrassTool
{
    public enum FoliageRenderMode
    {
        ProceduralBlade = 0,   // 🌿 High-performance GPU procedural compute blade (Color Gradient)
        ProceduralTexture = 1, // 🌾 High-performance GPU procedural compute blade with Alpha/Albedo Texture
        CustomMesh = 2         // 📦 Custom 3D Model / Mesh / Environment Props (GPU Instancing)
    }

    [CreateAssetMenu(fileName = "NewGrassType", menuName = "Modern Grass/Grass Type", order = 1)]
    public class ModernGrassType : ScriptableObject
    {
        [Header("General")]
        public string typeName = "Grass Type";
        public FoliageRenderMode renderMode = FoliageRenderMode.ProceduralBlade;

        [Header("Blade Texture & Cutout (Procedural Texture Mode)")]
        [Tooltip("2D Texture (e.g. Pampas grass fluff, wild grass silhouette, flower petals) mapped to procedural blades.")]
        public Texture2D bladeTexture;
        [Tooltip("Alpha discard threshold for cutout textures (0 = show all, 1 = discard all).")]
        [Range(0.01f, 1.0f)]
        public float alphaCutoff = 0.5f;
        [Tooltip("When enabled, multiplies texture color with blade gradient colors. When disabled, uses texture color directly.")]
        public bool blendWithBladeColor = true;

        [Header("Appearance and Tints (Procedural Blade)")]
        public Color topColor = new Color(0.44f, 0.76f, 0.28f, 1.0f);
        public Color bottomColor = new Color(0.12f, 0.35f, 0.08f, 1.0f);

        [Header("World-Space Noise Color Tinting (Ghibli / BOTW Aesthetic)")]
        [Tooltip("Enable subtle rolling color variations across the landscape.")]
        public bool enableColorNoise = true;
        [Tooltip("Secondary top tint blended across noise waves (e.g. sunlit golden green, dry grass tint).")]
        public Color noiseTopColor = new Color(0.72f, 0.85f, 0.30f, 1.0f);
        [Tooltip("Secondary root tint blended across noise waves.")]
        public Color noiseBottomColor = new Color(0.22f, 0.42f, 0.10f, 1.0f);
        [Tooltip("Frequency/scale of the color variation patches. Lower = broader fields, Higher = smaller patches.")]
        [Range(0.005f, 0.2f)]
        public float noiseScale = 0.035f;
        [Tooltip("Sharpness of color patch transitions.")]
        [Range(0.1f, 3.0f)]
        public float noiseContrast = 1.0f;
        [Tooltip("Enable rolling anime wind wave sweeps across the grass field.")]
        public bool enableNoiseWindWaves = true;

        [Header("World-Space Noise Height Variation (Organic High/Low Patches)")]
        [Tooltip("Enable organic high and low grass patches across the landscape using world-space noise.")]
        public bool enableHeightNoise = false;
        [Tooltip("Frequency/scale of the height variation patches in world space. Lower = broad undulating meadows, Higher = frequent height dips.")]
        [Range(0.005f, 0.2f)]
        public float heightNoiseScale = 0.03f;
        [Tooltip("Sharpness/contrast of transitions between tall and short grass.")]
        [Range(0.1f, 3.0f)]
        public float heightNoiseContrast = 1.0f;
        [Tooltip("How much height varies between high and low patches. 0.5 = 25% shorter to 25% taller. 1.0 = 50% shorter to 50% taller.")]
        [Range(0.0f, 1.0f)]
        public float heightNoiseIntensity = 0.5f;
        [Tooltip("Enable dynamic wind wave rolling across height variations.")]
        public bool enableHeightNoiseWaves = false;
        [Tooltip("Controls how strongly blade heights dip and swell as rolling wind waves pass through.")]
        [Range(0f, 1f)]
        public float heightWaveIntensity = 0.5f;

        [Header("Blade Shape and Geometry (Procedural Blade)")]
        [Range(1, 5)]
        public int bladeSegments = 3;
        public GrassTipShape tipShape = GrassTipShape.Pointed;
        [Range(0.05f, 1.0f)]
        public float tipWidth = 0.5f;
        [Range(0.2f, 2.0f)]
        public float bellyWidth = 1.0f;

        [Header("Blade Dimensions")]
        [Range(0.01f, 0.5f)]
        public float defaultWidth = 0.1f;
        [Range(0.1f, 3.0f)]
        public float defaultHeight = 0.7f;
        [Range(0.0f, 2.0f)]
        public float bottomWidth = 0.5f;
        [Range(0f, 1f)]
        public float bottomWidthVariation = 0.2f;
        [Range(0f, 1f)]
        public float widthVariation = 0.3f;
        [Range(0f, 1f)]
        public float heightVariation = 0.3f;

        [Header("Curvature & Growth Alignment")]
        [Range(0f, 1f)]
        public float bladeForward = 0.35f;
        [Range(1f, 4f)]
        public float bladeCurve = 2.0f;
        [Range(0f, 1f)]
        [Tooltip("Blends grass blade growth between surface normal (0 = sticking out perpendicular to slope) and world up (1 = growing straight up towards the sky).")]
        public float uprightIntensity = 0.8f;

        [Header("Clump Settings (Procedural Blade)")]
        [Range(1, 8)]
        public int bladesPerClump = 4;
        [Range(0.01f, 0.4f)]
        public float clumpRadius = 0.12f;
        [Range(0f, 1f)]
        public float clumpTilt = 0.35f;

        [Header("Custom 3D Mesh Mode (Future-Ready)")]
        [Tooltip("3D model mesh (e.g. flower, clover, fern, bush, rock).")]
        public Mesh customMesh;
        [Tooltip("Material for the custom 3D model.")]
        public Material customMaterial;
        [Tooltip("Random scale variation range.")]
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
        [Tooltip("Align mesh orientation to surface normal.")]
        public bool alignToNormal = true;
        [Tooltip("Random yaw rotation around up axis.")]
        public bool randomYRotation = true;

        [Header("Cut VFX and Particles")]
        [Tooltip("If enabled, this grass type can be cut by weapons, lawnmowers, or ModernGrassCutter. If disabled, this species cannot be cut.")]
        public bool canBeCut = true;
        [Tooltip("Custom Particle System prefab spawned when this grass type is cut. If null, a procedural shred particle is used.")]
        public ParticleSystem cutParticlePrefab;

        [Header("Fire, Burning & Charring")]
        [Tooltip("If enabled, this grass species can catch fire, burn down, and spread fire to nearby grass.")]
        public bool canCatchFire = true;
        [Tooltip("Duration in seconds for blades to burn before turning into charred ash stubble.")]
        [Range(0.5f, 10f)]
        public float burnDuration = 2.5f;
        [Tooltip("Maximum distance in meters fire can propagate outward from the ignition source before extinguishing.")]
        [Range(0.5f, 25f)]
        public float fireSpreadRadius = 3.5f;
        [Tooltip("Propagation speed multiplier at which fire spreads to neighboring grass (0.2 = slow creeping, 3.0 = fast wildfire).")]
        [Range(0.1f, 5f)]
        public float fireSpreadSpeed = 1.0f;
        [Tooltip("Maximum gap distance between grass blades. If the gap to the next grass blade exceeds this distance, fire stops spreading.")]
        [Range(0.2f, 5f)]
        public float fireMaxSpreadGap = 0.8f;
        [Tooltip("Color of charred scorched grass after burning or being hit by explosions.")]
        public Color charredColor = new Color(0.08f, 0.08f, 0.08f, 1f);
        [Tooltip("Optional custom Particle System prefab spawned when this grass species catches fire. If null, no fire particles are spawned.")]
        public ParticleSystem fireParticlePrefab;
        [Tooltip("Multiplier for fire particle emission density (0.1 to 5.0). Higher values produce denser, thicker flames.")]
        [Range(0.1f, 5.0f)]
        public float fireParticleDensity = 1.0f;

        [Header("Shading and Lighting")]
        [Range(0f, 1f)]
        public float normalUpBlend = 0.75f;
        [Range(0f, 1f)]
        public float translucency = 0.5f;
        [Range(0f, 1f)]
        public float edgeHighlight = 0.4f;

        [Header("Shadows (Stylized Soft Shadows)")]
        public bool castShadows = true;
        [Range(0f, 1f)]
        public float shadowCastDensity = 0.45f;
        [Range(0f, 1f)]
        public float bladeShadowStrength = 0.5f;

        [Header("Wind")]
        [Range(0f, 2f)]
        public float windStrength = 0.3f;
        [Range(0f, 10f)]
        public float windSpeed = 2.0f;
        [Range(0.01f, 2f)]
        public float windScale = 0.5f;
        [Tooltip("Controls spring recoil and organic bounce when blades return from swaying.")]
        [Range(0f, 1f)]
        public float windElasticity = 0.5f;
        [Tooltip("Softens the turnaround easing at rest pose, preventing stiffness and abrupt halts.")]
        [Range(0f, 1f)]
        public float windSmoothness = 0.5f;

        [Header("Culling and Distance LOD")]
        public float minFadeDistance = 40f;
        public float maxDrawDistance = 120f;
        [Range(0f, 1f)]
        public float normalLimit = 0.45f;

        [Header("Dynamic Segment LOD")]
        [Tooltip("Automatically reduces blade segment count at medium/far distance to save GPU vertex processing while preserving near detail.")]
        public bool enableSegmentLOD = false;
        [Range(10f, 100f)]
        public float segmentLODStartDist = 30f;
        [Range(20f, 150f)]
        public float segmentLODEndDist = 70f;
        [Range(1, 2)]
        public int minSegmentCount = 1;

        [Header("Player & Object Interaction")]
        [Tooltip("If enabled, characters, animals, and objects push and bend this grass species when moving through it.")]
        public bool enableInteraction = false;
        [Range(0.0f, 10.0f)]
        [Tooltip("Harmonic spring wobble as blades rebound when released from an interactor.")]
        public float elasticOscillation = 1.0f;
    }
}