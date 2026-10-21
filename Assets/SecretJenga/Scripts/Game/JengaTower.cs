using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Server-side tower bookkeeping: builds the tower (3 blocks per layer, alternating 90°), tracks logical layers,
    /// enforces the "no blocks from the top layers" rule, computes placement slots on top and checks the target height.
    /// Clients get the replicated summary (layer count, top fill, lowest forbidden layer) for HUD and hover feedback.
    /// </summary>
    public class JengaTower : NetworkBehaviour
    {
        public readonly NetworkVariable<int> LayerCount = new NetworkVariable<int>();
        public readonly NetworkVariable<int> TopLayerFill = new NetworkVariable<int>();
        /// <summary>Blocks whose Layer is >= this value may not be extracted.</summary>
        public readonly NetworkVariable<int> ForbiddenFromLayer = new NetworkVariable<int>(int.MaxValue);

        public static readonly Vector3 BaseCenter = new Vector3(0f, JengaDims.TableTopY, 0f);

        readonly List<List<JengaBlock>> _layers = new List<List<JengaBlock>>();

        public IReadOnlyList<List<JengaBlock>> Layers => _layers;

        public struct ExtractInfo
        {
            public int Layer;
            public int Slot;
            public int Remaining;
            public bool RemainingIsCenterOnly;
        }

        public struct Placement
        {
            public int Layer;
            public Vector3 Center;   // center of the 3-slot row, at block center height
            public float RotY;
            public byte FreeMask;
        }

        public static Quaternion LayerRotation(int layer) => Quaternion.Euler(0f, layer % 2 == 0 ? 0f : 90f, 0f);

        public static Vector3 SlotPosition(Vector3 rowCenter, float rotY, int slot)
        {
            var lateral = Quaternion.Euler(0f, rotY, 0f) * Vector3.forward;
            return rowCenter + lateral * ((slot - 1) * JengaDims.SlotSpacing);
        }

        // ------------------------------------------------------------------ Build / clear
        public void ServerBuild(int startLayers)
        {
            if (!IsServer) return;
            ServerClear();
            var assets = SecretJengaAssets.Instance;
            if (assets.blockPrefab == null)
            {
                Debug.LogError("[SecretJenga] Block prefab missing. Run Tools > Secret Jenga > Generate Project.");
                return;
            }

            // Colored blocks only in layers that can be extracted at the start (all but the top two).
            var colorPlan = new Dictionary<int, BlockColor>();
            var candidates = new List<int>();
            for (int l = 0; l < startLayers - 2; l++)
                for (int s = 0; s < 3; s++) candidates.Add(l * 3 + s);
            Shuffle(candidates);
            int perColor = Mathf.Clamp(candidates.Count / 8, 2, 3);
            int ci = 0;
            foreach (BlockColor c in new[] { BlockColor.Red, BlockColor.Blue, BlockColor.Green, BlockColor.Gold })
                for (int k = 0; k < perColor && ci < candidates.Count; k++) colorPlan[candidates[ci++]] = c;

            for (int l = 0; l < startLayers; l++)
            {
                _layers.Add(new List<JengaBlock>());
                float rotY = l % 2 == 0 ? 0f : 90f;
                var row = new Vector3(BaseCenter.x, JengaDims.LayerCenterY(l), BaseCenter.z);
                for (int s = 0; s < 3; s++)
                {
                    var color = colorPlan.TryGetValue(l * 3 + s, out var c) ? c : BlockColor.Natural;
                    var block = Spawn(assets.blockPrefab, SlotPosition(row, rotY, s), Quaternion.Euler(0, rotY, 0), color, (byte)Random.Range(0, 3), l, s);
                    if (block != null) _layers[l].Add(block);
                }
            }
            RefreshSummary();
        }

        JengaBlock Spawn(GameObject prefab, Vector3 pos, Quaternion rot, BlockColor color, byte wood, int layer, int slot)
        {
            var go = Instantiate(prefab, pos, rot);
            var block = go.GetComponent<JengaBlock>();
            block.ServerInit(color, wood, layer, slot);
            go.GetComponent<NetworkObject>().Spawn(true);
            block.Rb.linearVelocity = Vector3.zero;
            block.Rb.angularVelocity = Vector3.zero;
            block.Rb.Sleep();
            return block;
        }

        public void ServerClear()
        {
            if (!IsServer) return;
            var all = new List<JengaBlock>(JengaBlock.All.Values);
            foreach (var b in all)
                if (b != null && b.NetworkObject != null && b.NetworkObject.IsSpawned) b.NetworkObject.Despawn(true);
            _layers.Clear();
            RefreshSummary();
        }

        public void ServerDespawnBlock(JengaBlock b)
        {
            if (b == null) return;
            foreach (var l in _layers) l.Remove(b);
            if (b.NetworkObject != null && b.NetworkObject.IsSpawned) b.NetworkObject.Despawn(true);
            RefreshSummary();
        }

        void RefreshSummary()
        {
            LayerCount.Value = _layers.Count;
            TopLayerFill.Value = _layers.Count > 0 ? _layers[_layers.Count - 1].Count : 0;
            int top = _layers.Count - 1;
            ForbiddenFromLayer.Value = top < 0 ? 0 : (_layers[top].Count >= 3 ? top : top - 1);
        }

        // ------------------------------------------------------------------ Rules
        public bool CanExtract(JengaBlock b, out string reason)
        {
            reason = null;
            if (b == null || b.Layer.Value < 0) { reason = "That block is not part of the tower."; return false; }
            if (b.Layer.Value >= ForbiddenFromLayer.Value) { reason = "You can't take blocks from the top layers."; return false; }
            return true;
        }

        /// <summary>Client-side helper using replicated values only.</summary>
        public bool IsExtractableForUI(JengaBlock b) => b != null && b.Layer.Value >= 0 && b.Layer.Value < ForbiddenFromLayer.Value;

        public ExtractInfo ServerDetach(JengaBlock b)
        {
            var info = new ExtractInfo { Layer = b.Layer.Value, Slot = b.Slot };
            if (info.Layer >= 0 && info.Layer < _layers.Count)
            {
                var layer = _layers[info.Layer];
                layer.Remove(b);
                info.Remaining = layer.Count;
                info.RemainingIsCenterOnly = layer.Count == 1 && layer[0].Slot == 1;
            }
            b.Slot = -1;
            RefreshSummary();
            return info;
        }

        public void ServerAttach(JengaBlock b, int layer, int slot)
        {
            while (_layers.Count <= layer) _layers.Add(new List<JengaBlock>());
            _layers[layer].Add(b);
            b.ServerSetLayer(layer, slot);
            RefreshSummary();
        }

        public bool IsTargetReached(int target) =>
            _layers.Count >= target && _layers[_layers.Count - 1].Count >= 3;

        public int CountExtractableColor(BlockColor c)
        {
            int n = 0;
            int forbidden = ForbiddenFromLayer.Value;
            for (int l = 0; l < _layers.Count && l < forbidden; l++)
                foreach (var b in _layers[l]) if (b.Color.Value == c) n++;
            return n;
        }

        /// <summary>Horizontal center of the tower around a given layer (average of neighbouring layers).</summary>
        public Vector3 LocalAxisCenter(int layer)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int l = Mathf.Max(0, layer - 1); l <= Mathf.Min(_layers.Count - 1, layer + 1); l++)
            {
                if (l == layer) continue;
                foreach (var b in _layers[l]) { sum += b.transform.position; n++; }
            }
            if (n == 0) return BaseCenter;
            sum /= n;
            sum.y = 0;
            return sum;
        }

        /// <summary>True when the block has been pulled far enough out of the tower to be lifted.</summary>
        public bool IsOutOfTower(JengaBlock b, int sourceLayer)
        {
            var c = LocalAxisCenter(sourceLayer);
            var p = b.transform.position;
            p.y = 0;
            float along = Mathf.Abs(Vector3.Dot(p - c, b.LongAxis));
            return along > JengaDims.BlockLength * 0.87f;
        }

        // ------------------------------------------------------------------ Placement on top
        public Placement ComputePlacement()
        {
            var pl = new Placement();
            int top = _layers.Count - 1;
            if (top < 0)
            {
                pl.Layer = 0;
                pl.RotY = 0;
                pl.Center = new Vector3(BaseCenter.x, JengaDims.LayerCenterY(0), BaseCenter.z);
                pl.FreeMask = 7;
                return pl;
            }

            var topBlocks = _layers[top];
            bool partial = topBlocks.Count < 3;
            pl.Layer = partial ? top : top + 1;
            pl.RotY = pl.Layer % 2 == 0 ? 0f : 90f;

            Vector3 RowCenterOf(List<JengaBlock> blocks, int layerIndex)
            {
                Vector3 s = Vector3.zero;
                if (blocks.Count == 0) return new Vector3(BaseCenter.x, JengaDims.LayerCenterY(layerIndex), BaseCenter.z);
                float rot = layerIndex % 2 == 0 ? 0f : 90f;
                var lateral = Quaternion.Euler(0, rot, 0) * Vector3.forward;
                foreach (var b in blocks) s += b.transform.position - lateral * ((b.Slot - 1) * JengaDims.SlotSpacing);
                return s / blocks.Count;
            }

            if (partial)
            {
                pl.Center = RowCenterOf(topBlocks, top);
                byte mask = 7;
                foreach (var b in topBlocks) if (b.Slot >= 0 && b.Slot < 3) mask &= (byte)~(1 << b.Slot);
                pl.FreeMask = mask;
            }
            else
            {
                var c = RowCenterOf(topBlocks, top);
                float maxY = float.MinValue;
                foreach (var b in topBlocks) maxY = Mathf.Max(maxY, b.transform.position.y);
                c.y = maxY + JengaDims.BlockHeight;
                pl.Center = c;
                pl.FreeMask = 7;
            }
            pl.Center.y += 0.012f; // drop from slightly above to avoid interpenetration
            return pl;
        }

        static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
