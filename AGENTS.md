# AGENTS.md

Guidance for AI agents working in the **GAIA (Geoscience Analysis, Imaging & Automation)** codebase.
GAIA is a cross-platform .NET 10 desktop application for geoscientific data analysis, visualization, and simulation,
built on ImGui.NET + Veldrid with a custom scripting language (GeoScript).

> **Note:** Much of the in-repo documentation (README, wiki, `docs/CODEBASE_ANALYSIS.md`) still references
> ".NET 8.0". The solution was upgraded to **.NET 10** (`net10.0`). Trust `GAIA.sln` / `*.csproj` over docs.

---

## Build, Run, Test

```bash
# Restore + build the entire solution (what CI does)
dotnet restore GAIA.sln
dotnet build GAIA.sln -c Release --no-restore

# Build only the main app (faster feedback loop)
dotnet build GAIA.csproj

# Run the desktop app
dotnet run --project GAIA.csproj
# ...or with CLI flags / a project file:
dotnet run --project GAIA.csproj -- --ai-diagnostic
dotnet run --project GAIA.csproj -- --gui-diagnostic
dotnet run --project GAIA.csproj -- --test=all
dotnet run --project GAIA.csproj -- --test=SlopeStability_GravityDrop_MatchesAnalyticalFreeFall
dotnet run --project GAIA.csproj -- path/to/project.gtp
```

### Tests

The verification suite lives in `Tests/VerificationTests/` and uses **xUnit** (`[Fact]`, `global using Xunit;`).
Tests exercise the real `GAIA.Analysis.*` solvers and compare results against peer-reviewed literature
(sources + tolerances documented in `Tests/VerificationTests/TEST_README.MD` and per-test `Reports/*.md`).

```bash
# Standard xUnit run via dotnet test
dotnet test Tests/VerificationTests/VerificationTests.csproj

# Standalone runner (no test SDK required) — used by the installer bundle
dotnet run --project Tests/VerificationTestsRunner/VerificationTestsRunner.csproj -- [filters]
```

`VerificationTestsRunner` discovers/runs `VerificationTests` via `xunit.runner.utility` and is what the
`BuildVerificationTests` MSBuild target (in `GAIA.csproj`) copies into publish output so `--test=...`
works in shipped binaries. `SkipVerificationTests=true` is set on the main project by default, so normal
builds do **not** trigger the test-bundling target.

Other projects: `Tests/BenchmarkTests/` (commercial-software comparisons) and
`VerificationTests/RealCaseVerifier/` (real-data verification harness).

---

## Solution Architecture

`GAIA.sln` contains multiple projects that **intentionally share source files** rather than always using
project references. This is the single most error-prone aspect of the build — read `GAIA.csproj` before
assuming how something compiles.

| Project | Type | Purpose |
|---|---|---|
| `GAIA` (`GAIA.csproj`, root) | Exe | **Main app**: ImGui + Veldrid desktop UI, all datasets, simulations, GeoScript |
| `GAIA.Api` (`Api/`) | Library | Headless automation DLL wrapping loaders, GeoScript, verification sims (see `Api/README.md`) |
| `NodeEndpoint` (`NodeEndpoint/`) | ASP.NET Core Exe | Distributed compute server. **Cherry-picks** `.cs` files from main project via `<Compile Include="..\...">` to avoid UI deps |
| `GAIA.Gtk` (`GTK/`) | Exe | Alternative GTK frontend referencing the main project |
| `InstallerWizard` / `InstallerPackager` | Exe | TUI installer + packaging tool for `dotnet publish` outputs |
| `VerificationTests` / `BenchmarkTests` / `RealCaseVerifier` | Test | xUnit verification suites |

### Critical source-sharing rules (gotchas)

1. **The root `GAIA.csproj` excludes all sibling projects' folders** with `<Compile Remove="NodeEndpoint\**" />`,
   `<Compile Remove="Api\**" />`, `<Compile Remove="GTK\**" />`, `<Compile Remove="Tests\**" />`, etc.
   If you add a new sibling project, you must add a `<Compile Remove>` here or you'll hit duplicate-attribute errors.

2. **`AddIns/Development/**` is compiled in Debug but removed in Release** (`<Compile Remove="AddIns\Development\**\*.cs" />`
   under `Condition="'$(Configuration)' == 'Release'"`). Development add-ins ship only in debug builds.

3. **`Data/TwoDGeology/Geomechanics/TwoDGeomechanicsGtkViewer.cs`** is excluded from the main project but included in
   `GAIA.Gtk` — it has GTK dependencies. Don't move non-GTK code into it.

4. **`NodeEndpoint` does not reference `GAIA.csproj`.** It selectively `<Compile Include>`s headless files
   (Network, Settings, OpenCL, `Analysis/Geomechanics/*CPU*.cs`, `Analysis/AcousticSimulation/*CPU*.cs`, etc.).
   Any UI-dependent file added to those folders will break the NodeEndpoint build. There's a comment in its csproj
   noting e.g. `PNMDataset.cs` was excluded for "91 errors due to missing TableDataset UI dependencies".

5. **Assembly info is generated manually** (`GenerateAssemblyInfo=false`, `GenerateTargetFrameworkAttribute=false`
   in `Directory.Build.props` + every csproj). This is required because shared source files would otherwise emit
   duplicate `AssemblyVersion`/`TargetFramework` attributes across projects. Keep these flags `false`.

---

## Code Organization & Layering

Namespaces mirror folders (`GAIA.Data`, `GAIA.UI`, `GAIA.Business`, `GAIA.Analysis.*`, `GAIA.Util`, `GAIA.Settings`).
File-scoped namespaces are used throughout.

```
Program.cs / Application.cs   Entry point, window + graphics device lifecycle, splash/loading screens
UI/                           ImGui windows, panels, viewers, dialogs (ImGuiController, ImGuiDockBuilder)
Data/                         Dataset model + per-type datasets, Loaders/, Exporters/
Data/Loaders/                 IDataLoader implementations + DataLoaderFactory (extension→loader mapping)
Analysis/                     Simulation engines (CPU + GPU variants per domain)
Business/                     Domain logic: GeoScript engine, MaterialLibrary, CompoundLibrary, ProjectManager
Scripting/GeoScript/          GeoScript language runtime (lexer/parser/AST) + Operations
AddIns/                       Plugin framework (IAddIn, AddInManager) + sample/dev add-ins
Network/                      Distributed node discovery/messaging
Settings/                     AppSettings (JSON) + SettingsManager singleton
Util/                         Logger, image decoders/exporters, VeldridManager, cross-platform helpers
OpenCL/, Shaders/             GPU kernels + GLSL/SPIR-V shaders
```

### The Dataset pipeline (central abstraction)

Everything revolves around `Data/Dataset.cs`:

- `DatasetType` enum + abstract `Dataset` (`Load()`, `Unload()`, `GetSizeInBytes()`).
- To **add a dataset type**: add the enum value, create a subclass in `Data/<Type>/`, register a loader in
  `Data/Loaders/DataLoaderFactory.cs`, and wire UI via `UI/DatasetUIFactory.cs` +
  `IDatasetViewer` / `IDatasetTools` / `IDatasetPropertiesRenderer`. See `wiki/Developer-Guide.md` for the full recipe.
- `DataLoaderFactory` maps **file extensions → loader types**. Some extensions are ambiguous (`.tif`/`.tiff` →
  SingleImage *and* CtImageStack; `.wav` → Audio *and* AcousticVolume). Pass `preferredType` to disambiguate;
  otherwise last-registered wins, with a hardcoded `.tif`→SingleImage default.

### Simulation module convention

Analysis modules under `Analysis/<Domain>/` follow a split-file convention (not partial classes — separate files):

```
MyDomainParameters.cs     Input configuration (POCO)
MyDomainResults.cs        Output data
MyDomainSimulationCPU.cs  CPU solver
MyDomainSimulationGPU.cs  OpenCL GPU solver (optional)
MyDomainTool.cs / *UI.cs  ImGui interface wrapper
```

Tests construct parameters/results directly and call the CPU solver (e.g. `SlopeStabilitySimulator(dataset, parameters).RunSimulation()`),
so you can verify numerics without the UI.

---

## GeoScript — two command systems (important)

GeoScript is a pipeline DSL (`dataset |> COMMAND key=value`). There are **two separate registries** — don't confuse them:

1. **`Scripting/GeoScript/Operations/`** — `IOperation` + `OperationRegistry` (static ctor).
   Image/table/GIS/generic *operations* used by the pipeline runtime. Registered per `DatasetType`.

2. **`Business/GeoScript.cs`** — `IGeoScriptCommand` + `CommandRegistry` (static ctor in `Business/GeoScript.cs`).
   This is the larger system: ~150 commands across tables, GIS, thermodynamics, petrology, PhysicoChem, PNM,
   images, CT, borehole, seismic, mesh, etc. Commands live in `Business/GeoScript/Commands/<Domain>/` and in
   `Business/GeoScript<Domain>Commands.cs` partial-style files.

**Both registries are populated by manually `new`-ing each command in a static constructor — there is NO reflection-based
auto-discovery.** When you add a command, you must add it to the appropriate list in the static constructor or it won't be found.

Key GeoScript helpers:
- `GeoScriptArgumentParser` (`Business/GeoScriptArgumentParser.cs`) — `key=value` parsing with `GetString/GetFloat/GetDouble/GetInt/GetBool`,
  invariant culture, and variable resolution against the `GeoScriptContext`.
- `GeoScriptEngine.ExecuteAsync(script, inputDataset, contextDatasets)` — main entry point.
- `UI/GeoScriptInterpreter.cs` — the in-app REPL terminal (separate from the engine).

---

## Conventions & Patterns

- **C# style**: file-scoped namespaces, `ImplicitUsings` enabled, **`Nullable` is disabled** in the main `GAIA`
  project (but enabled in tests, API, NodeEndpoint, GTK). Be aware nullable annotations are inconsistent across the codebase.
- **`AllowUnsafeBlocks`** is on (ImGui callbacks, GPU interop, image buffers).
- **Singletons**: `ProjectManager`, `AddInManager.Instance`, `SettingsManager.Instance`, `GlobalPerformanceManager.Instance`,
  `Logger` (static). State is process-global.
- **Logging**: always use `GAIA.Util.Logger` (`Logger.Log/LogWarning/LogError`). It's a thread-safe static queue + optional
  file writer initialized from settings. There's an explicit `using LogLevel = GAIA.Settings.LogLevel;` alias to avoid clashing
  with `Microsoft.Extensions.Logging.LogLevel` in the ASP.NET Core projects — preserve that alias.
- **UI**: inherit `BasePanel` for dockable/pop-out panels (it manages OS-window pop-out via `PopOutWindow`). Dataset UIs
  implement `IDatasetViewer`/`IDatasetTools`/`IDatasetPropertiesRenderer`. ImGui docking is enabled
  (`DefineConstants=IMGUI_HAS_DOCK_BUILDER`).
- **Graphics**: `VeldridManager` (Util/) holds the shared device/window. `Application.Run()` tries platform-default
  backends with explicit fallbacks (D3D11 → ... on Windows; Vulkan/OpenGL on Linux/macOS). macOS picks up Homebrew `libSDL2.dylib`
  via the `CopyMacSDL2` publish target.
- **Warnings are heavily suppressed**: `Directory.Build.props` sets `<WarningLevel>0</WarningLevel>` and every csproj carries
  a long `<NoWarn>` list (CS86xx nullability, CS0169/CS0414 unused fields, CA1416 platform, etc.). Don't be alarmed by what
  looks like unaddressed warnings — but also don't rely on the compiler to catch nullability issues in the main project.

---

## Platform / Dependency Notes

- **OpenCV** (`OpenCvSharp4`) uses different runtime packages per OS/RID — Windows, `linux-x64`, `linux-arm64`, `osx-x64`,
  and `osx-arm64` each resolve to a different NuGet package via MSBuild conditions in `GAIA.csproj`. When publishing, always
  pass a `-r <RID>` (e.g. `-r linux-x64`), or the wrong/missing native runtime will be selected.
- **ONNX Runtime**: GPU package (`Microsoft.ML.OnnxRuntime.Gpu`) for `win-x64`/`linux-x64`; CPU package for ARM64/macOS/no-RID.
  AI segmentation models (SAM2, MicroSAM, Grounding DINO) are optional — see `ONNX/README.md` and `prepare-onnx.sh/.cmd`.
- **GDAL** + `MaxRev.Gdal.MacosRuntime.Minimal` for cross-platform geospatial. `NetTopologySuite`/`ProjNET` for vector/projections.
- **Native deps** on Linux/macOS: SDL2, OpenCV, Vulkan/OpenGL drivers are expected to be system-installed (see `docs/DEPENDENCIES.md`).
- RuntimeIdentifiers: `win-x64;linux-x64;linux-arm64;osx-x64;osx-arm64`.

---

## Where to look first

- **Architecture deep-dive**: `docs/CODEBASE_ANALYSIS.md` (module map, dataset hierarchy, tech stack).
- **Extending the app**: `wiki/Developer-Guide.md` (add dataset type, add analysis module, add GeoScript command, UI patterns).
- **GeoScript language**: `GEOSCRIPT_MANUAL.md`, `docs/GEOSCRIPT_IMAGE_OPERATIONS.md`, `wiki/GeoScript-Manual.md`.
- **User onboarding flow**: `START_HERE.md` → `GUIDE.md`.
- **Per-domain docs**: `docs/` (e.g. `SLOPE_STABILITY_SIMULATION.md`, `DUAL_PNM_IMPLEMENTATION.md`, `ORC_SYSTEM_GUIDE.md`)
  and `wiki/` (feature wikis).
- **API usage**: `Api/README.md` (headless automation examples).

---

## Multi-agent system

GAIA ships six specialised, directly-activatable agents. Each agent enforces **ALWAYS-ON online verification** against authoritative peer-reviewed sources and can innovate/contribute to the scientific community.

### Canonical source

**`.agents/*.md`** is the single source of truth. Run `bash scripts/sync-agents.sh` to distribute to `.claude/agents/` and `.cursor/agents/`.

| Agent | File | Domain |
| --- | --- | --- |
| `geotechnical-engineer` | `.agents/geotechnical-engineer.md` | Soil/rock mechanics, slope stability (3D DEM + 2D), triaxial simulation, Hoek-Brown/Mohr-Coulomb, bearing capacity, damage mechanics |
| `geophysical-engineer` | `.agents/geophysical-engineer.md` | Seismic processing (SEG-Y), acoustic FDTD simulation, earthquake wave propagation, thermal conductivity, rock physics |
| `geology-interpreter` | `.agents/geology-interpreter.md` | Stratigraphy (8 national + international systems), structural restoration, borehole correlation, geological consistency gates |
| `ai-vision-engineer` | `.agents/ai-vision-engineer.md` | ONNX AI segmentation (SAM2, MicroSAM, Grounding DINO), NeRF training (Instant-NGP), texture classification, CT/image AI pipelines |
| `frontend-builder` | `.agents/frontend-builder.md` | ImGui.NET + Veldrid UI, dock layouts, pop-out windows, dataset viewer/tools/properties panels, screenshot/PNG export |
| `photogrammetry-specialist` | `.agents/photogrammetry-specialist.md` | SfM pipeline (real-time + offline), SuperPoint/LightGlue/MiDaS, SIFT SIMD, dense reconstruction, mesh generation, georeferencing |

### How agents work together

Each agent is **directly activatable** and owns its domain end-to-end. Typical collaboration:

1. **Domain agents** (geophysicist, geologist, geotechnical engineer) specify *what* must be verified/built, with references and diagnostic requirements.
2. **`ai-vision-engineer`** implements the ONNX/NeRF/segmentation architecture and inference pipeline.
3. **`frontend-builder`** implements the UI, visualisation, and export from the domain agents' specifications.
4. **`photogrammetry-specialist`** owns the 3D reconstruction pipeline end-to-end.

All agents share two operating axes: **Verify → Certify → Defend** (trust) and **Innovate** (frontier). Every claim must be verified online or labeled `VERIFY`/`UNVERIFIED-FORBIDDEN`.
