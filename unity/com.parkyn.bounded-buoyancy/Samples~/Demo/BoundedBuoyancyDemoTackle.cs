// Bounded Buoyancy for Unity: the demo's fish, fishing tackle, angler and submarine, drawn as in the Godot plugin's demo
// (demo_tackle.gd, demo_sub_look.gd): perch, trout, bass and mackerel (TackleLook); a rod with a cork handle, a reel seat
// and whipped guides, bending with the pull on the line, and a fixed-spool reel whose handle turns as line is wound in;
// a stick float with a bright tip and a hook baited with a worm; an angler on his seat box whose hands follow the rod and
// the reel's handle; an open tackle box of lures; and the submarine with its sail, lit windows, searchlight, rudder and
// turning propeller. For show: none of it has a collider.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using UnityEngine;

namespace BoundedBuoyancy.Samples
{
    public partial class BoundedBuoyancyDemo
    {
        // Shapes ---------------------------------------------------------------------------------------------------

        /// <summary>A cylinder (or with `capsule`, a capsule) of radius `r` from `a` to `b`, in `parent`'s space.</summary>
        GameObject Stick(Transform parent, Vector3 a, Vector3 b, float r, Color c, bool capsule = false)
        {
            GameObject go = Shape(capsule ? PrimitiveType.Capsule : PrimitiveType.Cylinder, Vector3.zero, Vector3.one, c);
            DropCollider(go);
            go.transform.SetParent(parent, false);
            Place(go.transform, a, b, r, capsule);
            return go;
        }

        static void Place(Transform t, Vector3 a, Vector3 b, float r, bool capsule)
        {
            Vector3 d = b - a;
            float len = Mathf.Max(d.magnitude, 0.0005f);
            t.localPosition = 0.5f * (a + b);
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
            t.localScale = capsule ? new Vector3(2f * r, 0.5f * (len + 2f * r), 2f * r) : new Vector3(2f * r, 0.5f * len, 2f * r);
        }

        GameObject Blob(Transform parent, Vector3 at, float r, Color c, Vector3 squash)
        {
            return Part(PrimitiveType.Sphere, parent, at, 2f * r * squash, c);
        }

        GameObject Brick(Transform parent, Vector3 at, Vector3 size, Color c, Vector3 euler)
        {
            GameObject go = Part(PrimitiveType.Cube, parent, at, size, c);
            go.transform.localRotation = Quaternion.Euler(euler);
            return go;
        }

        /// <summary>A ring about `parent`'s Y axis at `at`.</summary>
        GameObject Ring(Transform parent, Vector3 at, float r, float thick, Color c)
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = ShipMesh("ring " + r + " " + thick, () => TackleLook.Torus(r, thick));
            go.AddComponent<MeshRenderer>().sharedMaterial = Paint(c);
            return go;
        }

        /// <summary>An empty object under `parent` that cancels `parent`'s own scale, so that what is put on it is in
        /// metres.</summary>
        static Transform Unscaled(Transform parent)
        {
            var go = new GameObject("Look");
            go.transform.SetParent(parent, false);
            Vector3 s = parent.localScale;
            go.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            return go.transform;
        }

        // Fish -----------------------------------------------------------------------------------------------------

        readonly List<KeyValuePair<BoundedBuoyancyFish, Transform>> fishLooks = new List<KeyValuePair<BoundedBuoyancyFish, Transform>>();

        /// <summary>A fish's look, `len` long, nose toward +Z: its body and fins, painted by kind, and its eyes.</summary>
        Transform FishLook(Transform parent, int kind, float len)
        {
            var go = new GameObject("Look");
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * len;
            go.AddComponent<MeshFilter>().sharedMesh = ShipMesh("fish " + kind, () => TackleLook.Fish(kind));
            go.AddComponent<MeshRenderer>().sharedMaterial = Textured("fish " + kind, () => TackleLook.FishPaint(kind, 512, 128), 512, 128);
            float er;
            P3 e = TackleLook.Eye(kind, out er);
            Color iris = kind == TackleLook.Trout ? new Color(0.78f, 0.66f, 0.40f) : kind == TackleLook.Mackerel ? new Color(0.8f, 0.82f, 0.85f) : new Color(0.85f, 0.62f, 0.15f);
            foreach (float side in new[] { -1f, 1f })
            {
                var at = new Vector3(side * e.x, e.y, e.z);
                Blob(go.transform, at, er, iris, Vector3.one);
                Blob(go.transform, at + new Vector3(side * 0.55f * er, 0f, 0f), 0.6f * er, new Color(0.01f, 0.01f, 0.01f), Vector3.one);
            }
            return go.transform;
        }

        /// <summary>The fish beat their tails (their whole bodies sway) as hard as they swim.</summary>
        void SwimFish()
        {
            float now = Time.time;
            foreach (KeyValuePair<BoundedBuoyancyFish, Transform> f in fishLooks)
                if (f.Key != null && f.Value != null)
                {
                    float effort = f.Key.Effort;
                    float yaw = Mathf.Sin(now * (6f + 10f * effort) + f.Key.GetInstanceID()) * (4f + 9f * effort);
                    f.Value.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
        }

        // The rod and reel -----------------------------------------------------------------------------------------

        const int RodSegments = 14;
        Transform[] rodSegments;
        Transform reelHandle, reelRotor, reelKnob;
        float rodBend, wound, lastLength;

        static float RodRadius(float along)
        {
            return along < 0.52f ? 0.0135f : Mathf.Lerp(0.0085f, 0.0018f, (along - 0.52f) / (RodLength - 0.52f));
        }

        /// <summary>The rod in pieces, each with its length along its Y and the side below the rod toward its +Z: cork on
        /// the first two, a rubber butt cap, the reel seat with the reel on it, guides whipped on along the blank, and the
        /// tip ring.</summary>
        void MakeRod()
        {
            float piece = RodLength / RodSegments;
            var cork = new Color(0.70f, 0.54f, 0.36f);
            var blank = new Color(0.06f, 0.16f, 0.12f);
            var whip = new Color(0.85f, 0.62f, 0.18f);
            var steel = new Color(0.75f, 0.77f, 0.80f);
            rodSegments = new Transform[RodSegments];
            for (int i = 0; i < RodSegments; i++)
            {
                var seg = new GameObject("Rod " + i).transform;
                seg.SetParent(root, false);
                rodSegments[i] = seg;
                float r = RodRadius((i + 0.5f) * piece);
                Part(PrimitiveType.Cylinder, seg, Vector3.zero, new Vector3(2f * r, 0.5f * piece * 1.03f, 2f * r), i < 2 ? cork : blank);
            }
            Stick(rodSegments[0], new Vector3(0f, -0.5f * piece - 0.015f, 0f), new Vector3(0f, -0.5f * piece + 0.01f, 0f), RodRadius(0f) * 1.25f, new Color(0.03f, 0.03f, 0.03f));
            float seatAt = 0.33f - 1.5f * piece;
            float r1 = RodRadius(0.33f);
            Transform seat = rodSegments[1];
            Stick(seat, new Vector3(0f, seatAt - 0.06f, 0f), new Vector3(0f, seatAt + 0.06f, 0f), r1 * 1.18f, new Color(0.20f, 0.21f, 0.23f));
            foreach (float e in new[] { -0.065f, 0.065f })
                Ring(seat, new Vector3(0f, seatAt + e, 0f), r1 * 1.17f, r1 * 0.3f, whip);
            int[] guideAt = { 3, 5, 7, 9, 11, 12, 13 };
            float[] guideR = { 0.019f, 0.014f, 0.011f, 0.009f, 0.007f, 0.006f, 0.005f };
            for (int g = 0; g < guideAt.Length; g++)
            {
                int i = guideAt[g];
                bool last = i == RodSegments - 1;
                float at = last ? 0.5f * piece - 0.004f : 0.5f * piece - 0.03f;
                float rb = RodRadius((i + 0.5f) * piece);
                float gr = guideR[g];
                float stand = last ? rb + gr * 0.6f : rb + 0.004f + gr;
                Transform part = rodSegments[i];
                Stick(part, new Vector3(0f, at - 0.018f, 0f), new Vector3(0f, at + 0.012f, 0f), rb * 1.45f, whip);
                Stick(part, new Vector3(0f, at, rb * 0.5f), new Vector3(0f, at, stand - gr), 0.0012f, steel);
                Ring(part, new Vector3(0f, at, stand), gr - 0.0009f, 0.0018f, steel);
            }
            MakeReel(seat, new Vector3(0f, seatAt, r1 * 1.18f));
        }

        /// <summary>A fixed-spool reel hung below the rod: its foot, the gearbox, the rotor with the bail arm, the spool full
        /// of line, and the handle on the angler's left with its knob.</summary>
        void MakeReel(Transform parent, Vector3 at)
        {
            var r = new GameObject("Reel").transform;
            r.SetParent(parent, false);
            r.localPosition = at;
            r.localScale = Vector3.one * 1.3f;   // a little larger than life, to be seen
            var body = new Color(0.16f, 0.17f, 0.19f);
            var gold = new Color(0.82f, 0.66f, 0.35f);
            var line = new Color(1f, 0.86f, 0.18f);
            var steel = new Color(0.78f, 0.80f, 0.83f);
            Brick(r, new Vector3(0f, 0f, 0.003f), new Vector3(0.012f, 0.07f, 0.005f), body, Vector3.zero);
            Brick(r, new Vector3(0f, -0.005f, 0.03f), new Vector3(0.01f, 0.022f, 0.05f), body, Vector3.zero);
            Stick(r, new Vector3(0.018f, -0.012f, 0.075f), new Vector3(-0.02f, -0.012f, 0.075f), 0.03f, body);
            reelRotor = new GameObject("Rotor").transform;
            reelRotor.SetParent(r, false);
            reelRotor.localPosition = new Vector3(0f, 0f, 0.075f);
            Stick(reelRotor, new Vector3(0f, 0.016f, 0f), new Vector3(0f, 0.05f, 0f), 0.027f, body);
            Ring(reelRotor, new Vector3(0f, 0.07f, 0f), 0.03f, 0.0022f, steel).transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            Stick(reelRotor, new Vector3(0.027f, 0.045f, 0f), new Vector3(0.031f, 0.07f, 0.006f), 0.0025f, steel);
            Stick(reelRotor, new Vector3(-0.027f, 0.045f, 0f), new Vector3(-0.031f, 0.07f, -0.006f), 0.0025f, steel);
            Stick(r, new Vector3(0f, 0.05f, 0.075f), new Vector3(0f, 0.088f, 0.075f), 0.025f, gold);
            Stick(r, new Vector3(0f, 0.055f, 0.075f), new Vector3(0f, 0.083f, 0.075f), 0.0262f, line);
            Stick(r, new Vector3(0f, 0.088f, 0.075f), new Vector3(0f, 0.094f, 0.075f), 0.012f, gold);
            reelHandle = new GameObject("Handle").transform;
            reelHandle.SetParent(r, false);
            reelHandle.localPosition = new Vector3(-0.022f, -0.012f, 0.075f);
            Stick(reelHandle, Vector3.zero, new Vector3(-0.012f, 0f, 0f), 0.006f, body);
            Stick(reelHandle, new Vector3(-0.012f, 0f, 0f), new Vector3(-0.014f, 0f, 0.06f), 0.004f, body);
            reelKnob = Stick(reelHandle, new Vector3(-0.016f, 0f, 0.06f), new Vector3(-0.04f, 0f, 0.06f), 0.008f, new Color(0.05f, 0.05f, 0.05f), true).transform;
        }

        /// <summary>Lay the rod out from the angler's hands at its pitch, bent toward the line by the pull on it (the tip
        /// bends most); turn the reel's handle as line is wound in; put the angler's hands on the rod and the handle.
        /// Returns where the tip is.</summary>
        Vector3 LayRod(float dt)
        {
            float p = rodPitch * Mathf.Deg2Rad;
            var dir = new Vector3(0f, Mathf.Sin(p), Mathf.Cos(p));
            Vector3 pull = Vector3.down;
            float want = 0f;
            if (mainLine != null && mainLine.body != null && !mainLine.Broken)
            {
                Vector3[] e = mainLine.Ends;
                pull = (e[1] - e[0]).normalized;
                want = Mathf.Min(mainLine.Tension / 9f, 1.4f);   // about a radian for each 9 N
            }
            rodBend = dt > 0f ? Mathf.Lerp(rodBend, want, 1f - Mathf.Exp(-dt / 0.06f)) : want;
            float bend = Mathf.Min(rodBend, Vector3.Angle(dir, pull) * Mathf.Deg2Rad);
            float total = 0f;
            for (int i = 0; i < RodSegments; i++)
                total += (i + 1) * (i + 1);
            float piece = RodLength / RodSegments;
            Vector3 at = RodHand, d = dir;
            for (int i = 0; i < RodSegments; i++)
            {
                d = Vector3.RotateTowards(d, pull, bend * (i + 1) * (i + 1) / total, 0f);
                Vector3 below = Vector3.ProjectOnPlane(Vector3.down, d);
                if (below.sqrMagnitude < 1e-6f)
                    below = Vector3.forward;
                rodSegments[i].position = at + d * (0.5f * piece);
                rodSegments[i].rotation = Quaternion.LookRotation(below.normalized, d);
                at += d * piece;
            }
            if (mainLine != null)
            {
                wound += Mathf.Max(lastLength - mainLine.length, 0f);
                lastLength = mainLine.length;
            }
            reelHandle.localRotation = Quaternion.Euler(wound * 1000f, 0f, 0f);
            reelRotor.localRotation = Quaternion.Euler(0f, wound * 5000f, 0f);
            Vector3 right = rodSegments[1].TransformPoint(new Vector3(0f, 0.41f - 1.5f * piece, -0.01f));
            PoseArms(reelKnob.position, right);
            return at;
        }

        // The angler -----------------------------------------------------------------------------------------------

        class Arm
        {
            public Vector3 shoulder;
            public float side;
            public Transform upper, fore, cuff, hand;
        }

        readonly List<Arm> arms = new List<Arm>();

        /// <summary>An angler sitting on a seat box whose top is at `seat`, facing +Z: wellingtons, dark trousers, an olive
        /// waxed jacket under a sand-coloured vest with pockets, a grey beard and a bucket hat.</summary>
        void MakeAngler(Vector3 seat)
        {
            Transform p = root;
            Vector3 up = Vector3.up, f = Vector3.forward, s = Vector3.right;
            var jacket = new Color(0.22f, 0.27f, 0.16f);
            var vest = new Color(0.62f, 0.55f, 0.38f);
            var pocket = new Color(0.52f, 0.46f, 0.31f);
            var trousers = new Color(0.14f, 0.15f, 0.17f);
            var welly = new Color(0.10f, 0.22f, 0.12f);
            var skin = new Color(0.86f, 0.64f, 0.50f);
            var beard = new Color(0.78f, 0.77f, 0.74f);
            var hat = new Color(0.58f, 0.53f, 0.38f);
            var trim = new Color(0.14f, 0.15f, 0.16f);
            // The seat box: drawers in front, a cushion on top.
            Vector3 b = seat - up * 0.19f;
            Brick(p, b, new Vector3(0.42f, 0.38f, 0.34f), new Color(0.55f, 0.58f, 0.55f), Vector3.zero);
            for (int k = 0; k < 3; k++)
            {
                Brick(p, b + new Vector3(0f, 0.11f - 0.11f * k, 0.171f), new Vector3(0.40f, 0.006f, 0.004f), trim, Vector3.zero);
                Brick(p, b + new Vector3(0f, 0.08f - 0.11f * k, 0.173f), new Vector3(0.08f, 0.02f, 0.01f), trim, Vector3.zero);
            }
            Brick(p, seat + up * 0.008f, new Vector3(0.40f, 0.016f, 0.32f), new Color(0.18f, 0.20f, 0.22f), Vector3.zero);
            Vector3 hips = seat + up * 0.1f + f * 0.02f;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 hip = hips + s * 0.1f * side;
                Vector3 knee = hips + f * 0.42f + s * 0.13f * side + up * 0.04f;
                Vector3 ankle = knee - up * 0.42f + f * 0.04f;
                Stick(p, hip, knee, 0.08f, trousers, true);
                Stick(p, knee, ankle, 0.066f, trousers, true);
                Stick(p, ankle - up * 0.03f, knee - up * 0.08f, 0.072f, welly, true);
                Stick(p, ankle - up * 0.04f, ankle - up * 0.05f + f * 0.17f, 0.055f, welly, true);
            }
            Vector3 chest = hips + up * 0.5f + f * 0.1f;
            Stick(p, hips + up * 0.05f, chest, 0.17f, jacket, true);
            Stick(p, hips + up * 0.2f + f * 0.04f, chest - up * 0.03f, 0.178f, vest, true);
            foreach (float side in new[] { -1f, 1f })
                for (int k = 0; k < 2; k++)
                    Brick(p, hips + up * (0.3f + 0.13f * k) + f * (0.22f + 0.025f * k) + s * 0.08f * side, new Vector3(0.09f, 0.08f, 0.025f), pocket, new Vector3(11f, 0f, 0f));
            Ring(p, chest + up * 0.08f + f * 0.01f, 0.06f, 0.03f, jacket);
            Vector3 head = chest + up * 0.24f + f * 0.04f;
            Blob(p, head, 0.105f, skin, Vector3.one);
            Blob(p, head + f * 0.05f - up * 0.045f, 0.085f, beard, new Vector3(1f, 0.9f, 0.85f));
            Blob(p, head + f * 0.1f, 0.02f, skin, Vector3.one);
            Part(PrimitiveType.Cylinder, p, head + up * 0.07f, new Vector3(0.34f, 0.0125f, 0.34f), hat).transform.localRotation = Quaternion.Euler(-7f, 0f, 0f);
            Stick(p, head + up * 0.06f, head + up * 0.17f, 0.112f, hat);
            Stick(p, head + up * 0.09f, head + up * 0.11f, 0.116f, new Color(0.25f, 0.22f, 0.16f));
            arms.Clear();
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 shoulder = chest + s * 0.19f * side - up * 0.04f;
                Blob(p, shoulder, 0.06f, jacket, Vector3.one);
                arms.Add(new Arm
                {
                    shoulder = shoulder,
                    side = side,
                    upper = Stick(p, shoulder, shoulder - up * 0.3f, 0.052f, jacket, true).transform,
                    fore = Stick(p, shoulder, shoulder - up * 0.28f, 0.046f, jacket, true).transform,
                    cuff = Stick(p, shoulder, shoulder - up * 0.02f, 0.049f, new Color(0.12f, 0.14f, 0.09f), true).transform,
                    hand = Blob(p, shoulder, 0.04f, skin, new Vector3(1f, 1.15f, 1f)).transform,
                });
            }
        }

        /// <summary>Put the angler's hands at `left` and `right`, bending his elbows out and down.</summary>
        void PoseArms(Vector3 left, Vector3 right)
        {
            const float l1 = 0.3f, l2 = 0.28f;
            foreach (Arm a in arms)
            {
                Vector3 hand = a.side < 0f ? left : right;
                Vector3 to = hand - a.shoulder;
                float d = Mathf.Clamp(to.magnitude, 0.05f, l1 + l2 - 0.001f);
                Vector3 dir = to.normalized;
                float x = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
                float h = Mathf.Sqrt(Mathf.Max(l1 * l1 - x * x, 0f));
                Vector3 pole = Vector3.ProjectOnPlane(new Vector3(a.side * 0.6f, -1f, -0.2f), dir).normalized;
                Vector3 elbow = a.shoulder + dir * x + pole * h;
                Vector3 along = (hand - elbow).normalized;
                Vector3 wrist = elbow + along * l2;
                Place(a.upper, a.shoulder, elbow, 0.052f, true);
                Place(a.fore, elbow, wrist, 0.046f, true);
                Place(a.cuff, wrist - along * 0.04f, wrist - along * 0.01f, 0.049f, true);
                a.hand.localPosition = wrist + along * 0.02f;
            }
        }

        // Float, bait and tackle box -------------------------------------------------------------------------------

        /// <summary>A stick float drawn on `parent` (its body, `FloatLong` long along Y): drawn twice as wide as it is, an
        /// olive body with a gold band, a white band under a black ring, and a bright tip that glows.</summary>
        void FloatLook(Transform parent)
        {
            float h = 0.5f * FloatLong, r = FloatWide * 0.8f;
            Stick(parent, new Vector3(0f, -h, 0f), new Vector3(0f, -h + 0.015f, 0f), r * 0.7f, new Color(0.5f, 0.35f, 0.2f));
            Stick(parent, new Vector3(0f, -h + 0.015f, 0f), new Vector3(0f, -h + 0.7f * FloatLong, 0f), r, new Color(0.14f, 0.20f, 0.10f));
            Stick(parent, new Vector3(0f, -h + 0.41f * FloatLong, 0f), new Vector3(0f, -h + 0.43f * FloatLong, 0f), r * 1.05f, new Color(0.85f, 0.65f, 0.2f));
            Stick(parent, new Vector3(0f, -h + 0.7f * FloatLong, 0f), new Vector3(0f, -h + 0.81f * FloatLong, 0f), r, new Color(0.97f, 0.97f, 0.94f));
            Stick(parent, new Vector3(0f, -h + 0.81f * FloatLong, 0f), new Vector3(0f, -h + 0.84f * FloatLong, 0f), r, new Color(0.02f, 0.02f, 0.02f));
            GameObject tip = Stick(parent, new Vector3(0f, -h + 0.84f * FloatLong, 0f), new Vector3(0f, h, 0f), r, new Color(1f, 0.24f, 0.02f));
            tip.GetComponent<Renderer>().sharedMaterial = Glowing(new Color(1f, 0.24f, 0.02f));
            tip.name = "Tip";
        }

        /// <summary>The hook under the shot (its centre at the origin, radius `shot`): a bronze hook with a red worm on it,
        /// drawn half as big again as it is so that it can be seen.</summary>
        void HookAndWorm(Transform parent, float shot)
        {
            var bronze = new Color(0.55f, 0.38f, 0.16f);
            var worm = new Color(0.72f, 0.22f, 0.24f);
            var band = new Color(0.85f, 0.42f, 0.40f);
            const float k = 1.5f, w = 0.0012f * k;
            var top = new Vector3(0f, -shot - 0.03f, 0f);
            Ring(parent, top, 0.0016f * k, 0.0008f * k, bronze).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Vector3 shankEnd = top + new Vector3(0f, -0.022f, 0f) * k;
            Stick(parent, top, shankEnd, w, bronze, true);
            Vector3 c = top + new Vector3(0.006f, -0.022f, 0f) * k;
            Vector3 prev = shankEnd;
            for (int i = 1; i < 8; i++)
            {
                float a = Mathf.PI + Mathf.PI * i / 7f;
                Vector3 q = c + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.006f * k;
                Stick(parent, prev, q, w, bronze, true);
                prev = q;
            }
            Stick(parent, prev, prev + new Vector3(-0.001f, 0.008f, 0f) * k, w * 0.8f, bronze, true);
            foreach (float end in new[] { -1f, 1f })
            {
                Vector3 q = c + new Vector3(0f, -0.006f * k, 0f);
                for (int i = 0; i < 9; i++)
                {
                    float a = end * (0.4f + 0.5f * i) + 0.3f * Mathf.Sin(i * 1.3f);
                    Vector3 nq = q + new Vector3(Mathf.Sin(a) * 0.006f, -0.0065f, -Mathf.Cos(a) * 0.004f) * k;
                    Stick(parent, q, nq, 0.0028f * k * (1f - 0.04f * i), i == 3 ? band : worm, true);
                    q = nq;
                }
            }
        }

        /// <summary>An open tackle box at `at`, turned `yaw` degrees: a tray of lures (spoons, spinners, plugs), spare floats
        /// and hooks, the lid open behind it.</summary>
        void TackleBox(Vector3 at, float yaw)
        {
            var box = new GameObject("Tackle box").transform;
            box.SetParent(root, false);
            box.position = at;
            box.rotation = Quaternion.Euler(0f, yaw, 0f);
            var shell = new Color(0.16f, 0.42f, 0.30f);
            var tray = new Color(0.82f, 0.84f, 0.80f);
            var silver = new Color(0.85f, 0.86f, 0.88f);
            var gold = new Color(0.92f, 0.70f, 0.25f);
            var red = new Color(0.85f, 0.08f, 0.06f);
            var yellow = new Color(1f, 0.85f, 0.05f);
            var black = new Color(0.02f, 0.02f, 0.02f);
            var orange = new Color(1f, 0.42f, 0.05f);
            const float W = 0.44f, D = 0.26f, H = 0.12f;
            Brick(box, new Vector3(0f, 0.5f * H, 0f), new Vector3(W, H, D), shell, Vector3.zero);
            Brick(box, new Vector3(0f, H - 0.004f, 0f), new Vector3(W - 0.02f, 0.008f, D - 0.02f), tray, Vector3.zero);
            for (int k = 0; k < 4; k++)
                Brick(box, new Vector3(-0.5f * W + 0.01f + (W - 0.02f) * (k + 1) / 5f, H + 0.008f, 0f), new Vector3(0.004f, 0.02f, D - 0.02f), tray, Vector3.zero);
            Brick(box, new Vector3(0f, H + 0.008f, 0f), new Vector3(W - 0.02f, 0.02f, 0.004f), tray, Vector3.zero);
            // The lid, hinged at the back and open past upright.
            Quaternion open = Quaternion.Euler(105f, 0f, 0f);
            var hinge = new Vector3(0f, H, 0.5f * D);
            GameObject lid = Brick(box, hinge - open * Vector3.forward * (0.5f * D) + open * Vector3.up * 0.012f, new Vector3(W, 0.024f, D), shell, Vector3.zero);
            lid.transform.localRotation = open;
            float y = H + 0.008f, cell = (W - 0.02f) / 5f;
            System.Func<int, float> cx = n => -0.5f * W + 0.01f + cell * (n + 0.5f);
            for (int j = 0; j < 2; j++)
            {
                float z = -0.25f * D + 0.5f * D * j;
                Blob(box, new Vector3(cx(0), y + 0.006f, z), 0.02f, j == 0 ? silver : gold, new Vector3(0.55f, 0.2f, 1.3f));
                if (j == 0)
                    Brick(box, new Vector3(cx(0), y + 0.011f, z), new Vector3(0.008f, 0.002f, 0.04f), red, Vector3.zero);
                var o = new Vector3(cx(1), y + 0.008f, z);
                Stick(box, o + new Vector3(0f, 0f, -0.035f), o + new Vector3(0f, 0f, 0.035f), 0.0012f, silver);
                Blob(box, o + new Vector3(0.008f, 0.002f, 0.015f), 0.016f, j == 0 ? gold : silver, new Vector3(0.6f, 0.15f, 1f)).transform.localRotation = Quaternion.Euler(0f, -17f, 0f);
                for (int bd = 0; bd < 3; bd++)
                    Blob(box, o + new Vector3(0f, 0f, -0.004f - 0.008f * bd), 0.004f, bd != 1 ? red : yellow, Vector3.one);
            }
            Color[][] plugs = { new[] { new Color(0.65f, 0.95f, 0.10f), orange }, new[] { silver, black }, new[] { yellow, red } };
            for (int j = 0; j < 3; j++)
            {
                Vector3 o = j < 2 ? new Vector3(cx(2), y + 0.012f, -0.22f * D + 0.44f * D * j) : new Vector3(cx(3), y + 0.012f, 0.02f);
                Blob(box, o, 0.03f, plugs[j][0], new Vector3(0.4f, 0.42f, 1f));
                Blob(box, o + new Vector3(0f, -0.004f, -0.004f), 0.024f, plugs[j][1], new Vector3(0.3f, 0.3f, 0.8f));
                foreach (float side in new[] { -1f, 1f })
                    Blob(box, o + new Vector3(side * 0.011f, 0.004f, 0.019f), 0.004f, black, Vector3.one);
                Brick(box, o + new Vector3(0f, -0.006f, 0.034f), new Vector3(0.012f, 0.002f, 0.014f), new Color(0.85f, 0.9f, 0.95f), new Vector3(-34f, 0f, 0f));
            }
            for (int j = 0; j < 3; j++)
            {
                float x = cx(4) - 0.025f + 0.025f * j;
                var a = new Vector3(x, y + 0.006f, 0.4f * D);
                var bb = new Vector3(x, y + 0.006f, -0.3f * D);
                Stick(box, a, bb, 0.005f, new Color(0.14f, 0.2f, 0.1f));
                Stick(box, a, a + new Vector3(0f, 0f, -0.03f), 0.0052f, orange);
            }
            for (int j = 0; j < 4; j++)
            {
                var o = new Vector3(cx(3) - 0.02f + 0.013f * j, y + 0.004f, -0.3f * D);
                Stick(box, o, o + new Vector3(0f, 0f, 0.025f), 0.0009f, gold);
            }
        }

        /// <summary>The lake bed four and a half metres down: sand, stones and clumps of weed.</summary>
        void LakeBed()
        {
            Brick(root, new Vector3(0f, -4.55f, 20f), new Vector3(60f, 0.1f, 60f), new Color(0.46f, 0.41f, 0.28f), Vector3.zero);
            var stone = new Color(0.42f, 0.42f, 0.38f);
            for (int i = 0; i < 70; i++)
            {
                float r = Rand(0.08f, 0.35f);
                Blob(root, new Vector3(Rand(-16f, 16f), -4.5f + 0.3f * r, Rand(2f, 30f)), r, stone, new Vector3(Rand(0.8f, 1.4f), Rand(0.4f, 0.7f), Rand(0.8f, 1.4f)));
            }
            Color[] greens = { new Color(0.22f, 0.48f, 0.16f), new Color(0.38f, 0.52f, 0.14f), new Color(0.16f, 0.38f, 0.20f) };
            for (int i = 0; i < 60; i++)
            {
                var at = new Vector3(Rand(-16f, 16f), -4.5f, Rand(3f, 30f));
                int n = 4 + rnd.Next(5);
                for (int k = 0; k < n; k++)
                {
                    float h = Rand(0.8f, 2.6f);
                    Brick(root, at + new Vector3(Rand(-0.25f, 0.25f), 0.5f * h, Rand(-0.25f, 0.25f)), new Vector3(Rand(0.04f, 0.08f), h, 0.005f), greens[i % 3],
                        new Vector3(Rand(-9f, 9f), Rand(0f, 360f), Rand(-9f, 9f)));
                }
            }
        }

        // The submarine --------------------------------------------------------------------------------------------

        Transform subPropLook;
        readonly List<Transform> subRudderLooks = new List<Transform>();
        float subTurned;

        /// <summary>The submarine drawn on `parent` (bow toward +Z): its hull, painted; the sail with planes, masts and
        /// navigation lights; lit round windows along both sides; a searchlight in the bow; control surfaces at the stern;
        /// and a seven-bladed bronze propeller.</summary>
        void SubLook(Transform parent, float length, float radius)
        {
            var look = new GameObject("Look");
            look.transform.SetParent(parent, false);
            Transform p = look.transform;
            look.AddComponent<MeshFilter>().sharedMesh = ShipMesh("submarine", () => TackleLook.SubHull(length, radius));
            look.AddComponent<MeshRenderer>().sharedMaterial = Textured("submarine", () => TackleLook.SubPaint(length, 1024, 256), 1024, 256);
            var grey = new Color(0.50f, 0.55f, 0.60f);
            var dark = new Color(0.05f, 0.06f, 0.07f);
            var bronze = new Color(0.80f, 0.55f, 0.25f);
            var sailAt = new Vector3(0f, radius + 1f, 0.2f * length);
            Part(PrimitiveType.Cylinder, p, sailAt + new Vector3(0f, -0.15f, 0f), new Vector3(0.96f, 1.45f, 4.2f), grey);
            foreach (float side in new[] { -1f, 1f })
            {
                Brick(p, sailAt + new Vector3(side * 0.36f, 0.95f, 1.55f), new Vector3(0.06f, 0.22f, 0.5f), dark, new Vector3(0f, -side * 29f, 0f));
                Brick(p, sailAt + new Vector3(side * 1.25f, 0.3f, 0.6f), new Vector3(1.6f, 0.1f, 0.9f), grey, Vector3.zero);
                GameObject lamp = Blob(p, sailAt + new Vector3(side * 0.5f, 1.2f, 1.2f), 0.09f, Color.white, Vector3.one);
                lamp.GetComponent<Renderer>().sharedMaterial = Glowing(side < 0f ? new Color(1f, 0.1f, 0.05f) : new Color(0.1f, 1f, 0.3f));
            }
            float[,] masts = { { 0.4f, 2f, 0.09f }, { -0.2f, 1.6f, 0.13f }, { -0.7f, 1.2f, 0.07f } };
            for (int i = 0; i < 3; i++)
                Stick(p, sailAt + new Vector3(0f, 1.3f, masts[i, 0]), sailAt + new Vector3(0f, 1.3f + masts[i, 1], masts[i, 0]), masts[i, 2], dark);
            Brick(p, sailAt + new Vector3(0f, 3.3f, 0.4f), new Vector3(0.14f, 0.18f, 0.3f), dark, Vector3.zero);
            foreach (float z in new[] { 0.38f * length, -0.12f * length })
                Stick(p, new Vector3(0f, radius - 0.04f, z), new Vector3(0f, radius + 0.08f, z), 0.4f, grey);
            // Round windows along both sides, lit from within, each in a dark rim.
            Material glow = Glowing(new Color(1f, 0.82f, 0.45f));
            foreach (float side in new[] { -1f, 1f })
                for (int k = 0; k < 7; k++)
                {
                    float z = 0.28f * length - k * 1.6f;
                    float a = 28f * Mathf.Deg2Rad;
                    float r = TackleLook.SubRadius(0.5f * length - z, length, radius);
                    var n = new Vector3(side * Mathf.Cos(a), Mathf.Sin(a), 0f);
                    Vector3 at = n * r + new Vector3(0f, 0f, z);
                    Stick(p, at - n * 0.04f, at + n * 0.04f, 0.24f, dark);
                    GameObject pane = Stick(p, at - n * 0.04f, at + n * 0.06f, 0.17f, Color.white);
                    pane.GetComponent<Renderer>().sharedMaterial = glow;
                }
            // A searchlight in the bow, lighting the water ahead.
            GameObject lens = Blob(p, new Vector3(0f, -0.4f, 0.5f * length - 0.25f), 0.28f, Color.white, new Vector3(1f, 1f, 0.5f));
            lens.GetComponent<Renderer>().sharedMaterial = Glowing(new Color(1f, 0.95f, 0.8f));
            var beam = new GameObject("Searchlight").AddComponent<Light>();
            beam.transform.SetParent(p, false);
            beam.transform.localPosition = new Vector3(0f, -0.4f, 0.5f * length + 0.2f);
            beam.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            beam.type = LightType.Spot;
            beam.color = new Color(1f, 0.95f, 0.85f);
            beam.intensity = 4f;
            beam.range = 45f;
            beam.spotAngle = 44f;
            // Control surfaces at the stern: the upper and lower ones are the rudder, and turn with it.
            float finsZ = -0.5f * length + 2.4f;
            float r0 = TackleLook.SubRadius(0.5f * length - finsZ, length, radius);
            subRudderLooks.Clear();
            for (int k = 0; k < 4; k++)
            {
                float a = 90f * k;
                var pivot = new GameObject("Fin").transform;
                pivot.SetParent(p, false);
                pivot.localPosition = new Vector3(0f, 0f, finsZ);
                Vector3 outward = Quaternion.Euler(0f, 0f, a) * Vector3.right;
                Brick(pivot, outward * (r0 + 0.6f), new Vector3(0.12f, 1.3f, 1.3f), grey, new Vector3(0f, 0f, a - 90f));
                if (k % 2 == 1)
                    subRudderLooks.Add(pivot);
            }
            // The propeller: a hub and seven broad blades, set at an angle.
            subPropLook = new GameObject("Propeller").transform;
            subPropLook.SetParent(p, false);
            subPropLook.localPosition = new Vector3(0f, 0f, -0.5f * length + 0.05f);
            Stick(subPropLook, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -0.8f), 0.22f, bronze);
            for (int k = 0; k < 7; k++)
            {
                var blade = new GameObject("Blade").transform;
                blade.SetParent(subPropLook, false);
                blade.localRotation = Quaternion.Euler(0f, 0f, 360f * k / 7f);
                Brick(blade, new Vector3(0f, 0.5f, -0.25f), new Vector3(0.36f, 0.72f, 0.04f), bronze, new Vector3(0f, -50f, 15f));
            }
        }
    }
}
