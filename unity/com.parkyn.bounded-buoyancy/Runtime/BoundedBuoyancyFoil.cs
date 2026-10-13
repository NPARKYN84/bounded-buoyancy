// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A thin flat foil on a Bounded Buoyancy Body: a keel, centreboard, rudder, fin, sail or wing.
    /// <para>Put it on a GameObject anywhere under the body. Its span runs along its own Y axis (up a keel or a mast)
    /// and its chord along its Z axis, leading edge forward (+Z) and trailing edge aft; its faces look along X. The flow
    /// past it makes a force square to its faces, lift and pressure drag together as for a flat plate, and a little
    /// friction along it, at its centre of effort: so a keel stops a boat sliding sideways, a rudder turns it, and a
    /// sail drives it across the wind and up into it.</para>
    /// <para>The force square to the faces is ½ ρ A q² C_N(α), with α the angle between the flow and the foil and
    /// C_N(α) = k sin α cos α + 1.2 sin² α, no more than 1.4 (a stalled plate); k is the lift slope of a wing of the
    /// foil's aspect ratio (Helmbold). Like the buoyancy, it is bounded: worked out with the flow at the end of the
    /// step, against the inertia the body has at the foil, it can stop the flow across the foil but never throw the
    /// body back the other way, however large the foil or light the body.</para>
    /// <para>In the water a foil acts on its part under the surface; in the air, on its part above it, with the wind.
    /// Turn a rudder with Angle (positive swings the trailing edge to starboard, +X, which turns a boat with the rudder
    /// at its stern to starboard). A sail with Auto Trim swings itself away from the wind it feels; put its GameObject
    /// at the mast and set Effort Offset to where its push acts. Child objects (a mesh, say) turn with it.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Foil")]
    [DisallowMultipleComponent]
    public class BoundedBuoyancyFoil : MonoBehaviour
    {
        [Tooltip("Water: a keel, centreboard, rudder or fin, acting on its part under the water. Air: a sail, wing or tail fin, acting on its part above the water, with the wind.")]
        public Medium medium = Medium.Water;

        [Tooltip("Area of one face, m².")]
        [Min(0f)] public float area = 0.1f;

        [Tooltip("Span² / area: about 1 to 2 for a keel, 2 to 3 for a rudder, 3 to 5 for a sail. Longer, narrower foils give more lift for their area.")]
        [Min(0.05f)] public float aspectRatio = 2f;

        [Tooltip("Angle the foil is turned about its span from where it was placed, degrees: a rudder's angle. Positive swings the trailing edge towards +X (starboard). With Auto Trim on, the foil sets it itself.")]
        [SerializeField, Range(-180f, 180f)] float angle;

        [Tooltip("Distance from this object (the pivot: a mast, a rudder stock) to where the force acts, along the chord towards the trailing edge, metres. 0: this object is the centre of the foil. A sail pushes about a third of its chord aft of the mast.")]
        [Min(0f)] public float effortOffset;

        [Header("Sail")]
        [Tooltip("Trim like a sail: swing about the span, away from the wind, to meet the flow at Trim Angle Of Attack, no further than Max Trim. Head to wind it lines up with the wind and makes no lift; running before the wind it swings out to Max Trim.")]
        public bool autoTrim;

        [Tooltip("Angle of attack a trimmed sail keeps to the flow, degrees.")]
        [Range(0f, 60f)] public float trimAngleOfAttack = 15f;

        [Tooltip("How far a trimmed sail may swing either way, degrees (the boom's limit).")]
        [Range(0f, 180f)] public float maxTrim = 85f;

        readonly Foil3D core = new Foil3D();
        Quaternion rest = Quaternion.identity;
        bool placed;
        BoundedBuoyancyBody body;

        /// <summary>Angle the foil is turned about its span from where it was placed (degrees): a rudder's angle.
        /// Positive swings the trailing edge to +X.</summary>
        public float Angle
        {
            get { return angle; }
            set
            {
                angle = value;
                if (placed)
                    transform.localRotation = rest * Quaternion.AngleAxis(-angle, Vector3.up);
            }
        }

        /// <summary>Mean force on the foil over the last physics step (world, N).</summary>
        public Vector3 Force { get { return Bridge.U(core.Force); } }
        /// <summary>Fraction of the foil in its medium this step.</summary>
        public float Immersion { get { return (float)core.Immersion; } }
        /// <summary>Angle between the flow and the foil this step, degrees.</summary>
        public float AngleOfAttack { get { return (float)core.AngleOfAttack; } }
        /// <summary>Length of the span (m).</summary>
        public float SpanLength { get { Sync(); return (float)core.SpanLength; } }
        /// <summary>Length of the chord (m).</summary>
        public float ChordLength { get { Sync(); return (float)core.ChordLength; } }
        /// <summary>The engine-free foil, for reading anything else.</summary>
        public Foil3D Core { get { return core; } }

        void Awake()
        {
            rest = transform.localRotation;
            placed = true;
            if (angle != 0f)
                Angle = angle;
        }

        void OnEnable()
        {
            body = GetComponentInParent<BoundedBuoyancyBody>();
            if (body != null)
                body.AddAppendage(this);
            else
                Debug.LogWarning("Bounded Buoyancy Foil '" + name + "' needs a Bounded Buoyancy Body on this object or one above it.", this);
        }

        void OnDisable()
        {
            if (body != null)
                body.RemoveAppendage(this);
            body = null;
        }

        void OnValidate()
        {
            area = Mathf.Max(area, 0f);
            aspectRatio = Mathf.Max(aspectRatio, 0.05f);
            if (Application.isPlaying && placed)
                Angle = angle;
        }

        void Sync()
        {
            core.Medium = medium;
            core.Area = area;
            core.AspectRatio = aspectRatio;
            core.AutoTrim = autoTrim;
            core.TrimAngleOfAttack = trimAngleOfAttack;
            core.MaxTrim = maxTrim;
            core.EffortOffset = effortOffset;
        }

        /// <summary>Hand the body the foil for this step: its settings and where it was placed, in world space. The
        /// span is passed reversed, so that turning about it by Angle matches Unity's own (left-handed) turn about
        /// -Y, and a positive angle swings the trailing edge to +X as in the Godot plugin.</summary>
        internal Foil3D Prepare()
        {
            Sync();
            Transform parent = transform.parent;
            Quaternion placedWorld = (parent != null ? parent.rotation : Quaternion.identity) * (placed ? rest : transform.localRotation);
            core.Pivot = Bridge.V(transform.position);
            core.Span = Bridge.V(placedWorld * Vector3.down);
            core.RestChord = Bridge.V(placedWorld * Vector3.back);
            core.Angle = angle;
            return core;
        }

        /// <summary>After the step: a self-trimming sail shows the angle it chose.</summary>
        internal void Finish()
        {
            if (autoTrim && Mathf.Abs((float)core.Angle - angle) > 1e-4f)
                Angle = (float)core.Angle;
        }

        void OnDrawGizmosSelected()
        {
            float span = SpanLength, chord = ChordLength;
            Gizmos.color = medium == Medium.Water ? new Color(0.2f, 0.5f, 0.9f, 0.5f) : new Color(0.95f, 0.95f, 0.85f, 0.5f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            // With an effort offset this object is the pivot at the leading edge, and the foil lies aft of it (-Z).
            Gizmos.DrawCube(new Vector3(0f, 0f, effortOffset > 0f ? -0.5f * chord : 0f), new Vector3(0.01f, span, chord));
            Gizmos.matrix = Matrix4x4.identity;
            if (Application.isPlaying && core.Force.LengthSquared > 0.0)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
                Vector3 p = Bridge.U(core.CentreOfEffort);
                Gizmos.DrawLine(p, p + Force * 0.002f);
            }
        }
    }
}
