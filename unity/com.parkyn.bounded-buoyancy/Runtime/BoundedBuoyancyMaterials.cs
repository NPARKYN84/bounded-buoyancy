using System.Collections.Generic;

namespace BoundedBuoyancy
{
    /// <summary>What a body is made of. Pick one on a body's Made Of and it takes that material's density, so it floats
    /// or sinks as the real thing does. Custom: set the density yourself.</summary>
    public enum BoundedBuoyancyMaterial
    {
        Custom, Foam, Balsa, Cork, Pine, Plywood, Teak, Oak, Ice, Plastic, Ebony, Rubber, PVC, Brick, Concrete, Glass, Stone,
        Aluminium, Iron, Steel, Lead, Gold,
    }

    /// <summary>Common materials and their densities (kg/m³): typical values for the dry, solid material (woods air-dried;
    /// foam is expanded polystyrene, plastic polyethylene, stone granite, iron cast iron, ice freshwater ice). A real
    /// piece varies by a few per cent, more for wood.</summary>
    public static class BoundedBuoyancyMaterials
    {
        static readonly Dictionary<BoundedBuoyancyMaterial, float> densities = new Dictionary<BoundedBuoyancyMaterial, float>
        {
            { BoundedBuoyancyMaterial.Foam, 30f }, { BoundedBuoyancyMaterial.Balsa, 160f }, { BoundedBuoyancyMaterial.Cork, 240f },
            { BoundedBuoyancyMaterial.Pine, 510f }, { BoundedBuoyancyMaterial.Plywood, 600f }, { BoundedBuoyancyMaterial.Teak, 660f },
            { BoundedBuoyancyMaterial.Oak, 720f }, { BoundedBuoyancyMaterial.Ice, 917f }, { BoundedBuoyancyMaterial.Plastic, 950f },
            { BoundedBuoyancyMaterial.Ebony, 1100f }, { BoundedBuoyancyMaterial.Rubber, 1100f }, { BoundedBuoyancyMaterial.PVC, 1380f },
            { BoundedBuoyancyMaterial.Brick, 1900f }, { BoundedBuoyancyMaterial.Concrete, 2400f }, { BoundedBuoyancyMaterial.Glass, 2500f },
            { BoundedBuoyancyMaterial.Stone, 2700f }, { BoundedBuoyancyMaterial.Aluminium, 2700f }, { BoundedBuoyancyMaterial.Iron, 7200f },
            { BoundedBuoyancyMaterial.Steel, 7850f }, { BoundedBuoyancyMaterial.Lead, 11340f }, { BoundedBuoyancyMaterial.Gold, 19300f },
        };

        /// <summary>The density of a material (kg/m³), or `fallback` for Custom.</summary>
        public static float DensityOf(BoundedBuoyancyMaterial material, float fallback = 500f)
        {
            float d;
            return densities.TryGetValue(material, out d) ? d : fallback;
        }
    }
}
