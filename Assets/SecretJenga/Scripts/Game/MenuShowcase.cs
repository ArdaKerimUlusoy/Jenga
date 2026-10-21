using System.Collections.Generic;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>Purely visual, non-networked tower (with a few gaps, mid-game) shown on the table while in menus / lobby.</summary>
    public class MenuShowcase : MonoBehaviour
    {
        public int layers = GameRules.DefaultStartLayers + 3;
        readonly List<GameObject> _blocks = new List<GameObject>();
        bool _visible = true;

        void Start()
        {
            var assets = SecretJengaAssets.Instance;
            var mesh = assets.blockMesh != null ? assets.blockMesh : ProceduralMeshes.JengaBlock();
            var mpb = new MaterialPropertyBlock();
            var rnd = new System.Random(42);
            for (int l = 0; l < layers; l++)
            {
                float rotY = l % 2 == 0 ? 0f : 90f;
                var row = new Vector3(0, JengaDims.LayerCenterY(l), 0);
                for (int s = 0; s < 3; s++)
                {
                    // leave a few gaps so it looks like a game in progress
                    if (l > 1 && l < layers - 2 && rnd.NextDouble() < 0.18 && s != 1) continue;
                    var go = new GameObject($"ShowBlock_{l}_{s}");
                    go.transform.SetParent(transform, false);
                    var pos = JengaTower.SlotPosition(row, rotY, s);
                    pos += new Vector3((float)(rnd.NextDouble() - 0.5) * 0.02f, 0, (float)(rnd.NextDouble() - 0.5) * 0.02f);
                    go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotY + (float)(rnd.NextDouble() - 0.5) * 1.2f, 0));
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    if (assets.woodMaterials != null && assets.woodMaterials.Length > 0 && assets.woodMaterials[0] != null)
                    {
                        var wood = assets.woodMaterials[rnd.Next(assets.woodMaterials.Length)];
                        mr.sharedMaterials = new[] { wood, assets.endGrainMaterial != null ? assets.endGrainMaterial : wood };
                    }
                    else mr.sharedMaterial = RuntimeArt.MakeLit(new Color(0.85f, 0.68f, 0.45f));

                    float k = 0.78f + 0.12f * (float)rnd.NextDouble();
                    mr.GetPropertyBlock(mpb);
                    mpb.SetColor("_BaseColor", new Color(k, k * 0.96f, k * 0.9f));
                    mpb.SetVector("_BaseMap_ST", new Vector4(1, 1, (float)rnd.NextDouble(), (float)rnd.NextDouble() * 0.3f));
                    mr.SetPropertyBlock(mpb);
                    _blocks.Add(go);
                }
            }
        }

        void Update()
        {
            var game = NetworkGameManager.Instance;
            bool show = game == null || !game.IsSpawned || game.Phase.Value == GamePhase.Lobby;
            if (show == _visible) return;
            _visible = show;
            foreach (var b in _blocks) if (b != null) b.SetActive(show);
        }
    }
}
