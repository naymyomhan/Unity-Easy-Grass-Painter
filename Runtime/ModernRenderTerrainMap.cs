using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModernGrassTool
{
    public enum MapBakeMode
    {
        SceneBounds,
        FollowTarget
    }

    [ExecuteAlways]
    [AddComponentMenu("Modern Grass/Modern Render Terrain Map")]
    public class ModernRenderTerrainMap : MonoBehaviour
    {
        [Header("Bake Mode & Target (Open World)")]
        [Tooltip("SceneBounds bakes entire level once. FollowTarget follows the player/camera dynamically for large open worlds.")]
        public MapBakeMode bakeMode = MapBakeMode.FollowTarget;
        [Tooltip("Target to follow in Open World. If empty, automatically targets Player or Main Camera.")]
        public Transform followTarget;
        [Tooltip("Radius around the target to capture (e.g. 50m covers 100m x 100m area).")]
        public float followRadius = 50f;
        [Tooltip("Camera height above the target.")]
        public float cameraHeightAboveTarget = 15f;
        [Tooltip("Re-bake when target moves this many meters (avoids unnecessary redraws).")]
        public float moveThreshold = 3.0f;

        [Header("Camera & Layers")]
        [Tooltip("Camera used to bake the ground top-down. If null, a child camera is created automatically.")]
        public Camera camToDrawWith;
        [Tooltip("LayerMask of ground and terrain objects to bake.")]
        public LayerMask terrainLayer = ~0;

        [Header("Texture Settings")]
        public int resolution = 1024;
        [Tooltip("Extra padding around the scene bounds.")]
        public float adjustScaling = 2.0f;

        [Header("Real-Time Updates (SceneBounds Mode)")]
        [Tooltip("If true, periodically updates the ground texture at runtime.")]
        public bool realTimeDiffuse = false;
        public float repeatRate = 5f;

        [Header("Debug / Status")]
        public Bounds bounds;
        public float orthographicSize;
        public Vector3 orthographicPos;

        private RenderTexture _tempTex;
        private Vector3 _lastBakePos;
        private bool _fogRestoreValue;
        private bool _suppressedFog;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;

            TryFindFollowTarget();
            SetupAndBake();
        }

        private void Start()
        {
            TryFindFollowTarget();
            if (Application.isPlaying && realTimeDiffuse && bakeMode == MapBakeMode.SceneBounds)
            {
                InvokeRepeating(nameof(DrawDiffuseMap), 1f, repeatRate);
            }
        }

        private void LateUpdate()
        {
            if (bakeMode == MapBakeMode.FollowTarget)
            {
                if (followTarget == null)
                {
                    TryFindFollowTarget();
                }

                if (followTarget != null)
                {
                    float dist = Vector3.Distance(
                        new Vector3(followTarget.position.x, 0, followTarget.position.z),
                        new Vector3(_lastBakePos.x, 0, _lastBakePos.z)
                    );

                    if (dist >= moveThreshold)
                    {
                        DrawDiffuseMap();
                    }
                }
            }
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

            if (Application.isPlaying && realTimeDiffuse)
            {
                CancelInvoke(nameof(DrawDiffuseMap));
            }
            ReleaseTexture();
        }

        private void OnDestroy()
        {
            ReleaseTexture();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam == camToDrawWith)
            {
                _fogRestoreValue = RenderSettings.fog;
                RenderSettings.fog = false;
                _suppressedFog = true;
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam == camToDrawWith && _suppressedFog)
            {
                RenderSettings.fog = _fogRestoreValue;
                _suppressedFog = false;
            }
        }

        public void TryFindFollowTarget()
        {
            if (followTarget != null) return;

            var interactor = FindAnyObjectByType<ModernGrassInteractor>();
            if (interactor != null)
            {
                followTarget = interactor.transform;
                return;
            }

            if (Camera.main != null)
            {
                followTarget = Camera.main.transform;
                return;
            }

            var playerGo = GameObject.Find("Player") ?? GameObject.Find("PlayerCapsule");
            if (playerGo != null)
            {
                followTarget = playerGo.transform;
            }
        }

        public void ReleaseTexture()
        {
            if (_tempTex != null)
            {
                _tempTex.Release();
                if (Application.isPlaying) Destroy(_tempTex);
                else DestroyImmediate(_tempTex);
                _tempTex = null;
            }
        }

        public void SetupAndBake()
        {
            if (bakeMode == MapBakeMode.FollowTarget)
            {
                TryFindFollowTarget();
            }
            else
            {
                CalculateBounds();
            }

            SetupCam();
            DrawDiffuseMap();
        }

        public void CalculateBounds()
        {
            bounds = new Bounds(transform.position, Vector3.zero);
            bool hasBounds = false;

            // Encompass terrains
            var terrains = Terrain.activeTerrains;
            if (terrains != null && terrains.Length > 0)
            {
                foreach (var t in terrains)
                {
                    if (t == null || t.terrainData == null) continue;
                    Vector3 center = t.GetPosition() + t.terrainData.bounds.center;
                    Bounds b = new Bounds(center, t.terrainData.bounds.size);
                    if (!hasBounds) { bounds = b; hasBounds = true; }
                    else bounds.Encapsulate(b);
                }
            }

            // Encompass renderers matching layer
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                if (r.GetComponent<ModernGrassManager>() != null) continue;
                if (r.GetComponent<ModernGrassInteractor>() != null) continue;
                if ((terrainLayer.value & (1 << r.gameObject.layer)) == 0) continue;

                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!hasBounds)
            {
                bounds = new Bounds(Vector3.zero, new Vector3(60f, 10f, 60f));
            }
            else
            {
                bounds.Expand(new Vector3(4f, 2f, 4f));
            }

            // Ensure square XZ ratio
            float maxExt = Mathf.Max(bounds.extents.x, bounds.extents.z);
            bounds.extents = new Vector3(maxExt, bounds.extents.y, maxExt);
        }

        private void SetupCam()
        {
            if (camToDrawWith == null)
            {
                camToDrawWith = GetComponentInChildren<Camera>();
                if (camToDrawWith == null)
                {
                    GameObject camObj = new GameObject("GroundBakerCam");
                    camObj.transform.SetParent(transform);
                    camToDrawWith = camObj.AddComponent<Camera>();
                }
            }

            var camData = camToDrawWith.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (camData == null)
            {
                camData = camToDrawWith.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            }
            camData.renderPostProcessing = false;
            camData.renderShadows = false;

            camToDrawWith.orthographic = true;
            camToDrawWith.cullingMask = terrainLayer;
            camToDrawWith.clearFlags = CameraClearFlags.Color;
            camToDrawWith.backgroundColor = new Color(0.15f, 0.35f, 0.2f, 1f);
            camToDrawWith.nearClipPlane = 0.1f;

            if (bakeMode == MapBakeMode.FollowTarget && followTarget != null)
            {
                camToDrawWith.orthographicSize = followRadius;
                camToDrawWith.farClipPlane = cameraHeightAboveTarget + 40f;
                camToDrawWith.transform.position = new Vector3(followTarget.position.x, followTarget.position.y + cameraHeightAboveTarget, followTarget.position.z);
            }
            else
            {
                float maxExt = Mathf.Max(bounds.extents.x, bounds.extents.z);
                camToDrawWith.orthographicSize = maxExt;
                camToDrawWith.farClipPlane = (bounds.max.y - bounds.min.y) + 200f;
                camToDrawWith.transform.position = new Vector3(bounds.center.x, bounds.max.y + 50f, bounds.center.z);
            }

            camToDrawWith.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camToDrawWith.enabled = false;
        }

        [ContextMenu("Bake Ground Map")]
        public void DrawDiffuseMap()
        {
            if (camToDrawWith == null) SetupCam();

            if (_tempTex == null || _tempTex.width != resolution || !_tempTex.IsCreated())
            {
                ReleaseTexture();
                var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.ARGBHalf, 24);
                desc.sRGB = false;
                _tempTex = new RenderTexture(desc);
                _tempTex.name = "ModernGrass_TerrainDiffuse";
                _tempTex.hideFlags = HideFlags.DontSave;
                _tempTex.wrapMode = TextureWrapMode.Clamp;
                _tempTex.filterMode = FilterMode.Bilinear;
                _tempTex.Create();
            }

            if (bakeMode == MapBakeMode.FollowTarget && followTarget != null)
            {
                orthographicSize = followRadius;
                camToDrawWith.orthographicSize = followRadius;

                // Texel grid snapping prevents texture shimmering/swimming when the camera follows the target
                float texelSize = (orthographicSize * 2.0f) / resolution;
                Vector3 targetPos = followTarget.position;
                float snappedX = Mathf.Floor(targetPos.x / texelSize) * texelSize;
                float snappedZ = Mathf.Floor(targetPos.z / texelSize) * texelSize;
                float camY = targetPos.y + cameraHeightAboveTarget;

                camToDrawWith.transform.position = new Vector3(snappedX, camY, snappedZ);
                camToDrawWith.farClipPlane = cameraHeightAboveTarget + 40f;
                _lastBakePos = targetPos;
            }
            else
            {
                orthographicSize = camToDrawWith.orthographicSize;
            }

            camToDrawWith.targetTexture = _tempTex;
            camToDrawWith.depthTextureMode = DepthTextureMode.Depth;

            orthographicPos = camToDrawWith.transform.position;

            // Auto-detect Ground layer if terrainLayer was left at ~0 (Everything)
            if (terrainLayer == ~0)
            {
                int gLayer = LayerMask.NameToLayer("Ground");
                if (gLayer != -1)
                {
                    terrainLayer = 1 << gLayer;
                    camToDrawWith.cullingMask = terrainLayer;
                }
            }

            // Auto-hide followTarget and Player renderers during the bake pass so their colors never bleed into the ground map
            var hiddenRenderers = new List<Renderer>();
            if (followTarget != null)
            {
                var targetRends = followTarget.GetComponentsInChildren<Renderer>();
                foreach (var r in targetRends)
                {
                    if (r != null && r.enabled)
                    {
                        r.enabled = false;
                        hiddenRenderers.Add(r);
                    }
                }
            }

            var playerObjects = GameObject.FindGameObjectsWithTag("Player");
            foreach (var pGo in playerObjects)
            {
                var pRends = pGo.GetComponentsInChildren<Renderer>();
                foreach (var r in pRends)
                {
                    if (r != null && r.enabled && !hiddenRenderers.Contains(r))
                    {
                        r.enabled = false;
                        hiddenRenderers.Add(r);
                    }
                }
            }

            Shader.SetGlobalFloat("_OrthographicCamSizeTerrain", orthographicSize);
            Shader.SetGlobalVector("_OrthographicCamPosTerrain", orthographicPos);

            try
            {
                // Render top-down camera (OnBeginCameraRendering / OnEndCameraRendering automatically bypasses fog safely)
                camToDrawWith.Render();
            }
            finally
            {
                foreach (var r in hiddenRenderers)
                {
                    if (r != null) r.enabled = true;
                }
            }

            Shader.SetGlobalTexture("_TerrainDiffuse", _tempTex);
            camToDrawWith.targetTexture = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.4f);
            if (bakeMode == MapBakeMode.FollowTarget && followTarget != null)
            {
                Gizmos.DrawWireCube(new Vector3(followTarget.position.x, followTarget.position.y, followTarget.position.z),
                                    new Vector3(followRadius * 2f, cameraHeightAboveTarget * 2f, followRadius * 2f));
            }
            else
            {
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }
}
