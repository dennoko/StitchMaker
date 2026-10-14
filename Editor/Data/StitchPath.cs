using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>パスの制御点。すべてメッシュ空間。</summary>
    [Serializable]
    public class StitchNode
    {
        public Vector3 position;
        public Vector3 normal = Vector3.up;
        // ノード位置からのオフセット。StitchPath.manualHandles が true のときだけ使う。
        public Vector3 handleIn;
        public Vector3 handleOut;
    }

    /// <summary>ミラーの対称軸（メッシュ空間）。対称面はその軸の座標が 0 の平面。</summary>
    public enum MirrorAxis
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 3,
    }

    [Serializable]
    public class StitchPath
    {
        public string name = "Path";
        public bool visible = true;
        public bool closed;
        public bool manualHandles;
        public MirrorAxis mirrorAxis = MirrorAxis.None;
        // 鏡映した制御点を反対側の表面へ吸着させるときの探索半径（メートル）。
        public float mirrorSnapRadius = 0.03f;
        // パスが乗っている UV アイランドにだけ描く（上に重なった別パーツへの漏れを防ぐ）。
        public bool islandConstrain = true;
        public Texture2D mask;
        public StitchSettings settings = new StitchSettings();
        public List<StitchNode> nodes = new List<StitchNode>();
    }
}
