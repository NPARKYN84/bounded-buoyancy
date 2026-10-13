// Bounded Buoyancy for Unity: buoyancy with the added mass of the submerged part in the body's inertia,
// every physics step. Method and benchmarks: Parkyn, N. (2026). Bounded Buoyancy: Added Mass and Two
// Ceilings for Stable, Accurate Buoyancy in Game and Simulation Engines, v1.1. Companion paper to
// ValerieX (VXXX) v2.2. Zenodo. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;
using UnityEngine.Events;

namespace BoundedBuoyancy
{
    /// <summary>
    /// Floats a Rigidbody. Buoyancy drives no fall faster than g and no rise faster than g/C, for every density
    /// (drag and collisions act on top); the first frame after a release is the classical value, and momentum is
    /// kept as the body enters the water. Buoyancy acts at the centre of the immersed volume, so bodies of any
    /// shape tilt, roll and right themselves.
    /// <para>Give the GameObject a Rigidbody and colliders (box, sphere, capsule or convex mesh; children count),
    /// add this component, set its density, and add a BoundedBuoyancyWater (or set waterLevel). The component sets
    /// the Rigidbody's mass, turns off its gravity and damping, and applies weight, buoyancy, drag and the righting
    /// moment itself. Collisions and your own forces act as usual and are resisted by the full inertia, water
    /// included.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Body")]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class BoundedBuoyancyBody : MonoBehaviour
    {
        public enum Model
        {
            /// <summary>Added mass of the submerged part in the inertia (Bounded Buoyancy).</summary>
            Bounded,
            /// <summary>Buoyancy applied to the body's own mass, as most engines do. For comparison.</summary>
            Usual,
        }

        [Tooltip("Bounded: the added mass of the water taken along is in the inertia. Usual: buoyancy on the body's own mass, as most engines do (for comparison).")]
        public Model model = Model.Bounded;

        [Tooltip("Density of the body, kg/m³ (water is 1000). Its own mass is density x volume. It can be changed while running: ballast tanks, or a hot-air balloon whose air is heated (helium balloon about 0.5).")]
        [SerializeField, Min(0.001f)] float density = 500f;

        [Tooltip("Added-mass coefficient C of the shape: 0.5 for a sphere, about 1 for a long cylinder moving side-on. Buoyancy-driven rising is capped at g/C.")]
        [Range(0f, 5f)] public float addedMassCoefficient = 0.5f;

        [Tooltip("Height of a flat water surface (world Y), used when the scene has no Bounded Buoyancy Water and Water Surface is not set.")]
        public float waterLevel = 0f;

        [Tooltip("Optional: a Bounded Buoyancy Water to use (its waves and current too), or any object whose Y is a flat surface. Takes priority over the water volumes in the scene.")]
        public Transform waterSurface;

        [Tooltip("Density of the water, kg/m³, for Water Level or a plain Water Surface. A Bounded Buoyancy Water uses its own.")]
        [Min(1f)] public float waterDensity = 1000f;

        [Tooltip("Quadratic drag coefficient (0.47 for a sphere), in the water and in the air.")]
        [Range(0f, 3f)] public float dragCoefficient = 0.47f;

        [Tooltip("Damping of spin in the water, per second when fully submerged; it scales with the submerged fraction. In air it acts in proportion to how light the body is against the air, so balloons are steadied and stones are not.")]
        [Min(0f)] public float angularDrag = 3f;

        [Tooltip("Keep momentum when the added mass grows as the body enters the water (von Kármán, 1929).")]
        public bool keepMomentumOnEntry = true;

        [Header("Air")]
        [Tooltip("Density of the air at height 0, kg/m³ (1.2 at room temperature). Balloons rise in it; for wood, stone or metal it changes the weight by under half a percent. 0 leaves air out, as the paper's benchmarks do.")]
        [Min(0f)] public float airDensity = 1.2f;

        [Tooltip("Height (m) over which the air's density halves, so a balloon rises until it floats where the air is as dense as it is. 0 keeps the air the same at every height.")]
        [Min(0f)] public float airHalfHeight = 0f;

        [Tooltip("Area of a sail or flag for the wind to push on, m². The sail turns to face the wind it feels, like a sail let out to run before the wind.")]
        [Min(0f)] public float sailArea = 0f;

        [Tooltip("Where the wind pushes the sail, in the body's own coordinates (from the Rigidbody's position, along its axes). Above the centre of mass the push heels the body over; a low centre of mass holds it up.")]
        public Vector3 sailCentre = new Vector3(0f, 0.5f, 0f);

        [Header("Shape")]
        [Tooltip("Optional closed mesh used for buoyancy instead of the colliders, in this object's local space (like a Mesh Filter's mesh): for example a simplified, watertight hull. It must be Read/Write enabled.")]
        public Mesh buoyancyMesh;

        [Tooltip("Facets round the curve of capsules, and of spheres that share a body with other colliders. The volume is corrected to the exact value, so a few facets are enough.")]
        [Range(8, 64)] public int curveSegments = 16;

        [Header("Mass")]
        [Tooltip("Put the Rigidbody's centre of mass at the centre of the shape's volume (plus the offset below). Turn off to keep the Rigidbody's own.")]
        public bool setCentreOfMass = true;

        [Tooltip("Moves the centre of mass from the centre of the volume, in the body's own coordinates. Lower it (negative Y) to give a boat ballast.")]
        public Vector3 centreOfMassOffset = Vector3.zero;

        [Tooltip("Work out the rotational inertia from the shape and density. Turn off to keep the Rigidbody's own (from its colliders, or set by your script); it is then held fixed while the mass changes.")]
        public bool setInertia = true;

        [Header("Events")]
        [Tooltip("The body has just touched the water: speed through the water (m/s) and the point on the surface.")]
        public UnityEvent<float, Vector3> waterEntered = new UnityEvent<float, Vector3>();

        [Tooltip("The body has just come wholly out of the water: speed (m/s) and the point on the surface.")]
        public UnityEvent<float, Vector3> waterExited = new UnityEvent<float, Vector3>();

        Rigidbody rb;
        readonly BodyCore3D core = new BodyCore3D();
        readonly List<BoundedBuoyancyFoil> foils = new List<BoundedBuoyancyFoil>();
        readonly List<BoundedBuoyancyThruster> thrusters = new List<BoundedBuoyancyThruster>();
        readonly List<Foil3D> foilCores = new List<Foil3D>();
        readonly List<Thruster3D> thrusterCores = new List<Thruster3D>();
        bool setUp;
        bool warned;
        float appliedMass = -1f;
        float appliedDamp = -1f;
        Vector3 tensor;
        Quaternion tensorRotation = Quaternion.identity;
        BoundedBuoyancyWater water;
        Transform surfaceCached;
        BoundedBuoyancyWater surfaceWater;
        float appliedDensity;
        bool savedGravity, savedAutoCom, savedAutoTensor;
        float savedLinearDamping, savedAngularDamping;

        /// <summary>Density of the body (kg/m³). Can be changed while running; mass and inertia follow.</summary>
        public float Density
        {
            get { return density; }
            set
            {
                density = Mathf.Max(value, 0.001f);
                ApplyDensity();
            }
        }

        /// <summary>The Rigidbody this component drives.</summary>
        public Rigidbody Body { get { if (rb == null) rb = GetComponent<Rigidbody>(); return rb; } }
        /// <summary>The body's own mass, density x volume (kg).</summary>
        public float OwnMass { get { return (float)core.OwnMass; } }
        /// <summary>Volume of the body's shape (m³).</summary>
        public float Volume { get { return (float)core.Shape.Volume; } }
        /// <summary>Submerged volume this step (m³).</summary>
        public float SubmergedVolume { get { return (float)core.SubmergedVolume; } }
        /// <summary>Added mass of the water and air taken along this step (kg).</summary>
        public float AddedMass { get { return (float)core.AddedMass; } }
        /// <summary>Acceleration from weight, buoyancy and drag in the last step (m/s²).</summary>
        public Vector3 LastAcceleration { get { return Bridge.U(core.LastAcceleration); } }
        /// <summary>Centre of the submerged volume this step (world). The water's buoyancy acts here.</summary>
        public Vector3 CentreOfBuoyancy { get { return Bridge.U(core.CentreOfBuoyancy); } }
        /// <summary>Density of the air at the body this step (kg/m³).</summary>
        public float AirDensityHere { get { return (float)core.AirDensityHere; } }
        /// <summary>Velocity of the wind at the body this step (m/s, world).</summary>
        public Vector3 WindHere { get { return Bridge.U(core.WindHere); } }
        /// <summary>The Bounded Buoyancy Water the body is in this step, if any.</summary>
        public BoundedBuoyancyWater Water { get { return water; } }
        /// <summary>The engine-free core, for reading anything else it works out.</summary>
        public BodyCore3D Core { get { return core; } }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void Start()
        {
            // Colliders added by other scripts' Awake are in place by now; any added later are picked up by
            // RebuildShape() or on the first physics step.
            if (!setUp)
            {
                BuildShape();
                if (!core.Shape.IsEmpty)
                    SetUp();
            }
        }

        void OnEnable()
        {
            if (!BoundedBuoyancySimulation.bodies.Contains(this))
                BoundedBuoyancySimulation.bodies.Add(this);
            if (setUp)
                TakeOver();
            // Foils and thrusters already under the body join it (they join by themselves when added later).
            foreach (BoundedBuoyancyFoil f in GetComponentsInChildren<BoundedBuoyancyFoil>())
                if (f.isActiveAndEnabled)
                    f.Register();
            foreach (BoundedBuoyancyThruster t in GetComponentsInChildren<BoundedBuoyancyThruster>())
                if (t.isActiveAndEnabled)
                    t.Register();
        }

        void OnDisable()
        {
            BoundedBuoyancySimulation.bodies.Remove(this);
            // Hand the Rigidbody back as it was, so that it falls and damps as usual.
            if (setUp && rb != null)
            {
                rb.useGravity = savedGravity;
                Bridge.SetLinearDamping(rb, savedLinearDamping);
                Bridge.SetAngularDamping(rb, savedAngularDamping);
                rb.mass = Mathf.Max((float)core.OwnMass, 1e-7f);
                if (savedAutoCom)
                    rb.ResetCenterOfMass();
                if (savedAutoTensor)
                {
                    rb.ResetInertiaTensor();
                }
                else
                {
                    rb.inertiaTensor = tensor;
                    rb.inertiaTensorRotation = tensorRotation;
                }
                appliedMass = -1f;
                appliedDamp = -1f;
            }
        }

        void OnValidate()
        {
            density = Mathf.Max(density, 0.001f);
            curveSegments = Mathf.Clamp(curveSegments, 8, 64);
            if (Application.isPlaying && setUp)
                ApplyDensity();
        }

        /// <summary>Re-read the colliders (or buoyancy mesh). Call it after adding, removing or resizing colliders
        /// while the game runs; the mass, centre of mass and inertia follow (unless kept as the Rigidbody's own).</summary>
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
            if (Bridge.FixedStep3D)
                Step(Time.fixedDeltaTime);
            else if (Physics.simulationMode == SimulationMode.Update)
                BoundedBuoyancySimulation.EnsureDriver();
        }

        /// <summary>One physics step of buoyancy. Called from FixedUpdate when physics runs there (the default);
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
                        Debug.LogWarning("Bounded Buoyancy Body '" + name + "' needs a collider (box, sphere, capsule or convex mesh) or a closed buoyancy mesh.", this);
                    }
                    return;
                }
                SetUp();
            }
            if (rb.isKinematic || dt <= 0f)
            {
                core.Frozen();
                return;
            }
            core.Bounded = model == Model.Bounded;
            core.Density = density;
            core.AddedMassCoefficient = addedMassCoefficient;
            core.DragCoefficient = dragCoefficient;
            core.AngularDrag = angularDrag;
            core.KeepMomentumOnEntry = keepMomentumOnEntry;
            core.AirDensity = airDensity;
            core.AirHalfHeight = airHalfHeight;
            core.SailArea = sailArea;
            core.SailCentre = Bridge.V(sailCentre);

            var pose = new BodyPose(Bridge.V(rb.position), Bridge.M(rb.rotation));
            V3 cw = core.CentroidWorld(pose);
            PhysicsScene scene = gameObject.scene.GetPhysicsScene();
            WaterSample sample = FindWater(cw, scene);
            V3 wind = BoundedBuoyancyWind.TotalAt(cw, scene);
            Vector3 v = Bridge.GetVelocity(rb);
            core.Step(pose, Bridge.V(rb.worldCenterOfMass), Bridge.V(v), sample, wind, -Physics.gravity.y, dt);
            V3 turn = V3.Zero;
            if (foils.Count > 0 || thrusters.Count > 0)
                turn = ApplyAppendages(sample, wind, dt);

            // The engine resists every force with the mass it has, so set it to the inertial mass (water and air
            // taken along included), and hold the rotational inertia at the body's own.
            float m = Mathf.Max((float)core.InertialMass, 1e-7f);
            if (Mathf.Abs(m - appliedMass) > 1e-6f * m)
            {
                rb.mass = m;
                rb.inertiaTensor = tensor;
                rb.inertiaTensorRotation = tensorRotation;
                appliedMass = m;
            }
            // Land exactly on the target velocity this step, whatever the mass.
            rb.AddForce(Bridge.U(core.VelocityTarget) - v, ForceMode.VelocityChange);
            if (turn.x != 0.0 || turn.y != 0.0 || turn.z != 0.0)
                rb.AddTorque(Bridge.U(turn), ForceMode.Impulse);
            if (core.Torque.x != 0.0 || core.Torque.y != 0.0 || core.Torque.z != 0.0)
                rb.AddTorque(Bridge.U(core.Torque), ForceMode.Force);
            float damp = (float)core.AngularDamp;
            if (Mathf.Abs(damp - appliedDamp) > 1e-6f)
            {
                Bridge.SetAngularDamping(rb, damp);
                appliedDamp = damp;
            }
            if (core.Crossing != Crossing.None)
                ReportCrossing();
        }

        /// <summary>Foils and thrusters under the body register themselves here.</summary>
        internal void AddAppendage(BoundedBuoyancyFoil f) { if (!foils.Contains(f)) foils.Add(f); }
        internal void AddAppendage(BoundedBuoyancyThruster t) { if (!thrusters.Contains(t)) thrusters.Add(t); }
        internal void RemoveAppendage(BoundedBuoyancyFoil f) { foils.Remove(f); }
        internal void RemoveAppendage(BoundedBuoyancyThruster t) { thrusters.Remove(t); }

        /// <summary>The foils and thrusters under this body.</summary>
        public IReadOnlyList<BoundedBuoyancyFoil> Foils { get { return foils; } }
        /// <summary>The thrusters under this body.</summary>
        public IReadOnlyList<BoundedBuoyancyThruster> Thrusters { get { return thrusters; } }

        /// <summary>Foils and thrusters, after the buoyancy and drag: each an impulse at its own point, bounded so it
        /// never overshoots (see Core.Appendages). Changes the target velocity; returns the angular impulse.</summary>
        V3 ApplyAppendages(WaterSample sample, V3 wind, float dt)
        {
            foilCores.Clear();
            thrusterCores.Clear();
            for (int i = 0; i < foils.Count; i++)
                if (foils[i] != null && foils[i].isActiveAndEnabled)
                    foilCores.Add(foils[i].Prepare(transform, rb));
            for (int i = 0; i < thrusters.Count; i++)
                if (thrusters[i] != null && thrusters[i].isActiveAndEnabled)
                    thrusterCores.Add(thrusters[i].Prepare(transform, rb));
            V3 before = core.VelocityTarget;
            V3 v = before;
            V3 w = Bridge.V(rb.angularVelocity);
            M3 axes = Bridge.M(rb.rotation * tensorRotation);
            V3 turn = Appendages.Apply(foilCores, thrusterCores, core.InertialMass, Bridge.V(rb.worldCenterOfMass), axes, Bridge.V(tensor),
                ref v, ref w, sample, wind, core.AirDensityHere, dt);
            core.VelocityTarget = v;
            core.LastAcceleration += (v - before) / dt;
            for (int i = 0; i < foils.Count; i++)
                if (foils[i] != null && foils[i].isActiveAndEnabled)
                    foils[i].Finish();
            return turn;
        }

        WaterSample FindWater(V3 cw, PhysicsScene scene)
        {
            water = null;
            if (waterSurface != null)
            {
                if (waterSurface != surfaceCached)
                {
                    surfaceCached = waterSurface;
                    surfaceWater = waterSurface.GetComponent<BoundedBuoyancyWater>();
                }
                if (surfaceWater != null && surfaceWater.isActiveAndEnabled)
                {
                    water = surfaceWater;
                    return surfaceWater.Core.Sample(cw);
                }
                return WaterSample.Flat(waterSurface.position.y, waterDensity);
            }
            bool sceneHasWater = false;
            List<BoundedBuoyancyWater> all = BoundedBuoyancyWater.active;
            for (int i = 0; i < all.Count; i++)
            {
                BoundedBuoyancyWater w = all[i];
                if (w.PhysicsScene != scene)
                    continue;
                sceneHasWater = true;
                if (w.Core.Contains(cw))
                {
                    water = w;
                    return w.Core.Sample(cw);
                }
            }
            return sceneHasWater ? WaterSample.None(waterLevel, waterDensity) : WaterSample.Flat(waterLevel, waterDensity);
        }

        void ReportCrossing()
        {
            float speed = (float)core.CrossingSpeed;
            Vector3 at = Bridge.U(core.CrossingPoint);
            bool entering = core.Crossing == Crossing.Entered;
            if (entering)
                waterEntered.Invoke(speed, at);
            else
                waterExited.Invoke(speed, at);
            if (water != null)
            {
                float across = 2f * Mathf.Sqrt(Mathf.Max((float)core.Shape.Area, 1e-6f) / Mathf.PI);
                water.Splash(at, entering ? speed : 0.4f * speed, across);
            }
        }

        void SetUp()
        {
            setUp = true;
            savedGravity = rb.useGravity;
            savedLinearDamping = Bridge.GetLinearDamping(rb);
            savedAngularDamping = Bridge.GetAngularDamping(rb);
            savedAutoCom = rb.automaticCenterOfMass;
            savedAutoTensor = rb.automaticInertiaTensor;
            core.Density = density;
            appliedDensity = density;
            TakeOver();
        }

        void TakeOver()
        {
            rb.useGravity = false;
            Bridge.SetLinearDamping(rb, 0f);
            Bridge.SetAngularDamping(rb, 0f);
            rb.mass = Mathf.Max((float)core.OwnMass, 1e-7f);
            // The centre of the volume, so that buoyancy and weight balance where they should, unless kept as the
            // Rigidbody's own (a lower centre of mass is how a boat is given ballast).
            if (setCentreOfMass)
                rb.centerOfMass = Bridge.U(core.Shape.Centroid) + centreOfMassOffset;
            if (setInertia)
            {
                ComputeInertia();
            }
            else
            {
                tensor = rb.inertiaTensor;
                tensorRotation = rb.inertiaTensorRotation;
            }
            appliedMass = -1f;
            appliedDamp = 0f;
        }

        void ComputeInertia()
        {
            V3 moments;
            M3 axes;
            (core.Shape.UnitInertia(Bridge.V(rb.centerOfMass)) * density).Principal(out moments, out axes);
            double floor = 1e-9 * System.Math.Max(System.Math.Max(moments.x, moments.y), System.Math.Max(moments.z, 1e-12));
            tensor = new Vector3((float)System.Math.Max(moments.x, floor), (float)System.Math.Max(moments.y, floor), (float)System.Math.Max(moments.z, floor));
            tensorRotation = Bridge.Q(axes);
            rb.inertiaTensor = tensor;
            rb.inertiaTensorRotation = tensorRotation;
        }

        void ApplyDensity()
        {
            core.Density = density;
            if (!setUp || density == appliedDensity)
                return;
            // Worked-out inertia follows the density; the Rigidbody's own (Set Inertia off) is held as it was.
            if (setInertia)
                ComputeInertia();
            appliedDensity = density;
            rb.inertiaTensor = tensor;
            rb.inertiaTensorRotation = tensorRotation;
            appliedMass = -1f;
        }

        void BuildShape()
        {
            if (rb == null)
                rb = GetComponent<Rigidbody>();
            var b = new ShapeBuilder3D { CurveSegments = curveSegments };
            Transform root = transform;
            Quaternion inv = Quaternion.Inverse(root.rotation);
            Vector3 origin = root.position;
            System.Func<Vector3, V3> point = p => Bridge.V(inv * (p - origin));
            System.Func<Vector3, V3> dir = d => Bridge.V(inv * d);

            if (buoyancyMesh != null)
            {
                if (!buoyancyMesh.isReadable)
                {
                    Debug.LogWarning("Bounded Buoyancy Body '" + name + "': the buoyancy mesh is not readable; tick Read/Write in its import settings.", this);
                }
                else
                {
                    Vector3[] mv = buoyancyMesh.vertices;
                    var pts = new V3[mv.Length];
                    for (int i = 0; i < mv.Length; i++)
                        pts[i] = point(root.TransformPoint(mv[i]));
                    b.AddMesh(pts, buoyancyMesh.triangles);
                }
            }
            else
            {
                foreach (Collider c in GetComponentsInChildren<Collider>())
                {
                    if (!c.enabled || c.isTrigger || c.attachedRigidbody != rb)
                        continue;
                    Transform t = c.transform;
                    Vector3 s = t.lossyScale;
                    Vector3 a = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                    var box = c as BoxCollider;
                    var sphere = c as SphereCollider;
                    var capsule = c as CapsuleCollider;
                    var mesh = c as MeshCollider;
                    if (box != null)
                    {
                        Vector3 h = 0.5f * box.size;
                        b.AddBox(point(t.TransformPoint(box.center)), dir(t.TransformVector(new Vector3(h.x, 0, 0))),
                            dir(t.TransformVector(new Vector3(0, h.y, 0))), dir(t.TransformVector(new Vector3(0, 0, h.z))));
                    }
                    else if (sphere != null)
                    {
                        b.AddSphere(point(t.TransformPoint(sphere.center)), sphere.radius * Mathf.Max(a.x, Mathf.Max(a.y, a.z)));
                    }
                    else if (capsule != null)
                    {
                        int k = Mathf.Clamp(capsule.direction, 0, 2);
                        Vector3 axis = k == 0 ? Vector3.right : (k == 1 ? Vector3.up : Vector3.forward);
                        float along = a[k];
                        float across = Mathf.Max(a[(k + 1) % 3], a[(k + 2) % 3]);
                        float r = capsule.radius * across;
                        b.AddCapsule(point(t.TransformPoint(capsule.center)), dir(t.TransformDirection(axis)), r,
                            Mathf.Max(capsule.height * along, 2f * r));
                    }
                    else if (mesh != null)
                    {
                        Mesh m = mesh.sharedMesh;
                        if (m == null)
                            continue;
                        if (!m.isReadable)
                        {
                            // The physics engine has the hull, but its points cannot be read; use the mesh's bounds.
                            Debug.LogWarning("Bounded Buoyancy Body '" + name + "': mesh '" + m.name + "' is not readable, so its bounding box is used; tick Read/Write, or set a buoyancy mesh.", this);
                            Bounds bb = m.bounds;
                            b.AddBox(point(t.TransformPoint(bb.center)), dir(t.TransformVector(new Vector3(bb.extents.x, 0, 0))),
                                dir(t.TransformVector(new Vector3(0, bb.extents.y, 0))), dir(t.TransformVector(new Vector3(0, 0, bb.extents.z))));
                            continue;
                        }
                        // A dynamic body's mesh collider is convex: the hull of its points.
                        Vector3[] mv = m.vertices;
                        var pts = new V3[mv.Length];
                        for (int i = 0; i < mv.Length; i++)
                            pts[i] = point(t.TransformPoint(mv[i]));
                        b.AddConvexHull(pts);
                    }
                    else if (c.GetType().Name != "WheelCollider")   // wheels have no volume (and need no Vehicles module here)
                    {
                        Debug.LogWarning("Bounded Buoyancy Body '" + name + "': " + c.GetType().Name + " is not supported; set a buoyancy mesh instead.", this);
                    }
                }
            }
            foreach (string w in b.Warnings)
                Debug.LogWarning("Bounded Buoyancy Body '" + name + "': " + w + ".", this);
            core.Shape = b.Build();
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || !setUp)
                return;
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            if (core.SubmergedVolume > 0.0)
                Gizmos.DrawSphere(CentreOfBuoyancy, 0.03f + 0.1f * Mathf.Pow(Volume, 1f / 3f));
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(rb.worldCenterOfMass, 0.03f + 0.1f * Mathf.Pow(Volume, 1f / 3f));
        }
    }
}
