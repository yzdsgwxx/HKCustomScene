using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 把本房间所有地形 MeshRenderer 的材质换成 tk2d 的 BlendVertexColor + 一张黑图，
    /// 这样地形就会像原版一样"参与地图、但不被相机直接画出来"。
    ///
    /// 挂在地形 mesh 所在的物体上（可以只挂在父物体，子物体递归生效）。
    /// </summary>
    public class SceneMapPatcher : MonoBehaviour
    {
        /// <summary>给一张纯黑 Texture（Unity 里随便建一张 4x4 黑的即可）。</summary>
        public Texture tex;

        private Material _material;

        public void Start()
        {
            if (_material == null)
            {
                Shader shader = Shader.Find("tk2d/BlendVertexColor");
                if (shader == null)
                {
                    Debug.LogError("[HKCS] 找不到 shader tk2d/BlendVertexColor");
                    return;
                }
                _material = new Material(shader);
                if (tex != null) _material.SetTexture(Shader.PropertyToID("_MainTex"), tex);
            }

            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(false))
            {
                r.material = _material;
            }
        }
    }
}
