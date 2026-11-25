using System;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// 2 値画像の距離変換（Felzenszwalb の 2 乗距離変換）。種の画素（seeds = true）までのユークリッド距離を返す。
    /// 1 画素の横・縦の大きさを別々に指定できる（実寸の縦横比が違うキャンバス用）。
    /// </summary>
    public static class DistanceField
    {
        const double Inf = 1e20;

        /// <param name="sx">1 画素の横の大きさ。</param>
        /// <param name="sy">1 画素の縦の大きさ。</param>
        /// <returns>距離（sx・sy と同じ単位）。種が 1 つも無ければ非常に大きな値。</returns>
        public static float[] Compute(bool[] seeds, int w, int h, float sx = 1f, float sy = 1f)
        {
            var f = new double[w * h];
            for (int i = 0; i < f.Length; i++) f[i] = seeds[i] ? 0.0 : Inf;

            int size = Math.Max(w, h);
            var line = new double[size];
            var d = new double[size];
            var v = new int[size];
            var z = new double[size + 1];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) line[y] = f[y * w + x];
                Transform1D(line, h, sy, d, v, z);
                for (int y = 0; y < h; y++) f[y * w + x] = d[y];
            }
            for (int y = 0; y < h; y++)
            {
                Array.Copy(f, y * w, line, 0, w);
                Transform1D(line, w, sx, d, v, z);
                Array.Copy(d, 0, f, y * w, w);
            }

            var dist = new float[w * h];
            for (int i = 0; i < dist.Length; i++) dist[i] = (float)Math.Sqrt(f[i]);
            return dist;
        }

        // 1 次元: d[q] = min_p (f[p] + ((q − p)·s)²)。放物線の下側包絡線を求める。
        static void Transform1D(double[] f, int n, double s, double[] d, int[] v, double[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = -Inf;
            z[1] = Inf;
            for (int q = 1; q < n; q++)
            {
                double qs = q * s;
                double intersection;
                while (true)
                {
                    double ps = v[k] * s;
                    intersection = ((f[q] + qs * qs) - (f[v[k]] + ps * ps)) / (2.0 * (qs - ps));
                    if (intersection <= z[k] && k > 0) { k--; continue; }
                    break;
                }
                k++;
                v[k] = q;
                z[k] = intersection;
                z[k + 1] = Inf;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                double qs = q * s;
                while (z[k + 1] < qs) k++;
                double dq = qs - v[k] * s;
                d[q] = dq * dq + f[v[k]];
            }
        }
    }
}
