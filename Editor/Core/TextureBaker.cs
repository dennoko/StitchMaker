using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>デカールメッシュを UV 空間へ描画し、パディングを掛け、PNG を書き出す。</summary>
    public sealed class TextureBaker : IDisposable
    {
        const string ShaderName = "Hidden/dennokoworks/StitchMaker/Bake";
        const int DecalPass = 0, IslandMaskPass = 1, DilatePass = 2;

        static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        static readonly int ValidTexId = Shader.PropertyToID("_ValidTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        static readonly int NormalScaleId = Shader.PropertyToID("_NormalScale");

        /// <summary>ARGB32 sRGB。RGB = 糸色、A = 被覆率。</summary>
        public RenderTexture Color { get; private set; }
        /// <summary>ARGB32 Linear。接空間法線（OpenGL、+Y）。</summary>
        public RenderTexture Normal { get; private set; }
        /// <summary>R8 Linear。</summary>
        public RenderTexture Height { get; private set; }

        RenderTexture colorTemp, normalTemp, heightTemp;
        RenderTexture valid, validTemp;
        RenderTexture depth;

        readonly Material material;
        readonly Mesh decalMesh;
        readonly List<StitchSample> samples = new List<StitchSample>();
        readonly RenderBuffer[] colorBuffers = new RenderBuffer[3];

        public TextureBaker()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException("シェーダー " + ShaderName + " が見つかりません。");
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            decalMesh = new Mesh { name = "StitchMaker Decal", hideFlags = HideFlags.HideAndDontSave };
        }

        public void Dispose()
        {
            ReleaseTargets();
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(decalMesh);
        }

        /// <summary>サンプリングからパディングまでを全パスについて実行する。</summary>
        public void Render(MeshSnapshot snapshot, StitchDocument document)
        {
            EnsureTargets(document.export.resolution);
            var previous = RenderTexture.active;

            Clear(Color, new UnityEngine.Color(0f, 0f, 0f, 0f));
            Clear(Normal, new UnityEngine.Color(0.5f, 0.5f, 1f, 1f));
            Clear(Height, UnityEngine.Color.black);

            colorBuffers[0] = Color.colorBuffer;
            colorBuffers[1] = Normal.colorBuffer;
            colorBuffers[2] = Height.colorBuffer;
            Graphics.SetRenderTarget(colorBuffers, depth.depthBuffer);
            GL.Clear(true, false, UnityEngine.Color.clear, 1f);

            for (int i = 0; i < document.paths.Count; i++)
            {
                var path = document.paths[i];
                if (!path.visible || path.mask == null || path.nodes.Count < 2) continue;

                PathSampler.Sample(path, samples, i, snapshot);
                if (samples.Count == 0) continue;
                DecalMeshBuilder.Build(snapshot, path.settings, samples, decalMesh);

                var settings = path.settings;
                float k = settings.normalStrength;
                float aspect = settings.width / Mathf.Max(settings.length, 1e-6f);
                material.SetTexture(MaskTexId, path.mask);
                material.SetColor(ColorId, settings.threadColor);
                material.SetFloat(HeightScaleId, settings.heightScale);
                material.SetVector(NormalScaleId, new Vector4(k * aspect, k, 0f, 0f));
                material.SetPass(DecalPass);
                Graphics.DrawMeshNow(decalMesh, Matrix4x4.identity);
            }

            Clear(valid, UnityEngine.Color.black);
            material.SetPass(IslandMaskPass);
            Graphics.DrawMeshNow(snapshot.Mesh, Matrix4x4.identity);

            int padding = Mathf.Clamp(document.export.padding, 0, 16);
            for (int i = 0; i < padding; i++)
            {
                material.SetTexture(ValidTexId, valid);
                Color = Dilate(Color, ref colorTemp);
                Normal = Dilate(Normal, ref normalTemp);
                Height = Dilate(Height, ref heightTemp);
                valid = Dilate(valid, ref validTemp);
            }

            RenderTexture.active = previous;
        }

        /// <summary>現在の RenderTexture を PNG として書き出す。新規ファイルにのみインポート設定を適用する。</summary>
        public void Export(ExportSettings export, string baseName)
        {
            if (Color == null) return;
            Directory.CreateDirectory(export.folder);

            string folder = export.folder.TrimEnd('/', '\\');
            string suffix = GetUniqueSuffix(export, folder, baseName);
            string prefix = folder + "/" + baseName + "_Stitch_";

            if (export.color)
            {
                var texture = Read(Color, TextureFormat.RGBA32, false);
                Write(prefix + "Color" + suffix + ".png", texture, importer =>
                {
                    importer.sRGBTexture = true;
                    importer.alphaIsTransparency = true;
                });
            }
            if (export.normal)
            {
                var texture = Read(Normal, TextureFormat.RGB24, true);
                Write(prefix + "Normal" + suffix + ".png", texture, importer => importer.textureType = TextureImporterType.NormalMap);
            }
            if (export.height)
            {
                var texture = ReadGray(Height, false);
                Write(prefix + "Height" + suffix + ".png", texture, importer => importer.sRGBTexture = false);
            }
            if (export.mask)
            {
                var texture = ReadGray(Color, true);
                Write(prefix + "Mask" + suffix + ".png", texture, importer => importer.sRGBTexture = false);
            }
        }

        static string GetUniqueSuffix(ExportSettings export, string folder, string baseName)
        {
            if (export.overwrite) return "";

            bool Exists(int n)
            {
                string s = n == 0 ? "" : " " + n;
                if (export.color && File.Exists(Path.Combine(folder, baseName + "_Stitch_Color" + s + ".png"))) return true;
                if (export.normal && File.Exists(Path.Combine(folder, baseName + "_Stitch_Normal" + s + ".png"))) return true;
                if (export.height && File.Exists(Path.Combine(folder, baseName + "_Stitch_Height" + s + ".png"))) return true;
                if (export.mask && File.Exists(Path.Combine(folder, baseName + "_Stitch_Mask" + s + ".png"))) return true;
                return false;
            }

            if (!Exists(0)) return "";

            int count = 1;
            while (Exists(count))
            {
                count++;
            }
            return " " + count;
        }

        void EnsureTargets(int resolution)
        {
            resolution = Mathf.Clamp(resolution, 16, 8192);
            if (Color != null && Color.width == resolution && Color.IsCreated()) return;
            ReleaseTargets();

            Color = Create(resolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            colorTemp = Create(resolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Normal = Create(resolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            normalTemp = Create(resolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Height = Create(resolution, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            heightTemp = Create(resolution, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            valid = Create(resolution, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            validTemp = Create(resolution, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            depth = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.Depth)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            depth.Create();
        }

        void ReleaseTargets()
        {
            Release(Color);
            Release(colorTemp);
            Release(Normal);
            Release(normalTemp);
            Release(Height);
            Release(heightTemp);
            Release(valid);
            Release(validTemp);
            Release(depth);
            Color = colorTemp = Normal = normalTemp = Height = heightTemp = valid = validTemp = depth = null;
        }

        // source を temp へ 1px 拡張して描き、役割を入れ替える。
        RenderTexture Dilate(RenderTexture source, ref RenderTexture temp)
        {
            Graphics.Blit(source, temp, material, DilatePass);
            var result = temp;
            temp = source;
            return result;
        }

        static RenderTexture Create(int resolution, RenderTextureFormat format, RenderTextureReadWrite readWrite)
        {
            var texture = new RenderTexture(resolution, resolution, 0, format, readWrite)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
            };
            texture.Create();
            return texture;
        }

        static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        static void Clear(RenderTexture target, UnityEngine.Color color)
        {
            Graphics.SetRenderTarget(target);
            GL.Clear(false, true, color);
        }

        static Texture2D Read(RenderTexture source, TextureFormat format, bool linear)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = source;
            var texture = new Texture2D(source.width, source.height, format, false, linear);
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            return texture;
        }

        // 1 チャンネル（R または A）をグレースケールの RGB24 として読む。
        static Texture2D ReadGray(RenderTexture source, bool fromAlpha)
        {
            var rgba = Read(source, TextureFormat.RGBA32, true);
            var pixels = rgba.GetPixels32();
            UnityEngine.Object.DestroyImmediate(rgba);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte value = fromAlpha ? pixels[i].a : pixels[i].r;
                pixels[i] = new Color32(value, value, value, 255);
            }
            var gray = new Texture2D(source.width, source.height, TextureFormat.RGB24, false, true);
            gray.SetPixels32(pixels);
            gray.Apply();
            return gray;
        }

        static void Write(string path, Texture2D texture, Action<TextureImporter> setup)
        {
            var png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);

            bool isNew = !File.Exists(path);
            File.WriteAllBytes(path, png);
            if (!path.Replace('\\', '/').StartsWith("Assets/")) return;

            AssetDatabase.ImportAsset(path);
            if (isNew && AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                setup(importer);
                importer.SaveAndReimport();
            }
        }
    }
}
