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
        const int DecalPass = 0, IslandMaskPass = 1, DilatePass = 2, GroovePass = 4;
        const int DownsampleColorPass = 5, DownsampleNormalPass = 6, DownsampleHeightPass = 7;
        // SSAA の作業解像度の上限。
        const int MaxWorkResolution = 8192;

        static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        static readonly int ValidTexId = Shader.PropertyToID("_ValidTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        static readonly int NormalScaleId = Shader.PropertyToID("_NormalScale");
        static readonly int GrooveDepthId = Shader.PropertyToID("_GrooveDepth");
        static readonly int FactorId = Shader.PropertyToID("_Factor");

        /// <summary>ARGB32 sRGB。RGB = 糸色、A = 被覆率。</summary>
        public RenderTexture Color => output?.color;
        /// <summary>ARGB32 Linear。接空間法線（OpenGL、+Y）。</summary>
        public RenderTexture Normal => output?.normal;
        /// <summary>R8 Linear。</summary>
        public RenderTexture Height => output?.height;

        // 出力解像度のターゲット。プレビューと書き出しが参照する。
        Targets output;

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
            output?.Dispose();
            output = null;
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(decalMesh);
        }

        /// <summary>
        /// 出力解像度と SSAA 倍率から、実際に使う倍率（1 / 2 / 4）を求める。
        /// 作業解像度（解像度 × 倍率）が上限を超える場合は、収まるまで倍率を半分にする。
        /// </summary>
        public static int EffectiveSsaa(int resolution, int ssaa)
        {
            int factor = ssaa >= 4 ? 4 : ssaa >= 2 ? 2 : 1;
            int max = Mathf.Min(MaxWorkResolution, SystemInfo.maxTextureSize);
            while (factor > 1 && ClampResolution(resolution) * factor > max)
                factor /= 2;
            return factor;
        }

        static int ClampResolution(int resolution) => Mathf.Clamp(resolution, 16, 8192);

        /// <summary>
        /// サンプリングからパディングまでを全パスについて実行する。
        /// ssaa > 1 のときは作業解像度で描いてから出力解像度へ縮小する（作業用のターゲットは描画後に解放する）。
        /// </summary>
        public void Render(MeshSnapshot snapshot, StitchDocument document, int ssaa)
        {
            int resolution = ClampResolution(document.export.resolution);
            if (output == null || output.resolution != resolution || !output.IsCreated)
            {
                output?.Dispose();
                output = new Targets(resolution);
            }

            int padding = Mathf.Clamp(document.export.padding, 0, 16);
            int factor = EffectiveSsaa(resolution, ssaa);
            var previous = RenderTexture.active;
            Targets work = null;
            try
            {
                if (factor == 1)
                {
                    Draw(output, snapshot, document, padding);
                    return;
                }

                // パディングも作業解像度で掛け、出力解像度での拡張幅を padding px に揃える。
                work = new Targets(resolution * factor);
                Draw(work, snapshot, document, padding * factor);
                material.SetFloat(FactorId, factor);
                Graphics.Blit(work.color, output.color, material, DownsampleColorPass);
                Graphics.Blit(work.normal, output.normal, material, DownsampleNormalPass);
                Graphics.Blit(work.height, output.height, material, DownsampleHeightPass);
            }
            finally
            {
                work?.Dispose();
                RenderTexture.active = previous;
            }
        }

        // ステッチと溝を描き、アイランドマスクを作って padding 回 Dilate する。
        void Draw(Targets t, MeshSnapshot snapshot, StitchDocument document, int padding)
        {
            Clear(t.color, new UnityEngine.Color(0f, 0f, 0f, 0f));
            Clear(t.normal, new UnityEngine.Color(0.5f, 0.5f, 1f, 1f));
            Clear(t.height, UnityEngine.Color.black);

            colorBuffers[0] = t.color.colorBuffer;
            colorBuffers[1] = t.normal.colorBuffer;
            colorBuffers[2] = t.height.colorBuffer;
            Graphics.SetRenderTarget(colorBuffers, t.depth.depthBuffer);
            GL.Clear(true, false, UnityEngine.Color.clear, 1f);

            for (int i = 0; i < document.paths.Count; i++)
            {
                var path = document.paths[i];
                if (!path.visible || path.mask == null || path.nodes.Count < 2) continue;

                var settings = path.settings;
                if (settings.HasGroove) DrawGroove(snapshot, path);

                PathSampler.Sample(path, samples, i, snapshot);
                if (samples.Count == 0) continue;
                DecalMeshBuilder.Build(snapshot, settings, samples, decalMesh);

                float k = settings.normalStrength;
                float aspect = settings.width / Mathf.Max(settings.length, 1e-6f);
                material.SetTexture(MaskTexId, path.mask);
                material.SetColor(ColorId, settings.threadColor);
                material.SetFloat(HeightScaleId, settings.heightScale);
                material.SetVector(NormalScaleId, new Vector4(k * aspect, k, 0f, 0f));
                material.SetPass(DecalPass);
                Graphics.DrawMeshNow(decalMesh, Matrix4x4.identity);
            }

            Clear(t.valid, UnityEngine.Color.black);
            material.SetPass(IslandMaskPass);
            Graphics.DrawMeshNow(snapshot.Mesh, Matrix4x4.identity);

            for (int i = 0; i < padding; i++)
            {
                material.SetTexture(ValidTexId, t.valid);
                Dilate(ref t.color, ref t.colorTemp);
                Dilate(ref t.normal, ref t.normalTemp);
                Dilate(ref t.height, ref t.heightTemp);
                Dilate(ref t.valid, ref t.validTemp);
            }
        }

        // 溝は幅の半分ごとに区切り、隣と半分ずつ重なる四角形で途切れなく覆う。
        void DrawGroove(MeshSnapshot snapshot, StitchPath path)
        {
            float width = path.settings.ScaledGrooveWidth;
            float step = width * 0.5f;
            PathSampler.SampleGroove(path, step, samples, snapshot);
            if (samples.Count == 0) return;
            DecalMeshBuilder.Build(snapshot, step * 2f, width, samples, decalMesh);

            material.SetColor(ColorId, path.settings.threadColor);
            material.SetFloat(GrooveDepthId, path.settings.grooveDepth);
            material.SetPass(GroovePass);
            Graphics.DrawMeshNow(decalMesh, Matrix4x4.identity);
        }

        /// <summary>現在の RenderTexture を PNG として書き出す。新規ファイルにのみインポート設定を適用する。</summary>
        public void Export(ExportSettings export, string baseName)
        {
            if (Color == null) return;
            Directory.CreateDirectory(export.folder);

            string folder = export.folder.TrimEnd('/', '\\');
            string suffix = GetUniqueSuffix(export, folder, baseName);
            string prefix = folder + "/" + baseName + "_Stitch_";

            // 透過出力では Color の A（被覆率）を Normal / Height のアルファにも使う（Normal は溝の分も加える）。
            Color32[] coverage = null;
            if (export.transparent && (export.normal || export.height))
            {
                var color = Read(Color, TextureFormat.RGBA32, false);
                coverage = color.GetPixels32();
                UnityEngine.Object.DestroyImmediate(color);
            }

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
                var texture = Read(Normal, coverage != null ? TextureFormat.RGBA32 : TextureFormat.RGB24, true);
                if (coverage != null)
                {
                    // 糸の無い溝も残るよう、法線が傾いている画素は傾きに応じて不透明にする（平らな画素は z = 255）。
                    var pixels = texture.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i].a = (byte)Mathf.Max(coverage[i].a, Mathf.Min(255, (255 - pixels[i].b) * 24));
                    texture.SetPixels32(pixels);
                    texture.Apply();
                }
                // Normal map タイプはアルファを透過として扱えないため、透過時は Default（リニア）で保存する。
                Write(prefix + "Normal" + suffix + ".png", texture, importer =>
                {
                    if (coverage != null)
                    {
                        importer.textureType = TextureImporterType.Default;
                        importer.sRGBTexture = false;
                        importer.alphaIsTransparency = true;
                    }
                    else
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                    }
                });
            }
            if (export.height)
            {
                var texture = ReadGray(Height, false, coverage);
                Write(prefix + "Height" + suffix + ".png", texture, importer =>
                {
                    importer.sRGBTexture = false;
                    importer.alphaIsTransparency = export.transparent;
                });
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

        // source を temp へ 1px 拡張して描き、役割を入れ替える。
        void Dilate(ref RenderTexture source, ref RenderTexture temp)
        {
            Graphics.Blit(source, temp, material, DilatePass);
            (source, temp) = (temp, source);
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
            if (!texture.Create())
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw CreateFailed(resolution);
            }
            return texture;
        }

        static Exception CreateFailed(int resolution)
        {
            return new InvalidOperationException(
                resolution + "px の RenderTexture を作成できません（VRAM 不足の可能性があります）。解像度かアンチエイリアスの倍率を下げてください。");
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

        // 1 チャンネル（R または A）をグレースケールとして読む。coverage を渡すと、その A をアルファにした RGBA32 になる。
        static Texture2D ReadGray(RenderTexture source, bool fromAlpha, Color32[] coverage = null)
        {
            var rgba = Read(source, TextureFormat.RGBA32, true);
            var pixels = rgba.GetPixels32();
            UnityEngine.Object.DestroyImmediate(rgba);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte value = fromAlpha ? pixels[i].a : pixels[i].r;
                pixels[i] = new Color32(value, value, value, coverage != null ? coverage[i].a : (byte)255);
            }
            var format = coverage != null ? TextureFormat.RGBA32 : TextureFormat.RGB24;
            var gray = new Texture2D(source.width, source.height, format, false, true);
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

        /// <summary>1 解像度分のレンダーターゲット一式（描画先と Dilate 用の一時バッファ、アイランドマスク、深度）。</summary>
        sealed class Targets : IDisposable
        {
            public readonly int resolution;
            public RenderTexture color, normal, height, valid;
            public RenderTexture colorTemp, normalTemp, heightTemp, validTemp;
            public RenderTexture depth;

            public Targets(int resolution)
            {
                this.resolution = resolution;
                try
                {
                    CreateAll();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            void CreateAll()
            {
                color = Create(resolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                colorTemp = Create(resolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                normal = Create(resolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                normalTemp = Create(resolution, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                height = Create(resolution, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
                heightTemp = Create(resolution, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
                valid = Create(resolution, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
                validTemp = Create(resolution, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
                depth = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.Depth)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (!depth.Create()) throw CreateFailed(resolution);
            }

            public bool IsCreated => color != null && color.IsCreated();

            public void Dispose()
            {
                Release(color);
                Release(colorTemp);
                Release(normal);
                Release(normalTemp);
                Release(height);
                Release(heightTemp);
                Release(valid);
                Release(validTemp);
                Release(depth);
                color = colorTemp = normal = normalTemp = height = heightTemp = valid = validTemp = depth = null;
            }
        }
    }
}
