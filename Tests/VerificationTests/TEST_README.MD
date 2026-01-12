# VerificationTests – Peer‑Reviewed Benchmarks

This suite verifies the simulations described in `VerificationReport.md` using data and parameters published in peer‑reviewed studies. Each test calls the **GeoscientistToolkit** APIs (no external simulations) and compares results against expected values derived from the literature.

## Covered tests

### Slope Stability – Free fall and inclined-plane sliding
- **Test**: `SlopeStability_GravityDrop_MatchesAnalyticalFreeFall`, `SlopeStability_TiltedGravitySliding_MatchesDownslopeDisplacement`
- **Reference data**: Verlet integration and DEM models with constant gravity used as benchmarks for drop tests and rigid sliding.
- **Use of data**: compare the final position with analytical free-fall motion and the downslope gravity component.
- **Sources (APA)**:
  - Cundall, P. A., & Strack, O. D. L. (1979). A discrete numerical model for granular assemblies. *Géotechnique, 29*(1), 47–65. https://doi.org/10.1680/geot.1979.29.1.47
  - Verlet, L. (1967). Computer “experiments” on classical fluids. I. Thermodynamical properties of Lennard‑Jones molecules. *Physical Review, 159*(1), 98–103. https://doi.org/10.1103/PhysRev.159.98

### PhysicoChem – 1D diffusion (tracer)
- **Test**: `PhysicoChem_DualBoxMixing_TracksReportedDiffusionMagnitude`
- **Reference data**: diffusion coefficients in aqueous solutions for multivalent ions under isothermal conditions.
- **Use of data**: compare the numerical diffusion solution against the analytical (erfc) solution for a step gradient.
- **Sources (APA)**:
  - Li, Y.‑H., & Gregory, S. (1974). Diffusion of ions in sea water and in deep‑sea sediments. *Geochimica et Cosmochimica Acta, 38*(5), 703–714. https://doi.org/10.1016/0016-7037(74)90145-8

### Geothermal – Dual continuum (matrix‑fracture)
- **Test**: `Geothermal_DualContinuumExchange_ReducesTemperatureGap`
- **Reference data**: matrix‑fracture transfer in the Warren‑Root model with exchange coefficients computed from spacing and fracture density.
- **Use of data**: verify that thermal exchange reduces the temperature gradient between fracture and matrix as expected in dual‑continuum models.
- **Sources (APA)**:
  - Warren, J. E., & Root, P. J. (1963). The behavior of naturally fractured reservoirs. *Society of Petroleum Engineers Journal, 3*(3), 245–255. https://doi.org/10.2118/426-PA

### Geomechanics – Triaxial Mohr‑Coulomb
- **Test**: `TriaxialSimulation_MohrCoulombPeakMatchesReference`
- **Reference data**: cohesion and friction angle parameters typical of granite in triaxial tests.
- **Use of data**: compare simulated σ₁,peak against the theoretical Mohr‑Coulomb value with the same lab parameters.
- **Sources (APA)**:
  - Mogi, K. (1967). Effect of the intermediate principal stress on rock failure. *Journal of Geophysical Research, 72*(20), 5117–5131. https://doi.org/10.1029/JZ072i020p05117

### TwoDGeology – Strip footing bearing capacity
- **Test**: `TwoDGeology_BearingCapacityStripFooting_AlignsWithReferenceFactors`, `TwoDGeology_BearingCapacityStripFooting_CohesiveClayMatchesReferenceFactors`
- **Reference data**: bearing-capacity factors for strip footings on Mohr‑Coulomb soils.
- **Use of data**: estimate collapse pressure with the 2D FEM solver and check it falls within the theoretical factor tolerance for two cohesive parameter sets, applying the smooth-footing reduction factor reported in the literature.
- **Sources (APA)**:
  - Meyerhof, G. G. (1951). The ultimate bearing capacity of foundations. *Géotechnique, 2*(4), 301–332. https://doi.org/10.1680/geot.1951.2.4.301
  - Bolton, M. D., & Lau, C. K. (1993). Vertical bearing capacity factors for circular and strip footings on Mohr‑Coulomb soil. *Canadian Geotechnical Journal, 30*(6), 1024–1033. https://doi.org/10.1139/t93-099
  - Meyerhof, G. G. (1963). Some recent research on the bearing capacity of foundations. *Canadian Geotechnical Journal, 1*(1), 16–26. https://doi.org/10.1139/t63-003

### Seismology – P/S arrivals
- **Test**: `SeismicSimulation_PAndSArrivalsMatchVelocityRatio`
- **Reference data**: relationship between P/S arrivals and elastic velocities in layered models.
- **Use of data**: P/S arrival-time ratio consistent with P/S velocity ratios in crustal models.
- **Sources (APA)**:
  - Kennett, B. L. N. (1991). The removal of free surface interactions from three‑component seismograms. *Geophysical Journal International, 104*(1), 153–163. https://doi.org/10.1111/j.1365-246X.1991.tb02498.x

### Hydrology – D8 flow direction / accumulation
- **Test**: `Hydrology_D8FlowAccumulation_MatchesBenchmarkPattern`
- **Reference data**: D8 algorithm and flow accumulation on synthetic DEMs.
- **Use of data**: verify that the valley cell accumulates more contributors than the source, consistent with D8 routing.
- **Sources (APA)**:
  - O’Callaghan, J. F., & Mark, D. M. (1984). The extraction of drainage networks from digital elevation data. *Computer Vision, Graphics, and Image Processing, 28*(3), 323–344. https://doi.org/10.1016/S0734-189X(84)80011-0

### Multiphase Flow – Water‑steam transition
- **Test**: `MultiphaseFlow_WaterSteamTransition_UpdatesSaturations`
- **Reference data**: phase equilibrium and saturation updates in EOS1 (water‑steam).
- **Use of data**: verify that updated saturations remain normalized and reflect two‑phase transition at high temperature.
- **Sources (APA)**:
  - Pruess, K. (2004). The TOUGH codes—A family of simulation tools for multiphase flow and transport. *Vadose Zone Journal, 3*(3), 738–746. https://doi.org/10.2136/vzj2004.0738

### Acoustic Simulation – Elastic FDTD
- **Test**: `AcousticSimulation_StressPulseGeneratesVelocity`
- **Reference data**: staggered‑grid propagation and stress‑pulse response.
- **Use of data**: a σxx pulse generates non‑zero velocities in adjacent nodes, consistent with elastic FDTD.
- **Sources (APA)**:
  - Virieux, J. (1986). P‑SV wave propagation in heterogeneous media: Velocity‑stress finite‑difference method. *Geophysics, 51*(4), 889–901. https://doi.org/10.1190/1.1442147

### PNM – Absolute permeability
- **Test**: `PnmAbsolutePermeability_SingleTubeMatchesPoiseuille`
- **Reference data**: Poiseuille flow in a single capillary and the Darcy permeability relationship.
- **Use of data**: compare the calculated permeability for a single tube with the theoretical order of magnitude for capillaries.
- **Sources (APA)**:
  - Dullien, F. A. L. (1992). *Porous Media: Fluid Transport and Pore Structure* (2nd ed.). Academic Press.

## Operational notes
- The tests **do not** cover data loaders, as requested.
- Each simulation has a dedicated report in `Reports/`.
