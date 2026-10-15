// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A line: a fishing line, a mooring rope, a tow line, a tether. It pulls but never pushes.
    /// <para>Put it on an object where the line is made fast (a rod's tip, a bollard, a boat's bow) and set Body to what is
    /// on the other end (a float, a sinker, a hooked fish, a buoy), made fast at End Offset in that body's own frame. If the
    /// object is on or under a Rigidbody that is not kinematic, that body is pulled too (a float with a hook hanging under
    /// it, a boat towing another); otherwise this end holds fast.</para>
    /// <para>The line is elastic, as nylon is: stretched by x beyond its length L it pulls with T = E A x / L, E the
    /// stiffness of its material and A its cross-section (Hooke's law). Shorter than its length it hangs slack and pulls
    /// nothing. Set Length to reel it in or pay it out.</para>
    /// <para>Like the buoyancy, the pull is bounded: each step it is worked out with the stretch and the speed at the end of
    /// the step, against the inertia of what is on each end at the points where the line is made fast (see Core.Tether),
    /// so a stiff line on a light float neither shakes nor flings it, and it never pushes. A line pulled harder than
    /// Breaking Strength breaks; one with a Slip Tension runs out instead of pulling harder than that.</para>
    /// <para>The line's own weight and the water's drag on it are left out: nylon is little denser than water and a line
    /// is thin.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Line")]
    [DefaultExecutionOrder(100)]   // after the bodies, so their LastAcceleration is this step's
    public class BoundedBuoyancyLine : MonoBehaviour
    {
        [Tooltip("What is on the other end. Leave empty for a line made fast to nothing.")]
        public Rigidbody body;

        [Tooltip("Where the line is made fast on Body, in its own coordinates (from its Rigidbody's position, along its axes).")]
        public Vector3 endOffset = Vector3.zero;

        [Tooltip("Length of the line unstretched, m. Shorter reels it in, longer pays it out.")]
        [Min(0f)] public float length = 2f;

        [Tooltip("Diameter of the line, m. A fishing line is 0.1 to 0.5 mm.")]
        [Min(0.00001f)] public float diameter = 0.0003f;

        [Tooltip("Stiffness of its material (Young's modulus), GPa: nylon monofilament 2 to 4, braided polyethylene 60 to 100, steel wire 200, a rubber band 0.01.")]
        [Min(0.0001f)] public float stiffness = 2.5f;

        [Tooltip("Damping of the line's stretch: 0 springs back for ever, 1 settles without springing back.")]
        [Range(0f, 2f)] public float damping = 0.2f;

        [Tooltip("The pull at which the line runs out, N, as a reel's drag or a winch's slipping clutch lets it: pulled harder, the line pays out instead of pulling harder, and its length grows. 0: it never runs out.")]
        [Min(0f)] public float slipTension = 0f;

        [Tooltip("The pull at which the line breaks, N. 0 never breaks. A 0.3 mm nylon line breaks at about 60 N (6 kg).")]
        [Min(0f)] public float breakingStrength = 0f;

        [Header("Look")]
        [Tooltip("Show the line.")]
        public bool showLine = true;

        [Tooltip("Its colour.")]
        public Color colour = new Color(0.9f, 0.9f, 0.85f, 0.9f);

        [Tooltip("Width it is drawn with, m.")]
        [Min(0.0001f)] public float lineWidth = 0.004f;

        [Header("Events")]
        [Tooltip("The line has broken: the pull that broke it (N).")]
        public UnityEvent<float> snapped = new UnityEvent<float>();

        internal static readonly List<BoundedBuoyancyLine> active = new List<BoundedBuoyancyLine>();

        /// <summary>Every enabled Bounded Buoyancy Line.</summary>
        public static IReadOnlyList<BoundedBuoyancyLine> Active { get { return active; } }

        const int Segments = 24;

        Rigidbody holder;
        LineRenderer drawn;
        readonly Vector3[] points = new Vector3[Segments + 1];

        /// <summary>Pull along the line over the last physics step, N.</summary>
        public float Tension { get; private set; }
        /// <summary>How far the line is stretched beyond its length, m (negative while slack).</summary>
        public float Stretch { get; private set; }
        /// <summary>Distance between its ends, m.</summary>
        public float Span { get; private set; }
        /// <summary>The line has broken. Set it false to tie it again.</summary>
        public bool Broken { get; set; }
        /// <summary>The Rigidbody this end is made fast to (on or above this object), if any; it is pulled unless kinematic.</summary>
        public Rigidbody Holder { get { return holder; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
            holder = GetComponentInParent<Rigidbody>();
        }

        void OnDisable()
        {
            active.Remove(this);
            Tension = 0f;
            if (drawn != null)
                drawn.enabled = false;
        }

        void OnTransformParentChanged()
        {
            holder = GetComponentInParent<Rigidbody>();
        }

        void OnValidate()
        {
            length = Mathf.Max(length, 0f);
            diameter = Mathf.Max(diameter, 0.00001f);
            stiffness = Mathf.Max(stiffness, 0.0001f);
            damping = Mathf.Clamp(damping, 0f, 2f);
            slipTension = Mathf.Max(slipTension, 0f);
            breakingStrength = Mathf.Max(breakingStrength, 0f);
            lineWidth = Mathf.Max(lineWidth, 0.0001f);
        }

        /// <summary>Spring constant of the whole line, N/m: E A / L.</summary>
        public float SpringConstant()
        {
            return (float)Tether.SpringConstant(stiffness, diameter, length);
        }

        /// <summary>Pay out (positive) or reel in (negative) `metres` of line.</summary>
        public void PayOut(float metres)
        {
            length = Mathf.Max(length + metres, 0f);
        }

        /// <summary>Where the line is made fast at this end and at the far end (world), as the physics sees them.</summary>
        public Vector3[] Ends
        {
            get
            {
                Vector3 a = Near();
                return new[] { a, Far(a) };
            }
        }

        Vector3 Near()
        {
            // On a moving body, this end goes where the body's Rigidbody pose puts it (its Transform may be interpolated).
            if (holder != null && holder.transform != transform)
            {
                Transform t = holder.transform;
                return holder.position + holder.rotation * (Quaternion.Inverse(t.rotation) * (transform.position - t.position));
            }
            return holder != null ? holder.position : transform.position;
        }

        Vector3 Far(Vector3 near)
        {
            return body != null ? body.position + body.rotation * endOffset : near;
        }

        void FixedUpdate()
        {
            if (Bridge.FixedStep3D)
                Step(Time.fixedDeltaTime);
            else if (Physics.simulationMode == SimulationMode.Update)
                BoundedBuoyancySimulation.EnsureDriver();
        }

        /// <summary>One physics step of the line. Called from FixedUpdate when physics runs there (the default); otherwise
        /// BoundedBuoyancySimulation calls it, after the bodies.</summary>
        public void Step(float dt)
        {
            Tension = 0f;
            if (Broken || body == null || dt <= 0f)
                return;
            length = Mathf.Max(length, 0f);
            Vector3 a = Near();
            Vector3 b = Far(a);
            Vector3 d = b - a;
            Span = d.magnitude;
            Stretch = Span - length;
            if (Span < 1e-6f)
                return;
            Vector3 n = d / Span;   // from this end toward the far end
            bool aMoves = Moves(holder) && holder != body;
            bool bMoves = Moves(body);
            if (!(aMoves || bMoves))
                return;
            // Speed at which the ends draw apart now, and how that would change over the step without the line (gravity,
            // buoyancy, drag), so that a weight hanging still stretches it by just m g / k.
            double u = 0.0;
            double w = 0.0;   // 1 / inertia along the line, both ends together
            if (bMoves)
            {
                u += Vector3.Dot(body.GetPointVelocity(b), n) + Vector3.Dot(OtherAcceleration(body), n) * dt;
                w += InverseMassAt(body, b, n);
            }
            if (aMoves)
            {
                u -= Vector3.Dot(holder.GetPointVelocity(a), n) + Vector3.Dot(OtherAcceleration(holder), n) * dt;
                w += InverseMassAt(holder, a, n);
            }
            if (w <= 0.0)
                return;
            // Implicit (backward Euler) spring and damper: the pull J (an impulse) is the one the stretch and the speed at
            // the end of the step give. It never pushes.
            double paidOut;
            double j = Tether.Pull(Stretch, u, w, Tether.SpringConstant(stiffness, diameter, length), damping, dt, slipTension, out paidOut);
            if (j <= 0.0)
                return;
            length += (float)paidOut;
            Tension = (float)(j / dt);
            if (breakingStrength > 0f && Tension > breakingStrength)
            {
                Broken = true;
                float t = Tension;
                Tension = 0f;
                snapped.Invoke(t);
                return;
            }
            Vector3 pull = n * Tension;
            if (bMoves)
                body.AddForceAtPosition(-pull, b, ForceMode.Force);
            if (aMoves)
                holder.AddForceAtPosition(pull, a, ForceMode.Force);
        }

        static bool Moves(Rigidbody rb)
        {
            return rb != null && !rb.isKinematic && rb.mass > 0f;
        }

        /// <summary>The acceleration `rb` has over the step from everything but the line: a Bounded Buoyancy Body's weight,
        /// buoyancy and drag, or else gravity if it uses gravity.</summary>
        static Vector3 OtherAcceleration(Rigidbody rb)
        {
            var bb = rb.GetComponent<BoundedBuoyancyBody>();
            if (bb != null && bb.isActiveAndEnabled)
                return bb.LastAcceleration;
            return rb.useGravity ? Physics.gravity : Vector3.zero;
        }

        /// <summary>1 / the inertia `rb` puts up against a pull along `n` at `at`: its mass and its turning.</summary>
        static double InverseMassAt(Rigidbody rb, Vector3 at, Vector3 n)
        {
            return Tether.InverseMassAt(rb.mass, Bridge.V(at - rb.worldCenterOfMass), Bridge.V(n), Bridge.V(rb.inertiaTensor),
                Bridge.M(rb.rotation * rb.inertiaTensorRotation));
        }

        /// <summary>The water's surface under `p`, or NaN.</summary>
        float SurfaceAt(Vector3 p)
        {
            PhysicsScene scene = gameObject.scene.GetPhysicsScene();
            List<BoundedBuoyancyWater> waters = BoundedBuoyancyWater.active;
            for (int i = 0; i < waters.Count; i++)
            {
                BoundedBuoyancyWater wn = waters[i];
                if (wn.PhysicsScene == scene && wn.Contains(new Vector3(p.x, wn.transform.position.y, p.z)))
                    return wn.HeightAt(p);
            }
            return float.NaN;
        }

        void LateUpdate()
        {
            bool show = showLine && !Broken && body != null;
            if (!show)
            {
                if (drawn != null)
                    drawn.enabled = false;
                return;
            }
            if (drawn == null)
            {
                GameObject go = Visuals.Child(transform, "Line");
                drawn = go.AddComponent<LineRenderer>();
                drawn.useWorldSpace = true;
                drawn.positionCount = Segments + 1;
                drawn.shadowCastingMode = ShadowCastingMode.Off;
                drawn.receiveShadows = false;
                drawn.sharedMaterial = Visuals.Lines;
                drawn.textureMode = LineTextureMode.Stretch;
            }
            drawn.enabled = true;
            drawn.widthMultiplier = lineWidth;
            drawn.startColor = colour;
            drawn.endColor = colour;
            // Drawn from the Transforms, which may be interpolated.
            Vector3 a = transform.position;
            Vector3 b = body.transform.TransformPoint(endOffset);
            float d = (b - a).magnitude;
            // Slack line hangs in a curve as long as the line: a parabola of sag s has length about D + 8 s² / 3 D. It
            // lies along the water rather than sinking through it.
            float sag = d > 1e-4f ? Mathf.Sqrt(3f * d * Mathf.Max(length - d, 0f) / 8f) : 0f;
            float floor = float.NaN;
            if (sag > 0f)
            {
                float s = SurfaceAt(0.5f * (a + b));
                if (!float.IsNaN(s))
                    floor = Mathf.Min(s, Mathf.Min(a.y, b.y));
            }
            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * (4f * sag * t * (1f - t));
                if (!float.IsNaN(floor) && p.y < floor)
                    p.y = floor;
                points[i] = p;
            }
            drawn.SetPositions(points);
        }
    }
}
