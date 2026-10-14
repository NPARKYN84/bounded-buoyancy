// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using UnityEngine;
using UnityEngine.Events;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A muzzle-loading cannon that fires round shot.
    /// <para>Put it on an object at the gun's muzzle, pointing the way it fires (its forward, +Z); tilt it up for
    /// elevation. Fire() sends out a Bounded Buoyancy Cannonball, which flies with a fast ball's drag, skips off the
    /// water when it comes down at a shallow angle, and strikes what it hits.</para>
    /// <para>On a ship (any Rigidbody above it), the shot leaves with the ship's own velocity at the muzzle added, and the
    /// ship takes the recoil at the muzzle: the shot's momentum and the powder gas's, which moves on average at half the
    /// shot's speed as it leaves (Lagrange's assumption of interior ballistics). So a broadside heels a ship as well as
    /// pushing it.</para>
    /// <para>Choose a gun and the shot is set from the British round shot of Muller's Treatise of Artillery (1768): 6 to
    /// 32 pounds, 3.5 to 6.1 inches across, cast iron. The muzzle speed is a long gun's with its full charge, a third of
    /// the shot's weight: 488 m/s (1600 ft/s).</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Cannon")]
    public class BoundedBuoyancyCannon : MonoBehaviour
    {
        public enum Gun
        {
            /// <summary>6 lb shot, 3.50 in.</summary>
            Pounder6,
            /// <summary>9 lb shot, 4.00 in.</summary>
            Pounder9,
            /// <summary>12 lb shot, 4.40 in.</summary>
            Pounder12,
            /// <summary>18 lb shot, 5.04 in.</summary>
            Pounder18,
            /// <summary>24 lb shot, 5.55 in.</summary>
            Pounder24,
            /// <summary>32 lb shot, 6.10 in.</summary>
            Pounder32,
            /// <summary>Set the shot's diameter and mass yourself.</summary>
            Custom,
        }

        public const float Pound = 0.45359237f;   // kg
        public const float Inch = 0.0254f;        // m
        // Round shot by gun: weight (lb) and diameter (in), Muller (1768).
        static readonly float[] ShotPounds = { 6f, 9f, 12f, 18f, 24f, 32f };
        static readonly float[] ShotInches = { 3.50f, 4.00f, 4.40f, 5.04f, 5.55f, 6.10f };

        [Tooltip("Size of gun: sets the shot's diameter and mass, and a full charge.")]
        [SerializeField] Gun gun = Gun.Pounder24;
        [SerializeField, HideInInspector] Gun appliedGun = Gun.Pounder24;

        [Tooltip("Diameter of the shot, m.")]
        [Min(0.005f)] public float shotDiameter = 5.55f * Inch;

        [Tooltip("Mass of the shot, kg. With a Shot Prefab, set it to the prefab's: the recoil uses it.")]
        [Min(0.001f)] public float shotMass = 24f * Pound;

        [Tooltip("Speed of the shot as it leaves the muzzle, m/s.")]
        [Min(1f)] public float muzzleSpeed = 488f;

        [Tooltip("Mass of the powder charge, kg. Its gas adds to the recoil.")]
        [Min(0f)] public float chargeMass = 8f * Pound;

        [Tooltip("Seconds between shots.")]
        [Min(0f)] public float reloadTime = 3f;

        [Tooltip("Scatter: shots leave up to this many degrees off the line the gun points along.")]
        [Range(0f, 10f)] public float spread = 0.3f;

        [Tooltip("Push the ship back as the gun fires.")]
        public bool recoil = true;

        [Tooltip("Seconds each shot lasts before it removes itself. 0 keeps them.")]
        [Min(0f)] public float shotLifetime = 30f;

        [Tooltip("Optional: a prefab to fire, with a Rigidbody, a Sphere Collider and a Bounded Buoyancy Cannonball. Empty: a plain iron ball is made.")]
        public BoundedBuoyancyCannonball shotPrefab;

        [Tooltip("Optional: where the shots go in the hierarchy. Empty: at the top of the scene.")]
        public Transform shotParent;

        [Tooltip("A shot has just left the muzzle.")]
        public UnityEvent<BoundedBuoyancyCannonball> fired = new UnityEvent<BoundedBuoyancyCannonball>();

        float reloadLeft;
        BoundedBuoyancyCannonball lastShot;

        /// <summary>Size of gun: sets the shot's diameter and mass, and a full charge.</summary>
        public Gun GunSize
        {
            get { return gun; }
            set { gun = value; ApplyGun(); }
        }

        /// <summary>Seconds until the gun can fire again.</summary>
        public float ReloadLeft { get { return reloadLeft; } }
        /// <summary>Whether the gun is loaded and can fire.</summary>
        public bool IsLoaded { get { return reloadLeft <= 0f; } }
        /// <summary>The last shot fired.</summary>
        public BoundedBuoyancyCannonball LastShot { get { return lastShot; } }

        /// <summary>Momentum the recoil gives the ship, kg m/s: the shot's, and the powder gas's at half its speed.</summary>
        public float RecoilMomentum { get { return (shotMass + 0.5f * chargeMass) * muzzleSpeed; } }

        void OnValidate()
        {
            // Only when the gun is changed, so that the shot and the charge can be set by hand after choosing it.
            if (gun != appliedGun)
                ApplyGun();
        }

        void ApplyGun()
        {
            appliedGun = gun;
            int i = (int)gun;
            if (i < 0 || i >= ShotPounds.Length)
                return;
            shotMass = ShotPounds[i] * Pound;
            shotDiameter = ShotInches[i] * Inch;
            chargeMass = shotMass / 3f;
        }

        void FixedUpdate()
        {
            if (reloadLeft > 0f)
                reloadLeft = Mathf.Max(reloadLeft - Time.fixedDeltaTime, 0f);
        }

        /// <summary>Fire, if loaded. Returns the shot, or null.</summary>
        public BoundedBuoyancyCannonball Fire()
        {
            if (reloadLeft > 0f || !isActiveAndEnabled)
                return null;
            reloadLeft = reloadTime;
            Vector3 forward = transform.forward;
            if (spread > 0f)
            {
                float off = spread * Mathf.Deg2Rad * Mathf.Sqrt(Random.value);
                float turn = 2f * Mathf.PI * Random.value;
                forward = (forward * Mathf.Cos(off) + (transform.right * Mathf.Cos(turn) + transform.up * Mathf.Sin(turn)) * Mathf.Sin(off)).normalized;
            }
            Vector3 muzzle = transform.position;
            Rigidbody ship = GetComponentInParent<Rigidbody>();
            Vector3 carried = ship != null ? ship.GetPointVelocity(muzzle) : Vector3.zero;
            BoundedBuoyancyCannonball ball = MakeShot(muzzle);
            Rigidbody brb = ball.Body;
            brb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Bridge.SetVelocity(brb, carried + forward * muzzleSpeed);
            if (ship != null)
            {
                // Clear of the ship's own hull before it can strike it.
                ball.IgnoreFor(ship.GetComponentsInChildren<Collider>(), 0.25f);
                if (recoil)
                    ship.AddForceAtPosition(-forward * RecoilMomentum, muzzle, ForceMode.Impulse);
            }
            lastShot = ball;
            fired.Invoke(ball);
            return ball;
        }

        BoundedBuoyancyCannonball MakeShot(Vector3 at)
        {
            BoundedBuoyancyCannonball ball;
            if (shotPrefab != null)
            {
                ball = Instantiate(shotPrefab, at, Quaternion.identity, shotParent);
            }
            else
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);   // its Sphere Collider has radius 0.5
                go.name = "Shot";
                go.transform.SetParent(shotParent, false);
                go.transform.position = at;
                go.transform.localScale = Vector3.one * shotDiameter;
                go.AddComponent<Rigidbody>();
                ball = go.AddComponent<BoundedBuoyancyCannonball>();
                ball.Density = shotMass / (Mathf.PI / 6f * shotDiameter * shotDiameter * shotDiameter);
            }
            ball.lifetime = shotLifetime;
            return ball;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.95f, 0.4f, 0.2f, 0.9f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 3f);
            Gizmos.DrawWireSphere(transform.position, 0.5f * shotDiameter);
        }
    }
}
