using System;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// ステッチの数値設定。長さの単位はメートル。JsonUtility でそのままプリセットになる。
    /// 描画では長さ・幅・間隔・位置のゆらぎに scale を掛けた Scaled* を使う（比率を保ったまま大きさを変える）。
    /// </summary>
    [Serializable]
    public class StitchSettings
    {
        public float length = 0.003f;
        public float width = 0.0008f;
        public float spacing = 0.004f;
        public float scale = 1f;
        public float angleOffset = 0f;
        public Color threadColor = Color.white;
        public float heightScale = 1f;
        public float normalStrength = 0.5f;
        public float jitterPosition = 0f;
        public float jitterAngle = 0f;
        public int seed = 0;

        public const float MinScale = 0.01f;

        public float ScaledLength => length * Scale;
        public float ScaledWidth => width * Scale;
        public float ScaledSpacing => spacing * Scale;
        public float ScaledJitterPosition => jitterPosition * Scale;
        float Scale => Mathf.Max(scale, MinScale);

        public StitchSettings Clone()
        {
            return (StitchSettings)MemberwiseClone();
        }
    }
}
