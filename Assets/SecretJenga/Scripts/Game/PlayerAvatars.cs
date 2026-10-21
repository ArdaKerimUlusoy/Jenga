using System.Collections.Generic;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Seated figures and their hands. Every connected player gets a hooded body and two articulated hands (HandRig).
    /// The local player sees only their own two hands (first person); everyone else is seen across the table.
    ///
    /// Your right hand IS your cursor (Hands Over style): it rests flat on the table and slides with the mouse inside
    /// your own reach, extends a finger to touch the block you aim at, flinches back from locked blocks, pinches the
    /// end of the block you pull and grips it from above while you carry it. With rising tension the hands tremble and
    /// the fingers start drumming. F knocks on the table. All of this is shared with the other players through
    /// HandSync, so you watch each other's hands the whole round. Purely visual - nothing here touches the physics.
    /// </summary>
    public class PlayerAvatars : MonoBehaviour
    {
        class Avatar
        {
            public GameObject Root;
            public Transform Head, Torso, Label;
            public TextMesh LabelText;
            public HandRig Right, Left;
            public int Seat = -1;
            public bool Local;
            public float KnockUntil;
        }

        readonly Dictionary<ulong, Avatar> _avatars = new Dictionary<ulong, Avatar>();
        readonly List<ulong> _remove = new List<ulong>();
        Mesh _torso, _head;
        Material _skin, _cloth, _hood;

        void Awake()
        {
            var a = SecretJengaAssets.Instance;
            _skin = a.skinMaterial != null ? a.skinMaterial : RuntimeArt.MakeLit(new Color(0.66f, 0.56f, 0.5f), 0.35f);
            _cloth = a.clothMaterial != null ? a.clothMaterial : RuntimeArt.MakeLit(new Color(0.16f, 0.15f, 0.15f), 0.1f);
            _hood = a.hoodMaterial != null ? a.hoodMaterial : RuntimeArt.MakeLit(new Color(0.11f, 0.105f, 0.11f), 0.05f);
            _torso = ProceduralMeshes.Lathe(new[]
            {
                new Vector2(0.0f, 0f), new Vector2(2.3f, 0.05f), new Vector2(2.5f, 2.5f), new Vector2(2.4f, 6.5f),
                new Vector2(2.3f, 8.8f), new Vector2(1.5f, 9.8f), new Vector2(0.75f, 10.2f), new Vector2(0f, 10.3f)
            }, 20, false, false, "TLB_Torso");
            _head = ProceduralMeshes.Lathe(new[]
            {
                new Vector2(0f, -1.7f), new Vector2(0.9f, -1.6f), new Vector2(1.45f, -0.9f), new Vector2(1.6f, 0.1f),
                new Vector2(1.45f, 1.1f), new Vector2(1.0f, 1.75f), new Vector2(0.35f, 2.15f), new Vector2(0f, 2.2f)
            }, 20, false, false, "TLB_Hood");
            HandSync.Knocked += OnKnocked;
        }

        void OnDestroy() => HandSync.Knocked -= OnKnocked;

        void OnKnocked(ulong clientId, Vector3 at)
        {
            if (_avatars.TryGetValue(clientId, out var av))
            {
                av.KnockUntil = Time.time + 0.5f;
                av.Right.SetPose(HandPoseId.Knock);
                av.Right.Knock();
            }
            var hits = SecretJengaAssets.Instance.blockHits;
            if (hits != null && hits.Length > 0)
            {
                AudioManager.Instance?.PlayAt(hits[0], at, 0.9f, 0.55f);
                AudioManager.Instance?.PlayAt(hits[hits.Length - 1], at, 0.8f, 0.5f);
            }
        }

        void LateUpdate()
        {
            var game = NetworkGameManager.Instance;
            if (game == null || !game.IsSpawned) { ClearAll(); return; }

            _remove.Clear();
            foreach (var id in _avatars.Keys)
                if (!game.TryGetPlayer(id, out var pd) || !pd.IsConnected) _remove.Add(id);
            foreach (var id in _remove) { Destroy(_avatars[id].Root); _avatars.Remove(id); }

            ulong local = game.NetworkManager.LocalClientId;
            float tension = game.Atmosphere != null ? game.Atmosphere.Tension.Value : 0f;
            foreach (var p in game.Players)
            {
                if (!p.IsConnected) continue;
                if (!_avatars.TryGetValue(p.ClientId, out var av)) { av = Build(p.Name.ToString(), p.ClientId == local); _avatars[p.ClientId] = av; }
                if (av.Seat != p.SeatIndex) { av.Seat = p.SeatIndex; PlaceBody(av); }
                if (av.LabelText != null)
                {
                    av.LabelText.text = p.Name.ToString();
                    av.LabelText.color = Color.Lerp(Palette.Paper, p.Color, 0.45f);
                }
                Animate(game, p.ClientId, av, tension);
            }
        }

        void ClearAll()
        {
            foreach (var av in _avatars.Values) if (av.Root != null) Destroy(av.Root);
            _avatars.Clear();
        }

        // ------------------------------------------------------------------ Build
        Transform Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        Avatar Build(string playerName, bool local)
        {
            var av = new Avatar { Local = local };
            av.Root = new GameObject(local ? "Avatar_Local" : "Avatar_" + playerName);
            av.Root.transform.SetParent(transform, false);
            var r = av.Root.transform;
            if (!local)
            {
                av.Torso = Part(r, "Torso", _torso, _cloth);
                av.Head = Part(r, "Head", _head, _hood);
                var label = new GameObject("NameLabel");
                label.transform.SetParent(r, false);
                var tm = label.AddComponent<TextMesh>();
                var font = SecretJengaAssets.Instance.fontBody != null ? SecretJengaAssets.Instance.fontBody : RuntimeArt.Font;
                tm.font = font;
                tm.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                tm.fontSize = 64;
                tm.characterSize = 0.13f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                av.Label = label.transform;
                av.LabelText = tm;
            }
            av.Right = HandRig.Create(r, "RightHand", 1f, _skin, _cloth);
            av.Left = HandRig.Create(r, "LeftHand", -1f, _skin, _cloth);
            return av;
        }

        void PlaceBody(Avatar av)
        {
            if (av.Torso == null) return;
            var outward = SeatLayout.Outward(av.Seat);
            var hips = outward * (SeatLayout.ChairRadius + 0.4f) + Vector3.up * (SeatLayout.FloorY + 9.2f);
            av.Torso.SetPositionAndRotation(hips, SeatLayout.Facing(av.Seat) * Quaternion.Euler(8f, 0f, 0f));
        }

        static Vector3 RestPoint(int seat, float side, Vector3 right)
        {
            var outward = SeatLayout.Outward(seat);
            return outward * (SeatLayout.TableRadius - 1.6f) + right * (2.3f * side) + Vector3.up * (JengaDims.TableTopY + 0.05f);
        }

        /// <summary>Keeps a resting/sliding hand inside the player's own part of the table (never in the tower).</summary>
        static Vector3 ClampToReach(int seat, Vector3 p)
        {
            var outward = SeatLayout.Outward(seat);
            var flat = new Vector3(p.x, 0f, p.z);
            float r = Mathf.Clamp(flat.magnitude, 3.4f, SeatLayout.TableRadius - 0.9f);
            float ang = Vector3.SignedAngle(outward, flat.sqrMagnitude > 1e-4f ? flat.normalized : outward, Vector3.up);
            ang = Mathf.Clamp(ang, -34f, 34f);
            var dir = Quaternion.AngleAxis(ang, Vector3.up) * outward;
            return dir * r + Vector3.up * (JengaDims.TableTopY + 0.05f);
        }

        static Quaternion Facing(Vector3 from, Vector3 to, float pitch)
        {
            var f = to - from;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            return Quaternion.LookRotation(f.normalized, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
        }

        // ------------------------------------------------------------------ Animate
        void Animate(NetworkGameManager game, ulong clientId, Avatar av, float tension)
        {
            var turns = game.Turns;
            var outward = SeatLayout.Outward(av.Seat);
            var forward = -outward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;

            // shoulders / head: from the camera for the local player (so leaning moves your arms), from the seat otherwise
            Vector3 headPos, shoulderCenter;
            if (av.Local && Camera.main != null)
            {
                var cam = Camera.main.transform;
                headPos = cam.position;
                var flatFwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                if (flatFwd.sqrMagnitude < 0.01f) flatFwd = forward;
                right = Vector3.Cross(Vector3.up, flatFwd).normalized;
                shoulderCenter = headPos - flatFwd * 1.2f - Vector3.up * 3.3f;
            }
            else
            {
                float idle = Mathf.Sin(Time.time * 0.9f + av.Seat) * 0.08f;
                headPos = SeatLayout.EyePosition(av.Seat) + outward * 0.5f + Vector3.up * idle;
                shoulderCenter = headPos + outward * 0.6f - Vector3.up * 3.0f;
            }
            var shoulderL = shoulderCenter - right * 2.1f;
            var shoulderR = shoulderCenter + right * 2.1f;
            av.Right.Shoulder = shoulderR;
            av.Left.Shoulder = shoulderL;

            bool playing = game.Phase.Value == GamePhase.Playing;
            bool active = playing && turns.ActiveClientId.Value == clientId;
            var stage = turns.Stage.Value;
            float wobble = game.Stability.Wobble.Value;
            Vector3 lookAt = new Vector3(0f, game.Tower.LayerCount.Value * JengaDims.BlockHeight * 0.5f, 0f);

            // ---------------- right hand
            HandPoseId pose = HandPoseId.Flat;
            Vector3 contact = RestPoint(av.Seat, 1f, right);
            Quaternion orient = Facing(shoulderR, contact, 4f);
            float follow = 10f;
            bool fromBlock = false;

            if (active && (stage == TurnStage.Dragging || stage == TurnStage.Carrying))
            {
                var held = JengaBlock.Find(turns.HeldBlockId.Value);
                if (held != null)
                {
                    fromBlock = true;
                    var c = held.transform.position;
                    var axis = held.LongAxis;
                    float s = Vector3.Dot(shoulderR - c, axis) >= 0 ? 1f : -1f;
                    if (stage == TurnStage.Dragging)
                    {
                        // pinch the end of the block that faces you, fingers pointing into it
                        var end = c + axis * s * (JengaDims.BlockLength * 0.5f + 0.06f);
                        pose = HandPoseId.Pinch;
                        contact = end;
                        orient = Quaternion.LookRotation(-axis * s, Vector3.up) * Quaternion.Euler(12f, 0f, 0f);
                    }
                    else
                    {
                        // carry it gripped from above
                        pose = HandPoseId.Hold;
                        contact = c + Vector3.up * (JengaDims.BlockHeight * 0.5f + 0.05f) + axis * s * 0.15f;
                        orient = Quaternion.LookRotation(-axis * s, Vector3.up) * Quaternion.Euler(28f, 0f, 0f);
                    }
                    follow = 30f;
                    lookAt = c;
                }
            }

            if (!fromBlock)
            {
                if (av.Local)
                {
                    var bic = BlockInteractionController.Instance;
                    if (active && stage == TurnStage.WaitingForGrab && bic != null && bic.HasHoverPoint)
                    {
                        contact = bic.HoverPoint + bic.HoverNormal * 0.05f;
                        if (bic.CursorPrompt == BlockInteractionController.Prompt.Grab)
                        {
                            pose = HandPoseId.Point;
                            orient = Facing(shoulderR, contact, 30f);
                        }
                        else
                        {
                            // locked block: the hand hesitates and pulls back
                            pose = HandPoseId.Recoil;
                            orient = Facing(shoulderR, contact, 20f);
                            contact -= (orient * Vector3.forward) * 1.4f + Vector3.down * -0.4f;
                        }
                        follow = 16f;
                    }
                    else if (bic != null && bic.HasTablePoint)
                    {
                        contact = active ? bic.TablePoint : ClampToReach(av.Seat, bic.TablePoint);
                        contact.y = JengaDims.TableTopY + 0.05f;
                        pose = tension > 0.55f && !active ? HandPoseId.Drum : HandPoseId.Flat;
                        orient = Facing(shoulderR, contact, 4f);
                    }
                    if (Time.time < av.KnockUntil) pose = HandPoseId.Knock;
                    HandSync.SubmitLocal(contact, pose);
                }
                else if (HandSync.Hands.TryGetValue(clientId, out var net))
                {
                    contact = net.Contact;
                    pose = (HandPoseId)net.Pose;
                    float pitch = pose == HandPoseId.Point ? 30f : pose == HandPoseId.Recoil ? 20f : 4f;
                    orient = Facing(shoulderR, contact, pitch);
                    follow = 9f;
                    if (pose == HandPoseId.Point || pose == HandPoseId.Recoil) lookAt = contact;
                }
                if (Time.time < av.KnockUntil) pose = HandPoseId.Knock;
            }

            av.Right.SetPose(pose);
            av.Right.SetContact(contact, orient, follow);
            float handlingWobble = active && fromBlock ? wobble * 1.2f : 0f;
            av.Right.Tremble = Mathf.Clamp01(0.08f + tension * 0.5f + handlingWobble);

            // ---------------- left hand: rests flat, drums when the room gets tense
            var leftPoint = RestPoint(av.Seat, -1f, right);
            av.Left.SetPose(tension > 0.5f && !active ? HandPoseId.Drum : HandPoseId.Flat);
            av.Left.SetContact(leftPoint, Facing(shoulderL, leftPoint, 4f), 6f);
            av.Left.Tremble = Mathf.Clamp01(0.05f + tension * 0.4f);

            // ---------------- body / head (remote only)
            if (!av.Local)
            {
                var headRot = Quaternion.LookRotation((lookAt - headPos).normalized, Vector3.up);
                av.Head.SetPositionAndRotation(headPos, Quaternion.Slerp(av.Head.rotation, headRot, 1f - Mathf.Exp(-3f * Time.deltaTime)));
                av.Torso.localScale = new Vector3(1f, 1f + Mathf.Sin(Time.time * 1.4f + av.Seat) * 0.008f, 1f);
                av.Label.position = headPos + Vector3.up * 2.9f;
                if (Camera.main != null) av.Label.rotation = Quaternion.LookRotation(av.Label.position - Camera.main.transform.position);
            }
        }
    }
}
