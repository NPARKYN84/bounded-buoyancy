// Bounded Buoyancy for Unity, 2D. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using UnityEngine;
using UnityEngine.Events;

namespace BoundedBuoyancy
{
    /// <summary>
    /// A muzzle-loading cannon that fires round shot, in a side view. As Bounded Buoyancy Cannon in 3D.
    /// <para>Place it at the gun's muzzle, pointing the way it fires (its right, +x); turn it up for elevation. Fire sends
    /// out a Bounded Buoyancy Cannonball 2D. On a ship (any Rigidbody2D above it), the shot leaves with the ship's own
    /// velocity at the muzzle added, and the ship takes the recoil at the muzzle: the shot's momentum and the powder
    /// gas's at half the shot's speed. Choose a gun and the shot is set from Muller's Treatise of Artillery (1768).</para>
    /// </summary>
    [AddComponentMenu("Physics 2D/Bounded Buoyancy Cannon 2D")]
    public class BoundedBuoyancyCannon2D : MonoBehaviour
    {
        static readonly float[] ShotPounds = { 6f, 9f, 12f, 18f, 24f, 32f };
        static readonly float[] ShotInches = { 3.50f, 4.00f, 4.40f, 5.04f, 5.55f, 6.10f };

        [Tooltip("Size of gun: sets the shot's diameter and mass, and a full charge.")]
        [SerializeField] BoundedBuoyancyCannon.Gun gun = BoundedBuoyancyCannon.Gun.Pounder24;
        [SerializeField, HideInInspector] BoundedBuoyancyCannon.Gun appliedGun = BoundedBuoyancyCannon.Gun.Pounder24;

        [Header("Shot")]
        [Tooltip("Diameter of the shot, m.")]
        [Min(0.005f)] public float shotDiameter = 5.55f * BoundedBuoyancyCannon.Inch;
        [Tooltip("Mass of the shot, kg.")]
        [Min(0.001f)] public float shotMass = 24f * BoundedBuoyancyCannon.Pound;
        [Tooltip("Speed of the shot leaving the muzzle, m/s.")]
        [Min(1f)] public float muzzleSpeed = 488f;
        [Tooltip("Mass of the powder charge, kg: a third of the shot's for a full charge.")]
        [Min(0f)] public float chargeMass = 8f * BoundedBuoyancyCannon.Pound;

        [Header("Firing")]
        [Tooltip("Seconds to reload after firing.")]
        [Min(0f)] public float reloadTime = 3f;
        [Tooltip("Scatter of the shot about where the gun points, in degrees.")]
        [Range(0f, 10f)] public float spread = 0.3f;
        [Tooltip("Push the ship back as the gun fires.")]
        public bool recoil = true;

        [Header("Shots in the scene")]
        [Tooltip("Seconds after which a shot removes itself. 0 keeps them.")]
        [Min(0f)] public float shotLifetime = 30f;
        [Tooltip("Where shots are put in the hierarchy. Empty: at the top.")]
        public Transform shotParent;
        [Tooltip("Colour the shot is drawn in.")]
        public Color shotColor = new Color(0.13f, 0.13f, 0.14f);

        [Tooltip("A shot has just left the muzzle.")]
        public UnityEvent<BoundedBuoyancyCannonball2D> fired = new UnityEvent<BoundedBuoyancyCannonball2D>();

        float reloadLeft;
        BoundedBuoyancyCannonball2D lastShot;
        static Sprite disc;

        /// <summary>Size of gun: sets the shot's diameter and mass, and a full charge.</summary>
        public BoundedBuoyancyCannon.Gun GunSize
        {
            get { return gun; }
            set { gun = value; ApplyGun(); }
        }

        /// <summary>Seconds until the gun can fire again.</summary>
        public float ReloadLeft { get { return reloadLeft; } set { reloadLeft = Mathf.Max(value, 0f); } }
        /// <summary>Whether the gun is loaded and can fire.</summary>
        public bool IsLoaded { get { return reloadLeft <= 0f; } }
        /// <summary>The last shot fired.</summary>
        public BoundedBuoyancyCannonball2D LastShot { get { return lastShot; } }
        /// <summary>Momentum the recoil gives the ship, kg m/s: the shot's, and the powder gas's at half its speed.</summary>
        public float RecoilMomentum { get { return (shotMass + 0.5f * chargeMass) * muzzleSpeed; } }

        void OnValidate()
        {
            if (gun != appliedGun)
                ApplyGun();
        }

        void ApplyGun()
        {
            appliedGun = gun;
            int i = (int)gun;
            if (i < 0 || i >= ShotPounds.Length)
                return;
            shotMass = ShotPounds[i] * BoundedBuoyancyCannon.Pound;
            shotDiameter = ShotInches[i] * BoundedBuoyancyCannon.Inch;
            chargeMass = shotMass / 3f;
        }

        void FixedUpdate()
        {
            if (reloadLeft > 0f)
                reloadLeft = Mathf.Max(reloadLeft - Time.fixedDeltaTime, 0f);
        }

        /// <summary>Fire, if loaded. Returns the shot, or null.</summary>
        public BoundedBuoyancyCannonball2D Fire()
        {
            if (reloadLeft > 0f || !isActiveAndEnabled)
                return null;
            reloadLeft = reloadTime;
            Vector2 forward = transform.right;
            if (spread > 0f)
            {
                float off = spread * Mathf.Deg2Rad * (Random.value + Random.value - 1f);
                forward = new Vector2(forward.x * Mathf.Cos(off) - forward.y * Mathf.Sin(off), forward.x * Mathf.Sin(off) + forward.y * Mathf.Cos(off));
            }
            Vector2 muzzle = transform.position;
            Rigidbody2D ship = GetComponentInParent<Rigidbody2D>();
            Vector2 carried = ship != null ? ship.GetPointVelocity(muzzle) : Vector2.zero;
            BoundedBuoyancyCannonball2D ball = MakeShot(muzzle);
            Bridge.SetVelocity(ball.Body, carried + forward * muzzleSpeed);
            if (ship != null)
            {
                ball.IgnoreFor(ship.GetComponentsInChildren<Collider2D>(), 0.25f);
                if (recoil)
                    ship.AddForceAtPosition(-forward * RecoilMomentum, muzzle, ForceMode2D.Impulse);
            }
            lastShot = ball;
            fired.Invoke(ball);
            return ball;
        }

        BoundedBuoyancyCannonball2D MakeShot(Vector2 at)
        {
            float r = 0.5f * shotDiameter;
            var go = new GameObject("Shot");
            go.transform.SetParent(shotParent, false);
            go.transform.position = at;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var circle = go.AddComponent<CircleCollider2D>();
            circle.radius = r;
            var look = go.AddComponent<SpriteRenderer>();
            look.sprite = Disc();
            look.color = shotColor;
            go.transform.localScale = Vector3.one * (2f * r);
            circle.radius = 0.5f;   // the scale makes it r
            var ball = go.AddComponent<BoundedBuoyancyCannonball2D>();
            ball.thickness = 4f * r / 3f;   // a disk 4r/3 deep has a ball's volume
            ball.Density = shotMass / (Mathf.PI / 6f * shotDiameter * shotDiameter * shotDiameter);
            ball.lifetime = shotLifetime;
            return ball;
        }

        static Sprite Disc()
        {
            if (disc != null)
                return disc;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - 0.5f * n, dy = y + 0.5f - 0.5f * n;
                    float a = Mathf.Clamp01(0.5f * n - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            disc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            return disc;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.95f, 0.4f, 0.2f, 0.9f);
            Gizmos.DrawLine(transform.position, transform.position + transform.right * 3f);
            Gizmos.DrawWireSphere(transform.position, 0.5f * shotDiameter);
        }
    }
}
