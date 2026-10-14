// Bounded Buoyancy for Unity, 2D. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;
using UnityEngine.Events;

namespace BoundedBuoyancy
{
    /// <summary>
    /// Floats a Rigidbody2D, in a side view: the same method as Bounded Buoyancy Body. Buoyancy drives no fall
    /// faster than g and no rise faster than g/C, the first frame after a release is the classical value, and
    /// buoyancy acts at the centre of the immersed area, so bodies tip, roll and right themselves. Out of the water
    /// the body is in air; water can flow and have waves, wind blows, and entries make splashes.
    /// <para>Give the GameObject a Rigidbody2D and colliders (circle, box, capsule or polygon; children count), add
    /// this component and set its density, then add a Bounded Buoyancy Water 2D (or set waterLevel). Lengths are in
    /// metres, y up, as in Unity 2D; thickness only turns areas into kilograms, so collisions with other bodies are
    /// fair.</para>
    /// </summary>
    [AddComponentMenu("Physics 2D/Bounded Buoyancy Body 2D")]
    [RequireComponent(typeof(Rigidbody2D))]
    [DisallowMultipleComponent]
    public class BoundedBuoyancyBody2D : MonoBehaviour
    {
        [Tooltip("Bounded: the added mass of the water taken along is in the inertia. Usual: buoyancy on the body's own mass (for comparison).")]
        public BoundedBuoyancyBody.Model model = BoundedBuoyancyBody.Model.Bounded;

        [Tooltip("Density of the body, kg/m³ (water is 1000). It can be changed while running.")]
        [SerializeField, Min(0.001f)] float density = 500f;

        [Tooltip("Added-mass coefficient C of the shape in 2D: 1 for a circle, more for a flat plate moving face-on. Buoyancy-driven rising is capped at g/C.")]
        [Range(0f, 5f)] public float addedMassCoefficient = 1f;

        [Tooltip("Height (y) of a flat water surface, used when the scene has no Bounded Buoyancy Water 2D.")]
        public float waterLevel = 0f;

        [Tooltip("Density of the water for Water Level (a Bounded Buoyancy Water 2D uses its own).")]
        [Min(1f)] public float waterDensity = 1000f;

        [Tooltip("Quadratic drag coefficient, in the water and in the air (about 1 for a circle in 2D).")]
        [Range(0f, 3f)] public float dragCoefficient = 1f;

        [Tooltip("Damping of spin in the water, per second when fully submerged; in air, in proportion to how light the body is against the air.")]
        [Min(0f)] public float angularDrag = 3f;

        [Tooltip("Keep momentum when the added mass grows as the body enters the water.")]
        public bool keepMomentumOnEntry = true;

        [Tooltip("Wave-making damping: a body bobbing at the surface sends out ripples (here to the left and right) that carry its energy away, so it settles: within a few bobs if it is wide and shallow (a raft, a boat), slowly if it is deep and narrow. It acts on bobbing relative to the surface, so riding waves is not held back. 1 is the physics; 0 leaves it out; other values scale it.")]
        [Range(0f, 3f)] public float waveDamping = 1f;

        [Header("Air")]
        [Tooltip("Density of the air, kg/m³ (1.2). 0 leaves air out.")]
        [Min(0f)] public float airDensity = 1.2f;

        [Tooltip("Height (m) over which the air's density halves, measured up from y = 0. 0 keeps it the same.")]
        [Min(0f)] public float airHalfHeight = 0f;

        [Tooltip("Length of a sail for the wind to push on, in metres (0 for none).")]
        [Min(0f)] public float sailSize = 0f;

        [Tooltip("Where the wind pushes the sail, in the body's own coordinates.")]
        public Vector2 sailCentre = new Vector2(0f, 0.4f);

        [Header("Mass")]
        [Tooltip("Depth of the body into the screen, in metres, used only to turn areas into kilograms.")]
        [Min(0.001f)] public float thickness = 0.2f;

        [Tooltip("Put the Rigidbody2D's centre of mass at the centre of the shape's area (plus the offset below). Turn off to keep its own.")]
        public bool setCentreOfMass = true;

        [Tooltip("Moves the centre of mass from the centre of the area, in the body's own coordinates. Lower it to give a boat ballast.")]
        public Vector2 centreOfMassOffset = Vector2.zero;

        [Tooltip("Work out the rotational inertia from the shape and density. Turn off to keep the Rigidbody2D's own; it is then held fixed while the mass changes.")]
        public bool setInertia = true;

        [Header("Events")]
        [Tooltip("The body has just touched the water: speed through the water (m/s) and the point on the surface.")]
        public UnityEvent<float, Vector2> waterEntered = new UnityEvent<float, Vector2>();

        [Tooltip("The body has just come wholly out of the water: speed (m/s) and the point on the surface.")]
        public UnityEvent<float, Vector2> waterExited = new UnityEvent<float, Vector2>();

        Rigidbody2D rb;
        readonly BodyCore2D core = new BodyCore2D();
        bool setUp;
        bool warned;
        float appliedMass = -1f;
        float appliedDamp = -1f;
        float inertia;
        float appliedDensity;
        BoundedBuoyancyWater2D water;
        float savedGravityScale, savedLinearDamping, savedAngularDamping;
        bool savedAutoMass;

        public float Density
        {
            get { return density; }
            set
            {
                density = Mathf.Max(value, 0.001f);
                ApplyDensity();
            }
        }

        public Rigidbody2D Body { get { if (rb == null) rb = GetComponent<Rigidbody2D>(); return rb; } }
        /// <summary>The body's own mass (kg).</summary>
        public float OwnMass { get { return (float)core.OwnMass; } }
        /// <summary>Area of the body's shape (m²).</summary>
        public float Area { get { return (float)core.Shape.Area; } }
        /// <summary>Area under water this step (m²).</summary>
        public float SubmergedArea { get { return (float)core.SubmergedArea; } }
        public float AddedMass { get { return (float)core.AddedMass; } }
        public Vector2 LastAcceleration { get { return Bridge.U(core.LastAcceleration); } }
        public Vector2 CentreOfBuoyancy { get { return Bridge.U(core.CentreOfBuoyancy); } }
        public Vector2 WindHere { get { return Bridge.U(core.WindHere); } }
        public BoundedBuoyancyWater2D Water { get { return water; } }
        public BodyCore2D Core { get { return core; } }

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
        }

        void Start()
        {
            if (!setUp)
            {
                BuildShape();
                if (!core.Shape.IsEmpty)
                    SetUp();
            }
        }

        void OnEnable()
        {
            core.Frozen();   // start afresh: the surface may have moved while it was off
            if (!BoundedBuoyancySimulation.bodies2D.Contains(this))
                BoundedBuoyancySimulation.bodies2D.Add(this);
            if (setUp)
                TakeOver();
        }

        void OnDisable()
        {
            BoundedBuoyancySimulation.bodies2D.Remove(this);
            // Hand the Rigidbody2D back as it was, so that it falls and damps as usual.
            if (setUp && rb != null)
            {
                rb.gravityScale = savedGravityScale;
                Bridge.SetLinearDamping(rb, savedLinearDamping);
                Bridge.SetAngularDamping(rb, savedAngularDamping);
                if (savedAutoMass)
                {
                    rb.useAutoMass = true;
                }
                else
                {
                    rb.mass = Mathf.Max((float)core.OwnMass, 1e-4f);
                    rb.inertia = inertia;
                }
                appliedMass = -1f;
                appliedDamp = -1f;
            }
        }

        void OnValidate()
        {
            density = Mathf.Max(density, 0.001f);
            thickness = Mathf.Max(thickness, 0.001f);
            if (Application.isPlaying && setUp)
                ApplyDensity();
        }

        /// <summary>Re-read the colliders after adding, removing or resizing them while the game runs.</summary>
        public void RebuildShape()
        {
            BuildShape();
            if (core.Shape.IsEmpty)
                return;
            if (!setUp)
            {
                SetUp();
                return;
            }
            if (setCentreOfMass)
                rb.centerOfMass = Bridge.U(core.Shape.Centroid) + centreOfMassOffset;
            if (setInertia)
                ComputeInertia();
            appliedMass = -1f;
        }

        void FixedUpdate()
        {
            if (Bridge.FixedStep2D)
                Step(Time.fixedDeltaTime);
            else if (Physics2D.simulationMode == SimulationMode2D.Update)
                BoundedBuoyancySimulation.EnsureDriver();
        }

        /// <summary>One physics step of buoyancy. Called from FixedUpdate when 2D physics runs there (the default);
        /// otherwise BoundedBuoyancySimulation calls it.</summary>
        public void Step(float dt)
        {
            if (!setUp)
            {
                BuildShape();
                if (core.Shape.IsEmpty)
                {
                    if (!warned)
                    {
                        warned = true;
                        Debug.LogWarning("Bounded Buoyancy Body 2D '" + name + "' needs a collider (circle, box, capsule or polygon).", this);
                    }
                    return;
                }
                SetUp();
            }
            if (rb.bodyType != RigidbodyType2D.Dynamic || !rb.simulated || dt <= 0f)
            {
                core.Frozen();
                return;
            }
            core.Bounded = model == BoundedBuoyancyBody.Model.Bounded;
            core.Density = density;
            core.AddedMassCoefficient = addedMassCoefficient;
            core.DragCoefficient = dragCoefficient;
            core.AngularDrag = angularDrag;
            core.KeepMomentumOnEntry = keepMomentumOnEntry;
            core.WaveDamping = waveDamping;
            core.AirDensity = airDensity;
            core.AirHalfHeight = airHalfHeight;
            core.SailSize = sailSize;
            core.SailCentre = Bridge.V(sailCentre);
            core.Thickness = thickness;

            var pose = new BodyPose2D(Bridge.V(rb.position), rb.rotation * Mathf.Deg2Rad);
            V2 cw = core.CentroidWorld(pose);
            PhysicsScene2D scene = gameObject.scene.GetPhysicsScene2D();
            WaterSample2D sample = FindWater(cw, scene);
            V2 wind = BoundedBuoyancyWind2D.TotalAt(cw, scene);
            Vector2 v = Bridge.GetVelocity(rb);
            core.Step(pose, Bridge.V(rb.worldCenterOfMass), Bridge.V(v), sample, wind, -Physics2D.gravity.y, dt);

            float m = Mathf.Max((float)core.InertialMass, 1e-4f);
            if (Mathf.Abs(m - appliedMass) > 1e-6f * m)
            {
                rb.mass = m;
                rb.inertia = inertia;
                appliedMass = m;
            }
            // An impulse of the body's own mass times the change lands exactly on the target velocity.
            rb.AddForce((Bridge.U(core.VelocityTarget) - v) * rb.mass, ForceMode2D.Impulse);
            if (core.Torque != 0.0)
                rb.AddTorque((float)core.Torque, ForceMode2D.Force);
            float damp = (float)core.AngularDamp;
            if (Mathf.Abs(damp - appliedDamp) > 1e-6f)
            {
                Bridge.SetAngularDamping(rb, damp);
                appliedDamp = damp;
            }
            if (core.Crossing != Crossing.None)
                ReportCrossing();
        }

        WaterSample2D FindWater(V2 cw, PhysicsScene2D scene)
        {
            water = null;
            bool sceneHasWater = false;
            List<BoundedBuoyancyWater2D> all = BoundedBuoyancyWater2D.active;
            for (int i = 0; i < all.Count; i++)
            {
                BoundedBuoyancyWater2D w = all[i];
                if (w.PhysicsScene != scene)
                    continue;
                sceneHasWater = true;
                if (w.Core.Contains(cw))
                {
                    water = w;
                    return w.Core.Sample(cw);
                }
            }
            return sceneHasWater ? WaterSample2D.None(waterLevel, waterDensity) : WaterSample2D.Flat(waterLevel, waterDensity);
        }

        void ReportCrossing()
        {
            float speed = (float)core.CrossingSpeed;
            Vector2 at = Bridge.U(core.CrossingPoint);
            bool entering = core.Crossing == Crossing.Entered;
            if (entering)
                waterEntered.Invoke(speed, at);
            else
                waterExited.Invoke(speed, at);
            if (water != null)
                water.Splash(at, entering ? speed : 0.4f * speed, (float)core.Shape.Box.x);
        }

        void SetUp()
        {
            setUp = true;
            savedGravityScale = rb.gravityScale;
            savedLinearDamping = Bridge.GetLinearDamping(rb);
            savedAngularDamping = Bridge.GetAngularDamping(rb);
            savedAutoMass = rb.useAutoMass;
            core.Density = density;
            core.Thickness = thickness;
            appliedDensity = density;
            TakeOver();
        }

        void TakeOver()
        {
            rb.gravityScale = 0f;
            Bridge.SetLinearDamping(rb, 0f);
            Bridge.SetAngularDamping(rb, 0f);
            rb.useAutoMass = false;
            rb.mass = Mathf.Max((float)core.OwnMass, 1e-4f);
            if (setCentreOfMass)
                rb.centerOfMass = Bridge.U(core.Shape.Centroid) + centreOfMassOffset;
            // Rigidbody2D.inertia is taken about the centre of mass (the play-mode test
            // Inertia2DIsAboutTheCentreOfMass checks this in Unity).
            if (setInertia)
                ComputeInertia();
            else
                inertia = rb.inertia;
            appliedMass = -1f;
            appliedDamp = 0f;
        }

        void ComputeInertia()
        {
            inertia = Mathf.Max((float)(core.Shape.UnitInertia(Bridge.V(rb.centerOfMass)) * density * thickness), 1e-9f);
            rb.inertia = inertia;
        }

        void ApplyDensity()
        {
            core.Density = density;
            if (!setUp || density == appliedDensity)
                return;
            if (setInertia)
                ComputeInertia();
            appliedDensity = density;
            rb.inertia = inertia;
            appliedMass = -1f;
        }

        void BuildShape()
        {
            if (rb == null)
                rb = GetComponent<Rigidbody2D>();
            var b = new ShapeBuilder2D();
            Transform root = transform;
            Vector2 origin = root.position;
            float angle = -root.eulerAngles.z * Mathf.Deg2Rad;
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            System.Func<Vector3, V2> point = p =>
            {
                float x = p.x - origin.x, y = p.y - origin.y;
                return new V2(ca * x - sa * y, sa * x + ca * y);
            };
            foreach (Collider2D c in GetComponentsInChildren<Collider2D>())
            {
                if (!c.enabled || c.isTrigger || c.attachedRigidbody != rb || c is CompositeCollider2D)
                    continue;
                Transform t = c.transform;
                Vector3 s = t.lossyScale;
                float sx = Mathf.Abs(s.x), sy = Mathf.Abs(s.y);
                var circle = c as CircleCollider2D;
                var box = c as BoxCollider2D;
                var capsule = c as CapsuleCollider2D;
                var polygon = c as PolygonCollider2D;
                if (circle != null)
                {
                    b.AddCircle(point(t.TransformPoint(c.offset)), circle.radius * Mathf.Max(sx, sy));
                }
                else if (box != null)
                {
                    Vector2 h = 0.5f * box.size;
                    float e = box.edgeRadius;
                    var pts = new List<V2>();
                    if (e <= 0f)
                    {
                        foreach (var corner in new[] { new Vector2(-h.x, -h.y), new Vector2(h.x, -h.y), new Vector2(h.x, h.y), new Vector2(-h.x, h.y) })
                            pts.Add(point(t.TransformPoint(c.offset + corner)));
                        b.AddPolygon(pts);
                    }
                    else
                    {
                        // Rounded corners: the box grown by the edge radius.
                        var centres = new[] { new Vector2(h.x, h.y), new Vector2(-h.x, h.y), new Vector2(-h.x, -h.y), new Vector2(h.x, -h.y) };
                        for (int k = 0; k < 4; k++)
                            for (int i = 0; i <= 4; i++)
                            {
                                float a = (k + i / 4f) * 0.5f * Mathf.PI;
                                pts.Add(point(t.TransformPoint(c.offset + centres[k] + e * new Vector2(Mathf.Cos(a), Mathf.Sin(a)))));
                            }
                        float w = 2f * h.x + 2f * e, hh = 2f * h.y + 2f * e;
                        b.AddPolygon(pts, (w * hh - (4f - Mathf.PI) * e * e) * sx * sy);
                    }
                }
                else if (capsule != null)
                {
                    Vector2 size = new Vector2(capsule.size.x * sx, capsule.size.y * sy);
                    bool vertical = capsule.direction == CapsuleDirection2D.Vertical;
                    float r = 0.5f * (vertical ? size.x : size.y);
                    float length = Mathf.Max(vertical ? size.y : size.x, 2f * r);
                    Vector3 axisWorld = t.TransformDirection(vertical ? Vector3.up : Vector3.right);
                    V2 axis = point(root.position + axisWorld);
                    b.AddCapsule(point(t.TransformPoint(c.offset)), axis, r, length);
                }
                else if (polygon != null)
                {
                    for (int k = 0; k < polygon.pathCount; k++)
                    {
                        Vector2[] path = polygon.GetPath(k);
                        var pts = new List<V2>(path.Length);
                        foreach (Vector2 p in path)
                            pts.Add(point(t.TransformPoint(p + c.offset)));
                        b.AddPolygon(pts);
                    }
                }
                else
                {
                    Debug.LogWarning("Bounded Buoyancy Body 2D '" + name + "': " + c.GetType().Name + " has no area and is left out.", this);
                }
            }
            foreach (string w in b.Warnings)
                Debug.LogWarning("Bounded Buoyancy Body 2D '" + name + "': " + w + ".", this);
            core.Shape = b.Build();
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || !setUp)
                return;
            float r = 0.03f + 0.1f * Mathf.Sqrt(Area);
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            if (core.SubmergedArea > 0.0)
                Gizmos.DrawSphere(CentreOfBuoyancy, r);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(rb.worldCenterOfMass, r);
        }
    }
}
