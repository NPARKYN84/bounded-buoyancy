// Bounded Buoyancy for Unity. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using System.Collections.Generic;
using BoundedBuoyancy.Core;
using UnityEngine;

namespace BoundedBuoyancy
{
    /// <summary>
    /// The air of a scene: the real atmosphere, for every Bounded Buoyancy Body in it whose air is not fixed.
    /// <para>Put it on an object at sea level and set the day's weather there: Temperature, Pressure and Humidity. The air's
    /// density at every height then follows the standard atmosphere (the US Standard Atmosphere, 1976, the same as the
    /// International Standard Atmosphere to 32 km; see Core.Atmosphere): its temperature falls 6.5 °C for each kilometre
    /// up to 11 km and stays the same above, and its pressure falls with the weight of the air above. Damp air is less
    /// dense than dry, as water vapour is lighter than the air it replaces. Bounded Buoyancy Thermals warm it where they
    /// are.</para>
    /// <para>Without one, bodies use the standard atmosphere at 15 °C and 1013.25 hPa, dry, with sea level at height 0. A
    /// body whose Air Density or Air Half Height is set uses those instead (Fixed Air).</para>
    /// </summary>
    [AddComponentMenu("Physics/Bounded Buoyancy Air")]
    [DisallowMultipleComponent]
    public class BoundedBuoyancyAir : MonoBehaviour
    {
        [Tooltip("Temperature of the air at sea level (this object's height), °C.")]
        [Range(-60f, 60f)] public float temperature = 15f;

        [Tooltip("Pressure of the air at sea level, hPa: 1013.25 on an average day, about 980 in a deep low and 1040 in a strong high.")]
        [Range(800f, 1100f)] public float pressure = 1013.25f;

        [Tooltip("Relative humidity of the air, %: 0 for dry air, as the standard atmosphere is.")]
        [Range(0f, 100f)] public float humidity = 0f;

        internal static readonly List<BoundedBuoyancyAir> active = new List<BoundedBuoyancyAir>();
        static readonly AirField standard = new AirField();

        /// <summary>Every enabled Bounded Buoyancy Air.</summary>
        public static IReadOnlyList<BoundedBuoyancyAir> Active { get { return active; } }

        readonly AirField field = new AirField();
        PhysicsScene physicsScene;

        /// <summary>The engine-free air field the bodies ask.</summary>
        public AirField Field { get { Sync(); return field; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
            physicsScene = gameObject.scene.GetPhysicsScene();
        }

        void OnDisable()
        {
            active.Remove(this);
        }

        void Sync()
        {
            field.SeaLevel = transform.position.y;
            field.Temperature = temperature;
            field.Pressure = pressure;
            field.Humidity = humidity;
            BoundedBuoyancyThermal.Gather(physicsScene, field.Thermals);
        }

        /// <summary>The air in `scene`: its Bounded Buoyancy Air's, or the standard atmosphere with sea level at height 0,
        /// with the scene's thermals either way. The field is shared: use it at once.</summary>
        public static AirField In(PhysicsScene scene)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].physicsScene == scene)
                {
                    active[i].Sync();
                    return active[i].field;
                }
            }
            BoundedBuoyancyThermal.Gather(scene, standard.Thermals);
            return standard;
        }

        /// <summary>Density of the air at the world point p in `scene`, kg/m³.</summary>
        public static float DensityIn(PhysicsScene scene, Vector3 p) { return (float)In(scene).DensityAt(Bridge.V(p)); }

        /// <summary>Speed of sound in the air at the world point p in `scene`, m/s: √(γ R T) from the air's temperature
        /// there (dry air, γ = 1.4; 340.3 at 15 °C).</summary>
        public static float SoundSpeedIn(PhysicsScene scene, Vector3 p) { return (float)In(scene).SoundSpeedAt(Bridge.V(p)); }

        /// <summary>Density of the air at the world point p, kg/m³.</summary>
        public float DensityAt(Vector3 p) { return (float)Field.DensityAt(Bridge.V(p)); }

        /// <summary>Temperature of the air at the world point p, °C.</summary>
        public float TemperatureAt(Vector3 p) { return (float)(Field.KelvinAt(Bridge.V(p)) - 273.15); }

        /// <summary>Density of the air `height` metres above sea level, kg/m³, in the standard atmosphere with
        /// `seaTemperature` °C and `seaPressure` hPa at sea level, at `humidity` % relative humidity, and `warmer` °C warmer
        /// than the air around.</summary>
        public static float StandardDensity(float height, float seaTemperature = 15f, float seaPressure = 1013.25f, float humidity = 0f, float warmer = 0f)
        {
            return (float)Atmosphere.Density(height, seaTemperature, seaPressure, humidity, warmer);
        }

        /// <summary>Temperature (x, K) and pressure (y, Pa) of the standard atmosphere `height` metres above sea level.</summary>
        public static Vector2 StandardAir(float height, float seaTemperature = 15f, float seaPressure = 1013.25f)
        {
            double t, p;
            Atmosphere.StandardAir(height, seaTemperature, seaPressure, out t, out p);
            return new Vector2((float)t, (float)p);
        }
    }
}
