// GeoscientistToolkit/Business/MaterialLibraryExtensions.cs
//
// Adds an extended catalog of physical materials that complement the
// default entries bundled with the MaterialLibrary singleton.
//
// DATA SOURCES (material-specific citations listed with each entry):
// - Batzle, M. & Wang, Z., 1992. Seismic properties of pore fluids. Geophysics 57(11).
// - Christensen, N.I., 1996. Poisson's ratio and crustal seismology. Journal of Geophysical Research, 101(B2).
// - Fofonoff, P. & Millard Jr., R.C., 1983. Algorithms for computation of fundamental properties of seawater. UNESCO Tech. Paper in Marine Science No. 44.
// - Kestin, J. et al., 1981. Viscosity of aqueous NaCl solutions. Journal of Physical and Chemical Reference Data.
// - Mindess, S. & Young, J.F., 2002. Concrete. 2nd ed. Prentice Hall.
// - Neville, A.M., 2011. Properties of Concrete, 5th ed. Pearson.
// - Petrenko, V.F. & Whitworth, R.W., 1999. Physics of Ice. Oxford University Press.
// - Schon, J., 2015. Physical Properties of Rocks, 2nd ed. Elsevier.
// - Additional rock property compilations: Carmichael, R.S., 1982. Handbook of Physical Properties of Rocks.

using System;
using System.Collections.Generic;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
///     Extension methods that append additional, peer-reviewed materials to the
///     <see cref="MaterialLibrary"/> without bloating the base class.
/// </summary>
public static class MaterialLibraryExtensions
{
    /// <summary>
    ///     Adds advanced laboratory and reservoir materials (fluids, rocks, engineered
    ///     media) that are useful in simulations beyond the default seed set.
    /// </summary>
    public static void SeedExtendedPhysicalMaterials(this MaterialLibrary library)
    {
        if (library == null) throw new ArgumentNullException(nameof(library));

        Logger.Log("[MaterialLibraryExtensions] Seeding extended physical materials...");

        var extended = new List<PhysicalMaterial>
        {
            new PhysicalMaterial
            {
                Name = "Seawater (35 PSU, 15degC)",
                Phase = PhaseType.Liquid,
                Viscosity_Pa_s = 1.19e-3,
                Density_kg_m3 = 1025,
                ThermalConductivity_W_mK = 0.58,
                SpecificHeatCapacity_J_kgK = 3990,
                TypicalWettability_contactAngle_deg = 10,
                Vp_m_s = 1530,
                AcousticImpedance_MRayl = 1.57,
                ElectricalResistivity_Ohm_m = 0.2,
                Notes = "Open-ocean seawater at practical salinity 35 and 15 °C.",
                Sources = new List<string>
                {
                    "Fofonoff & Millard (1983) UNESCO Tech Paper 44",
                    "Millero, Chemical Oceanography (CRC Press)"
                },
                IsUserMaterial = false
            },
            new PhysicalMaterial
            {
                Name = "Brine (25 wt% NaCl, 25degC)",
                Phase = PhaseType.Liquid,
                Viscosity_Pa_s = 2.5e-3,
                Density_kg_m3 = 1200,
                ThermalConductivity_W_mK = 0.45,
                SpecificHeatCapacity_J_kgK = 3500,
                TypicalWettability_contactAngle_deg = 20,
                Vp_m_s = 1650,
                AcousticImpedance_MRayl = 1.98,
                ElectricalResistivity_Ohm_m = 0.02,
                Notes = "Concentrated NaCl solution used to represent formation brines.",
                Sources = new List<string>
                {
                    "Batzle & Wang (1992) Geophysics 57(11)",
                    "Kestin et al. (1981) J. Phys. Chem. Ref. Data"
                },
                IsUserMaterial = false
            },
            new PhysicalMaterial
            {
                Name = "Ice Ih (0degC)",
                Phase = PhaseType.Solid,
                MohsHardness = 1.5,
                Density_kg_m3 = 917,
                YoungModulus_GPa = 9,
                PoissonRatio = 0.33,
                CompressiveStrength_MPa = 5,
                TensileStrength_MPa = 1,
                ThermalConductivity_W_mK = 2.2,
                SpecificHeatCapacity_J_kgK = 2100,
                TypicalPorosity_fraction = 0.0,
                Vp_m_s = 3900,
                Vs_m_s = 1900,
                AcousticImpedance_MRayl = 3.58,
                ElectricalResistivity_Ohm_m = 1e8,
                Notes = "Polycrystalline ice Ih at the melting point.",
                Sources = new List<string>
                {
                    "Petrenko & Whitworth (1999) Physics of Ice",
                    "Cuffey & Paterson (2010) Physics of Glaciers"
                },
                IsUserMaterial = false
            },
            new PhysicalMaterial
            {
                Name = "Halite (Rock Salt)",
                Phase = PhaseType.Solid,
                MohsHardness = 2.5,
                Density_kg_m3 = 2160,
                YoungModulus_GPa = 30,
                PoissonRatio = 0.35,
                FrictionAngle_deg = 32,
                CompressiveStrength_MPa = 25,
                TensileStrength_MPa = 2,
                ThermalConductivity_W_mK = 5.5,
                SpecificHeatCapacity_J_kgK = 860,
                TypicalPorosity_fraction = 0.01,
                Vp_m_s = 4500,
                Vs_m_s = 2500,
                AcousticImpedance_MRayl = 9.7,
                ElectricalResistivity_Ohm_m = 1e8,
                Notes = "Massive halite salt with very low porosity.",
                Sources = new List<string>
                {
                    "Carmichael (1982) Handbook of Physical Properties of Rocks",
                    "Schon (2015) Physical Properties of Rocks"
                },
                IsUserMaterial = false
            },
            new PhysicalMaterial
            {
                Name = "Dolostone (dense)",
                Phase = PhaseType.Solid,
                MohsHardness = 3.5,
                Density_kg_m3 = 2820,
                YoungModulus_GPa = 50,
                PoissonRatio = 0.28,
                FrictionAngle_deg = 35,
                CompressiveStrength_MPa = 170,
                TensileStrength_MPa = 8,
                ThermalConductivity_W_mK = 3.2,
                SpecificHeatCapacity_J_kgK = 880,
                TypicalPorosity_fraction = 0.05,
                Vp_m_s = 6000,
                Vs_m_s = 3400,
                AcousticImpedance_MRayl = 16.9,
                ElectricalResistivity_Ohm_m = 1e4,
                Notes = "Tightly cemented dolostone common in carbonate reservoirs.",
                Sources = new List<string>
                {
                    "Wang, Z. et al. (2000) Seismic velocities in carbonate rocks, SEG",
                    "Schon (2015) Physical Properties of Rocks"
                },
                IsUserMaterial = false
            },
            new PhysicalMaterial
            {
                Name = "Peridotite (olivine-rich)",
                Phase = PhaseType.Solid,
                MohsHardness = 6.5,
                Density_kg_m3 = 3300,
                YoungModulus_GPa = 110,
                PoissonRatio = 0.28,
                FrictionAngle_deg = 40,
                CompressiveStrength_MPa = 250,
                ThermalConductivity_W_mK = 4.0,
                SpecificHeatCapacity_J_kgK = 800,
                TypicalPorosity_fraction = 0.005,
                Vp_m_s = 8200,
                Vs_m_s = 4700,
                AcousticImpedance_MRayl = 27.1,
                MagneticSusceptibility_SI = 4e-3,
                Notes = "Ultramafic mantle-derived rock dominated by olivine and pyroxene.",
                Sources = new List<string>
                {
                    "Christensen (1996) JGR 101(B2)",
                    "Carmichael (1982) Handbook of Physical Properties of Rocks"
                },
                IsUserMaterial = false
            },
            new PhysicalMaterial
            {
                Name = "High-Strength Concrete (C60)",
                Phase = PhaseType.Solid,
                Density_kg_m3 = 2450,
                YoungModulus_GPa = 38,
                PoissonRatio = 0.2,
                CompressiveStrength_MPa = 65,
                TensileStrength_MPa = 4,
                ThermalConductivity_W_mK = 2.0,
                SpecificHeatCapacity_J_kgK = 900,
                Vp_m_s = 4000,
                Vs_m_s = 2400,
                ElectricalResistivity_Ohm_m = 100,
                Notes = "Modern high-strength Portland cement concrete used in infrastructure.",
                Sources = new List<string>
                {
                    "Neville (2011) Properties of Concrete",
                    "Mindess & Young (2002) Concrete"
                },
                IsUserMaterial = false
            },
            new PhysicalMaterial
            {
                Name = "Bentonite Clay (saturated)",
                Phase = PhaseType.Solid,
                MohsHardness = 1.5,
                Density_kg_m3 = 2000,
                YoungModulus_GPa = 2,
                PoissonRatio = 0.45,
                FrictionAngle_deg = 15,
                CompressiveStrength_MPa = 5,
                TensileStrength_MPa = 0.5,
                ThermalConductivity_W_mK = 0.9,
                SpecificHeatCapacity_J_kgK = 900,
                TypicalPorosity_fraction = 0.40,
                Vp_m_s = 1500,
                Vs_m_s = 600,
                ElectricalResistivity_Ohm_m = 1,
                Notes = "Water-saturated sodium bentonite representative of sealing clays.",
                Sources = new List<string>
                {
                    "Lambe & Whitman (1969) Soil Mechanics",
                    "Mitchell & Soga (2005) Fundamentals of Soil Behavior"
                },
                IsUserMaterial = false
            },
            new PhysicalMaterial
            {
                Name = "Hydrogen (H2, 20degC, 1 atm)",
                Phase = PhaseType.Gas,
                Density_kg_m3 = 0.0838,
                Viscosity_Pa_s = 8.9e-6,
                ThermalConductivity_W_mK = 0.180,
                SpecificHeatCapacity_J_kgK = 14300,
                Vp_m_s = 1280,
                Notes = "Dry hydrogen gas at ambient conditions.",
                Sources = new List<string>
                {
                    "NIST Chemistry WebBook thermophysical tables",
                    "Span et al. (2000) High-Temperature Hydrogen Properties, J. Phys. Chem. Ref. Data"
                },
                IsUserMaterial = false
            }
        };

        foreach (var mat in extended)
        {
            library.AddOrUpdate(mat);
        }

        Logger.Log($"[MaterialLibraryExtensions] Added {extended.Count} extended physical materials.");
    }
}
