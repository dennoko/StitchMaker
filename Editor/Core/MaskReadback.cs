using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// マスクの画素を CPU に読み戻す（穴の検出・沈み込みフィールド用）。マスク（インスタンスと版）ごとにキャッシュする。
    /// GPU で描いて読むので、インポート設定の Read/Write や圧縮形式に依存しない。大きなマスクは縮小する。
    /// </summary>
    public static class MaskReadback
    {
        const int MaxSize = 512;   // 読み戻す最大辺（px）

        public sealed class Pixels
        {
            public int width, height;
            public float[] values;    // 0-1、行 0 = テクスチャの下端
            public bool tileable;     // 左右の端に糸がある（左右に繋がる）マスク。シェーダーの判定（端の値 > 0.02）と揃える

            /// <summary>テクスチャ座標（0-1）でバイリニア補間した値。範囲外は端の値。</summary>
            public float Sample(float u, float v)
            {
                float x = Mathf.Clamp(u * width - 0.5f, 0f, width - 1.001f);
                float y = Mathf.Clamp(v * height - 0.5f, 0f, height - 1.001f);
                int x0 = (int)x, y0 = (int)y;
                float fx = x - x0, fy = y - y0;
                int i = y0 * width + x0;
                int right = x0 + 1 < width ? 1 : 0;
                int up = y0 + 1 < height ? width : 0;
                return Mathf.Lerp(Mathf.Lerp(values[i], values[i + right], fx),
                    Mathf.Lerp(values[i + up], values[i + up + right], fx), fy);
            }
        }

        static readonly Dictionary<int, (uint version, Pixels pixels)> cache = new Dictionary<int, (uint, Pixels)>();

        /// <summary>マスクの画素を返す。RenderTarget を一時的に変えるので、描画先を設定する前に呼ぶこと。</summary>
        public static Pixels Get(Texture2D mask)
        {
            if (mask == null) return null;
            int id = mask.GetInstanceID();
            if (cache.TryGetValue(id, out var cached) && cached.version == mask.updateCount) return cached.pixels;
            if (cache.Count > 32) cache.Clear();
            var pixels = Read(mask);
            cache[id] = (mask.updateCount, pixels);
            return pixels;
        }

        static Pixels Read(Texture2D mask)
        {
            float scale = Mathf.Min(1f, (float)MaxSize / Mathf.Max(mask.width, mask.height));
            int w = Mathf.Max(4, Mathf.RoundToInt(mask.width * scale));
            int h = Mathf.Max(4, Mathf.RoundToInt(mask.height * scale));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Graphics.Blit(mask, rt);
            RenderTexture.active = rt;
            var readback = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var colors = readback.GetPixels32();
            Object.DestroyImmediate(readback);

            var pixels = new Pixels { width = w, height = h, values = new float[w * h] };
            for (int i = 0; i < pixels.values.Length; i++) pixels.values[i] = colors[i].r / 255f;
            for (int y = 0; y < h && !pixels.tileable; y++)
                pixels.tileable = pixels.values[y * w] > 0.02f || pixels.values[y * w + w - 1] > 0.02f;
            return pixels;
        }
    }
}
