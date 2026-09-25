using System;
using System.Collections.Generic;
using System.IO;
using Game.Prefabs;
using Game.Rendering;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace SkylinesMaps.Systems
{
    internal sealed class JourneyLabelRenderer : IDisposable
    {
        public const float kReferenceHeight = 1080f;

        public const float kTailHeight = 7f;

        private const float kTailHalfWidth = 7f;

        private const float kCornerRadius = 4f;

        private const float kShadowMargin = 4f;

        private const float kShadowDrop = 1.5f;

        private const float kShadowBlur = 2.5f;

        private const float kShadowAlpha = 0.3f;

        private const float kPaddingTop = 6f;

        private const float kPaddingBottom = 6f;

        private const float kPaddingLeft = 10f;

        private const float kPaddingRight = 12f;

        private const float kIconSize = 24f;

        private const float kIconGap = 6f;

        private const float kLineGap = 1f;

        private const float kBoldOffset = 0.06f;

        private const float kTextSharpness = 1.15f;

        private const int kMaxSlots = 12;

        private const int kQueuesPerSlot = 2;

        private const int kLastQueue = 3949;

        private const int kLowestQueue = 3851;

        private const float kAfterDrsTransparent = 9f;

        public sealed class Line
        {
            public string m_Text;
            public float m_Size;
        }

        public sealed class Label
        {
            public int m_Index;
            public string m_Signature;
            public Vector2 m_Size;
            public int m_Icon;
            public Color m_Background;
            public Color m_Foreground;
            public float3 m_Anchor;
            public bool m_Below;
            public bool m_Visible;
            public int m_Slot;
            internal Texture2D m_MultiplyAbove;
            internal Texture2D m_AddAbove;
            internal Texture2D m_MultiplyBelow;
            internal Texture2D m_AddBelow;
            internal float m_Resolution;
            internal float2 m_TipAbove;
            internal float2 m_TipBelow;
        }

        private readonly OverlayRenderSystem m_OverlayRenderSystem;
        private readonly Shader m_UnlitShader;
        private readonly NativeArray<byte>[] m_IconAlpha;
        private readonly int[] m_IconSizes;
        private readonly Dictionary<Texture, (int offset, int2 size)> m_AtlasOffsets = new Dictionary<Texture, (int, int2)>();
        private readonly List<Glyph> m_Glyphs = new List<Glyph>();
        private NativeList<byte> m_AtlasData;
        private NativeArray<byte> m_EmptyBytes;
        private bool m_AtlasStale = true;
        private readonly Dictionary<int, Material> m_Materials = new Dictionary<int, Material>();
        private readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();
        private readonly int m_UnlitColorId = Shader.PropertyToID("_UnlitColor");
        private readonly int m_UnlitColorMapId = Shader.PropertyToID("_UnlitColorMap");
        private readonly int m_GradientScaleId = Shader.PropertyToID("_GradientScale");
        private readonly int m_FirstQueue;
        private Mesh m_Quad;
        private bool m_LoggedFailure;

        public List<Label> Labels { get; } = new List<Label>();

        public bool Supported { get; }

        public JourneyLabelRenderer(OverlayRenderSystem overlayRenderSystem, int routeQueue, string[] iconResources)
        {
            m_OverlayRenderSystem = overlayRenderSystem;
            m_UnlitShader = Shader.Find("HDRP/Unlit");
            if (m_UnlitShader == null)
            {
                Mod.log.Warn("HDRP/Unlit shader not found, so journey labels cannot be drawn.");
                return;
            }

            m_FirstQueue = math.max(kLastQueue - kMaxSlots * kQueuesPerSlot + 1, math.min(routeQueue + 1, kLastQueue - kQueuesPerSlot + 1));
            m_FirstQueue = math.max(m_FirstQueue, kLowestQueue);
            Mod.log.Info($"Journey labels draw from render queue {m_FirstQueue} (route lines use {routeQueue}).");

            m_AtlasData = new NativeList<byte>(Allocator.Persistent);
            m_EmptyBytes = new NativeArray<byte>(1, Allocator.Persistent);
            m_IconAlpha = new NativeArray<byte>[iconResources.Length];
            m_IconSizes = new int[iconResources.Length];
            for (int i = 0; i < iconResources.Length; i++)
            {
                m_IconAlpha[i] = LoadIcon(iconResources[i], out m_IconSizes[i]);
            }

            m_Quad = CreateQuad();

            Supported = true;
            RenderPipelineManager.beginContextRendering += Render;
        }

        private static NativeArray<byte> LoadIcon(string resource, out int size)
        {
            size = 0;
            using (Stream stream = typeof(JourneyLabelRenderer).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    Mod.log.Warn($"Journey label icon {resource} is missing.");
                    return new NativeArray<byte>(1, Allocator.Persistent);
                }

                byte[] bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count <= 0)
                    {
                        break;
                    }

                    read += count;
                }

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(bytes, false);
                Color32[] pixels = texture.GetPixels32();
                size = texture.width == texture.height ? texture.width : 0;
                UnityEngine.Object.Destroy(texture);

                NativeArray<byte> alpha = new NativeArray<byte>(math.max(1, pixels.Length), Allocator.Persistent);
                for (int i = 0; i < pixels.Length; i++)
                {
                    alpha[i] = pixels[i].a;
                }

                return alpha;
            }
        }

        private static Mesh CreateQuad()
        {
            Mesh mesh = new Mesh { name = "SkylinesMaps journey label quad" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(0f, 1f, 0f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return mesh;
        }

        private struct Glyph
        {
            public float4 m_Rect;
            public float4 m_Uv;
            public float4 m_Colour;
            public float m_Bold;
            public float m_Sharpness;
            public int m_AtlasOffset;
            public int2 m_AtlasSize;
        }

        [BurstCompile]
        private struct RasterJob : IJob
        {
            public int m_Width;
            public int m_Height;
            public float2 m_Centre;
            public float2 m_Half;
            public float m_Radius;
            public float m_Drop;
            public float m_Blur;
            public float m_ShadowAlpha;
            public float2 m_Apex;
            public float m_BaseY;
            public float m_HalfTail;
            public bool m_Up;
            public float3 m_Background;
            public float4 m_IconRect;
            public int m_IconSize;
            public float4 m_IconColour;
            public float2 m_GlyphOffset;

            [ReadOnly] public NativeArray<byte> m_Icon;
            [ReadOnly] public NativeArray<Glyph> m_Glyphs;
            [ReadOnly] public NativeArray<byte> m_Atlas;

            public NativeArray<float4> m_Canvas;
            public NativeArray<Color32> m_Multiply;
            public NativeArray<Color32> m_Add;

            public void Execute()
            {
                float2 left = new float2(m_Apex.x - m_HalfTail, m_BaseY);
                float2 right = new float2(m_Apex.x + m_HalfTail, m_BaseY);
                float2 a1 = m_Up ? m_Apex : left;
                float2 b1 = m_Up ? left : m_Apex;
                float2 a2 = m_Up ? right : m_Apex;
                float2 b2 = m_Up ? m_Apex : right;
                float2 n1 = Normal(a1, b1);
                float2 n2 = Normal(a2, b2);

                for (int y = 0; y < m_Height; y++)
                {
                    for (int x = 0; x < m_Width; x++)
                    {
                        float2 p = new float2(x + 0.5f, y + 0.5f);
                        int index = y * m_Width + x;
                        float4 value = float4.zero;

                        float2 shadowPoint = p + new float2(0f, m_Drop);
                        float shadow = math.min(RoundedRect(shadowPoint), Tail(shadowPoint, a1, n1, a2, n2));
                        value = Over(value, float3.zero, m_ShadowAlpha * math.saturate(0.5f - shadow / m_Blur));

                        float shape = math.min(RoundedRect(p), Tail(p, a1, n1, a2, n2));
                        value = Over(value, m_Background, math.saturate(0.5f - shape));
                        m_Canvas[index] = value;
                    }
                }

                if (m_IconSize > 0)
                {
                    int x0 = math.max(0, (int)math.floor(m_IconRect.x));
                    int y0 = math.max(0, (int)math.floor(m_IconRect.y));
                    int x1 = math.min(m_Width, (int)math.ceil(m_IconRect.x + m_IconRect.z));
                    int y1 = math.min(m_Height, (int)math.ceil(m_IconRect.y + m_IconRect.w));
                    for (int y = y0; y < y1; y++)
                    {
                        for (int x = x0; x < x1; x++)
                        {
                            float u = (x + 0.5f - m_IconRect.x) / m_IconRect.z;
                            float v = (y + 0.5f - m_IconRect.y) / m_IconRect.w;
                            float alpha = Sample(m_Icon, 0, new int2(m_IconSize, m_IconSize), u, v);
                            int index = y * m_Width + x;
                            m_Canvas[index] = Over(m_Canvas[index], m_IconColour.xyz, alpha * m_IconColour.w);
                        }
                    }
                }

                for (int g = 0; g < m_Glyphs.Length; g++)
                {
                    Glyph glyph = m_Glyphs[g];
                    float4 rect = glyph.m_Rect + new float4(m_GlyphOffset, m_GlyphOffset);
                    int x0 = math.max(0, (int)math.floor(rect.x));
                    int y0 = math.max(0, (int)math.floor(rect.y));
                    int x1 = math.min(m_Width, (int)math.ceil(rect.z));
                    int y1 = math.min(m_Height, (int)math.ceil(rect.w));
                    float width = rect.z - rect.x;
                    float height = rect.w - rect.y;
                    for (int y = y0; y < y1; y++)
                    {
                        float v = math.lerp(glyph.m_Uv.y, glyph.m_Uv.w, (y + 0.5f - rect.y) / height);
                        for (int x = x0; x < x1; x++)
                        {
                            float u = math.lerp(glyph.m_Uv.x, glyph.m_Uv.z, (x + 0.5f - rect.x) / width);
                            float distance = Sample(m_Atlas, glyph.m_AtlasOffset, glyph.m_AtlasSize, u, v);
                            float coverage = math.saturate((distance - 0.5f + glyph.m_Bold) * glyph.m_Sharpness + 0.5f);
                            int index = y * m_Width + x;
                            m_Canvas[index] = Over(m_Canvas[index], glyph.m_Colour.xyz, coverage * glyph.m_Colour.w);
                        }
                    }
                }

                for (int i = 0; i < m_Canvas.Length; i++)
                {
                    float4 value = math.saturate(m_Canvas[i]);
                    float alpha = value.w;
                    float3 colour = alpha > 0f ? math.saturate(value.xyz / alpha) : float3.zero;
                    float3 linear = ToLinear(colour) * alpha;
                    byte keep = (byte)math.round((1f - alpha) * 255f);
                    m_Multiply[i] = new Color32(keep, keep, keep, 255);
                    m_Add[i] = new Color32(
                        (byte)math.round(linear.x * 255f),
                        (byte)math.round(linear.y * 255f),
                        (byte)math.round(linear.z * 255f),
                        255);
                }
            }

            private static float3 ToLinear(float3 colour)
            {
                float3 low = colour / 12.92f;
                float3 high = math.pow((colour + 0.055f) / 1.055f, 2.4f);
                return math.select(high, low, colour <= 0.04045f);
            }

            private static float2 Normal(float2 a, float2 b)
            {
                float2 edge = b - a;
                return math.normalize(new float2(edge.y, -edge.x));
            }

            private float RoundedRect(float2 p)
            {
                float2 q = math.abs(p - m_Centre) - (m_Half - m_Radius);
                return math.length(math.max(q, 0f)) + math.min(math.max(q.x, q.y), 0f) - m_Radius;
            }

            private float Tail(float2 p, float2 a1, float2 n1, float2 a2, float2 n2)
            {
                float sides = math.max(math.dot(p - a1, n1), math.dot(p - a2, n2));
                float cap = m_Up ? m_BaseY - p.y : p.y - m_BaseY;
                return math.max(sides, cap);
            }

            private static float4 Over(float4 current, float3 colour, float alpha)
            {
                return alpha <= 0f ? current : new float4(colour * alpha, alpha) + current * (1f - alpha);
            }

            private static float Sample(NativeArray<byte> data, int offset, int2 size, float u, float v)
            {
                float fx = math.clamp(u * size.x - 0.5f, 0f, size.x - 1f);
                float fy = math.clamp(v * size.y - 0.5f, 0f, size.y - 1f);
                int ix = (int)fx;
                int iy = (int)fy;
                int nx = math.min(ix + 1, size.x - 1);
                int ny = math.min(iy + 1, size.y - 1);
                float tx = fx - ix;
                float ty = fy - iy;
                float a = math.lerp(data[offset + iy * size.x + ix], data[offset + iy * size.x + nx], tx);
                float b = math.lerp(data[offset + ny * size.x + ix], data[offset + ny * size.x + nx], tx);
                return math.lerp(a, b, ty) / 255f;
            }
        }

        public void BeginFrame()
        {
            m_AtlasStale = true;
        }

        public void Build(Label label, IReadOnlyList<Line> lines, float resolution)
        {
            if (m_AtlasStale)
            {
                m_AtlasOffsets.Clear();
                m_AtlasData.Clear();
                m_AtlasStale = false;
            }

            TextMeshPro text = m_OverlayRenderSystem.GetTextMesh();
            text.alignment = TextAlignmentOptions.TopLeft;
            text.rectTransform.sizeDelta = new Vector2(8000f, 2000f);

            List<(TMP_TextInfo info, Vector2 size, Vector2 origin)> built = new List<(TMP_TextInfo, Vector2, Vector2)>(lines.Count);
            float textWidth = 0f;
            float textHeight = 0f;
            foreach (Line line in lines)
            {
                text.fontSize = line.m_Size * 10f * resolution;
                Vector2 preferred = text.GetPreferredValues(line.m_Text) / resolution;
                TMP_TextInfo info = CopyInfo(text.GetTextInfo(line.m_Text));
                Rect rect = text.rectTransform.rect;
                built.Add((info, preferred, new Vector2(-rect.xMin, -rect.yMax)));
                textWidth = math.max(textWidth, preferred.x);
                textHeight += preferred.y;
            }

            textHeight += kLineGap * math.max(0, lines.Count - 1);
            float width = math.ceil(kPaddingLeft + kIconSize + kIconGap + textWidth + kPaddingRight);
            float height = math.ceil(kPaddingTop + math.max(textHeight, kIconSize) + kPaddingBottom);
            label.m_Size = new Vector2(width, height);

            m_Glyphs.Clear();
            label.m_Resolution = resolution;
            float textLeft = (kPaddingLeft + kIconSize + kIconGap) * resolution;
            float cursor = (height - kPaddingTop - math.max(0f, (kIconSize - textHeight) / 2f)) * resolution;
            foreach ((TMP_TextInfo info, Vector2 size, Vector2 origin) in built)
            {
                AddGlyphs(info, new float2(textLeft + origin.x, cursor + origin.y));
                cursor -= (size.y + kLineGap) * resolution;
            }

            NativeArray<Glyph> glyphs = new NativeArray<Glyph>(m_Glyphs.Count, Allocator.TempJob);
            for (int i = 0; i < m_Glyphs.Count; i++)
            {
                glyphs[i] = m_Glyphs[i];
            }

            try
            {
                label.m_TipAbove = RenderVariant(label, glyphs, resolution, false, ref label.m_MultiplyAbove, ref label.m_AddAbove);
                label.m_TipBelow = RenderVariant(label, glyphs, resolution, true, ref label.m_MultiplyBelow, ref label.m_AddBelow);
            }
            finally
            {
                glyphs.Dispose();
            }
        }

        private void AddGlyphs(TMP_TextInfo info, float2 offset)
        {
            float baseline = 0f;
            for (int c = 0; c < info.characterCount; c++)
            {
                if (info.characterInfo[c].isVisible)
                {
                    baseline = info.characterInfo[c].baseLine;
                    break;
                }
            }

            offset = new float2(math.round(offset.x), math.round(offset.y + baseline) - baseline);
            for (int c = 0; c < info.characterCount; c++)
            {
                TMP_CharacterInfo character = info.characterInfo[c];
                if (!character.isVisible)
                {
                    continue;
                }

                int meshIndex = character.materialReferenceIndex;
                if (meshIndex < 0 || meshIndex >= info.meshInfo.Length)
                {
                    continue;
                }

                TMP_MeshInfo mesh = info.meshInfo[meshIndex];
                int vertex = character.vertexIndex;
                if (mesh.vertices == null || mesh.uvs0 == null || vertex + 3 >= mesh.vertices.Length || mesh.material == null)
                {
                    continue;
                }

                if (!TryGetAtlas(mesh.material.mainTexture, out int atlasOffset, out int2 atlasSize))
                {
                    continue;
                }

                Vector3 bl = mesh.vertices[vertex];
                Vector3 tr = mesh.vertices[vertex + 2];
                Vector2 uvBl = mesh.uvs0[vertex];
                Vector2 uvTr = mesh.uvs0[vertex + 2];
                float4 rect = new float4(offset.x + bl.x, offset.y + bl.y, offset.x + tr.x, offset.y + tr.y);
                if (rect.z <= rect.x || rect.w <= rect.y)
                {
                    continue;
                }

                float gradient = mesh.material.HasProperty(m_GradientScaleId) ? mesh.material.GetFloat(m_GradientScaleId) : 10f;
                float texelsPerPixel = math.max(1e-4f, (uvTr.x - uvBl.x) * atlasSize.x / (rect.z - rect.x));
                Color colour = character.color;

                m_Glyphs.Add(new Glyph
                {
                    m_Rect = rect,
                    m_Uv = new float4(uvBl.x, uvBl.y, uvTr.x, uvTr.y),
                    m_Colour = new float4(colour.r, colour.g, colour.b, colour.a),
                    m_Bold = (character.style & FontStyles.Bold) != 0 ? kBoldOffset : 0f,
                    m_Sharpness = 2f * gradient / texelsPerPixel * kTextSharpness,
                    m_AtlasOffset = atlasOffset,
                    m_AtlasSize = atlasSize,
                });
            }
        }

        private float2 RenderVariant(Label label, NativeArray<Glyph> glyphs, float resolution, bool below, ref Texture2D multiply, ref Texture2D add)
        {
            float margin = math.round(kShadowMargin * resolution);
            float tail = math.round(kTailHeight * resolution);
            float boxWidth = math.round(label.m_Size.x * resolution);
            float boxHeight = math.round(label.m_Size.y * resolution);
            int pixelWidth = (int)(boxWidth + margin * 2f);
            int pixelHeight = (int)(boxHeight + tail + margin * 2f);

            multiply = Prepare(multiply, pixelWidth, pixelHeight);
            add = Prepare(add, pixelWidth, pixelHeight);

            float boxLeft = margin;
            float boxBottom = margin + (below ? 0f : tail);
            float tip = below ? boxBottom + boxHeight + tail : boxBottom - tail;
            Color background = label.m_Background;
            Color foreground = label.m_Foreground;
            bool hasIcon = label.m_Icon >= 0 && label.m_Icon < m_IconAlpha.Length && m_IconSizes[label.m_Icon] > 0;
            float iconSize = kIconSize * resolution;

            NativeArray<float4> canvas = new NativeArray<float4>(pixelWidth * pixelHeight, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            try
            {
                new RasterJob
                {
                    m_Width = pixelWidth,
                    m_Height = pixelHeight,
                    m_Centre = new float2(boxLeft + boxWidth / 2f, boxBottom + boxHeight / 2f),
                    m_Half = new float2(boxWidth / 2f, boxHeight / 2f),
                    m_Radius = kCornerRadius * resolution,
                    m_Drop = kShadowDrop * resolution,
                    m_Blur = kShadowBlur * resolution,
                    m_ShadowAlpha = kShadowAlpha,
                    m_Apex = new float2(boxLeft + math.floor(boxWidth / 2f), tip),
                    m_BaseY = below ? boxBottom + boxHeight - resolution : boxBottom + resolution,
                    m_HalfTail = kTailHalfWidth * resolution,
                    m_Up = below,
                    m_Background = new float3(background.r, background.g, background.b),
                    m_IconRect = new float4(boxLeft + math.round(kPaddingLeft * resolution), boxBottom + math.round((boxHeight - iconSize) / 2f), iconSize, iconSize),
                    m_IconSize = hasIcon ? m_IconSizes[label.m_Icon] : 0,
                    m_IconColour = new float4(foreground.r, foreground.g, foreground.b, label.m_Foreground.a),
                    m_GlyphOffset = new float2(boxLeft, boxBottom),
                    m_Icon = hasIcon ? m_IconAlpha[label.m_Icon] : m_EmptyBytes,
                    m_Glyphs = glyphs,
                    m_Atlas = m_AtlasData.AsArray(),
                    m_Canvas = canvas,
                    m_Multiply = multiply.GetPixelData<Color32>(0),
                    m_Add = add.GetPixelData<Color32>(0),
                }.Run();
            }
            finally
            {
                canvas.Dispose();
            }

            multiply.Apply(false, false);
            add.Apply(false, false);
            return new float2(boxLeft + math.floor(boxWidth / 2f), tip);
        }

        private static Texture2D Prepare(Texture2D texture, int width, int height)
        {
            if (texture != null && texture.width == width && texture.height == height)
            {
                return texture;
            }

            DestroyObject(texture);
            return new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "SkylinesMaps journey label",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        private static TMP_TextInfo CopyInfo(TMP_TextInfo source)
        {
            TMP_TextInfo copy = new TMP_TextInfo();
            copy.characterCount = source.characterCount;
            copy.characterInfo = new TMP_CharacterInfo[source.characterCount];
            Array.Copy(source.characterInfo, copy.characterInfo, source.characterCount);
            copy.meshInfo = new TMP_MeshInfo[source.meshInfo.Length];
            for (int i = 0; i < source.meshInfo.Length; i++)
            {
                TMP_MeshInfo mesh = source.meshInfo[i];
                copy.meshInfo[i] = new TMP_MeshInfo
                {
                    vertices = (Vector3[])mesh.vertices?.Clone(),
                    uvs0 = (Vector2[])mesh.uvs0?.Clone(),
                    material = mesh.material,
                    vertexCount = mesh.vertexCount,
                };
            }

            return copy;
        }

        private bool TryGetAtlas(Texture texture, out int offset, out int2 size)
        {
            offset = 0;
            size = default;
            if (!(texture is Texture2D texture2D))
            {
                return false;
            }

            if (m_AtlasOffsets.TryGetValue(texture, out (int offset, int2 size) entry))
            {
                offset = entry.offset;
                size = entry.size;
                return true;
            }

            size = new int2(texture2D.width, texture2D.height);
            offset = m_AtlasData.Length;
            int count = size.x * size.y;

            if (texture2D.isReadable && texture2D.format == TextureFormat.Alpha8)
            {
                NativeArray<byte> data = texture2D.GetPixelData<byte>(0);
                if (data.Length < count)
                {
                    return false;
                }

                m_AtlasData.AddRange(data.GetSubArray(0, count));
            }
            else
            {
                Color32[] pixels;
                if (texture2D.isReadable)
                {
                    pixels = texture2D.GetPixels32();
                }
                else
                {
                    RenderTexture target = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    RenderTexture previous = RenderTexture.active;
                    Graphics.Blit(texture2D, target);
                    RenderTexture.active = target;
                    Texture2D copy = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, true);
                    copy.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0, false);
                    copy.Apply(false, false);
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    pixels = copy.GetPixels32();
                    UnityEngine.Object.Destroy(copy);
                }

                if (pixels.Length < count)
                {
                    return false;
                }

                m_AtlasData.Resize(offset + count, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < count; i++)
                {
                    m_AtlasData[offset + i] = pixels[i].a;
                }
            }

            m_AtlasOffsets[texture] = (offset, size);
            return true;
        }

        private Material GetMaterial(int slot, int layer)
        {
            int queue = math.min(kLastQueue, m_FirstQueue + math.clamp(slot, 0, kMaxSlots - 1) * kQueuesPerSlot + layer);
            if (m_Materials.TryGetValue(queue, out Material material))
            {
                return material;
            }

            bool multiply = layer == 0;
            material = new Material(m_UnlitShader) { name = "SkylinesMaps journey label" };
            HDMaterial.SetSurfaceType(material, true);
            material.SetFloat("_BlendMode", 0f);
            material.SetFloat("_EnableFogOnTransparent", 0f);
            material.SetFloat("_DoubleSidedEnable", 1f);
            material.SetFloat("_RenderQueueType", kAfterDrsTransparent);
            HDMaterial.ValidateMaterial(material);

            material.DisableKeyword("_BLENDMODE_ALPHA");
            material.DisableKeyword("_BLENDMODE_ADD");
            material.DisableKeyword("_BLENDMODE_PRE_MULTIPLY");
            material.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetFloat("_SrcBlend", (float)(multiply ? BlendMode.DstColor : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(multiply ? BlendMode.Zero : BlendMode.One));
            material.SetFloat("_AlphaSrcBlend", (float)BlendMode.Zero);
            material.SetFloat("_AlphaDstBlend", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_ZTestTransparent", (float)CompareFunction.Always);
            material.SetFloat("_ZTestDepthEqualForOpaque", (float)CompareFunction.Always);
            material.SetFloat("_CullMode", (float)CullMode.Off);
            material.SetFloat("_CullModeForward", (float)CullMode.Off);
            material.renderQueue = queue;

            m_Materials[queue] = material;
            return material;
        }

        private void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (Labels.Count == 0)
            {
                return;
            }

            try
            {
                foreach (Camera camera in cameras)
                {
                    if (camera.cameraType != CameraType.Game)
                    {
                        continue;
                    }

                    Transform transform = camera.transform;
                    Vector3 position = transform.position;
                    Vector3 forward = transform.forward;
                    Quaternion rotation = transform.rotation;
                    float perDepth = 2f * math.tan(math.radians(camera.fieldOfView) * 0.5f) / kReferenceHeight;
                    float pixelsPerUnit = camera.pixelHeight / kReferenceHeight;

                    foreach (Label label in Labels)
                    {
                        if (!label.m_Visible)
                        {
                            continue;
                        }

                        Vector3 anchor = label.m_Anchor;
                        float depth = Vector3.Dot(anchor - position, forward);
                        if (depth <= camera.nearClipPlane)
                        {
                            continue;
                        }

                        float scale = depth * perDepth;
                        Texture2D multiply = label.m_Below ? label.m_MultiplyBelow : label.m_MultiplyAbove;
                        Texture2D add = label.m_Below ? label.m_AddBelow : label.m_AddAbove;
                        if (multiply == null || add == null)
                        {
                            continue;
                        }

                        float resolution = math.max(1e-3f, label.m_Resolution);
                        float2 tip = label.m_Below ? label.m_TipBelow : label.m_TipAbove;
                        float quadLeft = -tip.x / resolution;
                        float quadBottom = -tip.y / resolution;
                        float quadWidth = multiply.width / resolution;
                        float quadHeight = multiply.height / resolution;

                        Vector3 screen = camera.WorldToScreenPoint(anchor);
                        float2 corner = new float2(screen.x, screen.y) + new float2(quadLeft, quadBottom) * pixelsPerUnit;
                        float2 snap = (math.round(corner) - corner) / pixelsPerUnit;

                        Matrix4x4 matrix = Matrix4x4.TRS(anchor, rotation, new Vector3(scale, scale, scale))
                            * Matrix4x4.Translate(new Vector3(quadLeft + snap.x, quadBottom + snap.y, 0f))
                            * Matrix4x4.Scale(new Vector3(quadWidth, quadHeight, 1f));

                        m_Properties.Clear();
                        m_Properties.SetColor(m_UnlitColorId, Color.white);
                        m_Properties.SetTexture(m_UnlitColorMapId, multiply);
                        Graphics.DrawMesh(m_Quad, matrix, GetMaterial(label.m_Slot, 0), 0, camera, 0, m_Properties, false, false);

                        m_Properties.Clear();
                        m_Properties.SetColor(m_UnlitColorId, Color.white);
                        m_Properties.SetTexture(m_UnlitColorMapId, add);
                        Graphics.DrawMesh(m_Quad, matrix, GetMaterial(label.m_Slot, 1), 0, camera, 0, m_Properties, false, false);
                    }
                }
            }
            catch (Exception e)
            {
                if (!m_LoggedFailure)
                {
                    m_LoggedFailure = true;
                    Mod.log.Error($"Drawing journey labels failed: {e}");
                }
            }
        }

        public void DestroyGeometry(Label label)
        {
            DestroyObject(label.m_MultiplyAbove);
            DestroyObject(label.m_AddAbove);
            DestroyObject(label.m_MultiplyBelow);
            DestroyObject(label.m_AddBelow);
            label.m_MultiplyAbove = null;
            label.m_AddAbove = null;
            label.m_MultiplyBelow = null;
            label.m_AddBelow = null;
            label.m_Signature = null;
        }

        public void Dispose()
        {
            if (Supported)
            {
                RenderPipelineManager.beginContextRendering -= Render;
            }

            foreach (Label label in Labels)
            {
                DestroyGeometry(label);
            }

            Labels.Clear();

            foreach (Material material in m_Materials.Values)
            {
                DestroyObject(material);
            }

            m_Materials.Clear();
            m_AtlasOffsets.Clear();
            if (m_AtlasData.IsCreated)
            {
                m_AtlasData.Dispose();
            }

            if (m_EmptyBytes.IsCreated)
            {
                m_EmptyBytes.Dispose();
            }

            if (m_IconAlpha != null)
            {
                foreach (NativeArray<byte> icon in m_IconAlpha)
                {
                    if (icon.IsCreated)
                    {
                        icon.Dispose();
                    }
                }
            }
            DestroyObject(m_Quad);
            m_Quad = null;
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
