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

    [Serializable]
    public class StitchPath
    {
        public string name = "Path";
        public bool visible = true;
        public bool closed;
        public bool manualHandles;
        public Texture2D mask;
        public StitchSettings settings = new StitchSettings();
        public List<StitchNode> nodes = new List<StitchNode>();
    }
}
