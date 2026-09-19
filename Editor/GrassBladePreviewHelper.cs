using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ModernGrassTool.Editor
{
    public class GrassBladePreviewHelper
    {
        private PreviewRenderUtility _previewUtility;
        private Mesh _previewMesh;
        private Mesh _wireframeMesh;
        private Material _previewMaterial;
        private Material _wireframeMaterial;
        private Mesh _groundDiscMesh;
        private Material _groundMaterial;

        public Vector2 dragAngles = new Vector2(35f, -12f);
        public float zoomDistance = 1.7f;
        public bool showWireframe = true;

        public void Cleanup()
        {
            if (_previewUtility != null)
            {
                _previewUtility.Cleanup();
                _previewUtility = null;
            }
            if (_previewMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewMesh);
                _previewMesh = null;
            }
            if (_wireframeMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_wireframeMesh);
                _wireframeMesh = null;
            }
            if (_previewMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewMaterial);
                _previewMaterial = null;
            }
            if (_wireframeMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_wireframeMaterial);
                _wireframeMaterial = null;
            }
            if (_groundDiscMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_groundDiscMesh);
                _groundDiscMesh = null;
            }
            if (_groundMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_groundMaterial);
                _groundMaterial = null;
            }
        }

        private void InitUtility()
        {
            if (_previewUtility == null)
            {
                _previewUtility = new PreviewRenderUtility();
                _previewUtility.camera.fieldOfView = 28f;
                _previewUtility.camera.nearClipPlane = 0.02f;
                _previewUtility.camera.farClipPlane = 20f;
                _previewUtility.camera.clearFlags = CameraClearFlags.Color;
                _previewUtility.camera.backgroundColor = new Color(0.12f, 0.14f, 0.16f, 1.0f);

                if (_previewUtility.lights.Length > 0)
                {
                    _previewUtility.lights[0].intensity = 1.3f;
                    _previewUtility.lights[0].transform.rotation = Quaternion.Euler(42f, 35f, 0f);
                    _previewUtility.lights[0].color = new Color(1.0f, 0.98f, 0.93f);
                }
                if (_previewUtility.lights.Length > 1)
                {
                    _previewUtility.lights[1].intensity = 0.5f;
                    _previewUtility.lights[1].transform.rotation = Quaternion.Euler(-30f, -140f, 0f);
                    _previewUtility.lights[1].color = new Color(0.6f, 0.75f, 0.9f);
                }

                _previewUtility.ambientColor = new Color(0.35f, 0.38f, 0.42f);
            }
        }

        private void EnsureMaterials(Shader grassShader)
        {
            Shader vertexColorShader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (_previewMaterial == null && vertexColorShader != null)
            {
                _previewMaterial = new Material(vertexColorShader)
                {
                    hideFlags = HideFlags.DontSave
                };
            }

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (_groundMaterial == null && unlit != null)
            {
                _groundMaterial = new Material(unlit)
                {
                    hideFlags = HideFlags.DontSave,
                    color = new Color(0.18f, 0.22f, 0.25f, 0.8f)
                };
            }

            if (_wireframeMaterial == null && unlit != null)
            {
                _wireframeMaterial = new Material(unlit)
                {
                    hideFlags = HideFlags.DontSave,
                    color = new Color(0.2f, 1.0f, 0.7f, 0.75f)
                };
            }

            if (_groundDiscMesh == null)
            {
                _groundDiscMesh = CreateDiscMesh(0.4f, 24);
            }
        }

        private Mesh CreateDiscMesh(float radius, int segments)
        {
            Mesh m = new Mesh { name = "GrassPreviewDisc", hideFlags = HideFlags.DontSave };
            var verts = new Vector3[segments + 1];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero;

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(angle) * radius, -0.005f, Mathf.Sin(angle) * radius);
                tris[i * 3 + 0] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segments + 1;
            }

            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            return m;
        }

        public void RebuildPreviewMesh(GrassLayer layer)
        {
            if (layer == null) return;
            if (_previewMesh == null)
            {
                _previewMesh = new Mesh { name = "GrassBladePreviewMesh", hideFlags = HideFlags.DontSave };
            }
            if (_wireframeMesh == null)
            {
                _wireframeMesh = new Mesh { name = "GrassBladeWireframeMesh", hideFlags = HideFlags.DontSave };
            }

            int numSegs = Mathf.Clamp(layer.bladeSegments, 1, 5);
            float bladeHeight = layer.defaultHeight;
            float bladeWidth = layer.defaultWidth;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            Vector3 upNorm = Vector3.up;
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            for (int s = 0; s < numSegs; s++)
            {
                float tA = (float)s / numSegs;
                float tB = (float)(s + 1) / numSegs;

                // Positions along curve
                Vector3 segA = GetPreviewSegmentPos(tA, bladeHeight, layer.bladeForward, layer.bladeCurve);
                Vector3 segB = GetPreviewSegmentPos(tB, bladeHeight, layer.bladeForward, layer.bladeCurve);

                float wFactorA = GetBladeWidthFactor(tA, layer.bottomWidth, (int)layer.tipShape, layer.tipWidth, layer.bellyWidth);
                float wFactorB = GetBladeWidthFactor(tB, layer.bottomWidth, (int)layer.tipShape, layer.tipWidth, layer.bellyWidth);

                float halfWA = bladeWidth * 0.5f * wFactorA;
                float halfWB = bladeWidth * 0.5f * wFactorB;

                Vector3 p0 = segA - right * halfWA;
                Vector3 p1 = segA + right * halfWA;
                Vector3 p2;
                Vector3 p3;

                if (s == numSegs - 1 && layer.tipShape == GrassTipShape.Pointed)
                {
                    p2 = segB;
                    p3 = segB;
                }
                else
                {
                    p2 = segB - right * halfWB;
                    p3 = segB + right * halfWB;
                }

                Vector3 geomN = Vector3.Cross(p2 - p0, p1 - p0).normalized;
                if (Vector3.Dot(geomN, forward) < 0f) geomN = -geomN;
                Vector3 n = Vector3.Lerp(geomN, upNorm, layer.normalUpBlend).normalized;

                Color colA = Color.Lerp(layer.bottomColor, layer.topColor, tA);
                Color colB = Color.Lerp(layer.bottomColor, layer.topColor, tB);

                float uv_xA_L = 0.0f;
                float uv_xA_R = 1.0f;
                float uv_xB_L = (s == numSegs - 1 && layer.tipShape == GrassTipShape.Pointed) ? 0.5f : 0.0f;
                float uv_xB_R = (s == numSegs - 1 && layer.tipShape == GrassTipShape.Pointed) ? 0.5f : 1.0f;

                int baseVert = vertices.Count;

                vertices.Add(p0); normals.Add(n); uvs.Add(new Vector2(uv_xA_L, tA)); colors.Add(colA);
                vertices.Add(p2); normals.Add(n); uvs.Add(new Vector2(uv_xB_L, tB)); colors.Add(colB);
                vertices.Add(p1); normals.Add(n); uvs.Add(new Vector2(uv_xA_R, tA)); colors.Add(colA);

                triangles.Add(baseVert + 0);
                triangles.Add(baseVert + 1);
                triangles.Add(baseVert + 2);

                // Only add non-degenerate second triangle
                if (!(s == numSegs - 1 && layer.tipShape == GrassTipShape.Pointed))
                {
                    vertices.Add(p1); normals.Add(n); uvs.Add(new Vector2(uv_xA_R, tA)); colors.Add(colA);
                    vertices.Add(p2); normals.Add(n); uvs.Add(new Vector2(uv_xB_L, tB)); colors.Add(colB);
                    vertices.Add(p3); normals.Add(n); uvs.Add(new Vector2(uv_xB_R, tB)); colors.Add(colB);

                    triangles.Add(baseVert + 3);
                    triangles.Add(baseVert + 4);
                    triangles.Add(baseVert + 5);
                }
            }

            _previewMesh.Clear();
            _previewMesh.vertices = vertices.ToArray();
            _previewMesh.normals = normals.ToArray();
            _previewMesh.uv = uvs.ToArray();
            _previewMesh.colors = colors.ToArray();
            _previewMesh.triangles = triangles.ToArray();
            _previewMesh.RecalculateBounds();

            // Build wireframe lines
            var lineIndices = new List<int>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];
                lineIndices.Add(i0); lineIndices.Add(i1);
                lineIndices.Add(i1); lineIndices.Add(i2);
                lineIndices.Add(i2); lineIndices.Add(i0);
            }
            _wireframeMesh.Clear();
            _wireframeMesh.vertices = vertices.ToArray();
            _wireframeMesh.SetIndices(lineIndices.ToArray(), MeshTopology.Lines, 0);
        }

        private Vector3 GetPreviewSegmentPos(float t, float height, float forwardCurve, float curvePower)
        {
            Vector3 pos = Vector3.up * (t * height);
            float f = Mathf.Pow(Mathf.Clamp01(t), curvePower) * (forwardCurve * height);
            pos += Vector3.forward * f;
            return pos;
        }

        private float GetBladeWidthFactor(float t, float bottomW, int tipShape, float tipW, float bellyW)
        {
            float targetTipW = (tipShape == 1) ? Mathf.Max(0.05f, tipW) : 0.0f;
            float baseW = Mathf.Lerp(bottomW, targetTipW, t);
            float bellyCurve = 4.0f * t * (1.0f - t);
            float widthWithBelly = baseW + bellyCurve * (bellyW - 1.0f) * 0.4f;

            return Mathf.Max(0.0f, widthWithBelly);
        }

        public void DrawPreview(Rect rect, GrassLayer layer, Shader grassShader)
        {
            if (layer == null) return;

            InitUtility();
            EnsureMaterials(grassShader);
            RebuildPreviewMesh(layer);

            // Handle Mouse Orbit and Zoom (only if active GUI event exists)
            Event evt = Event.current;
            if (evt != null && rect.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.MouseDrag && evt.button == 0)
                {
                    dragAngles.x += evt.delta.x * 0.9f;
                    dragAngles.y = Mathf.Clamp(dragAngles.y + evt.delta.y * 0.9f, -75f, 75f);
                    evt.Use();
                    GUI.changed = true;
                }
                else if (evt.type == EventType.ScrollWheel)
                {
                    zoomDistance = Mathf.Clamp(zoomDistance + evt.delta.y * 0.12f, 0.4f, 5.0f);
                    evt.Use();
                    GUI.changed = true;
                }
            }

            // Camera positioning
            float h = Mathf.Max(0.2f, layer.defaultHeight);
            Vector3 target = new Vector3(0f, h * 0.48f, (layer.bladeForward * h) * 0.3f);
            Quaternion rot = Quaternion.Euler(-dragAngles.y, dragAngles.x, 0f);
            Vector3 camPos = target + rot * (Vector3.back * (zoomDistance * h * 1.5f));

            _previewUtility.camera.transform.position = camPos;
            _previewUtility.camera.transform.rotation = rot;

            // Setup material parameters
            if (_previewMaterial != null)
            {
                _previewMaterial.SetColor("_BaseColor", Color.white);
                _previewMaterial.SetFloat("_Translucency", layer.translucency);
                _previewMaterial.SetFloat("_EdgeHighlight", layer.edgeHighlight);
                _previewMaterial.SetFloat("_TipShape", (float)layer.tipShape);
            }

            // Only render preview during Repaint event
            if (Event.current == null || Event.current.type == EventType.Repaint)
            {
                _previewUtility.BeginPreview(rect, GUIStyle.none);

                // Draw Ground disc
                if (_groundDiscMesh != null && _groundMaterial != null)
                {
                    _previewUtility.DrawMesh(_groundDiscMesh, Matrix4x4.identity, _groundMaterial, 0);
                }

                // Draw Blade Mesh
                if (_previewMesh != null && _previewMaterial != null)
                {
                    _previewUtility.DrawMesh(_previewMesh, Matrix4x4.identity, _previewMaterial, 0);
                }

                // Draw Wireframe Lines
                if (showWireframe && _wireframeMesh != null && _wireframeMaterial != null)
                {
                    _previewUtility.DrawMesh(_wireframeMesh, Matrix4x4.identity, _wireframeMaterial, 0);
                }

                _previewUtility.camera.Render();

                Texture result = _previewUtility.EndPreview();
                if (result != null)
                {
                    GUI.DrawTexture(rect, result);
                }
            }

            // Overlay controls badge
            DrawOverlayBadge(rect, layer);
        }

        public Texture2D RenderPreviewTexture(int width, int height, GrassLayer layer, Shader grassShader)
        {
            if (layer == null) return null;

            InitUtility();
            EnsureMaterials(grassShader);
            RebuildPreviewMesh(layer);

            float h = Mathf.Max(0.2f, layer.defaultHeight);
            Vector3 target = new Vector3(0f, h * 0.48f, (layer.bladeForward * h) * 0.3f);
            Quaternion rot = Quaternion.Euler(-dragAngles.y, dragAngles.x, 0f);
            Vector3 camPos = target + rot * (Vector3.back * (zoomDistance * h * 1.5f));

            _previewUtility.camera.transform.position = camPos;
            _previewUtility.camera.transform.rotation = rot;

            if (_previewMaterial != null)
            {
                _previewMaterial.SetColor("_BaseColor", Color.white);
                _previewMaterial.SetFloat("_Translucency", layer.translucency);
                _previewMaterial.SetFloat("_EdgeHighlight", layer.edgeHighlight);
                _previewMaterial.SetFloat("_TipShape", (float)layer.tipShape);
            }

            Rect rect = new Rect(0, 0, width, height);
            _previewUtility.BeginStaticPreview(rect);

            if (_groundDiscMesh != null && _groundMaterial != null)
            {
                _previewUtility.DrawMesh(_groundDiscMesh, Matrix4x4.identity, _groundMaterial, 0);
            }
            if (_previewMesh != null && _previewMaterial != null)
            {
                _previewUtility.DrawMesh(_previewMesh, Matrix4x4.identity, _previewMaterial, 0);
            }
            if (showWireframe && _wireframeMesh != null && _wireframeMaterial != null)
            {
                _previewUtility.DrawMesh(_wireframeMesh, Matrix4x4.identity, _wireframeMaterial, 0);
            }

            _previewUtility.camera.Render();

            Texture2D tex = _previewUtility.EndStaticPreview();
            return tex;
        }

        private void DrawOverlayBadge(Rect rect, GrassLayer layer)
        {
            int numSegs = Mathf.Clamp(layer.bladeSegments, 1, 5);
            int tris = (layer.tipShape == GrassTipShape.Pointed) ? (numSegs * 2 - 1) : (numSegs * 2);
            int verts = numSegs * 6;

            string shapeName = layer.tipShape switch
            {
                GrassTipShape.Pointed => "Pointed (Sharp)",
                GrassTipShape.Blunt   => "Blunt (Flat Cut)",
                _ => "Pointed (Sharp)"
            };

            string badge = $"{shapeName} • {numSegs} Segments • {tris} Tris ({verts}v)";
            Rect badgeRect = new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 20);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(badgeRect, EditorGUIUtility.whiteTexture);
            GUI.color = Color.white;

            GUIStyle style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.85f, 1.0f, 0.85f) }
            };
            GUI.Label(badgeRect, badge, style);

            // Reset camera & Wireframe toggles inside bottom of preview
            Rect botLeft = new Rect(rect.x + 8, rect.y + rect.height - 24, 75, 18);
            if (GUI.Button(botLeft, "Reset View", EditorStyles.miniButton))
            {
                dragAngles = new Vector2(35f, -12f);
                zoomDistance = 1.7f;
            }

            Rect botRight = new Rect(rect.x + rect.width - 100, rect.y + rect.height - 24, 92, 18);
            showWireframe = GUI.Toggle(botRight, showWireframe, " Wireframe", EditorStyles.miniButton);
        }
    }
}
