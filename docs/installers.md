# Cross-platform installers

The installer ecosystem is made of two projects:

1. **InstallerPackager** (`net8.0` console) publishes self-contained builds, creates per-product archives (ImGui, GTK, Node Server, Node Endpoint) and generates the installer executable for each runtime. It updates `docs/installer-manifest.json` with package URLs, sizes, and SHA256 hashes.
2. **InstallerWizard** (ImGui + Terminal.Gui) downloads or reads the archives, lets users pick a package, installation folder, optional ONNX installer bundle, and desktop shortcuts. It supports UAC elevation on Windows and automatic updates by reading the manifest.

## Prerequisites

- .NET 8 SDK to run the packager and build self-contained binaries.
- A place to publish `docs/installer-manifest.json` and the generated archives.

The installer itself is self-contained, so target machines do **not** need the .NET SDK.

## Manifest configuration

1. Update `docs/installer-manifest.json` with the version number, prerequisite descriptions, and package components shown in the installer. Each component must specify:
   - `relativePath`: folder inside the archive
   - `targetSubdirectory`: install destination under the chosen install root
2. Update `InstallerWizard/installer-settings.json` to set the product name, default install path, and manifest URL for interactive runs.
3. Update `InstallerPackager/packager-settings.json` to set the output directory, package base URL, and project paths for each product.

## Creating packages

```bash
# From /workspace/GeoscientistToolkit
cd InstallerPackager
# Optional: verify/update packager-settings.json
cat packager-settings.json
# Build all packages and update docs/installer-manifest.json
# Also publishes the installer executable per runtime
# (output folder only contains the installer executable + archives)
dotnet run --project InstallerPackager.csproj
```

For each runtime listed in the manifest, the packager:

1. Runs `dotnet publish` for the selected package project to produce a self-contained bundle.
2. Adds the ONNX installer placeholder (scripts + optional `models/` content) to the payload.
3. Compresses the payload into `artifacts/installers/<PackageName>-<packageId>-<rid>.zip`.
4. Calculates SHA256 + size, and updates `packageUrl`, `sha256`, and `sizeBytes` in the manifest.
5. Publishes the self-contained installer executable (one per runtime) into the same output directory.

Publish the generated `.zip` files and the updated manifest where your installer can reach them.

## Using the InstallerWizard

- The installer defaults to the ImGui UI and automatically falls back to the terminal UI when a graphical environment is unavailable.
- You can override the UI on the command line:
  - `--ui imgui`
  - `--ui terminal`
  - `--imgui`
  - `--terminal`

During installation the user can:

- Choose a package (ImGui, GTK, Node Server, or Node Endpoint)
- Pick a custom install folder
- Optionally include the ONNX installer bundle
- Create desktop shortcuts
- Elevate privileges on Windows when required

The installer downloads, verifies, extracts, and installs the selected components, then writes `install-info.json` in the install folder.

## Automatic updates

- `install-info.json` stores the installed version, runtime, package id, and selected components.
- The installer checks the manifest on startup and offers updates when a newer version is available.
- Re-run `InstallerPackager` and publish the updated manifest + archives to distribute updates.

## Notes

- Place ONNX models under `ONNX/` (or in the ONNX installer `models/` folder) before running the packager if you want them bundled.
- Keep multiple `packager-settings.json` variants for staging/production as needed.
- Add platform-specific prerequisites in the manifest (GPU drivers, extra runtimes). They appear on the welcome screen.
