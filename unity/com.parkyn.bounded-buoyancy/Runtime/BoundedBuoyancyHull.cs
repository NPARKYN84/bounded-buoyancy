// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;
using UnityEngine.Events;

namespace BoundedBuoyancy
{
    /// <summary>
    /// The planking of a ship (or of anything shot can strike: a fort's timber, a target): how thick it is and what it is
    /// made of. Put it on the body (the object with the Rigidbody and the Bounded Buoyancy Body), or on any object above
    /// the colliders shot can strike.
    /// <para>Shot that strikes them (Bounded Buoyancy Cannonball) is resisted by the wood with a pressure σ + ρ v² on its
    /// cross-section, v its speed through it: the wood's strength σ and its density ρ (Poncelet's law of penetration; see
    /// Planking). With oak (ρ 750 kg/m³, σ 15 MPa) a 32-pounder shot goes 42 inches into solid oak from 400 yards and 31.5
    /// inches from 1000 yards, as the gunners' handbook gives (Elvin, 1805). Shot that has the speed to go through the
    /// planking does, slower, punching a hole as big as itself; through a hole below the waterline the sea comes in (the
    /// hole goes in the body's nearest Bounded Buoyancy Compartment), so a ship holed there floods, lists and sinks. The
    /// wood it punches out flies off as splinters, which float, and the body takes the momentum the shot loses, less what
    /// the splinters carry off.
    /// Shot without the speed lodges in the wood. A shot meeting the planking at a glancing angle (more than about 78° off
    /// square) glances off.</para>
    /// <para>The body's colliders are taken as its outside, and as solid: shot goes in through the planking where it
    /// strikes them and out through the planking where its path leaves them. Colliders that touch or overlap (a ship of
    /// several boxes) count as one; a part beyond a gap along the shot's path (a mast clear of the hull) is not struck by
    /// the same shot.</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Hull")]
    public class BoundedBuoyancyHull : MonoBehaviour
    {
        [Tooltip("Thickness of the planking, m. A frigate's side was about 0.4 m of oak at the waterline, a ship of the line's 0.6 m; a small boat's a few centimetres.")]
        [Min(0.001f)] public float thickness = 0.2f;

        [Tooltip("Density of the wood, kg/m³: oak 750. It resists fast shot (ρ v²), and the splinters are made of it.")]
        [Min(1f)] public float density = 750f;

        [Tooltip("Strength of the wood against shot, MPa: oak 15. It stops slow shot.")]
        [Min(0f)] public float strength = 15f;

        [Tooltip("Size of a shot hole, as a multiple of the shot's own cross-section.")]
        [Min(0f)] public float holeSize = 1f;

        [Tooltip("Splinters thrown off each time shot goes in or out, made of the wood it punches out. 0 for none.")]
        [Range(0, 32)] public int splinters = 4;

        [Tooltip("Seconds the splinters last. 0 keeps them.")]
        [Min(0f)] public float splinterLifetime = 60f;

        [Tooltip("Colour of the splinters.")]
        public Color splinterColor = new Color(0.62f, 0.45f, 0.28f);

        [Tooltip("Shot has struck the planking: the shot, where (world), whether it went through, and its speed out (m/s; 0 if it lodged).")]
        public UnityEvent<BoundedBuoyancyCannonball, Vector3, bool, float> struck = new UnityEvent<BoundedBuoyancyCannonball, Vector3, bool, float>();

        [Tooltip("A hole has been made: where (world), its area (m²), and the compartment it is in (none if the body has none).")]
        public UnityEvent<Vector3, float, BoundedBuoyancyCompartment> holed = new UnityEvent<Vector3, float, BoundedBuoyancyCompartment>();

        Material splinterMaterial;
        readonly List<Pending> pending = new List<Pending>();

        struct Pending
        {
            public float At;
            public Collider A, B;
        }

        /// <summary>The speed of shot of `mass` (kg) and `radius` (m) after going through this planking, met at `cosIn`
        /// (the cosine of its angle off square) at `speed`. 0 if it lodges.</summary>
        public float SpeedThrough(float mass, float radius, float speed, float cosIn)
        {
            double path = thickness / Mathf.Max(cosIn, (float)Planking.Glance);
            return (float)Planking.SpeedAfter(mass, Mathf.PI * radius * radius, speed, path, density, strength * 1e6);
        }

        /// <summary>Make a hole of `area` m² at `at` (world) in the body's nearest compartment, if it has any; invokes
        /// holed. Returns the compartment.</summary>
        public BoundedBuoyancyCompartment MakeHole(Vector3 at, float area)
        {
            BoundedBuoyancyBody body = GetComponentInParent<BoundedBuoyancyBody>();
            BoundedBuoyancyCompartment best = null;
            float bestDistance = float.PositiveInfinity;
            if (body != null)
            {
                foreach (BoundedBuoyancyCompartment c in body.Compartments)
                {
                    if (c == null || !c.isActiveAndEnabled)
                        continue;
                    Vector3 l = Quaternion.Inverse(c.transform.rotation) * (at - c.transform.position);
                    Vector3 h = 0.5f * c.size;
                    Vector3 onBox = new Vector3(Mathf.Clamp(l.x, -h.x, h.x), Mathf.Clamp(l.y, -h.y, h.y), Mathf.Clamp(l.z, -h.z, h.z));
                    float d = (l - onBox).magnitude;
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = c;
                    }
                }
            }
            if (best != null)
                best.AddHole(at, area);
            holed.Invoke(at, area, best);
            return best;
        }

        /// <summary>Throw the wood punched out by shot of `radius` going through here, as splinters from `at` (world) along
        /// `dir`, at up to `speed`, carried along with the body's own `velocity` there. They float. They do not strike
        /// `ignore` (the shot) or each other, nor the body for the first moment. Returns the momentum they carry off,
        /// relative to the body (kg m/s).</summary>
        public Vector3 ThrowSplinters(Vector3 at, Vector3 dir, float speed, float radius, float cosIn, Vector3 velocity, Collider ignore)
        {
            if (splinters <= 0)
                return Vector3.zero;
            float wood = Mathf.PI * radius * radius * thickness / Mathf.Max(cosIn, (float)Planking.Glance);   // m³ punched out
            float s = Mathf.Pow(wood / splinters / 8f, 1f / 3f);   // each a splinter 8 x 1 x 1
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(side, dir).normalized;
            Rigidbody shipBody = GetComponentInParent<Rigidbody>();
            Collider[] ship = (shipBody != null ? shipBody.transform : transform).GetComponentsInChildren<Collider>();
            var thrown = new List<Collider>();
            Vector3 momentum = Vector3.zero;
            for (int i = 0; i < splinters; i++)
            {
                float off = Random.value * 40f * Mathf.Deg2Rad;
                float turn = Random.value * 2f * Mathf.PI;
                Vector3 d = (dir * Mathf.Cos(off) + (side * Mathf.Cos(turn) + up * Mathf.Sin(turn)) * Mathf.Sin(off)).normalized;
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Splinter";
                go.transform.position = at + d * (4f * s);
                go.transform.rotation = Quaternion.LookRotation(d);
                go.transform.localScale = new Vector3(s, s, 8f * s);
                Renderer look = go.GetComponent<Renderer>();
                if (look != null)
                {
                    if (splinterMaterial == null)
                        splinterMaterial = new Material(look.sharedMaterial);
                    splinterMaterial.color = splinterColor;
                    look.sharedMaterial = splinterMaterial;
                }
                Rigidbody rb = go.AddComponent<Rigidbody>();
                BoundedBuoyancyBody b = go.AddComponent<BoundedBuoyancyBody>();
                b.Density = density;
                Vector3 away = d * speed * Random.Range(0.3f, 1f);
                Bridge.SetVelocity(rb, velocity + away);
                momentum += density * (wood / splinters) * away;
                rb.angularVelocity = new Vector3(Random.Range(-10f, 10f), Random.Range(-10f, 10f), Random.Range(-10f, 10f));
                Collider own = go.GetComponent<Collider>();
                if (ignore != null)
                    Physics.IgnoreCollision(own, ignore, true);
                foreach (Collider other in thrown)
                    Physics.IgnoreCollision(own, other, true);
                thrown.Add(own);
                foreach (Collider c in ship)
                {
                    if (c == null)
                        continue;
                    Physics.IgnoreCollision(own, c, true);
                    pending.Add(new Pending { At = Time.time + 0.4f, A = own, B = c });
                }
                if (splinterLifetime > 0f)
                    Destroy(go, splinterLifetime);
            }
            return momentum;
        }

        void FixedUpdate()
        {
            // Splinters clear of the hull strike it again.
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < pending[i].At)
                    continue;
                if (pending[i].A != null && pending[i].B != null)
                    Physics.IgnoreCollision(pending[i].A, pending[i].B, false);
                pending.RemoveAt(i);
            }
        }
    }
}
