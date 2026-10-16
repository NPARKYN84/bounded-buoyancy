// Bounded Buoyancy for Unity: the 3D demo's ships (drawn from ShipLook) and its FLOOD and CANNONS scenes, as in the
// Godot plugin's demo.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoundedBuoyancy.Samples
{
    public partial class BoundedBuoyancyDemo
    {
        // Drawing the ships -------------------------------------------------------------------------------------------

        static readonly float[] ShipPorts = { -4f, -2f, 0f, 2f, 4f };   // along the ship, as ShipLook takes them
        readonly Dictionary<string, Material> shipPaint = new Dictionary<string, Material>();
        readonly Dictionary<string, Mesh> shipMeshes = new Dictionary<string, Mesh>();
        Mesh holeMesh;

        /// <summary>A material painted from `paint` (RGBA bytes, row 0 at the bottom), made once per key and kept, so
        /// the paint is worked out only the first time.</summary>
        Material Textured(string key, Func<byte[]> paint, int w, int h)
        {
            Material m;
            if (shipPaint.TryGetValue(key, out m))
                return m;
            byte[] rgba = paint();
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            // Level 0 from the bytes, and the smaller mip levels made from it.
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2], rgba[4 * i + 3]);
            tex.SetPixels32(px);
            tex.Apply(true, false);
            m = new Material(baseMaterial) { color = Color.white, mainTexture = tex };
            shipPaint[key] = m;
            return m;
        }

        Material Glowing(Color c)
        {
            string key = "glow" + c;
            Material m;
            if (shipPaint.TryGetValue(key, out m))
                return m;
            m = new Material(baseMaterial) { color = c };
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 1.5f);
            shipPaint[key] = m;
            return m;
        }

        /// <summary>The mesh made from `make`, once per key and kept (every ship has the same shape).</summary>
        Mesh ShipMesh(string key, Func<MeshData> make)
        {
            Mesh mesh;
            if (!shipMeshes.TryGetValue(key, out mesh))
            {
                mesh = ToMesh(key, make());
                shipMeshes[key] = mesh;
            }
            return mesh;
        }

        static Mesh ToMesh(string name, MeshData d)
        {
            var verts = new Vector3[d.Positions.Count];
            var uv = new Vector2[d.Positions.Count];
            for (int i = 0; i < verts.Length; i++)
            {
                P3 p = d.Positions[i];
                verts[i] = new Vector3(p.x, p.y, p.z);
                uv[i] = new Vector2(d.UV[2 * i], d.UV[2 * i + 1]);
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = d.Triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Vector3 V(P3 p) { return new Vector3(p.x, p.y, p.z); }

        void Drawn(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>Dress a ship's body (bow toward its +Z, origin on the keel line) as a brig: her planked and painted hull
        /// with its gun ports and round windows, the deck, masts and yards with every sail set, rigging, the port lids
        /// (and with runOut a gun's muzzle in each port), a stern lantern and a flag, which it returns. For show: none of
        /// it has a collider.</summary>
        GameObject DressShip(Transform ship, string name, ShipColours colours, bool runOut)
        {
            var look = new GameObject("Look").transform;
            look.SetParent(ship, false);
            Drawn(look, "Hull", ShipMesh("Hull", ShipLook.Hull), Textured("hull " + name, () => ShipLook.HullPaint(2048, 512, colours, ShipPorts), 2048, 512));
            Drawn(look, "Transom", ShipMesh("Transom", ShipLook.Transom), Textured("transom " + name, () => ShipLook.TransomPaint(512, 512, colours), 512, 512));
            Drawn(look, "Deck", ShipMesh("Deck", ShipLook.DeckMesh), Textured("deck", () => ShipLook.DeckPaint(1024, 256), 1024, 256));
            ShipLook.Rig rig = ShipLook.MakeRig(ShipPorts, runOut);
            Material canvas = Textured("sail", () => ShipLook.SailPaint(256, 256), 256, 256);
            for (int i = 0; i < rig.Sails.Count; i++)
            {
                MeshData sail = rig.Sails[i];
                Drawn(look, "Sail", ShipMesh("Sail " + i, () => sail), canvas);
            }
            Drawn(look, "Jib", ShipMesh("Jib", () => rig.JibSail), canvas);
            if (holeMesh == null)
            {
                // The sphere's mesh for the hole marks, taken from a primitive switched off before it can collide.
                GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.SetActive(false);
                holeMesh = ball.GetComponent<MeshFilter>().sharedMesh;
                Destroy(ball);
            }
            Color lids = new Color(colours.Lids[0], colours.Lids[1], colours.Lids[2]);
            foreach (Part p in rig.Parts)
                RigPart(look, p, lids);
            Color flagColour = new Color(colours.Flag[0], colours.Flag[1], colours.Flag[2]);
            return RigPart(look, rig.Flag, flagColour);
        }

        GameObject RigPart(Transform parent, Part p, Color lids)
        {
            Color c;
            switch (p.Look)
            {
                case "rope": c = new Color(0.12f, 0.10f, 0.08f); break;
                case "iron": c = new Color(0.07f, 0.07f, 0.08f); break;
                case "lid": c = lids; break;
                case "flag": c = lids; break;
                case "gold": c = new Color(0.78f, 0.58f, 0.2f); break;
                case "lantern": c = new Color(1f, 0.78f, 0.45f); break;
                default: c = new Color(0.30f, 0.20f, 0.12f); break;
            }
            GameObject go;
            if (p.Cylinder)
            {
                Vector3 a = V(p.A), b = V(p.B);
                go = Shape(PrimitiveType.Cylinder, Vector3.zero, Vector3.one, c);
                DropCollider(go);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = 0.5f * (a + b);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                go.transform.localScale = new Vector3(2f * p.Radius, 0.5f * (b - a).magnitude, 2f * p.Radius);   // a cylinder is 2 high
            }
            else
            {
                go = Shape(PrimitiveType.Cube, Vector3.zero, Vector3.one, c);
                DropCollider(go);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = V(p.Centre);
                go.transform.localRotation = Quaternion.LookRotation(V(p.Z), V(p.Y));
                go.transform.localScale = V(p.Size);
            }
            if (p.Look == "lantern")
                go.GetComponent<Renderer>().sharedMaterial = Glowing(c);
            return go;
        }

        /// <summary>A brig's body: her hull's convex shape as the physics has it (lower than she is drawn, with an upright
        /// stern), the collider not drawn, `density` overall, the centre of mass 1.2 m above the keel; dressed by
        /// DressShip.</summary>
        BoundedBuoyancyBody BrigBody(Vector3 at, float heading, float density, string name, ShipColours colours, bool runOut, out GameObject flag)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= 8; i++)
            {
                float t = -1f + 2f * i / 8f;          // bow to stern
                float z = -t * 0.5f * ShipLook.Length;   // the bow toward +Z
                foreach (float y in new[] { ShipLook.KeelAt(t), ShipLook.KeelAt(t) + 0.45f, ShipLook.MaxBeam, ShipLook.HullRailAt(t) })
                {
                    float w = ShipLook.HalfWidth(t, y, ShipLook.HullRailAt(t));
                    pts.Add(new Vector3(w, y, z));
                    pts.Add(new Vector3(-w, y, z));
                }
            }
            GameObject go = HullObject(name, pts.ToArray(), at, heading, Color.gray);
            go.GetComponent<MeshRenderer>().enabled = false;   // drawn by DressShip instead
            flag = DressShip(go.transform, name, colours, runOut);
            BoundedBuoyancyBody b = Floater(go, density);
            b.dragCoefficient = 0.05f;              // a ship's hull is shaped to slip through the water
            b.keepMomentumOnEntry = false;
            b.angularDrag = 1.5f;
            b.RebuildShape();
            b.centreOfMassOffset = new Vector3(0f, ShipLook.Keel + 1.2f - (float)b.Core.Shape.Centroid.y, 0f);
            b.RebuildShape();
            return b;
        }

        /// <summary>A dark mark where shot has holed a ship, three times the size of the hole, as the shot is drawn. It has
        /// no collider (the splinters thrown from the hole are told to ignore every collider on the ship).</summary>
        void MarkHole(Transform ship, Vector3 at, float area)
        {
            var mark = new GameObject("Hole");
            mark.transform.position = at;
            mark.transform.localScale = Vector3.one * 6f * Mathf.Sqrt(area / Mathf.PI);   // the sphere is 1 across
            mark.AddComponent<MeshFilter>().sharedMesh = holeMesh;
            mark.AddComponent<MeshRenderer>().sharedMaterial = Paint(new Color(0.06f, 0.05f, 0.04f));
            mark.transform.SetParent(ship, true);
        }

        /// <summary>Shot is drawn three times its size, so that it can be seen.</summary>
        void ShowShot(BoundedBuoyancyCannonball ball)
        {
            if (ball == null)
                return;
            Renderer own = ball.GetComponent<Renderer>();
            if (own != null)
                own.enabled = false;
            GameObject big = Shape(PrimitiveType.Sphere, Vector3.zero, Vector3.one, new Color(0.08f, 0.08f, 0.09f));
            DropCollider(big);
            big.transform.SetParent(ball.transform, false);
            big.transform.localPosition = Vector3.zero;
            big.transform.localScale = Vector3.one * 3f;
        }

        // FLOOD --------------------------------------------------------------------------------------------------------
        // Two ships, the same hole below the waterline on the starboard side. The one with a single big hold floods
        // right across and goes down; the one divided into six compartments floods only the holed one, lists a little
        // toward it, and stays afloat. Pumps on the divided ship bring her upright again.

        const float HoleArea = 0.25f;                                  // m², a ragged breach about 55 cm across
        static readonly Vector3 HoleAt = new Vector3(1f, -0.5f, -0.3f);  // starboard, low below the waterline
        readonly List<BoundedBuoyancyBody> floodShips = new List<BoundedBuoyancyBody>();
        readonly List<string> floodNames = new List<string>();
        float floodTime;
        bool floodHoled;

        void BuildFlood()
        {
            BoundedBuoyancyWater sea = Pool(Vector3.zero, new Vector3(300f, 20f, 300f), false);
            sea.waveHeight = 0.03f;
            sea.waveLength = 9f;
            sea.waveDirection = 95f;
            sea.surfaceColor = new Color(0.06f, 0.26f, 0.36f, 0.85f);
            floodShips.Clear();
            floodNames.Clear();
            floodTime = 0f;
            floodHoled = false;
            FloodShip(new Vector3(-4.2f, 0f, 0f), "One big hold", new[] { new[] { new Vector3(0f, 0f, 0.1f), new Vector3(1.75f, 1.15f, 5.3f) } });
            var six = new List<Vector3[]>();
            foreach (float side in new[] { -0.44f, 0.44f })
                foreach (float end in new[] { 1.85f, 0.1f, -1.65f })
                    six.Add(new[] { new Vector3(side, 0f, end), new Vector3(0.87f, 1.15f, 1.75f) });
            FloodShip(new Vector3(4.2f, 0f, 0f), "Six compartments", six.ToArray());
            orbit = false;
            eyeSet = true;
            eye = new Vector3(0f, 3.4f, -12.5f);
            focus = new Vector3(0f, 0.5f, 0f);
        }

        /// <summary>A small ship 6.6 m long and 2.2 m wide, ballasted low, its bow toward +Z, with its compartments given
        /// as [centre, size].</summary>
        void FloodShip(Vector3 at, string caption, Vector3[][] rooms)
        {
            var pts = new[]
            {
                new Vector3(-1.1f, 0.6f, 3f), new Vector3(1.1f, 0.6f, 3f), new Vector3(-1.1f, 0.6f, -2.8f), new Vector3(1.1f, 0.6f, -2.8f), new Vector3(0f, 0.6f, -3.6f),
                new Vector3(-0.9f, -0.6f, 2.8f), new Vector3(0.9f, -0.6f, 2.8f), new Vector3(-0.9f, -0.6f, -2.6f), new Vector3(0.9f, -0.6f, -2.6f), new Vector3(0f, -0.4f, -3.3f),
            };
            GameObject go = HullObject(caption, pts, at, 0f, new Color(0.42f, 0.27f, 0.16f));
            Part(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.6f, 0.1f), new Vector3(2.15f, 0.04f, 5.75f), new Color(0.78f, 0.66f, 0.46f));
            Part(PrimitiveType.Cube, go.transform, new Vector3(0f, 1f, 1.6f), new Vector3(1.3f, 0.8f, 1.6f), new Color(0.92f, 0.92f, 0.88f));
            BoundedBuoyancyBody b = Floater(go, 330f);
            b.angularDrag = 2f;
            b.RebuildShape();
            b.centreOfMassOffset = new Vector3(0f, -0.3f - (float)b.Core.Shape.Centroid.y, 0f);   // ballast low in the keel
            b.RebuildShape();
            foreach (Vector3[] r in rooms)
            {
                var room = new GameObject("Compartment");
                room.transform.SetParent(go.transform, false);
                room.transform.localPosition = r[0];
                room.AddComponent<BoundedBuoyancyCompartment>().size = r[1];
            }
            floodShips.Add(b);
            floodNames.Add(caption);
        }

        /// <summary>The same hole in both ships, in whichever compartment is at that point.</summary>
        void HoleShips()
        {
            if (floodHoled)
                return;
            floodHoled = true;
            foreach (BoundedBuoyancyBody b in floodShips)
            {
                if (b == null)
                    continue;
                Vector3 at = b.transform.TransformPoint(HoleAt);
                BoundedBuoyancyCompartment best = null;
                float bestD = float.PositiveInfinity;
                foreach (BoundedBuoyancyCompartment c in b.GetComponentsInChildren<BoundedBuoyancyCompartment>())
                {
                    float d = (c.transform.position - at).magnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = c;
                    }
                }
                if (best != null)
                    best.AddHole(at, HoleArea);
                GameObject mark = Shape(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.56f, 0.01f, 0.56f), new Color(0.05f, 0.04f, 0.03f));
                DropCollider(mark);
                mark.transform.SetParent(b.transform, false);
                mark.transform.localPosition = new Vector3(0.92f, HoleAt.y, HoleAt.z);
                mark.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        void FloodStep(float dt)
        {
            floodTime += dt;
            if (!floodHoled && floodTime >= 2f)
                HoleShips();
        }

        void FloodGUI()
        {
            if (!floodHoled && GUILayout.Button("Hole them now"))
                HoleShips();
            if (floodShips.Count > 1 && floodShips[1] != null && GUILayout.Button("Pumps on the divided ship"))
                foreach (BoundedBuoyancyCompartment c in floodShips[1].GetComponentsInChildren<BoundedBuoyancyCompartment>())
                {
                    c.PatchHoles();
                    c.pumpRate = 0.25f;
                }
            for (int i = 0; i < floodShips.Count; i++)
            {
                BoundedBuoyancyBody b = floodShips[i];
                if (b == null)
                    continue;
                float water = 0f;
                foreach (BoundedBuoyancyCompartment c in b.GetComponentsInChildren<BoundedBuoyancyCompartment>())
                    water += c.floodVolume;
                float heel = Mathf.Asin(Mathf.Clamp(-b.transform.right.y, -1f, 1f)) * Mathf.Rad2Deg;   // as the Godot demo
                float freeboard = b.transform.TransformPoint(new Vector3(0f, 0.6f, 0f)).y;
                string state = freeboard < -0.05f ? "sunk" : (Mathf.Abs(heel) > 45f ? "capsized" : "afloat");
                GUILayout.Label(floodNames[i] + ": " + water.ToString("0.0") + " t of water in, heel " + heel.ToString("+0;-0") + "°, " + state);
            }
            if (GUILayout.Button("Run again"))
                pendingScene = (int)Scene.Flood;
        }

        // CANNONS ------------------------------------------------------------------------------------------------------
        // A ship fires broadsides at another at close range. Fired low, round shot skips off the water; each gun kicks the
        // ship back as it fires, so a broadside heels her. Shot that reaches the other ship, deep with cargo, goes through
        // her 25 cm oak sides and out of the far side, holing the compartments it passes; the sea comes in at the lowest
        // holes, she settles, more of them go under, and in the end she sinks. Both ships are moored fore and aft, a
        // gentle sideways pull, so that the recoil and the shot do not push them out of range.

        const float Gap = 40f, Every = 6f, Laden = 480f, Mooring = 20f;
        BoundedBuoyancyBody gunShip, target;
        Vector3 gunShipHome, targetHome;
        readonly List<BoundedBuoyancyCannon> battery = new List<BoundedBuoyancyCannon>();
        readonly List<KeyValuePair<BoundedBuoyancyCannon, float>> ripple = new List<KeyValuePair<BoundedBuoyancyCannon, float>>();
        readonly HashSet<BoundedBuoyancyCannonball> counted = new HashSet<BoundedBuoyancyCannonball>();
        float gunElevation = -2f, cannonTime, nextBroadside;
        int targetHits, targetHoles;
        bool targetSunk, fromAstern = true;

        void BuildCannons()
        {
            BoundedBuoyancyWater sea = Pool(Vector3.zero, new Vector3(3000f, 60f, 3000f), false);
            sea.salinity = 35f;
            sea.waveHeight = 0.04f;
            sea.waveLength = 9f;
            sea.waveDirection = 20f;
            sea.splashMinSpeed = 1f;
            sea.surfaceColor = new Color(0.06f, 0.26f, 0.36f, 0.88f);
            battery.Clear();
            ripple.Clear();
            counted.Clear();
            cannonTime = 0f;
            nextBroadside = 0.8f;
            targetHits = targetHoles = 0;
            targetSunk = false;
            GameObject flag;
            gunShipHome = Vector3.zero;
            targetHome = new Vector3(Gap, 0f, 0f);
            gunShip = BrigBody(gunShipHome, 0f, 330f, "Navy", ShipLook.Navy, false, out flag);
            target = BrigBody(targetHome, 0f, Laden, "Merchant", ShipLook.Merchant, true, out flag);
            foreach (BoundedBuoyancyBody b in new[] { gunShip, target })
            {
                var planking = b.gameObject.AddComponent<BoundedBuoyancyHull>();
                planking.thickness = 0.25f;
                BoundedBuoyancyBody ship = b;
                planking.holed.AddListener((at, area, room) =>
                {
                    MarkHole(ship.transform, at, area);
                    if (ship == target)
                        targetHoles++;
                });
                Compartments(b.transform, new[] { 6.7f, 4.6f, 1.6f, -1.6f, -4.6f, -7.3f });
            }
            // Five 12-pounders on her starboard side, toward the other ship.
            for (int i = 0; i < 5; i++)
            {
                float z = -4f + 2f * i;
                float t = -z / (0.5f * ShipLook.Length);
                float y = ShipLook.Deck + 0.4f;
                var g = new GameObject("Gun");
                g.transform.SetParent(gunShip.transform, false);
                g.transform.localPosition = new Vector3(ShipLook.HalfWidth(t, y) + 0.4f, y, z);
                var gun = g.AddComponent<BoundedBuoyancyCannon>();
                gun.GunSize = BoundedBuoyancyCannon.Gun.Pounder12;
                gun.spread = 0.4f;
                gun.shotLifetime = 12f;
                gun.shotParent = root;
                gun.fired.AddListener(ShowShot);
                gun.fired.AddListener(ball =>
                {
                    if (ball != null)
                        ball.hit.AddListener((col, at, v) =>
                        {
                            if (target != null && col != null && col.attachedRigidbody == target.Body && counted.Add(ball))
                                targetHits++;
                        });
                });
                Part(PrimitiveType.Cylinder, g.transform, new Vector3(0f, 0f, -0.3f), new Vector3(0.22f, 0.7f, 0.22f), new Color(0.15f, 0.15f, 0.16f))
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                battery.Add(gun);
            }
            AimBattery();
            cam.farClipPlane = 3000f;
            orbit = false;
            eyeSet = true;
            eye = Vector3.zero;
        }

        void AimBattery()
        {
            foreach (BoundedBuoyancyCannon g in battery)
                if (g != null)
                    g.transform.localRotation = Quaternion.Euler(-gunElevation, 90f, 0f);
        }

        void CannonBroadside()
        {
            for (int i = 0; i < battery.Count; i++)
                ripple.Add(new KeyValuePair<BoundedBuoyancyCannon, float>(battery[i], cannonTime + 0.15f * i));
        }

        /// <summary>Pull a ship back toward her place by cables at bow and stern: a gentle spring, sideways only.</summary>
        void Moor(BoundedBuoyancyBody b, Vector3 home)
        {
            if (b == null)
                return;
            Rigidbody rb = b.Body;
            float w = 2f * Mathf.PI / Mooring;
            foreach (float end in new[] { -5f, 5f })
            {
                Vector3 p = b.transform.TransformPoint(new Vector3(0f, 0f, end));
                Vector3 v = rb.GetPointVelocity(p);
                Vector3 f = -0.5f * rb.mass * (w * w * (p - home - new Vector3(0f, 0f, end)) + w * v);
                f.y = 0f;
                rb.AddForceAtPosition(f, p, ForceMode.Force);
            }
        }

        void CannonsStep(float dt)
        {
            if (gunShip == null || target == null)
                return;
            cannonTime += dt;
            if (cannonTime >= nextBroadside && !targetSunk)
            {
                nextBroadside = cannonTime + Every;
                CannonBroadside();
            }
            for (int i = ripple.Count - 1; i >= 0; i--)
                if (cannonTime >= ripple[i].Value)
                {
                    BoundedBuoyancyCannon g = ripple[i].Key;
                    ripple.RemoveAt(i);
                    if (g != null)
                        g.Fire();
                }
            Moor(gunShip, gunShipHome);
            Moor(target, targetHome);
            targetSunk = targetSunk || target.transform.TransformPoint(new Vector3(0f, ShipLook.Deck, 0f)).y < -1f;
        }

        bool CannonsCamera()
        {
            if (gunShip == null)
                return false;
            // From astern of the firing ship, or from the other ship's quarter looking back at the broadside.
            Vector3 want = fromAstern ? new Vector3(-0.36f * Gap, 7.5f, -25f) : new Vector3(1.4f * Gap, 7f, -32f);
            Vector3 look = fromAstern ? new Vector3(0.6f * Gap, 5f, 4f) : new Vector3(0.3f * Gap, 4f, 4f);
            eye = want;
            focus = look;
            return true;
        }

        void CannonsGUI()
        {
            if (gunShip == null)
                return;
            if (GUILayout.Button("Fire a broadside"))
                CannonBroadside();
            GUILayout.Label("Guns at " + gunElevation.ToString("+0;-0") + "°");
            float e = GUILayout.HorizontalSlider(gunElevation, -3f, 10f);
            if (Mathf.Abs(e - gunElevation) > 0.01f)
            {
                gunElevation = Mathf.Round(e);
                AimBattery();
            }
            if (GUILayout.Button(Time.timeScale < 0.5f ? "Normal speed" : "Slow motion"))
                Time.timeScale = Time.timeScale < 0.5f ? 1f : 0.2f;
            if (GUILayout.Button(Time.timeScale > 2f ? "Normal speed " : "Four times faster"))
                Time.timeScale = Time.timeScale > 2f ? 1f : 4f;
            fromAstern = GUILayout.Toggle(fromAstern, " From astern of the firing ship");
            float water = 0f;
            foreach (BoundedBuoyancyCompartment c in target.GetComponentsInChildren<BoundedBuoyancyCompartment>())
                water += c.floodVolume;
            float heel = Mathf.Asin(Mathf.Clamp(target.transform.right.y, -1f, 1f)) * Mathf.Rad2Deg;
            string state = targetSunk ? "sunk" : (water > 25f ? "sinking" : (water > 1f ? "taking in water" : "afloat"));
            GUILayout.Label("The other ship: " + targetHits + " hits, " + targetHoles + " holes, " + water.ToString("0.0") + " t of water in, heel " + heel.ToString("+0.0;-0.0") + "°, " + state);
            if (GUILayout.Button("Run again"))
                pendingScene = (int)Scene.Cannons;
        }
    }
}
