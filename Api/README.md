# GAIA.Api

This project exposes a lightweight API DLL that wraps core simulation workflows, dataset loaders, and GeoScript execution.
It is intended for automation scenarios (batch verification, integrations, or external tooling) that need to call into
GAIA (Geoscience Analysis, Imaging & Automation) without hosting the full UI.

## What it provides

- **Verification simulations** aligned with `VerificationReport.md` (geomechanics, seismology, slope stability,
  thermodynamics, PNM, acoustic, heat transfer, hydrology, geothermal).
- **Dataset loader access** for the same loader classes used by the UI.
- **GeoScript command execution** (pipeline-ready).
- **Acoustic 3D velocity simulation** via `ChunkedAcousticSimulator`.
- **Seismic cube construction/export** via `SeismicCubeApi`.

## Usage

```csharp
using GAIA.Api;

var api = new VerificationSimulationApi();
var triaxial = api.RunGeomechanicsGraniteVerification();

var geoScriptApi = new GeoScriptApi();
var output = await geoScriptApi.ExecuteAsync("INFO", inputDataset);

var loaderApi = new LoaderApi();
var dataset = await loaderApi.LoadAcousticVolumeAsync("/path/to/volume");

var cubeApi = new SeismicCubeApi();
var cube = cubeApi.CreateCube("Survey_Cube", "Field_2024");
cubeApi.AddLineFromHeaders(cube, seismicLine);
cubeApi.DetectIntersections(cube);
cubeApi.BuildVolume(cube, 200, 200, 1500, 25f, 25f, 4f);
await cubeApi.ExportAsync(cube, "/path/to/cube.seiscube");
```

## Referencing the API DLL

After building the solution, the API assembly is available at:

```
Api/bin/<Configuration>/net8.0/GAIA.Api.dll
```

To use it in another project, add a project or file reference:

```xml
<!-- Preferred: project reference inside the same solution -->
<ItemGroup>
  <ProjectReference Include="..\GAIA\Api\GAIA.Api.csproj" />
</ItemGroup>

<!-- Alternatively: reference the built DLL -->
<ItemGroup>
  <Reference Include="GAIA.Api">
    <HintPath>path\to\GAIA.Api.dll</HintPath>
  </Reference>
</ItemGroup>
```

The API DLL targets .NET 8.0, so ensure your project targets `net8.0` (or a compatible framework).

## Documentation output

The project generates XML documentation during build (`GAIA.Api.xml`).
Use this file with IDE tooling or documentation generators to surface API comments.
