# GeoscientistToolkit.Api

This project exposes a lightweight API DLL that wraps core simulation workflows, dataset loaders, and GeoScript execution.
It is intended for automation scenarios (batch verification, integrations, or external tooling) that need to call into
Geoscientist Toolkit without hosting the full UI.

## What it provides

- **Verification simulations** aligned with `VerificationReport.md` (geomechanics, seismology, slope stability,
  thermodynamics, PNM, acoustic, heat transfer, hydrology, geothermal).
- **Dataset loader access** for the same loader classes used by the UI.
- **GeoScript command execution** (pipeline-ready).
- **Acoustic 3D velocity simulation** via `ChunkedAcousticSimulator`.

## Usage

```csharp
using GeoscientistToolkit.Api;

var api = new VerificationSimulationApi();
var triaxial = api.RunGeomechanicsGraniteVerification();

var geoScriptApi = new GeoScriptApi();
var output = await geoScriptApi.ExecuteAsync("INFO", inputDataset);

var loaderApi = new LoaderApi();
var dataset = await loaderApi.LoadAcousticVolumeAsync("/path/to/volume");
```

## Documentation output

The project generates XML documentation during build (`GeoscientistToolkit.Api.xml`).
Use this file with IDE tooling or documentation generators to surface API comments.
