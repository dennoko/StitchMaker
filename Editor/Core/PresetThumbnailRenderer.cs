using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// プリセット一覧用のサムネイルを描く。平面上の直線パスにステッチを並べて Bake シェーダーの Decal パスで描き、
    /// Normal による簡易ライティングで 1 枚に合成する。メッシュへの投影はしない。
    /// </summary>
    public sealed class PresetThumbnailRenderer : IDisposable
    {
        public const int Width = 192, Height = 96;

        const string ShaderName = "Hidden/dennokoworks/StitchMaker/Bake";
        const int DecalPass = 0, ThumbnailPass = 3, GroovePass = 4;
        const int Supersample = 2;
        // 横に並べる目の数と、サムネイル内でステッチが占める割合。
        const int StitchCount = 5;
        const float FillX = 0.92f, FillY = 0.8f;

        static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        static readonly int NormalTexId = Shader.PropertyToID("_NormalTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        static readonly int NormalScaleId = Shader.PropertyToID("_NormalScale");
        static readonly int GrooveDepthId = Shader.PropertyToID("_GrooveDepth");
        static readonly Vector2[] Corners = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };

        readonly Material material;
        readonly Mesh mesh;
        readonly Mesh grooveMesh;
        readonly StitchPath path = new StitchPath();
        readonly List<StitchSample> samples = new List<StitchSample>();
        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<Vector3> stitchCoords = new List<Vector3>();
        readonly List<Vector3> tangentAxes = new List<Vector3>();
        readonly List<Vector3> bitangentAxes = new List<Vector3>();
        readonly List<int> indices = new List<int>();
        readonly RenderBuffer[] colorBuffers = new RenderBuffer[3];
        float pixelsPerMeter;

        public PresetThumbnailRenderer()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException("シェーダー " + ShaderName + " が見つかりません。");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mesh = new Mesh { name = "StitchMaker Thumbnail", hideFlags = HideFlags.HideAndDontSave };
            grooveMesh = new Mesh { name = "StitchMaker Thumbnail Groove", hideFlags = HideFlags.HideAndDontSave };

            // XY 平面上の直線。法線 +Z、接線 +X、従法線 +Y（= サムネイルの上方向）。
            path.nodes.Add(new StitchNode { normal = Vector3.forward });
            path.nodes.Add(new StitchNode { normal = Vector3.forward });
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(grooveMesh);
        }

        /// <summary>サムネイルを新しい Texture2D に描く（破棄は呼び出し側）。mask が null なら布地だけになる。</summary>
        public Texture2D Render(StitchSettings settings, Texture2D mask)
        {
            int w = Width * Supersample, h = Height * Supersample;
            var color = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var normal = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var height = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            var depth = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.Depth);
            var lit = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var result = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            lit.filterMode = FilterMode.Bilinear;
            var previous = RenderTexture.active;

            Clear(color, new Color(0f, 0f, 0f, 0f));
            Clear(normal, new Color(0.5f, 0.5f, 1f, 1f));
            Clear(height, Color.black);

            if (mask != null && BuildMesh(settings, w, h))
            {
                colorBuffers[0] = color.colorBuffer;
                colorBuffers[1] = normal.colorBuffer;
                colorBuffers[2] = height.colorBuffer;
                Graphics.SetRenderTarget(colorBuffers, depth.depthBuffer);
                GL.Clear(true, false, Color.clear, 1f);

                if (settings.HasGroove)
                {
                    BuildGrooveMesh(settings, h);
                    material.SetColor(ColorId, settings.threadColor);
                    material.SetFloat(GrooveDepthId, settings.grooveDepth);
                    material.SetPass(GroovePass);
                    Graphics.DrawMeshNow(grooveMesh, Matrix4x4.identity);
                }

                float k = settings.normalStrength;
                float aspect = settings.width / Mathf.Max(settings.length, 1e-6f);
                material.SetTexture(MaskTexId, mask);
                material.SetColor(ColorId, settings.threadColor);
                material.SetFloat(HeightScaleId, settings.heightScale);
                material.SetVector(NormalScaleId, new Vector4(k * aspect, k, 0f, 0f));
                material.SetPass(DecalPass);
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
            }

            material.SetTexture(NormalTexId, normal);
            Graphics.Blit(color, lit, material, ThumbnailPass);
            // 2 倍で描いたものをバイリニアで縮小し、輪郭のジャギーを抑える。
            Graphics.Blit(lit, result);

            RenderTexture.active = result;
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply(false, true);
            RenderTexture.active = previous;

            RenderTexture.ReleaseTemporary(color);
            RenderTexture.ReleaseTemporary(normal);
            RenderTexture.ReleaseTemporary(height);
            RenderTexture.ReleaseTemporary(depth);
            RenderTexture.ReleaseTemporary(lit);
            RenderTexture.ReleaseTemporary(result);
            return texture;
        }

        // 間隔を基準に StitchCount 目が横に収まる縮尺を決め、ステッチごとに四角形を 1 枚置く。
        bool BuildMesh(StitchSettings settings, int w, int h)
        {
            float length = Mathf.Max(settings.ScaledLength, 1e-6f);
            float width = Mathf.Max(settings.ScaledWidth, 1e-6f);
            float spacing = Mathf.Max(settings.ScaledSpacing, 1e-5f);

            // 角度オフセットで回したときの 1 目の外接矩形（ゆらぎの分も含める）。
            float angle = settings.angleOffset * Mathf.Deg2Rad;
            float cos = Mathf.Abs(Mathf.Cos(angle)), sin = Mathf.Abs(Mathf.Sin(angle));
            float jitter = settings.ScaledJitterPosition * 2f;
            float extentX = length * cos + width * sin + jitter;
            float extentY = length * sin + width * cos + jitter;
            if (settings.HasGroove) extentY = Mathf.Max(extentY, settings.ScaledGrooveWidth);

            pixelsPerMeter = Mathf.Min(
                w * FillX / (spacing * (StitchCount - 1) + extentX),
                h * FillY / extentY);
            // 目の中心を置ける範囲。端の目が切れないよう、1 目分の幅を除く。
            // 開いたパスの目数は floor(全長 / 間隔) なので、範囲に入る目数 + 1 間隔分を全長にする。
            float span = Mathf.Max(w * FillX / pixelsPerMeter - extentX, 0f);
            float total = span + spacing * 1.001f;

            path.settings = settings;
            path.nodes[0].position = new Vector3(-total * 0.5f, 0f, 0f);
            path.nodes[1].position = new Vector3(total * 0.5f, 0f, 0f);
            PathSampler.Sample(path, samples);
            if (samples.Count == 0) return false;

            positions.Clear();
            stitchCoords.Clear();
            tangentAxes.Clear();
            bitangentAxes.Clear();
            indices.Clear();

            for (int s = 0; s < samples.Count; s++)
            {
                var center = samples[s].position;
                var t = samples[s].tangent;
                var b = Vector3.Cross(samples[s].normal, t);

                int first = positions.Count;
                foreach (var corner in Corners)
                {
                    var p = center + t * ((corner.x - 0.5f) * length) + b * ((corner.y - 0.5f) * width);
                    positions.Add(new Vector3(0.5f + p.x * pixelsPerMeter / w, 0.5f + p.y * pixelsPerMeter / h, 0f));
                    stitchCoords.Add(new Vector3(corner.x, corner.y, 0f));
                    // 平面なのでメッシュ接空間 = ワールドの XYZ。
                    tangentAxes.Add(t);
                    bitangentAxes.Add(b);
                }
                indices.Add(first);
                indices.Add(first + 1);
                indices.Add(first + 2);
                indices.Add(first);
                indices.Add(first + 2);
                indices.Add(first + 3);
            }

            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, stitchCoords);
            mesh.SetUVs(1, tangentAxes);
            mesh.SetUVs(2, bitangentAxes);
            mesh.SetTriangles(indices, 0, false);
            return true;
        }

        // 溝はサムネイルの左端から右端まで 1 枚の四角形で描く（BuildMesh の縮尺を使う）。
        void BuildGrooveMesh(StitchSettings settings, int h)
        {
            float half = settings.ScaledGrooveWidth * 0.5f * pixelsPerMeter / h;
            var vertices = new Vector3[4];
            var coords = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                var corner = Corners[i];
                vertices[i] = new Vector3(corner.x, 0.5f + (corner.y * 2f - 1f) * half, 0f);
                coords[i] = new Vector3(corner.x, corner.y, 0f);
            }
            grooveMesh.Clear();
            grooveMesh.vertices = vertices;
            grooveMesh.SetUVs(0, coords);
            grooveMesh.SetUVs(1, new[] { Vector3.right, Vector3.right, Vector3.right, Vector3.right });
            grooveMesh.SetUVs(2, new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            grooveMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, false);
        }

        static void Clear(RenderTexture target, Color color)
        {
            Graphics.SetRenderTarget(target);
            GL.Clear(false, true, color);
        }
    }
}
