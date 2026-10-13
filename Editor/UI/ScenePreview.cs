using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    public enum PreviewMode
    {
        Combined,
        Normal,
        Height,
    }

    /// <summary>
    /// ベイク結果を対象メッシュの上に重ね描きする。対象の Renderer・マテリアルには触れない。
    /// </summary>
    public static class ScenePreview
    {
        const string ShaderName = "Hidden/dennokoworks/StitchMaker/Preview";

        static readonly int ColorTexId = Shader.PropertyToID("_ColorTex");
        static readonly int NormalTexId = Shader.PropertyToID("_NormalTex");
        static readonly int HeightTexId = Shader.PropertyToID("_HeightTex");
        static readonly int ModeId = Shader.PropertyToID("_Mode");
        static readonly int CameraPositionId = Shader.PropertyToID("_CameraPosition");

        static Material material;

        /// <summary>シーンビューの Repaint イベント中に呼ぶ。</summary>
        public static void Draw(MeshSnapshot snapshot, TextureBaker baker, Matrix4x4 meshToWorld, PreviewMode mode)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (baker.Color == null) return;
            var camera = Camera.current;
            if (camera == null) return;

            if (material == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null) return;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            material.SetTexture(ColorTexId, baker.Color);
            material.SetTexture(NormalTexId, baker.Normal);
            material.SetTexture(HeightTexId, baker.Height);
            material.SetFloat(ModeId, (float)mode);
            material.SetVector(CameraPositionId, camera.transform.position);
            material.SetPass(0);
            Graphics.DrawMeshNow(snapshot.Mesh, meshToWorld);
        }
    }
}
