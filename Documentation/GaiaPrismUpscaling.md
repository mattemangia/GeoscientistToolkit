# GAIA ↔ PRISM Upscaling Interface

The upscaling interface extends the existing GPEX bridge (`Interop/GaiaPrism/`) with a new
`upscaling` domain (contract `1.0.0`) that moves petrography/petrophysics/CT-scale results up to
reservoir scale (PRISM) and reservoir-model wells back down to pore scale (GAIA).

## What it exchanges

A `.gpex` package with `domain = "upscaling"` carries:

- `payload/wells.json` — wells (header, CRS/coordinates, elevation, total depth), stratigraphic
  intervals with canonical SI properties, and depth-indexed logs (parameter tracks,
  temperature profiles).
- `payload/pnm/<id>.json` — one summary per pore network: voxel/pore/throat statistics, porosity,
  Darcy / Navier-Stokes / lattice-Boltzmann permeability (mD), tortuosity, formation factor,
  diffusivity, and a qualification status.
- Manifest `effectiveProperties` — package-level zone upscale (porosity, kh, kv in SI) plus REV
  assessment and validation messages, following the same qualification gating as the
  geomechanics domain.

Canonical property names/units live in `UpscalingPropertyNames` (SI: permeability in m²,
porosity as a fraction). GAIA display conventions (porosity %, permeability mD, Young GPa) are
converted at the mapper boundary only.

## GAIA → PRISM (upscale)

```csharp
var manifest = UpscalingGpexExporter.Export(
    "exchange.gpex", projectId: "my-project",
    boreholes: [boreholeDataset],           // GAIA BoreholeDataset(s)
    pnms: [pnmDataset],                     // characterised PNMDataset(s)
    assignments: [new UpscalingGpexExporter.PnmWellAssignment(pnmDataset, 10f, 20f)]);
```

- Lithology units become intervals; unit parameters become canonical properties.
- PNMs are attached to the intervals they overlap (explicit assignments, or auto-discovered from
  `LithologyUnit.ParameterSources` entries created by *Import Parameters from Dataset*).
- Intervals lacking measured values are filled by upscaling the assigned networks
  (`IntervalUpscaler`): porosity arithmetic, permeability geometric (with arithmetic/harmonic
  bounds), preferring lattice-Boltzmann > Navier-Stokes > Darcy permeability.
- Well stacks are upscaled to zone values with the classical layered bounds: kh
  thickness-weighted arithmetic, kv thickness-weighted harmonic.

PRISM imports the package with `prism-gaia-bridge import-wells exchange.gpex`, producing
boreholes whose `StratigraphyLayer` petrophysics (porosity, permeability, density, thermal,
Vp/Vs) are filled and tagged `PetrophysicsOrigin = "GaiaPnmUpscaled"`.

## PRISM → GAIA (downscale)

PRISM exports reservoir-model wells (`prism-gaia-bridge export-wells wells.json --output pkg.gpex`);
GAIA reads them with:

```csharp
var package = UpscalingGpexImporter.Read("pkg.gpex");
var borehole = UpscalingGpexImporter.ToBoreholeDataset(package.Wells.Wells[0], "well.borehole");
```

Intervals become lithology units (parameters converted back to GAIA units), logs become
parameter tracks — ready to serve as targets/boundary conditions for pore-scale simulation.

## CLI

- `GAIA --bridge-validate <pkg.gpex>` — checksum + manifest validation (any domain).
- `GAIA --bridge-wells <pkg.gpex>` — per-well interval table and zone upscale (phi, kh, kv).
- `GAIA --bridge-inspect / --bridge-extract` — unchanged.

## Qualification

Single-plug PNM results are exchanged as screening priors: the manifest REV assessment defaults
to `NotEvaluated` and validators emit `rev.not-representative` warnings until a nested-window REV
analysis is attached. The most conservative qualification of any contributing network gates all
manifest effective properties.

The contract types (`UpscalingContracts.cs`, `IntervalUpscaler.cs`) are mirrored verbatim in
PRISM's `Prism.GaiaBridge` (namespace aside); keep the two copies in sync.
