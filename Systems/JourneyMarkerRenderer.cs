using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkylinesMaps.Systems
{
    internal sealed class JourneyMarkerRenderer : IDisposable
    {
        private const int kSegments = 40;

        private const float kFillShare = 0.72f;

        private const int kFillSubMesh = 0;

        private const int kRingSubMesh = 1;

        private readonly Material m_RingMaterial;
        private readonly Material m_StartMaterial;
        private readonly Material m_EndMaterial;
        private Mesh m_Mesh;
        private bool m_LoggedFailure;

        public bool Supported { get; }

        public bool Visible { get; set; }

        public Vector3 StartPosition { get; set; }

        public Vector3 EndPosition { get; set; }

        public float Diameter { get; set; } = 10f;

        public float Lift { get; set; } = 0.05f;

        public JourneyMarkerRenderer(Color ring, Color start, Color end)
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                Mod.log.Warn("HDRP/Unlit shader not found, so journey markers cannot be drawn.");
                return;
            }

            m_RingMaterial = CreateMaterial(shader, ring, "ring");
            m_StartMaterial = CreateMaterial(shader, start, "start");
            m_EndMaterial = CreateMaterial(shader, end, "end");
            m_Mesh = CreateMarkerMesh();

            Supported = true;
            RenderPipelineManager.beginContextRendering += Render;
        }

        private static Material CreateMaterial(Shader shader, Color color, string name)
        {
            Material material = new Material(shader);
            material.name = $"SkylinesMaps journey marker {name}";
            material.color = color;
            material.SetFloat("_DoubleSidedEnable", 1f);
            material.SetFloat("_CullMode", (float)CullMode.Off);
            material.SetFloat("_CullModeForward", (float)CullMode.Off);
            return material;
        }

        private static Mesh CreateMarkerMesh()
        {
            Vector3[] vertices = new Vector3[1 + kSegments * 3];
            Vector3[] normals = new Vector3[vertices.Length];
            int[] fill = new int[kSegments * 3];
            int[] ring = new int[kSegments * 6];

            vertices[0] = Vector3.zero;
            for (int i = 0; i < kSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / kSegments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[1 + i] = direction * (0.5f * kFillShare);
                vertices[1 + kSegments + i] = direction * (0.5f * kFillShare);
                vertices[1 + kSegments * 2 + i] = direction * 0.5f;
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                normals[i] = Vector3.up;
            }

            for (int i = 0; i < kSegments; i++)
            {
                int next = (i + 1) % kSegments;

                fill[i * 3] = 0;
                fill[i * 3 + 1] = 1 + next;
                fill[i * 3 + 2] = 1 + i;

                int innerA = 1 + kSegments + i;
                int innerB = 1 + kSegments + next;
                int outerA = 1 + kSegments * 2 + i;
                int outerB = 1 + kSegments * 2 + next;

                ring[i * 6] = innerA;
                ring[i * 6 + 1] = innerB;
                ring[i * 6 + 2] = outerA;
                ring[i * 6 + 3] = outerA;
                ring[i * 6 + 4] = innerB;
                ring[i * 6 + 5] = outerB;
            }

            Mesh mesh = new Mesh();
            mesh.name = "SkylinesMaps journey marker";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(fill, kFillSubMesh);
            mesh.SetTriangles(ring, kRingSubMesh);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!Visible || m_Mesh == null)
            {
                return;
            }

            try
            {
                Vector3 scale = new Vector3(Diameter, 1f, Diameter);
                Matrix4x4 start = Matrix4x4.TRS(StartPosition + Vector3.up * Lift, Quaternion.identity, scale);
                Matrix4x4 end = Matrix4x4.TRS(EndPosition + Vector3.up * Lift, Quaternion.identity, scale);

                foreach (Camera camera in cameras)
                {
                    if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                    {
                        continue;
                    }

                    Graphics.DrawMesh(m_Mesh, start, m_StartMaterial, 0, camera, kFillSubMesh, null, castShadows: false, receiveShadows: false);
                    Graphics.DrawMesh(m_Mesh, start, m_RingMaterial, 0, camera, kRingSubMesh, null, castShadows: false, receiveShadows: false);
                    Graphics.DrawMesh(m_Mesh, end, m_EndMaterial, 0, camera, kFillSubMesh, null, castShadows: false, receiveShadows: false);
                    Graphics.DrawMesh(m_Mesh, end, m_RingMaterial, 0, camera, kRingSubMesh, null, castShadows: false, receiveShadows: false);
                }
            }
            catch (Exception e)
            {
                if (!m_LoggedFailure)
                {
                    m_LoggedFailure = true;
                    Mod.log.Error($"Drawing journey markers failed: {e}");
                }
            }
        }

        public void Dispose()
        {
            if (Supported)
            {
                RenderPipelineManager.beginContextRendering -= Render;
            }

            DestroyObject(m_RingMaterial);
            DestroyObject(m_StartMaterial);
            DestroyObject(m_EndMaterial);
            DestroyObject(m_Mesh);
            m_Mesh = null;
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (target != null)
            {
                UnityEngine.Object.Destroy(target);
            }
        }
    }
}
