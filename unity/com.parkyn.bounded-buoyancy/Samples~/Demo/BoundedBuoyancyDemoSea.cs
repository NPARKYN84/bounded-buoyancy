// Bounded Buoyancy for Unity: the 3D demo's sea scenes, FISHING, SUBMARINE and BATTLE, as in the Godot plugin's demo.
// Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using UnityEngine;

namespace BoundedBuoyancy.Samples
{
    public partial class BoundedBuoyancyDemo
    {
        // Shared ---------------------------------------------------------------------------------------------------

        bool eyeSet;               // the scene places the camera itself (at eye, looking at focus)
        Vector3 eye;
        string news = "";
        float newsLeft;

        void ResetSea()
        {
            eyeSet = false;
            news = "";
            newsLeft = 0f;
            rodTip = null;
            bobber = null;
            hookShot = null;
            mainLine = null;
            snood = null;
            fishes.Clear();
            fishLooks.Clear();
            fishKinds.Clear();
            arms.Clear();
            rodSegments = null;
            rodBend = wound = lastLength = 0f;
            fishUnder = false;
            underEye = underLook = Vector3.zero;
            subPropLook = null;
            subRudderLooks.Clear();
            subTurned = 0f;
            onLine = null;
            castOut = inFlight = false;
            castWait = 1f;
            strikeLeft = takeTime = idleTime = brokenTime = 0f;
            rodPitch = rodPitchTo = RestPitch;
            sub = null;
            subClock = 0f;
            subMain = subMainTo = subTrim = 0f;
            pirate = navy = null;
            battleTime = 0f;
            battleOver = false;
            overTime = 0f;
            shots.Clear();
            pirateHelm = 0f;
            subThrottle = 0f;
            RenderSettings.fog = false;
            Time.timeScale = 1f;
            gunShip = target = null;
            floodShips.Clear();
            if (cam != null)
                cam.farClipPlane = 500f;
        }

        void Say(string text, float seconds)
        {
            news = text;
            newsLeft = seconds;
        }

        static void SetVelocity(Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        GameObject Part(PrimitiveType type, Transform parent, Vector3 local, Vector3 scale, Color color)
        {
            GameObject go = Shape(type, Vector3.zero, scale, color);
            DropCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            return go;
        }

        /// <summary>Sea scenes set the camera themselves. Returns true if this one did.</summary>
        bool SeaCamera()
        {
            float k = 1f - Mathf.Exp(-3f * Time.deltaTime);
            if (scene == Scene.Fishing && rodTip != null)
            {
                Vector3 target = bobber != null ? bobber.Body.position : new Vector3(0f, 0f, 12f);
                if (onLine != null)
                    target = Vector3.Lerp(onLine.Body.position, target, 0.4f);
                else if (!castOut)
                    target = new Vector3(0f, 0f, 12f);
                target.y *= 0.5f;
                focus = Vector3.Lerp(focus, target, k);
                eye = new Vector3(-0.5f, 2.6f, -2.6f);
                RenderSettings.fog = false;
                if (!fishUnder)
                    underEye = underLook = Vector3.zero;
                if (fishUnder && hookShot != null)
                {
                    // Under the water, a little way from the hook (or the fish on it), looking at it; the light green.
                    Vector3 h = onLine != null ? onLine.transform.position : hookShot.transform.position;
                    Vector3 want = h + new Vector3(1.4f, 0.15f, -1.2f);
                    want.y = Mathf.Min(want.y, -0.25f);
                    float soft = 1f - Mathf.Exp(-2f * Time.deltaTime);
                    underEye = underEye == Vector3.zero ? want : Vector3.Lerp(underEye, want, soft);
                    underLook = underLook == Vector3.zero ? h : Vector3.Lerp(underLook, h, soft);
                    eye = underEye;
                    focus = underLook;
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = 0.12f;
                    RenderSettings.fogColor = new Color(0.20f, 0.40f, 0.36f);
                }
            }
            else if (scene == Scene.Submarine && sub != null)
            {
                Vector3 at = sub.Body.position;
                Vector3 fwd = sub.transform.forward;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                Vector3 want = subFromBelow
                    ? new Vector3(at.x, Mathf.Clamp(at.y - 8f, -SeaFloor + 3f, -2f), at.z) - side * 30f + fwd * 14f
                    : at - side * 24f - fwd * 8f + Vector3.up * 3f;
                eye = Vector3.Lerp(eye == Vector3.zero ? want : eye, want, k);
                focus = Vector3.Lerp(focus, at + fwd * (subFromBelow ? 6f : 0f), k);
                // Under the water the light is lost with depth.
                float depth = -eye.y;
                RenderSettings.fog = depth > 0f;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.024f;
                RenderSettings.fogColor = Color.Lerp(new Color(0.06f, 0.22f, 0.28f), new Color(0.01f, 0.04f, 0.07f), Mathf.Clamp01(depth / SeaFloor));
            }
            else if (scene == Scene.Battle && pirate != null)
            {
                Vector3 p = pirate.body.Body.position;
                Vector3 n = navy.body.Body.position;
                Vector3 want, look;
                if (battleSideView)
                {
                    Vector3 mid = 0.5f * (p + n);
                    Vector3 line = n - p;
                    line.y = 0f;
                    Vector3 across = Vector3.Cross(line, Vector3.up).normalized;
                    want = mid + across * Mathf.Max(0.9f * line.magnitude, 60f) + Vector3.up * 16f;
                    look = mid + Vector3.up * 2f;
                }
                else
                {
                    // Astern of the pirate and above, looking past her toward the navy brig.
                    Vector3 away = p - n;
                    away.y = 0f;
                    away = away.magnitude > 1f ? away.normalized : Vector3.back;
                    want = p + away * 34f + Vector3.up * 13f + Vector3.Cross(away, Vector3.up) * 8f;
                    look = Vector3.Lerp(p, n, 0.35f) + Vector3.up * 2f;
                }
                eye = Vector3.Lerp(eye == Vector3.zero ? want : eye, want, 1f - Mathf.Exp(-2f * Time.deltaTime));
                focus = Vector3.Lerp(focus, look, k);
            }
            else if (scene == Scene.Flood && eyeSet)
            {
                // A fixed view of both ships.
            }
            else if (scene == Scene.Cannons && CannonsCamera())
            {
            }
            else
                return false;
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation(focus - eye, Vector3.up);
            return true;
        }

        void SeaStep(float dt)
        {
            if (newsLeft > 0f)
                newsLeft -= dt;
            SwimFish();
            if (scene == Scene.Fishing)
                FishingStep(dt);
            else if (scene == Scene.Submarine)
                SubmarineStep(dt);
            else if (scene == Scene.Battle)
                BattleStep(dt);
            else if (scene == Scene.Flood)
                FloodStep(dt);
            else if (scene == Scene.Cannons)
                CannonsStep(dt);
        }

        void SeaGUI()
        {
            if (scene == Scene.Flood)
                FloodGUI();
            else if (scene == Scene.Cannons)
                CannonsGUI();
            if (scene == Scene.Fishing && rodTip != null)
            {
                // Drawn from last frame's choice: IMGUI changes a toggle in the mouse-up event, after its layout.
                bool wasAuto = autoAngler;
                autoAngler = GUILayout.Toggle(autoAngler, " Angler fishes by himself");
                fishUnder = GUILayout.Toggle(fishUnder, " Watch under the water");
                if (!wasAuto)
                {
                    if (!castOut && !inFlight && GUILayout.Button("Cast"))
                        Cast();
                    if (castOut && GUILayout.Button("Strike"))
                        Strike();
                    if (castOut && GUILayout.RepeatButton("Reel in") && mainLine != null)
                        mainLine.length = Mathf.Max(mainLine.length - ReelSpeed * Time.deltaTime, 1f);
                    if (castOut && GUILayout.RepeatButton("Give line") && mainLine != null)
                        mainLine.length += ReelSpeed * Time.deltaTime;
                }
                GUILayout.Label("Drag " + Mathf.RoundToInt(reelDrag) + " N");
                reelDrag = GUILayout.HorizontalSlider(reelDrag, 2f, 28f);
                if (mainLine != null)
                {
                    mainLine.slipTension = reelDrag;
                    GUILayout.Label("Pull on the line " + mainLine.Tension.ToString("0.0") + " N (breaks at " + Mathf.RoundToInt(LineBreaks) + ")");
                    GUILayout.Label("Line out " + mainLine.length.ToString("0.0") + " m");
                }
                if (onLine != null)
                    GUILayout.Label("Fish on: " + onLine.State + ", strength " + Mathf.RoundToInt(100f * onLine.Stamina) + "%");
                GUILayout.Label("Caught " + caught + " (" + caughtKg.ToString("0.00") + " kg)");
                if (newsLeft > 0f)
                    GUILayout.Label(news);
            }
            else if (scene == Scene.Submarine && sub != null)
            {
                bool wasAuto = subAuto;
                subAuto = GUILayout.Toggle(subAuto, " Dive and surface by itself");
                if (!wasAuto)
                {
                    if (GUILayout.Button("Flood main tanks"))
                        subMainTo = 1f;
                    if (GUILayout.Button("Blow main tanks"))
                        subMainTo = 0f;
                    if (GUILayout.RepeatButton("Trim: take in water"))
                        subTrim = Mathf.Min(subTrim + TrimRate * Time.deltaTime, TrimMax);
                    if (GUILayout.RepeatButton("Trim: pump out"))
                        subTrim = Mathf.Max(subTrim - TrimRate * Time.deltaTime, -TrimMax);
                    GUILayout.Label("Propeller " + Mathf.RoundToInt(100f * subThrottle) + "%");
                    subThrottle = GUILayout.HorizontalSlider(subThrottle, -0.5f, 1f);
                }
                subFromBelow = GUILayout.Toggle(subFromBelow, " Watch from below");
                float depth = -sub.Body.position.y;
                GUILayout.Label("Depth " + depth.ToString("0.0") + " m, rising " + Velocity(sub.Body).y.ToString("+0.00;-0.00") + " m/s");
                GUILayout.Label("Submarine " + sub.Density.ToString("0.0") + " kg/m³");
                GUILayout.Label("Water there " + subSea.DensityAt(sub.Body.position).ToString("0.0") + " kg/m³");
                GUILayout.Label("Main tanks " + Mathf.RoundToInt(100f * subMain) + "%, trim " + subTrim.ToString("+0;-0") + " kg");
                foreach (BoundedBuoyancyBody f in subFloats)
                    if (f != null)
                        GUILayout.Label("Float at " + (-f.Body.position.y).ToString("0") + " m: " + f.Density.ToString("0.0") + " kg/m³");
            }
            else if (scene == Scene.Battle && pirate != null)
            {
                battleSideView = GUILayout.Toggle(battleSideView, " Watch from the side");
                bool wasAuto = pirateAuto;
                pirateAuto = GUILayout.Toggle(pirateAuto, " The pirate sails herself");
                if (!wasAuto)
                {
                    GUILayout.Label("Rudder " + Mathf.RoundToInt(pirateHelm) + "°");
                    pirateHelm = GUILayout.HorizontalSlider(pirateHelm, -30f, 30f);
                    if (GUILayout.Button("Fire to port"))
                        Broadside(pirate, -1);
                    if (GUILayout.Button("Fire to starboard"))
                        Broadside(pirate, 1);
                }
                foreach (Ship s in new[] { pirate, navy })
                {
                    Vector3 v = Velocity(s.body.Body);
                    GUILayout.Label((s == pirate ? "Pirate: " : "Navy: ") + (new Vector2(v.x, v.z).magnitude * 1.943844f).ToString("0.0") + " kn, "
                        + s.hits + " hits, " + WaterIn(s).ToString("0.0") + " t of water in" + (s.struck ? ", struck" : ""));
                }
                Vector3 to = navy.body.Body.position - pirate.body.Body.position;
                GUILayout.Label("Range " + Mathf.RoundToInt(new Vector2(to.x, to.z).magnitude) + " m");
                if (newsLeft > 0f)
                    GUILayout.Label(news);
                if (GUILayout.Button("Start again"))
                    pendingScene = (int)Scene.Battle;
            }
        }

        // FISHING --------------------------------------------------------------------------------------------------
        // Float fishing from a jetty on a lake, the lake toward +Z. The float, the shot under it and the fish are
        // ordinary bodies; the line is 0.25 mm nylon (about 3 kg breaking strain) with the reel's drag as its slip
        // tension. The angler casts, strikes when a fish takes the bait, plays it on a tight line and lands it.

        const float JettyDeck = 0.8f;
        static readonly Vector3 RodHand = new Vector3(0.45f, 1.35f, 0.3f);
        const float RodLength = 3.6f, RestPitch = 38f, StrikePitch = 66f;
        const float FloatLong = 0.25f, FloatWide = 0.02f, FloatDensity = 280f, TipShowing = 0.025f, HookDepth = 1.3f;
        const float ReelSpeed = 0.7f, LineBreaks = 30f;
        Rigidbody rodTip;
        bool fishUnder;
        Vector3 underEye, underLook;
        BoundedBuoyancyBody bobber, hookShot;
        BoundedBuoyancyLine mainLine, snood;
        readonly List<BoundedBuoyancyFish> fishes = new List<BoundedBuoyancyFish>();
        BoundedBuoyancyFish onLine;
        float rodPitch = RestPitch, rodPitchTo = RestPitch, strikeLeft, takeTime, castWait, idleTime, brokenTime, reelDrag = 12f;
        bool castOut, inFlight, autoAngler = true;
        int caught;
        float caughtKg;

        void BuildFishing()
        {
            BoundedBuoyancyWater lake = Pool(Vector3.zero, new Vector3(120f, 6f, 120f));
            lake.waveHeight = 0.012f;
            lake.waveLength = 2.5f;
            lake.waveDirection = 35f;
            lake.splashMinSpeed = 0.6f;
            lake.surfaceColor = new Color(0.10f, 0.30f, 0.26f, 0.7f);
            // The jetty: a deck on posts, the angler at its end.
            Color wood = new Color(0.47f, 0.35f, 0.24f), old = new Color(0.31f, 0.24f, 0.18f);
            Shape(PrimitiveType.Cube, new Vector3(0f, JettyDeck - 0.05f, -2.6f), new Vector3(2.4f, 0.1f, 7.4f), wood);
            foreach (float z in new[] { 0.8f, -2.2f, -5.2f })
                foreach (float x in new[] { -1.1f, 1.1f })
                    Shape(PrimitiveType.Cylinder, new Vector3(x, JettyDeck - 1.6f, z), new Vector3(0.2f, 2.2f, 0.2f), old);
            // The rod: its tip is a kinematic body, so the line holds fast there; it is drawn bending toward the pull. The
            // angler sits on his seat box behind it, his tackle box beside him; the lake bed lies below.
            var tip = new GameObject("Rod tip");
            tip.transform.SetParent(root, false);
            rodTip = tip.AddComponent<Rigidbody>();
            rodTip.isKinematic = true;
            rodTip.interpolation = RigidbodyInterpolation.None;   // moved with the drawn rod, each step
            MakeAngler(new Vector3(RodHand.x - 0.12f, JettyDeck + 0.38f, RodHand.z - 0.18f));
            MakeRod();
            TackleBox(new Vector3(-0.55f, JettyDeck + 0.025f, 0.15f), -20f);
            LakeBed();
            PlaceRod(true);
            for (int i = 0; i < 5; i++)
                AddFish();
            MakeTackle();
            orbit = false;
            eyeSet = true;
            focus = new Vector3(0f, 0f, 12f);
            Say("The angler casts, waits for a bite, strikes, plays the fish and lands it.", 6f);
        }

        void PlaceRod(bool snap, float dt = 0f)
        {
            Vector3 tip = LayRod(dt);
            if (snap)
                rodTip.position = tip;
            else
                rodTip.MovePosition(tip);
        }

        /// <summary>The float (a stick float, shotted down to its tip), the hook with its shot, and the line from the rod's
        /// tip to the float, hanging from the rod ready to cast.</summary>
        void MakeTackle()
        {
            Vector3 tip = rodTip.position;
            GameObject f = Shape(PrimitiveType.Cylinder, tip + Vector3.down, new Vector3(FloatWide, 0.5f * FloatLong, FloatWide), new Color(0.1f, 0.16f, 0.1f));
            f.GetComponent<Renderer>().enabled = false;   // drawn by FloatLook, wider, so that it can be seen
            FloatLook(Unscaled(f.transform));
            bobber = Floater(f, FloatDensity);
            bobber.angularDrag = 6f;
            bobber.Body.detectCollisions = false;   // the tackle and the fish touch nothing, as in the Godot demo
            // The shot that cocks it: enough that only TipShowing of it stays out, less its own weight.
            float area = Mathf.PI * FloatWide * FloatWide * 0.25f;
            float spare = 1000f * area * (FloatLong - TipShowing) - FloatDensity * area * FloatLong;
            float r = Mathf.Pow(spare / (11340f - 1000f) * 3f / (4f * Mathf.PI), 1f / 3f);
            hookShot = Ball(r, 11340f, f.transform.position + Vector3.down * (0.5f * FloatLong + HookDepth), new Color(0.35f, 0.36f, 0.38f));
            hookShot.gameObject.AddComponent<BoundedBuoyancyBait>();
            HookAndWorm(Unscaled(hookShot.transform), r);
            hookShot.Body.detectCollisions = false;
            var s = new GameObject("Hook length");
            s.transform.SetParent(f.transform, false);
            s.transform.position = f.transform.position + Vector3.down * (0.5f * FloatLong);
            snood = s.AddComponent<BoundedBuoyancyLine>();
            snood.body = hookShot.Body;
            snood.length = HookDepth;
            snood.diameter = 0.0002f;
            snood.breakingStrength = 25f;
            snood.colour = new Color(0.85f, 0.88f, 0.85f, 0.7f);
            snood.lineWidth = 0.002f;
            var m = new GameObject("Line");
            m.transform.SetParent(rodTip.transform, false);
            mainLine = m.AddComponent<BoundedBuoyancyLine>();
            mainLine.body = bobber.Body;
            mainLine.endOffset = new Vector3(0f, 0.5f * FloatLong, 0f);
            mainLine.length = 1f;
            lastLength = 1f;
            mainLine.diameter = 0.00025f;
            mainLine.damping = 0.5f;
            mainLine.breakingStrength = LineBreaks;
            mainLine.slipTension = reelDrag;
            mainLine.colour = new Color(1f, 0.86f, 0.18f, 0.95f);   // a bright line, to be seen
            mainLine.lineWidth = 0.003f;
            castOut = inFlight = false;
            brokenTime = 0f;
        }

        void NewTackle()
        {
            foreach (BoundedBuoyancyBody b in new[] { bobber, hookShot })
                if (b != null)
                {
                    bodies.Remove(b);
                    Destroy(b.gameObject);
                }
            if (mainLine != null)
                Destroy(mainLine.gameObject);
            rodPitchTo = RestPitch;
            MakeTackle();
        }

        /// <summary>A fish out where the float lands, a perch, a trout or a bass: 20 to 50 cm long, as strong as its size;
        /// trout dash hardest, bass pull hardest.</summary>
        void AddFish()
        {
            int kind = rnd.Next(3);
            float len = kind == TackleLook.Perch ? Rand(0.2f, 0.34f) : Rand(0.3f, 0.5f);
            var at = new Vector3(Rand(-6f, 6f), -Rand(1f, 2.5f), Rand(9f, 20f));
            GameObject go = FishBody(at, len, kind);
            go.transform.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.detectCollisions = false;
            var f = go.AddComponent<BoundedBuoyancyFish>();
            f.mouth = new Vector3(0f, 0f, 0.5f * len);
            f.cruiseSpeed = 1f * len;
            f.burstSpeed = (kind == TackleLook.Trout ? 8.5f : 7f) * len;
            float kg = 1000f * Mathf.PI * Mathf.Pow(0.11f * len, 2f) * len * 0.85f;
            f.maxThrust = (kind == TackleLook.Bass ? 10f : 8f) * kg;
            f.endurance = (kind == TackleLook.Trout ? 6f : 8f) + 10f * kg;
            fishKinds[f] = kind;
            f.bitePull = 0.08f;
            f.appetite = 0.08f;
            f.minDepth = 0.6f;
            f.maxDepth = 3f;
            f.homeRange = 6f;
            f.senseRange = 5f;
            f.randomSeed = rnd.Next() | 1;
            f.nibbled.AddListener(b => { if (onLine == null && newsLeft <= 0f) Say("A nibble...", 1.5f); });
            f.tookBait.AddListener(b => { if (onLine == null) Say("The float's going under!", 2f); });
            f.droppedBait.AddListener(b => Say("Missed it: it dropped the bait.", 2.5f));
            f.hooked.AddListener(() => { onLine = f; Say("Fish on! Keep the line tight; reel when it tires.", 3f); });
            f.tired.AddListener(() => { if (onLine == f) Say("It's tiring: reel in!", 2.5f); });
            f.escaped.AddListener(() =>
            {
                if (onLine == f)
                {
                    onLine = null;
                    rodPitchTo = RestPitch;
                    Say("It threw the hook.", 3f);
                }
            });
            fishes.Add(f);
            bodies.Add(f);
            fishLooks.Add(new KeyValuePair<BoundedBuoyancyFish, Transform>(f, go.transform.Find("Look")));
        }

        readonly Dictionary<BoundedBuoyancyFish, int> fishKinds = new Dictionary<BoundedBuoyancyFish, int>();

        /// <summary>A fish's body: a capsule collider along Z, and its look.</summary>
        GameObject FishBody(Vector3 at, float len, int kind)
        {
            var go = new GameObject(TackleLook.Names[kind]);
            go.transform.SetParent(root, false);
            go.transform.position = at;
            var cc = go.AddComponent<CapsuleCollider>();
            cc.direction = 2;
            cc.radius = 0.11f * len;
            cc.height = len;
            FishLook(go.transform, kind, len);
            return go;
        }

        void Cast()
        {
            if (mainLine == null || mainLine.Broken || castOut || inFlight)
                return;
            float speed = Rand(9f, 13f);
            Vector3 aim = new Vector3(Rand(-0.12f, 0.12f), 0.55f, 1f).normalized;
            SetVelocity(bobber.Body, aim * speed);
            SetVelocity(hookShot.Body, aim * speed * 0.97f);
            rodPitch = 15f;
            rodPitchTo = RestPitch;
            inFlight = true;
            idleTime = 0f;
        }

        /// <summary>Strike: sweep the rod back, which snatches the line tight.</summary>
        void Strike()
        {
            if (!castOut || mainLine == null || mainLine.Broken)
                return;
            rodPitchTo = StrikePitch;
            strikeLeft = 0.6f;
        }

        void FishingStep(float dt)
        {
            if (rodTip == null)
                return;
            if (strikeLeft > 0f)
            {
                strikeLeft -= dt;
                if (strikeLeft <= 0f)
                    rodPitchTo = RestPitch + (onLine != null ? 20f : 0f);
            }
            rodPitch = Mathf.MoveTowards(rodPitch, rodPitchTo, (rodPitchTo > rodPitch ? 300f : 260f) * dt);
            PlaceRod(false, dt);
            if (mainLine == null || bobber == null)
                return;
            // A broken line: new tackle on the rod a few seconds later.
            if (mainLine.Broken || (snood != null && snood.Broken))
            {
                if (brokenTime == 0f)
                    Say("Snap! The " + (mainLine.Broken ? "line" : "hook length") + " broke.", 3f);
                brokenTime += dt;
                if (brokenTime > 3f && onLine == null)
                {
                    NewTackle();
                    castWait = 1f;
                }
                return;
            }
            Vector3[] e = mainLine.Ends;
            float span = (e[1] - e[0]).magnitude;
            if (inFlight)
            {
                // The bail is open: line runs off the reel as fast as the float pulls it.
                mainLine.length = Mathf.Max(mainLine.length, span + 2f);
                if (bobber.SubmergedVolume > 0f)
                {
                    inFlight = false;
                    castOut = true;
                    mainLine.length = span + 0.3f;
                }
            }
            else if (!castOut)
            {
                castWait -= dt;
                if (autoAngler && castWait <= 0f)
                    Cast();
            }
            else if (autoAngler)
                Angle(dt);
            if (castOut && mainLine.length <= 1.05f && onLine == null)
            {
                castOut = false;
                castWait = 1.5f;
            }
            Land();
        }

        /// <summary>The angler: strike when a fish has had the bait a moment, play it on a tight line and reel as it
        /// tires; reel in and cast again if nothing comes or the float drifts in.</summary>
        void Angle(float dt)
        {
            if (onLine == null)
            {
                idleTime += dt;
                bool taking = false;
                foreach (BoundedBuoyancyFish f in fishes)
                    if (f != null && f.State == BoundedBuoyancyFish.FishState.Take)
                        taking = true;
                takeTime = taking ? takeTime + dt : 0f;
                if (takeTime > 0.4f && strikeLeft <= 0f)
                {
                    Strike();
                    idleTime = 0f;
                }
                Vector3 fp = bobber.Body.position;
                if (idleTime > 60f || new Vector2(fp.x - RodHand.x, fp.z - RodHand.z).magnitude < 4f)
                    mainLine.length = Mathf.Max(mainLine.length - 2f * ReelSpeed * dt, 1f);
                return;
            }
            if (onLine.State == BoundedBuoyancyFish.FishState.Tired || mainLine.Tension < 0.2f * reelDrag)
                mainLine.length = Mathf.Max(mainLine.length - ReelSpeed * dt, 1f);
        }

        /// <summary>A hooked fish brought to the jetty, tired and near the surface, is landed (with a net on a pole).</summary>
        void Land()
        {
            if (onLine == null)
                return;
            Vector3 fp = onLine.Body.position;
            if (new Vector2(fp.x - RodHand.x, fp.z - RodHand.z).magnitude < 4.2f && fp.y > -0.8f && onLine.State == BoundedBuoyancyFish.FishState.Tired)
            {
                float kg = onLine.OwnMass;
                caught++;
                caughtKg += kg;
                BoundedBuoyancyFish f = onLine;
                int kind;
                Say("Landed a " + TackleLook.Names[fishKinds.TryGetValue(f, out kind) ? kind : 0] + "! " + kg.ToString("0.00") + " kg.", 4f);
                fishKinds.Remove(f);
                onLine = null;
                fishes.Remove(f);
                bodies.Remove(f);
                Destroy(f.gameObject);
                AddFish();
                NewTackle();
                castWait = 4f;
            }
        }

        // SUBMARINE ------------------------------------------------------------------------------------------------
        // Real sea water, layered as off a river mouth: brackish water at the top over cold, salty water from about 25 m
        // down, its density from its temperature, salt and pressure. Flooded, the submarine sinks until the water round it
        // is as dense as it is, and rests there on the salt layer; the trim tank moves it up or down a little; blown, it
        // comes up. Three drifting floats, each as dense as the water at one depth, hang at 10, 25 and 45 m.

        const float SubLong = 22f, SubRadius = 1.6f, SubRestsAt = 26f, MainFill = 14f, MainBlow = 9f;
        const float TrimRate = 60f, TrimMax = 1500f, SeaFloor = 70f;
        BoundedBuoyancyBody sub;
        BoundedBuoyancyThruster subProp;
        BoundedBuoyancyFoil subRudder;
        BoundedBuoyancyWater subSea;
        readonly List<BoundedBuoyancyBody> subFloats = new List<BoundedBuoyancyBody>();
        float subDry, subMainCapacity, subMain, subMainTo, subTrim, subClock, subVolume, subThrottle;
        bool subAuto = true, subFromBelow;

        void BuildSubmarine()
        {
            subSea = Pool(Vector3.zero, new Vector3(1500f, SeaFloor + 10f, 1500f));
            subSea.salinity = 28f;
            subSea.temperature = 16f;
            subSea.deepTemperature = 6f;
            subSea.deepSalinityChange = 6f;
            subSea.thermoclineDepth = 25f;
            subSea.waveHeight = 0.15f;
            subSea.waveLength = 14f;
            subSea.surfaceColor = new Color(0.05f, 0.20f, 0.26f, 0.85f);
            // Bow toward +Z: drawn by SubLook, with its sail forward of the middle and its rudder and propeller at the stern.
            var go = new GameObject("Submarine");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, 0.6f, 0f);
            var hullShape = go.AddComponent<CapsuleCollider>();
            hullShape.direction = 2;
            hullShape.radius = SubRadius;
            hullShape.height = SubLong;
            SubLook(go.transform, SubLong, SubRadius);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            sub = go.AddComponent<BoundedBuoyancyBody>();
            sub.addedMassCoefficient = 0.1f;     // a long body moving along its length takes little water with it
            sub.angularDrag = 2f;
            sub.dragCoefficient = 0.1f;
            sub.centreOfMassOffset = new Vector3(0f, -0.45f, 0f);   // its weight low in the hull keeps it upright
            bodies.Add(sub);
            var prop = new GameObject("Propeller");
            prop.transform.SetParent(go.transform, false);
            prop.transform.localPosition = new Vector3(0f, 0f, -0.5f * SubLong);
            subProp = prop.AddComponent<BoundedBuoyancyThruster>();
            subProp.maxThrust = 60000f;
            subProp.maxSpeed = 6f;
            subProp.diameter = 2f;
            var rudder = new GameObject("Rudder");
            rudder.transform.SetParent(go.transform, false);
            rudder.transform.localPosition = new Vector3(0f, 0f, -0.5f * SubLong + 0.5f);
            subRudder = rudder.AddComponent<BoundedBuoyancyFoil>();
            subRudder.area = 4f;
            subRudder.aspectRatio = 1.5f;
            var fin = new GameObject("Fin");
            fin.transform.SetParent(go.transform, false);
            fin.transform.localPosition = new Vector3(0f, SubRadius + 1.2f, 0.2f * SubLong);
            BoundedBuoyancyFoil finFoil = fin.AddComponent<BoundedBuoyancyFoil>();
            finFoil.area = 6f;
            finFoil.aspectRatio = 0.8f;
            // Its weight without ballast: with its main tanks full and no trim it is as dense as the water SubRestsAt
            // down. Its main tanks hold a tenth of its displacement, a submarine's reserve of buoyancy on the surface.
            subVolume = Mathf.PI * SubRadius * SubRadius * (SubLong - 2f * SubRadius) + 4f / 3f * Mathf.PI * Mathf.Pow(SubRadius, 3f);
            float neutral = subSea.DensityAt(new Vector3(0f, -SubRestsAt, 0f)) * subVolume;
            subMainCapacity = 0.1f * neutral;
            subDry = neutral - subMainCapacity;
            SubBallast();
            subFloats.Clear();
            float[] depths = { 10f, 25f, 45f };
            for (int i = 0; i < depths.Length; i++)
            {
                var at = new Vector3(-12f + 12f * i, -depths[i] + 6f, 30f);
                GameObject fl = Shape(PrimitiveType.Cylinder, at, new Vector3(0.24f, 0.8f, 0.24f), new Color(1f, 0.78f, 0.1f));
                BoundedBuoyancyBody b = Floater(fl, subSea.DensityAt(new Vector3(at.x, -depths[i], at.z)));
                b.angularDrag = 8f;
                b.centreOfMassOffset = new Vector3(0f, -0.5f, 0f);
                b.Body.detectCollisions = false;
                subFloats.Add(b);
            }
            for (int i = 0; i < 8; i++)
            {
                float len = Rand(0.5f, 0.9f);
                var at = new Vector3(Rand(-30f, 30f), -Rand(8f, 40f), Rand(15f, 60f));
                GameObject fg = FishBody(at, len, TackleLook.Mackerel);
                fg.GetComponent<CapsuleCollider>().radius = 0.1f * len;
                Rigidbody frb = fg.AddComponent<Rigidbody>();
                frb.interpolation = RigidbodyInterpolation.Interpolate;
                frb.detectCollisions = false;
                var f = fg.AddComponent<BoundedBuoyancyFish>();
                f.mouth = new Vector3(0f, 0f, 0.5f * len);
                f.cruiseSpeed = 1.2f * len;
                f.burstSpeed = 6f * len;
                f.maxThrust = 30f * len;
                f.minDepth = -at.y - 4f;
                f.maxDepth = -at.y + 4f;
                f.homeRange = 12f;
                f.appetite = 0f;
                f.randomSeed = rnd.Next() | 1;
                bodies.Add(f);
                fishLooks.Add(new KeyValuePair<BoundedBuoyancyFish, Transform>(f, fg.transform.Find("Look")));
            }
            orbit = false;
            eyeSet = true;
            eye = Vector3.zero;
            focus = go.transform.position;
        }

        /// <summary>Its density from its own weight and the water in its tanks.</summary>
        void SubBallast()
        {
            sub.Density = (subDry + subMain * subMainCapacity + subTrim) / subVolume;
        }

        void SubmarineStep(float dt)
        {
            if (sub == null)
                return;
            if (subAuto)
            {
                // Flood, rest on the salt layer, blow and surface, and again.
                subClock += dt;
                subMainTo = subClock > 3f && subClock < 120f ? 1f : 0f;
                if (subClock > 200f)
                    subClock = 0f;
                subThrottle = 0.5f;
                subRudder.Angle = 10f;   // round in a circle, so it stays in the sea
            }
            else
                subRudder.Angle = 0f;
            // Flooding is slower than blowing; both stop when the tanks are full or empty.
            if (subMain < subMainTo)
                subMain = Mathf.Min(subMain + dt / MainFill, subMainTo);
            else if (subMain > subMainTo)
                subMain = Mathf.Max(subMain - dt / MainBlow, subMainTo);
            subProp.throttle = subThrottle;
            SubBallast();
            // The propeller turns with the throttle, the rudder's blades with the rudder.
            subTurned += subThrottle * 500f * dt;
            if (subPropLook != null)
                subPropLook.localRotation = Quaternion.Euler(0f, 0f, subTurned);
            foreach (Transform r in subRudderLooks)
                r.localRotation = Quaternion.Euler(0f, -subRudder.Angle, 0f);
        }

        // BATTLE ---------------------------------------------------------------------------------------------------
        // A pirate brig against a navy brig, under sail. Their sails are foils that trim themselves to the wind (square
        // sails, so no nearer the wind than about 65 degrees), a keel stops them sliding sideways and a rudder turns them.
        // Five 12-pounders a side; shot goes through the other ship's 25 cm oak sides (Bounded Buoyancy Hull), holing the
        // compartments behind, and the sea comes in at the holes at and below the waterline. A ship with more than 35 t of
        // water in her strikes her colours. North is +Z; the wind blows from the north.

        class Ship
        {
            public BoundedBuoyancyBody body;
            public BoundedBuoyancyFoil rudder;
            public readonly List<BoundedBuoyancyFoil> sails = new List<BoundedBuoyancyFoil>();
            public readonly List<BoundedBuoyancyCannon>[] guns = { new List<BoundedBuoyancyCannon>(), new List<BoundedBuoyancyCannon>() };   // [0] port, [1] starboard
            public Ship enemy;
            public GameObject flag;
            public float elevation = 1f;
            public int hits;
            public readonly HashSet<BoundedBuoyancyCannonball> struckBy = new HashSet<BoundedBuoyancyCannonball>();
            public bool struck;
        }

        const float ShipLength = 15f, ShipBeam = 4.6f, Keel = -1.3f, MaxBeam = 0.7f, ShipDeck = 1.35f, ShipRail = 2.05f, Tuck = 0.45f;
        const float WindSpeed = 9f, Reload = 18f, SailArea = 115f, CloseHauled = 65f, StrikesAt = 35f;
        Ship pirate, navy;
        float battleTime, overTime, pirateHelm;
        bool battleOver, battleSideView, pirateAuto = true;
        readonly List<KeyValuePair<BoundedBuoyancyCannon, float>> shots = new List<KeyValuePair<BoundedBuoyancyCannon, float>>();

        void BuildBattle()
        {
            BoundedBuoyancyWater sea = Pool(Vector3.zero, new Vector3(3000f, 60f, 3000f), false);
            sea.salinity = 35f;
            sea.waveHeight = 0.25f;
            sea.waveLength = 22f;
            sea.waveDirection = 90f;
            sea.splashMinSpeed = 1f;
            sea.surfaceColor = new Color(0.06f, 0.26f, 0.36f, 0.85f);
            var wind = new GameObject("Wind").AddComponent<BoundedBuoyancyWind>();
            wind.transform.SetParent(root, false);
            wind.velocity = new Vector3(0f, 0f, -WindSpeed);
            wind.gustStrength = 0.15f;
            wind.directionSwing = 4f;
            // Both reaching across the wind on converging courses: the pirate heading east, the navy brig west, to the
            // south of her.
            pirate = Brig(new Vector3(0f, 0f, 0f), 90f, "Pirate", ShipLook.Pirate);
            navy = Brig(new Vector3(230f, 0f, -55f), 270f, "Navy", ShipLook.Navy);
            pirate.enemy = navy;
            navy.enemy = pirate;
            cam.farClipPlane = 3000f;
            orbit = false;
            eyeSet = true;
            eye = Vector3.zero;
            focus = Vector3.zero;
            Say("Two brigs under sail close to fight broadside to broadside.", 6f);
        }

        // The hull's shape as the physics has it (ShipLook): half its width at t along it (-1 at the bow, 1 at the stern)
        // and height y, the keel, the rail and the deck.
        static float HalfWidth(float t, float y) { return ShipLook.HalfWidth(t, y, ShipLook.HullRailAt(t)); }

        static float KeelAt(float t) { return ShipLook.KeelAt(t); }

        static float RailAt(float t) { return ShipLook.HullRailAt(t); }

        static float DeckAt(float t) { return ShipLook.HullDeckAt(t); }

        /// <summary>A brig at `at`, heading `heading` degrees clockwise from north (+Z), her bow toward her +Z: the hull
        /// (its convex hull, to float on and to be struck, drawn by DressShip), compartments, planking, guns, sails, a keel
        /// and a rudder.</summary>
        Ship Brig(Vector3 at, float heading, string name, ShipColours colours)
        {
            var s = new Ship();
            GameObject flag;
            BoundedBuoyancyBody b = BrigBody(at, heading, 400f, name, colours, false, out flag);
            GameObject go = b.gameObject;
            s.flag = flag;
            s.body = b;
            // The sails on each mast: foils that trim themselves to the wind (drawn by DressShip).
            foreach (float z in new[] { 0.22f * ShipLength, -0.1f * ShipLength })
            {
                var sailGo = new GameObject("Sail");
                sailGo.transform.SetParent(go.transform, false);
                sailGo.transform.localPosition = new Vector3(0f, ShipDeck + 7.5f, z);
                var sail = sailGo.AddComponent<BoundedBuoyancyFoil>();
                sail.medium = Core.Medium.Air;
                sail.area = SailArea;
                sail.aspectRatio = 1.2f;
                sail.autoTrim = true;
                sail.maxTrim = 70f;
                sail.trimAngleOfAttack = 18f;
                s.sails.Add(sail);
            }
            var planking = go.AddComponent<BoundedBuoyancyHull>();
            planking.thickness = 0.25f;
            planking.struck.AddListener((ball, p, through, outSpeed) =>
            {
                if (ball != null && s.struckBy.Add(ball))
                    s.hits++;
            });
            Compartments(go.transform, new[] { 6.7f, 4.6f, 1.6f, -1.6f, -4.6f, -7.3f });
            // Five guns a side at her ports.
            for (int i = 0; i < 5; i++)
            {
                float z = -4f + 2f * i;
                float t = -z / (0.5f * ShipLength);
                float y = ShipDeck + 0.4f;
                foreach (int side in new[] { -1, 1 })
                {
                    var g = new GameObject("Gun");
                    g.transform.SetParent(go.transform, false);
                    g.transform.localPosition = new Vector3(side * (HalfWidth(t, y) + 0.4f), y, z);
                    g.transform.localRotation = Quaternion.Euler(-1f, 90f * side, 0f);
                    var gun = g.AddComponent<BoundedBuoyancyCannon>();
                    gun.GunSize = BoundedBuoyancyCannon.Gun.Pounder12;
                    gun.spread = 0.5f;
                    gun.reloadTime = Reload;
                    gun.shotLifetime = 10f;
                    gun.shotParent = root;
                    Part(PrimitiveType.Cylinder, g.transform, new Vector3(0f, 0f, -0.3f), new Vector3(0.22f, 0.7f, 0.22f), new Color(0.15f, 0.15f, 0.16f))
                        .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    s.guns[side > 0 ? 1 : 0].Add(gun);
                }
            }
            // A keel (the hull's long underwater side) and a rudder.
            Color wood = new Color(colours.Bottom[0], colours.Bottom[1], colours.Bottom[2]);
            BoundedBuoyancyFoil keel = BoatFoil(b, new Vector3(0f, Keel + 0.5f, 0f), 16f, 0.6f, Core.Medium.Water, 0f, wood, 0.05f);
            keel.name = "Keel";
            s.rudder = BoatFoil(b, new Vector3(0f, Keel + 0.8f, -0.5f * ShipLength + 0.2f), 3.5f, 1.5f, Core.Medium.Water, 0.4f, wood, 0.1f);
            SetVelocity(b.Body, Quaternion.Euler(0f, heading, 0f) * Vector3.forward * 2.5f);
            return s;
        }

        /// <summary>Between each pair of stations (z, bow first), the hold each side of the middle, from just above the keel
        /// to the deck, as a box holding as much as the hull does there; and above the deck the well inside the bulwarks,
        /// open to the sea over the rail.</summary>
        void Compartments(Transform ship, float[] stations)
        {
            for (int k = 0; k < stations.Length - 1; k++)
            {
                float z0 = stations[k], z1 = stations[k + 1];
                float lo = float.NegativeInfinity, deck = float.PositiveInfinity, rail = float.PositiveInfinity;
                for (int i = 0; i <= 8; i++)
                {
                    float t = -Mathf.Lerp(z0, z1, i / 8f) / (0.5f * ShipLength);
                    lo = Mathf.Max(lo, KeelAt(t) + 0.2f);
                    deck = Mathf.Min(deck, DeckAt(t));
                    rail = Mathf.Min(rail, RailAt(t));
                }
                float held = 0f;
                const int n = 16;
                for (int i = 0; i < n; i++)
                {
                    float t = -Mathf.Lerp(z0, z1, (i + 0.5f) / n) / (0.5f * ShipLength);
                    for (int j = 0; j < n; j++)
                        held += Mathf.Max(HalfWidth(t, Mathf.Lerp(lo, deck, (j + 0.5f) / n)) - 0.08f, 0f);
                }
                float wide = held / (n * n);
                float wellWide = float.PositiveInfinity;
                for (int i = 0; i <= 8; i++)
                    wellWide = Mathf.Min(wellWide, HalfWidth(-Mathf.Lerp(z0, z1, i / 8f) / (0.5f * ShipLength), rail) - 0.1f);
                foreach (float side in new[] { -1f, 1f })
                {
                    var room = new GameObject("Hold");
                    room.transform.SetParent(ship, false);
                    room.transform.localPosition = new Vector3(side * 0.5f * wide, 0.5f * (lo + deck), 0.5f * (z0 + z1));
                    room.AddComponent<BoundedBuoyancyCompartment>().size = new Vector3(wide, deck - lo, Mathf.Abs(z1 - z0));
                    var well = new GameObject("Well");
                    well.transform.SetParent(ship, false);
                    well.transform.localPosition = new Vector3(side * 0.5f * wellWide, 0.5f * (deck + rail), 0.5f * (z0 + z1));
                    var w = well.AddComponent<BoundedBuoyancyCompartment>();
                    w.size = new Vector3(wellWide, rail - deck, Mathf.Abs(z1 - z0));
                    w.holes.Add(new Vector4(0f, 0.5f * (rail - deck), 0f, 0.5f * wellWide * Mathf.Abs(z1 - z0)));
                }
            }
        }

        static float Heading(Vector3 v)
        {
            return Mathf.Repeat(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, 360f);
        }

        static float Heading(Ship s)
        {
            return Heading(s.body.transform.forward);
        }

        float WaterIn(Ship s)
        {
            float w = 0f;
            foreach (BoundedBuoyancyCompartment c in s.body.GetComponentsInChildren<BoundedBuoyancyCompartment>())
                w += c.floodVolume;
            return w;
        }

        bool Sunk(Ship s)
        {
            return s.body.transform.TransformPoint(new Vector3(0f, ShipDeck, 0f)).y < -1.5f;
        }

        static int Loaded(Ship s, int side)
        {
            int n = 0;
            foreach (BoundedBuoyancyCannon g in s.guns[side > 0 ? 1 : 0])
                if (g.IsLoaded)
                    n++;
            return n;
        }

        /// <summary>Fire a broadside to `side` (1 starboard, -1 port): each loaded gun in turn, a fifth of a second apart.</summary>
        void Broadside(Ship s, int side)
        {
            if (s.struck || Sunk(s))
                return;
            // After any already waiting to fire, and not twice.
            float at = battleTime;
            var waiting = new HashSet<BoundedBuoyancyCannon>();
            foreach (KeyValuePair<BoundedBuoyancyCannon, float> q in shots)
            {
                waiting.Add(q.Key);
                at = Mathf.Max(at, q.Value + 0.2f);
            }
            foreach (BoundedBuoyancyCannon g in s.guns[side > 0 ? 1 : 0])
                if (g.IsLoaded && !waiting.Contains(g))
                {
                    shots.Add(new KeyValuePair<BoundedBuoyancyCannon, float>(g, at));
                    at += 0.2f;
                }
        }

        /// <summary>Steer toward heading `want`, but no nearer the wind (from the north) than a square-rigger can sail.</summary>
        void Steer(Ship s, float want)
        {
            float off = Mathf.DeltaAngle(0f, want);
            if (Mathf.Abs(off) < CloseHauled)
                off = CloseHauled * (off >= 0f ? 1f : -1f);
            float error = Mathf.DeltaAngle(Heading(s), off);
            s.rudder.Angle = Mathf.Clamp(1.2f * error - 30f * s.body.Body.angularVelocity.y, -30f, 30f);
        }

        /// <summary>A captain: close in when far off, then sail across her bearing with loaded guns toward her (turning to
        /// show her the other side while one side reloads), and fire when she is abeam.</summary>
        void Captain(Ship s)
        {
            Ship e = s.enemy;
            if (s.struck || e.struck || Sunk(s))
                return;
            Vector3 to = e.body.Body.position - s.body.Body.position;
            to.y = 0f;
            float range = to.magnitude;
            Vector3 lead = to + Velocity(e.body.Body) * Mathf.Clamp(range / 8f, 0f, 15f);
            lead.y = 0f;
            float bearing = Heading(lead);
            int side = Mathf.DeltaAngle(Heading(s), bearing) > 0f ? 1 : -1;
            if (Loaded(s, side) == 0 && Loaded(s, -side) > 0)
                side = -side;
            float across = range > 120f ? 20f : (range < 50f ? 125f : 90f);
            Steer(s, bearing - side * across);
            // Guns laid at her waterline, a little below it, plus the drop over the range.
            float gunY = s.body.transform.TransformPoint(new Vector3(0f, ShipDeck + 0.4f, 0f)).y;
            float aimY = e.body.Body.position.y + 0.1f;
            s.elevation = Mathf.Clamp(Mathf.Rad2Deg * (Mathf.Atan2(aimY - gunY, Mathf.Max(range, 1f)) + 0.5f * Mathf.Asin(Mathf.Clamp01(9.8f * range / (488f * 488f)))), -6f, 6f);
            float rel = Mathf.DeltaAngle(Heading(s), Heading(to));
            int facing = rel > 0f ? 1 : -1;
            if (range < 160f && Mathf.Abs(Mathf.Abs(rel) - 90f) < 8f && Loaded(s, facing) >= 3)
                Broadside(s, facing);
        }

        /// <summary>Lay each gun against the horizon, as gun crews did with the wedge under the breech: the ship's heel taken
        /// out, within 8 degrees down and 12 up.</summary>
        static void Quoins(Ship s)
        {
            for (int k = 0; k < 2; k++)
                foreach (BoundedBuoyancyCannon g in s.guns[k])
                {
                    float up = Mathf.Asin(Mathf.Clamp(g.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                    float now = -Mathf.DeltaAngle(0f, g.transform.localEulerAngles.x);
                    float lay = Mathf.Clamp(now + (s.elevation - up), -8f, 12f);
                    g.transform.localRotation = Quaternion.Euler(-lay, k == 1 ? 90f : -90f, 0f);
                }
        }

        void BattleStep(float dt)
        {
            if (pirate == null)
                return;
            battleTime += dt;
            if (battleTime < 1f)
                return;    // hold fire while the ships settle
            Captain(navy);
            if (pirateAuto)
                Captain(pirate);
            else
                pirate.rudder.Angle = pirateHelm;
            Quoins(pirate);
            Quoins(navy);
            for (int i = shots.Count - 1; i >= 0; i--)
                if (battleTime >= shots[i].Value)
                {
                    BoundedBuoyancyCannon g = shots[i].Key;
                    shots.RemoveAt(i);
                    if (g != null && g.IsLoaded)
                        g.Fire();
                }
            foreach (Ship s in new[] { pirate, navy })
                if (Sunk(s))
                    foreach (BoundedBuoyancyFoil sail in s.sails)
                        sail.area = 0f;    // a sinking ship's sails come down as she goes
            if (!battleOver)
            {
                foreach (Ship s in new[] { navy, pirate })
                    if (Sunk(s) || WaterIn(s) > StrikesAt)
                    {
                        battleOver = true;
                        s.struck = true;
                        s.flag.SetActive(false);
                        shots.RemoveAll(q => q.Key == null || s.guns[0].Contains(q.Key) || s.guns[1].Contains(q.Key));
                        foreach (BoundedBuoyancyFoil sail in s.sails)
                            sail.area = 0.25f * SailArea;
                        Say((s == navy ? "The navy brig" : "The pirate") + (Sunk(s) ? " sinks." : " strikes her colours.") + (s == navy ? " The pirates have won." : " The navy has won."), 20f);
                        break;
                    }
            }
            else
            {
                overTime += dt;
                if (overTime > 30f)
                    pendingScene = (int)Scene.Battle;
            }
        }
    }
}
