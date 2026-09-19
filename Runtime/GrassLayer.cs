using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernGrassTool
{
    public enum GrassTipShape
    {
        Pointed = 0,  // Sharp apex point - natural wild grass
        Blunt = 1     // Flat horizontal cut - trimmed lawn / reeds
    }

    [Serializable]
    public class GrassLayer
    {
        [Header("Grass Type Asset")]
        [Tooltip("The ScriptableObject asset defining this grass type's visual style, shape, colors, and render mode.")]
        public ModernGrassType grassType;

        [Header("Layer Info")]
        public string layerName = "Grass Layer";
        public bool isVisible = true;

        [Header("Ground Blending")]
        public bool enableGroundBlend = false;
        [Range(-1f, 2f)] public float groundBlendFade = 0.0f;
        [Range(0.1f, 5f)] public float groundBlendStretch = 2.5f;
        [Range(0f, 2f)] public float groundBlendBrightness = 1.0f;
        [Range(0f, 2f)] public float groundBlendSaturation = 1.0f;
        public Color ambientAdjustmentColor = Color.white;

        // Serialized fallback backing fields (used for migration & when grassType is not yet set)
        [SerializeField] private Color _topColor = new Color(0.44f, 0.76f, 0.28f, 1.0f);
        [SerializeField] private Color _bottomColor = new Color(0.12f, 0.35f, 0.08f, 1.0f);
        [SerializeField] private int _bladesPerClump = 4;
        [SerializeField] private float _clumpRadius = 0.12f;
        [SerializeField] private float _clumpTilt = 0.35f;
        [SerializeField] private int _bladeSegments = 3;
        [SerializeField] private GrassTipShape _tipShape = GrassTipShape.Pointed;
        [SerializeField] private float _tipWidth = 0.5f;
        [SerializeField] private float _bellyWidth = 1.0f;
        [SerializeField] private float _defaultWidth = 0.1f;
        [SerializeField] private float _defaultHeight = 0.8f;
        [SerializeField] private float _bottomWidth = 0.5f;
        [SerializeField] private float _bottomWidthVariation = 0.3f;
        [SerializeField] private float _widthVariation = 0.3f;
        [SerializeField] private float _heightVariation = 0.4f;
        [SerializeField] private float _bladeForward = 0.38f;
        [SerializeField] private float _bladeCurve = 2.0f;
        [SerializeField] private float _uprightIntensity = 0.8f;
        [SerializeField] private float _normalLimit = 0.45f;
        [SerializeField] private float _normalUpBlend = 0.75f;
        [SerializeField] private float _translucency = 0.5f;
        [SerializeField] private float _edgeHighlight = 0.4f;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private float _shadowCastDensity = 0.45f;
        [SerializeField] private float _bladeShadowStrength = 0.5f;
        [SerializeField] private bool _canBeCut = true;
        [SerializeField] private ParticleSystem _cutParticlePrefab;
        [SerializeField] private float _windStrength = 0.3f;
        [SerializeField] private float _windSpeed = 2.0f;
        [SerializeField] private float _windScale = 0.5f;
        [SerializeField] private float _windElasticity = 0.5f;
        [SerializeField] private float _windSmoothness = 0.5f;
        [SerializeField] private float _minFadeDistance = 40f;
        [SerializeField] private float _maxDrawDistance = 120f;
        [SerializeField] private bool _enableSegmentLOD = false;
        [SerializeField] private float _segmentLODStartDist = 30f;
        [SerializeField] private float _segmentLODEndDist = 70f;
        [SerializeField] private int _minSegmentCount = 1;
        [SerializeField] private int _cullingTreeDepth = 4;

        // Player & Object Interaction
        [SerializeField] private bool _enableInteraction = false;
        [SerializeField] private float _elasticOscillation = 0.5f;

        // World-Space Noise Color Tint & Waves
        [SerializeField] private bool _enableColorNoise = true;
        [SerializeField] private Color _noiseTopColor = new Color(0.72f, 0.85f, 0.30f, 1.0f);
        [SerializeField] private Color _noiseBottomColor = new Color(0.22f, 0.42f, 0.10f, 1.0f);
        [SerializeField] private float _noiseScale = 0.035f;
        [SerializeField] private float _noiseContrast = 1.0f;
        [SerializeField] private bool _enableNoiseWindWaves = true;

        // World-Space Noise Height Variation & Waves
        [SerializeField] private bool _enableHeightNoise = false;
        [SerializeField] private float _heightNoiseScale = 0.03f;
        [SerializeField] private float _heightNoiseContrast = 1.0f;
        [SerializeField] private float _heightNoiseIntensity = 0.5f;
        [SerializeField] private bool _enableHeightNoiseWaves = false;
        [SerializeField] private float _heightWaveIntensity = 0.5f;

        // Seamless delegates to grassType (with fallback to local serialized fields)
        public FoliageRenderMode RenderMode => grassType != null ? grassType.renderMode : FoliageRenderMode.ProceduralBlade;
        public string DisplayName => grassType != null ? grassType.typeName : layerName;

        public Texture2D bladeTexture => grassType != null ? grassType.bladeTexture : null;
        public float alphaCutoff => grassType != null ? grassType.alphaCutoff : 0.5f;
        public bool blendWithBladeColor => grassType != null ? grassType.blendWithBladeColor : true;

        public string FormatIcon => RenderMode switch
        {
            FoliageRenderMode.ProceduralBlade => "🌿",
            FoliageRenderMode.ProceduralTexture => "🌾",
            FoliageRenderMode.CustomMesh => "📦",
            _ => "🌿"
        };

        public string FormatName => RenderMode switch
        {
            FoliageRenderMode.ProceduralBlade => "Procedural Color",
            FoliageRenderMode.ProceduralTexture => "Procedural Texture",
            FoliageRenderMode.CustomMesh => "Custom 3D Mesh",
            _ => "Procedural"
        };

        public Color topColor { get => grassType != null ? grassType.topColor : _topColor; set { if (grassType != null) grassType.topColor = value; else _topColor = value; } }
        public Color bottomColor { get => grassType != null ? grassType.bottomColor : _bottomColor; set { if (grassType != null) grassType.bottomColor = value; else _bottomColor = value; } }
        public int bladesPerClump { get => grassType != null ? grassType.bladesPerClump : _bladesPerClump; set { if (grassType != null) grassType.bladesPerClump = value; else _bladesPerClump = value; } }
        public float clumpRadius { get => grassType != null ? grassType.clumpRadius : _clumpRadius; set { if (grassType != null) grassType.clumpRadius = value; else _clumpRadius = value; } }
        public float clumpTilt { get => grassType != null ? grassType.clumpTilt : _clumpTilt; set { if (grassType != null) grassType.clumpTilt = value; else _clumpTilt = value; } }
        public int bladeSegments { get => grassType != null ? grassType.bladeSegments : _bladeSegments; set { if (grassType != null) grassType.bladeSegments = value; else _bladeSegments = value; } }
        public GrassTipShape tipShape
        {
            get
            {
                GrassTipShape val = grassType != null ? grassType.tipShape : _tipShape;
                return (int)val > 1 ? GrassTipShape.Pointed : val;
            }
            set
            {
                GrassTipShape safeVal = (int)value > 1 ? GrassTipShape.Pointed : value;
                if (grassType != null) grassType.tipShape = safeVal; else _tipShape = safeVal;
            }
        }
        public float tipWidth { get => grassType != null ? grassType.tipWidth : _tipWidth; set { if (grassType != null) grassType.tipWidth = value; else _tipWidth = value; } }
        public float bellyWidth { get => grassType != null ? grassType.bellyWidth : _bellyWidth; set { if (grassType != null) grassType.bellyWidth = value; else _bellyWidth = value; } }
        public float defaultWidth { get => grassType != null ? grassType.defaultWidth : _defaultWidth; set { if (grassType != null) grassType.defaultWidth = value; else _defaultWidth = value; } }
        public float defaultHeight { get => grassType != null ? grassType.defaultHeight : _defaultHeight; set { if (grassType != null) grassType.defaultHeight = value; else _defaultHeight = value; } }
        public float bottomWidth { get => grassType != null ? grassType.bottomWidth : _bottomWidth; set { if (grassType != null) grassType.bottomWidth = value; else _bottomWidth = value; } }
        public float bottomWidthVariation { get => grassType != null ? grassType.bottomWidthVariation : _bottomWidthVariation; set { if (grassType != null) grassType.bottomWidthVariation = value; else _bottomWidthVariation = value; } }
        public float widthVariation { get => grassType != null ? grassType.widthVariation : _widthVariation; set { if (grassType != null) grassType.widthVariation = value; else _widthVariation = value; } }
        public float heightVariation { get => grassType != null ? grassType.heightVariation : _heightVariation; set { if (grassType != null) grassType.heightVariation = value; else _heightVariation = value; } }
        public float bladeForward { get => grassType != null ? grassType.bladeForward : _bladeForward; set { if (grassType != null) grassType.bladeForward = value; else _bladeForward = value; } }
        public float bladeCurve { get => grassType != null ? grassType.bladeCurve : _bladeCurve; set { if (grassType != null) grassType.bladeCurve = value; else _bladeCurve = value; } }
        public float uprightIntensity { get => grassType != null ? grassType.uprightIntensity : _uprightIntensity; set { if (grassType != null) grassType.uprightIntensity = value; else _uprightIntensity = value; } }
        public float normalLimit { get => grassType != null ? grassType.normalLimit : _normalLimit; set { if (grassType != null) grassType.normalLimit = value; else _normalLimit = value; } }
        public float normalUpBlend { get => grassType != null ? grassType.normalUpBlend : _normalUpBlend; set { if (grassType != null) grassType.normalUpBlend = value; else _normalUpBlend = value; } }
        public float translucency { get => grassType != null ? grassType.translucency : _translucency; set { if (grassType != null) grassType.translucency = value; else _translucency = value; } }
        public float edgeHighlight { get => grassType != null ? grassType.edgeHighlight : _edgeHighlight; set { if (grassType != null) grassType.edgeHighlight = value; else _edgeHighlight = value; } }
        public bool castShadows { get => grassType != null ? grassType.castShadows : _castShadows; set { if (grassType != null) grassType.castShadows = value; else _castShadows = value; } }
        public float shadowCastDensity { get => grassType != null ? grassType.shadowCastDensity : _shadowCastDensity; set { if (grassType != null) grassType.shadowCastDensity = value; else _shadowCastDensity = value; } }
        public float bladeShadowStrength { get => grassType != null ? grassType.bladeShadowStrength : _bladeShadowStrength; set { if (grassType != null) grassType.bladeShadowStrength = value; else _bladeShadowStrength = value; } }
        public bool canBeCut { get => grassType != null ? grassType.canBeCut : _canBeCut; set { if (grassType != null) grassType.canBeCut = value; else _canBeCut = value; } }
        public ParticleSystem cutParticlePrefab { get => grassType != null ? grassType.cutParticlePrefab : _cutParticlePrefab; set { if (grassType != null) grassType.cutParticlePrefab = value; else _cutParticlePrefab = value; } }
        public float windStrength { get => grassType != null ? grassType.windStrength : _windStrength; set { if (grassType != null) grassType.windStrength = value; else _windStrength = value; } }
        public float windSpeed { get => grassType != null ? grassType.windSpeed : _windSpeed; set { if (grassType != null) grassType.windSpeed = value; else _windSpeed = value; } }
        public float windScale { get => grassType != null ? grassType.windScale : _windScale; set { if (grassType != null) grassType.windScale = value; else _windScale = value; } }
        public float windElasticity { get => grassType != null ? grassType.windElasticity : _windElasticity; set { if (grassType != null) grassType.windElasticity = value; else _windElasticity = value; } }
        public float windSmoothness { get => grassType != null ? grassType.windSmoothness : _windSmoothness; set { if (grassType != null) grassType.windSmoothness = value; else _windSmoothness = value; } }
        public float minFadeDistance { get => grassType != null ? grassType.minFadeDistance : _minFadeDistance; set { if (grassType != null) grassType.minFadeDistance = value; else _minFadeDistance = value; } }
        public float maxDrawDistance { get => grassType != null ? grassType.maxDrawDistance : _maxDrawDistance; set { if (grassType != null) grassType.maxDrawDistance = value; else _maxDrawDistance = value; } }
        public bool enableSegmentLOD { get => grassType != null ? grassType.enableSegmentLOD : _enableSegmentLOD; set { if (grassType != null) grassType.enableSegmentLOD = value; else _enableSegmentLOD = value; } }
        public float segmentLODStartDist { get => grassType != null ? grassType.segmentLODStartDist : _segmentLODStartDist; set { if (grassType != null) grassType.segmentLODStartDist = value; else _segmentLODStartDist = value; } }
        public float segmentLODEndDist { get => grassType != null ? grassType.segmentLODEndDist : _segmentLODEndDist; set { if (grassType != null) grassType.segmentLODEndDist = value; else _segmentLODEndDist = value; } }
        public int minSegmentCount { get => grassType != null ? grassType.minSegmentCount : _minSegmentCount; set { if (grassType != null) grassType.minSegmentCount = value; else _minSegmentCount = value; } }
        public int cullingTreeDepth { get => _cullingTreeDepth; set => _cullingTreeDepth = value; }

        public bool enableInteraction { get => grassType != null ? grassType.enableInteraction : _enableInteraction; set { if (grassType != null) grassType.enableInteraction = value; else _enableInteraction = value; } }
        public float elasticOscillation { get => grassType != null ? grassType.elasticOscillation : _elasticOscillation; set { if (grassType != null) grassType.elasticOscillation = value; else _elasticOscillation = value; } }

        public bool enableColorNoise { get => grassType != null ? grassType.enableColorNoise : _enableColorNoise; set { if (grassType != null) grassType.enableColorNoise = value; else _enableColorNoise = value; } }
        public Color noiseTopColor { get => grassType != null ? grassType.noiseTopColor : _noiseTopColor; set { if (grassType != null) grassType.noiseTopColor = value; else _noiseTopColor = value; } }
        public Color noiseBottomColor { get => grassType != null ? grassType.noiseBottomColor : _noiseBottomColor; set { if (grassType != null) grassType.noiseBottomColor = value; else _noiseBottomColor = value; } }
        public float noiseScale { get => grassType != null ? grassType.noiseScale : _noiseScale; set { if (grassType != null) grassType.noiseScale = value; else _noiseScale = value; } }
        public float noiseContrast { get => grassType != null ? grassType.noiseContrast : _noiseContrast; set { if (grassType != null) grassType.noiseContrast = value; else _noiseContrast = value; } }
        public bool enableNoiseWindWaves { get => grassType != null ? grassType.enableNoiseWindWaves : _enableNoiseWindWaves; set { if (grassType != null) grassType.enableNoiseWindWaves = value; else _enableNoiseWindWaves = value; } }
        public bool enableHeightNoise { get => grassType != null ? grassType.enableHeightNoise : _enableHeightNoise; set { if (grassType != null) grassType.enableHeightNoise = value; else _enableHeightNoise = value; } }
        public float heightNoiseScale { get => grassType != null ? grassType.heightNoiseScale : _heightNoiseScale; set { if (grassType != null) grassType.heightNoiseScale = value; else _heightNoiseScale = value; } }
        public float heightNoiseContrast { get => grassType != null ? grassType.heightNoiseContrast : _heightNoiseContrast; set { if (grassType != null) grassType.heightNoiseContrast = value; else _heightNoiseContrast = value; } }
        public float heightNoiseIntensity { get => grassType != null ? grassType.heightNoiseIntensity : _heightNoiseIntensity; set { if (grassType != null) grassType.heightNoiseIntensity = value; else _heightNoiseIntensity = value; } }
        public bool enableHeightNoiseWaves { get => grassType != null ? grassType.enableHeightNoiseWaves : _enableHeightNoiseWaves; set { if (grassType != null) grassType.enableHeightNoiseWaves = value; else _enableHeightNoiseWaves = value; } }
        public float heightWaveIntensity { get => grassType != null ? grassType.heightWaveIntensity : _heightWaveIntensity; set { if (grassType != null) grassType.heightWaveIntensity = value; else _heightWaveIntensity = value; } }

        [HideInInspector]
        public List<GrassPoint> points = new List<GrassPoint>();

        [HideInInspector]
        public List<ModernGrassChunk> chunks = new List<ModernGrassChunk>();

        [NonSerialized]
        private Dictionary<Vector2Int, ModernGrassChunk> _chunkLookup;

        [NonSerialized]
        public HashSet<Vector2Int> activeChunkCoords = new HashSet<Vector2Int>();

        [NonSerialized]
        public ComputeBuffer sourceBuffer;
        [NonSerialized]
        public ComputeBuffer drawBuffer;
        [NonSerialized]
        public ComputeBuffer visibleIDBuffer;
        [NonSerialized]
        public ComputeBuffer cutBuffer;
        [NonSerialized]
        public ComputeBuffer argsBuffer;

        [NonSerialized]
        public float[] cutHeights;
        [NonSerialized]
        public ModernCullingTreeNode cullingTree;
        [NonSerialized]
        public List<int> visibleIDs = new List<int>();

        [NonSerialized]
        public Material layerMaterial;
        [NonSerialized]
        private bool _isDirty = true;
        public bool isDirty
        {
            get => _isDirty;
            set
            {
                _isDirty = value;
                if (value) boundsDirty = true;
            }
        }
        [NonSerialized]
        public bool cutDirty = true;
        [NonSerialized]
        public bool treeDirty = true;
        [NonSerialized]
        public bool boundsDirty = true;
        [NonSerialized]
        private Bounds _cachedBounds;

        public const int DRAW_VERTEX_STRIDE = 64; // 16-byte aligned: float3(12)+float3(12)+float2(8)+float4(16)+float(4)+float3(12)=64
        public const int VERTICES_PER_BLADE = 18; // Default 3 segments * 6 verts

        public int BladeSegments => Mathf.Clamp(bladeSegments, 1, 5);
        public int VerticesPerBlade => BladeSegments * 6;
        public int BladesPerClump => Mathf.Max(1, bladesPerClump);
        public int PointCount => points != null ? points.Count : 0;
        public int TotalBladeCount => PointCount * BladesPerClump;
        public int VertexCount => TotalBladeCount * VerticesPerBlade;
        public int TotalChunkCount => chunks != null ? chunks.Count : 0;

        public int TotalWorldPoints
        {
            get
            {
                if (chunks != null && chunks.Count > 0)
                {
                    int total = 0;
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        if (chunks[i] != null) total += chunks[i].PointCount;
                    }
                    return total;
                }
                return PointCount;
            }
        }

        public Bounds CalculateBounds()
        {
            if (!boundsDirty) return _cachedBounds;

            // Fast path: if chunk streaming is active, calculate bounds by encapsulating active chunk bounds
            if (activeChunkCoords != null && activeChunkCoords.Count > 0 && chunks != null && chunks.Count > 0)
            {
                Bounds combined = default;
                bool first = true;
                foreach (var coord in activeChunkCoords)
                {
                    var ch = GetChunk(coord);
                    if (ch != null && ch.PointCount > 0)
                    {
                        if (first) { combined = ch.bounds; first = false; }
                        else { combined.Encapsulate(ch.bounds); }
                    }
                }
                if (!first)
                {
                    combined.Expand(Mathf.Max(defaultHeight * 2f, 2f));
                    _cachedBounds = combined;
                    boundsDirty = false;
                    return _cachedBounds;
                }
            }

            if (points == null || points.Count == 0)
            {
                _cachedBounds = new Bounds(Vector3.zero, Vector3.one * 10f);
                boundsDirty = false;
                return _cachedBounds;
            }

            Vector3 min = points[0].position;
            Vector3 max = points[0].position;
            float extra = Mathf.Max(defaultHeight * 2f, 2f);

            for (int i = 1; i < points.Count; i++)
            {
                Vector3 p = points[i].position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            Bounds b = new Bounds();
            b.SetMinMax(min - Vector3.one * extra, max + Vector3.one * extra);
            _cachedBounds = b;
            boundsDirty = false;
            return _cachedBounds;
        }

        public void RebuildCullingTree()
        {
            int count = PointCount;
            if (count == 0)
            {
                cullingTree = null;
                treeDirty = false;
                return;
            }

            Bounds b = CalculateBounds();
            cullingTree = new ModernCullingTreeNode(b, cullingTreeDepth);

            for (int i = 0; i < count; i++)
            {
                cullingTree.FindLeaf(points[i].position, i);
            }
            cullingTree.ClearEmpty();
            treeDirty = false;
        }

        public void ReleaseBuffers()
        {
            if (sourceBuffer != null)
            {
                sourceBuffer.Release();
                sourceBuffer = null;
            }
            if (drawBuffer != null)
            {
                drawBuffer.Release();
                drawBuffer = null;
            }
            if (visibleIDBuffer != null)
            {
                visibleIDBuffer.Release();
                visibleIDBuffer = null;
            }
            if (cutBuffer != null)
            {
                cutBuffer.Release();
                cutBuffer = null;
            }
            if (argsBuffer != null)
            {
                argsBuffer.Release();
                argsBuffer = null;
            }
        }

        public void EnsureBuffers()
        {
            int count = PointCount;
            if (count == 0)
            {
                ReleaseBuffers();
                return;
            }

            // Geometric capacity allocation to avoid destroying and recreating buffers on every brush hit
            if (sourceBuffer == null || sourceBuffer.count < count || sourceBuffer.count > count * 2 + 4096)
            {
                if (sourceBuffer != null) sourceBuffer.Release();
                int targetCap = Mathf.Max(count + 2048, (int)(count * 1.25f));
                sourceBuffer = new ComputeBuffer(targetCap, GrassPoint.STRIDE);
                isDirty = true;
            }

            int totalVertices = VertexCount;
            if (drawBuffer == null || drawBuffer.count < totalVertices || drawBuffer.count > totalVertices * 2 + 32768)
            {
                if (drawBuffer != null) drawBuffer.Release();
                int targetVertCap = Mathf.Max(totalVertices + 16384, (int)(totalVertices * 1.25f));
                drawBuffer = new ComputeBuffer(targetVertCap, DRAW_VERTEX_STRIDE);
            }

            if (argsBuffer == null || argsBuffer.count != 4)
            {
                if (argsBuffer != null) argsBuffer.Release();
                argsBuffer = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
                argsBuffer.SetData(new uint[] { 0, 1, 0, 0 });
            }

            if (visibleIDBuffer == null || visibleIDBuffer.count < count || visibleIDBuffer.count > count * 2 + 4096)
            {
                if (visibleIDBuffer != null) visibleIDBuffer.Release();
                int targetCap = Mathf.Max(count + 2048, (int)(count * 1.25f));
                visibleIDBuffer = new ComputeBuffer(targetCap, sizeof(int));
            }

            if (cutBuffer == null || cutBuffer.count < count || cutBuffer.count > count * 2 + 4096)
            {
                if (cutBuffer != null) cutBuffer.Release();
                int targetCap = Mathf.Max(count + 2048, (int)(count * 1.25f));
                cutBuffer = new ComputeBuffer(targetCap, sizeof(float));
                cutDirty = true;
            }

            if (cutHeights == null || cutHeights.Length < count)
            {
                int newLen = Mathf.Max(count + 2048, (int)(count * 1.25f));
                float[] newArr = new float[newLen];
                for (int i = 0; i < newLen; i++) newArr[i] = -1.0f;
                if (cutHeights != null)
                {
                    Array.Copy(cutHeights, newArr, Math.Min(cutHeights.Length, count));
                }
                cutHeights = newArr;
                cutDirty = true;
            }

            if (isDirty && sourceBuffer != null)
            {
                sourceBuffer.SetData(points, 0, 0, count);
                isDirty = false;
            }

            if (cutDirty && cutBuffer != null)
            {
                cutBuffer.SetData(cutHeights, 0, 0, count);
                cutDirty = false;
            }

            // Culling octree is only required for CPU-culled CustomMesh instancing. Skip during active brush strokes.
            if (!ModernGrassManager.isPaintingActive && RenderMode == FoliageRenderMode.CustomMesh && (treeDirty || cullingTree == null))
            {
                RebuildCullingTree();
            }
        }

        public static Vector2Int PositionToChunkCoord(Vector3 pos, float chunkSize)
        {
            if (chunkSize <= 0f) chunkSize = 64f;
            int cx = Mathf.FloorToInt(pos.x / chunkSize);
            int cz = Mathf.FloorToInt(pos.z / chunkSize);
            return new Vector2Int(cx, cz);
        }

        public void BuildChunkLookup()
        {
            if (_chunkLookup == null) _chunkLookup = new Dictionary<Vector2Int, ModernGrassChunk>();
            _chunkLookup.Clear();
            if (chunks == null) chunks = new List<ModernGrassChunk>();
            for (int i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                if (c != null)
                {
                    _chunkLookup[c.coord] = c;
                }
            }
        }

        public ModernGrassChunk GetChunk(Vector2Int coord)
        {
            if (_chunkLookup == null || _chunkLookup.Count != chunks.Count) BuildChunkLookup();
            _chunkLookup.TryGetValue(coord, out var c);
            return c;
        }

        public ModernGrassChunk GetOrCreateChunk(Vector2Int coord, float chunkSize)
        {
            if (_chunkLookup == null || _chunkLookup.Count != chunks.Count) BuildChunkLookup();
            if (_chunkLookup.TryGetValue(coord, out var chunk) && chunk != null)
                return chunk;

            chunk = new ModernGrassChunk(coord, chunkSize);
            chunks.Add(chunk);
            _chunkLookup[coord] = chunk;
            return chunk;
        }

        public List<ModernGrassChunk> GetChunksInRadius(Vector3 center, float radius, float chunkSize)
        {
            var result = new List<ModernGrassChunk>();
            if (chunks == null || chunks.Count == 0) return result;
            if (_chunkLookup == null || _chunkLookup.Count != chunks.Count) BuildChunkLookup();

            if (chunkSize <= 0f) chunkSize = 64f;
            int minX = Mathf.FloorToInt((center.x - radius) / chunkSize);
            int maxX = Mathf.FloorToInt((center.x + radius) / chunkSize);
            int minZ = Mathf.FloorToInt((center.z - radius) / chunkSize);
            int maxZ = Mathf.FloorToInt((center.z + radius) / chunkSize);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (_chunkLookup.TryGetValue(new Vector2Int(x, z), out var chunk) && chunk != null)
                    {
                        result.Add(chunk);
                    }
                }
            }
            return result;
        }

        public void AutoMigratePointsToChunks(float chunkSize)
        {
            if (chunks == null) chunks = new List<ModernGrassChunk>();
            if (chunks.Count == 0 && points != null && points.Count > 0)
            {
                BuildChunkLookup();
                for (int i = 0; i < points.Count; i++)
                {
                    GrassPoint pt = points[i];
                    Vector2Int coord = PositionToChunkCoord(pt.position, chunkSize);
                    ModernGrassChunk chunk = GetOrCreateChunk(coord, chunkSize);
                    chunk.points.Add(pt);
                }
                for (int i = 0; i < chunks.Count; i++)
                {
                    chunks[i].RecalculateBounds(chunkSize);
                    chunks[i].EnsureCutHeights();
                }
            }
            else
            {
                BuildChunkLookup();
            }
        }

        public void StreamActiveChunks(HashSet<Vector2Int> targetCoords, float chunkSize)
        {
            if (activeChunkCoords != null && activeChunkCoords.SetEquals(targetCoords) && points.Count > 0) return;

            if (activeChunkCoords == null) activeChunkCoords = new HashSet<Vector2Int>();
            activeChunkCoords.Clear();
            activeChunkCoords.UnionWith(targetCoords);

            if (points == null) points = new List<GrassPoint>();
            points.Clear();

            int totalPoints = 0;
            foreach (var coord in targetCoords)
            {
                var chunk = GetChunk(coord);
                if (chunk != null) totalPoints += chunk.PointCount;
            }

            if (points.Capacity < totalPoints) points.Capacity = totalPoints;
            if (cutHeights == null || cutHeights.Length < totalPoints) cutHeights = new float[totalPoints];

            int writeIdx = 0;
            foreach (var coord in targetCoords)
            {
                var chunk = GetChunk(coord);
                if (chunk == null || chunk.PointCount == 0) continue;
                chunk.EnsureCutHeights();
                for (int i = 0; i < chunk.points.Count; i++)
                {
                    points.Add(chunk.points[i]);
                    cutHeights[writeIdx++] = chunk.cutHeights[i];
                }
            }

            isDirty = true;
            cutDirty = true;
            treeDirty = true;
            boundsDirty = true;
            EnsureBuffers();
        }
    }
}