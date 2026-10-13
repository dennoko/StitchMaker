using System;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>ステッチの数値設定。長さの単位はメートル。JsonUtility でそのままプリセットになる。</summary>
    [Serializable]
    public class StitchSettings
    {
        public float length = 0.003f;
        public float width = 0.0008f;
        public float spacing = 0.004f;
        public float angleOffset = 0f;
        public Color threadColor = Color.white;
        public float heightScale = 1f;
        public float normalStrength = 0.5f;
        public float jitterPosition = 0f;
        public float jitterAngle = 0f;
        public int seed = 0;

        public StitchSettings Clone()
        {
            return (StitchSettings)MemberwiseClone();
        }
    }
}
