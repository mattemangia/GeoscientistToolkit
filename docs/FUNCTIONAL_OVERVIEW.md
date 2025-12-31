# Functional Overview

This document maps the main product functions to their primary guides and code entry points. It is intended to answer “where is this feature described?” and “where does it live in the code?” in one place.

If you are looking for a linear onboarding flow, start at [START_HERE.md](../START_HERE.md).

---

## Core platform concepts

- **Projects, datasets, materials, metadata**
  - Documentation: [GUIDE.md](../GUIDE.md)
  - Code entry points: `Data/` (dataset types), `Business/` (project management), `UI/` (import/export flows)

- **GeoScript (scripting and pipelines)**
  - Documentation: [GEOSCRIPT_MANUAL.md](../GEOSCRIPT_MANUAL.md)
  - Image pipeline ops: [GEOSCRIPT_IMAGE_OPERATIONS.md](GEOSCRIPT_IMAGE_OPERATIONS.md)
  - Code entry points: `Scripting/`, `Api/GeoScriptApi.cs`

---

## Imaging, segmentation, and visualization

- **CT imaging and segmentation (manual + AI)**
  - Documentation: [GUIDE.md](../GUIDE.md), [AI_SEGMENTATION_GUIDE.md](AI_SEGMENTATION_GUIDE.md)
  - Model info: [ONNX_MODELS.md](ONNX_MODELS.md)
  - Code entry points: `Data/CtImageStack/`, `UI/Tools/`, `ONNX/`

- **2D image processing & filters**
  - Documentation: [GEOSCRIPT_IMAGE_OPERATIONS.md](GEOSCRIPT_IMAGE_OPERATIONS.md)
  - Code entry points: `Data/Image/`, `Business/GeoScriptImageCommands.cs`

- **3D visualization and rendering**
  - Documentation: [GUIDE.md](../GUIDE.md)
  - Code entry points: `UI/`, `GTK/`

---

## Geophysics & geology workflows

- **Seismic analysis, well ties, and earthquake simulation**
  - Documentation: [GUIDE.md](../GUIDE.md)
  - Earthquake guides: [EARTHQUAKE_SIM_QUICKSTART.md](EARTHQUAKE_SIM_QUICKSTART.md), [EARTHQUAKE_SUBSURFACE_INTEGRATION.md](EARTHQUAKE_SUBSURFACE_INTEGRATION.md)
  - Code entry points: `Analysis/Seismology/`, `UI/Seismic/`, `Business/GeoScriptSeismicCommands.cs`

- **Slope stability (3D/2D)**
  - Documentation: [SLOPE_STABILITY_SIMULATION.md](SLOPE_STABILITY_SIMULATION.md)
  - Module README: [Analysis/SlopeStability/README.md](../Analysis/SlopeStability/README.md)
  - Code entry points: `Analysis/SlopeStability/`, `UI/Tools/`

---

## Petrophysics, flow, and pore networks

- **Pore network modeling (single/dual)**
  - Documentation: [DUAL_PNM_IMPLEMENTATION.md](DUAL_PNM_IMPLEMENTATION.md)
  - Code entry points: `Analysis/PNM/`, `Business/GeoScriptPNMCommands.cs`

- **Hydrology & flow analysis**
  - Documentation: [GUIDE.md](../GUIDE.md)
  - Code entry points: `Analysis/Hydrological/`

---

## Thermodynamics, geochemistry, and reactors

- **Thermodynamics & geochemistry**
  - Documentation: [THERMODYNAMICS_ENHANCEMENTS.md](THERMODYNAMICS_ENHANCEMENTS.md)
  - Guides: [PHYSICOCHEM_GUIDE.md](../PHYSICOCHEM_GUIDE.md), [QUICK_START_REACTIONS.md](../QUICK_START_REACTIONS.md)
  - Code entry points: `Analysis/Thermodynamic/`, `Business/GeoScriptThermodynamicsExtensions.cs`

- **Physico-chemical reactors (PHYSICOCHEM)**
  - Documentation: [PHYSICOCHEM_GUIDE.md](../PHYSICOCHEM_GUIDE.md)
  - Code entry points: `Analysis/PhysicoChem/`, `Business/GeoScriptPhysicoChemExtensions.cs`

---

## Simulation modules

- **Acoustic velocity simulation**
  - Documentation: [GUIDE.md](../GUIDE.md)
  - API: `Api/AcousticVolumeSimulationApi.cs`
  - Code entry points: `Analysis/AcousticSimulation/`

- **Geomechanics**
  - Documentation: [GUIDE.md](../GUIDE.md)
  - Code entry points: `Analysis/Geomechanics/`

- **Thermal & geothermal simulation**
  - Documentation: [GUIDE.md](../GUIDE.md)
  - Code entry points: `Analysis/ThermalConductivity/`, `Analysis/Geothermal/`

---

## Additional analysis modules and utilities

These modules are implemented under `Analysis/` and are primarily exposed through the UI tool panels and/or GeoScript commands. If you add new functionality, document it here with a short description and the entry points.

- **Ambient occlusion segmentation**: rapid volumetric shading segmentation helpers.  
  - Code entry points: `Analysis/AmbientOcclusionSegmentation/`
- **Image adjustment**: brightness/contrast/filters used in 2D workflows.  
  - Code entry points: `Analysis/ImageAdjustement/`, `Business/GeoScriptImageCommands.cs`
- **Material statistics**: compute per-material summaries for segmented volumes.  
  - Code entry points: `Analysis/MaterialStatistics/`
- **Material library tools**: material definitions and UI editors.  
  - Code entry points: `Analysis/Materials/`, `Business/MaterialLibrary.cs`, `UI/MaterialLibraryWindow.cs`
- **Multiphase workflows**: multiphase dataset preparation and coupling.  
  - Code entry points: `Analysis/Multiphase/`, `Business/GeoScriptMultiphaseExtensions.cs`
- **NMR simulation**: T2 relaxation and related NMR workflows.  
  - Code entry points: `Analysis/NMR/`
- **Particle separator**: particle classification and separation utilities.  
  - Code entry points: `Analysis/ParticleSeparator/`
- **Photogrammetry**: reconstruction and mesh utilities.  
  - Documentation: [Analysis/Photogrammetry/README.md](../Analysis/Photogrammetry/README.md)  
  - Code entry points: `Analysis/Photogrammetry/`
- **Rock core extraction**: core ROI extraction and related helpers.  
  - Code entry points: `Analysis/RockCoreExtractor/`
- **Texture classification**: classification of textures from imagery.  
  - Code entry points: `Analysis/TextureClassification/`
- **Transform utilities**: geometry transforms and dataset conversions.  
  - Code entry points: `Analysis/Transform/`
- **Unified calibration manager**: shared calibration utilities.  
  - Code entry points: `Analysis/UnifiedCalibrationManager.cs`

---

## Automation and API

- **API usage for external automation**
  - Documentation: [Api/README.md](../Api/README.md)
  - Code entry points: `Api/` (e.g., `GeoScriptApi.cs`, `LoaderApi.cs`, `VerificationSimulationApi.cs`)

- **Verification scenarios**
  - Documentation: [VerificationReport.md](../VerificationReport.md)
  - Code entry points: `VerificationTests/`, `VerificationReport.md`, `Api/VerificationSimulationApi.cs`

---

## Installation and packaging

- **Installers & packaging**
  - Documentation: [installers.md](installers.md)
  - Code entry points: `InstallerWizard/`, `InstallerPackager/`, `InstallerWizard/Installers/`

---

If any function is missing from this map, add it here with a short description, the relevant guide, and the primary code entry point(s).
