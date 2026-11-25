using System;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// ステッチの数値設定。長さの単位はメートル。JsonUtility でそのままプリセットになる。
    /// 描画では長さ・幅・間隔・位置のゆらぎ・溝の幅・沈み込みの幅に scale を掛けた Scaled* を使う（比率を保ったまま大きさを変える）。
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
        // パスに沿って布地を凹ませる溝。幅 0 で溝なし。深さは溝の壁の傾き（Normal のみに出る）。
        public float grooveWidth = 0f;
        public float grooveDepth = 0.5f;
        // 糸が革に入る穴と、糸の周りの沈み込み。既定は OFF。
        // 穴の位置はマスクから自動で決まり（HoleDetector）、holeSize は糸の太さに対する穴の大きさの倍率。
        // 深さは AO の濃さと Normal の傾きの両方を決める（Normal には normalStrength も掛かる）。
        public bool hole = false;
        public float holeSize = 1f;
        public float holeDepth = 0.5f;
        public bool sink = false;
        public float sinkWidth = 0.001f;
        public float sinkDepth = 0.3f;

        public const float MinScale = 0.01f;

        public float ScaledLength => length * Scale;
        public float ScaledWidth => width * Scale;
        public float ScaledSpacing => spacing * Scale;
        public float ScaledJitterPosition => jitterPosition * Scale;
        public float ScaledGrooveWidth => grooveWidth * Scale;
        public float ScaledSinkWidth => sinkWidth * Scale;
        public bool HasGroove => grooveWidth > 1e-6f && grooveDepth > 1e-6f;
        public bool HasSink => sink && sinkWidth > 1e-6f && sinkDepth > 1e-6f;
        public bool HasHole => hole && holeSize > 1e-6f && holeDepth > 1e-6f;
        float Scale => Mathf.Max(scale, MinScale);

        public StitchSettings Clone()
        {
            return (StitchSettings)MemberwiseClone();
        }

        /// <summary>
        /// プリセットの JSON で上書きする。先に既定値へ戻すので、JSON に無い項目（古いプリセットの scale や溝など）は既定値になる。
        /// </summary>
        public void OverwriteFromPreset(string json)
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new StitchSettings()), this);
            JsonUtility.FromJsonOverwrite(json, this);
        }
    }
}
