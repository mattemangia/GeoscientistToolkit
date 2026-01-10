# Installation

This page covers system requirements, installation methods, and configuration for Geoscientist's Toolkit.

---

## System Requirements

### Minimum Requirements

| Component | Requirement |
|-----------|-------------|
| **Operating System** | Windows 10/11, Linux (x64), or macOS (ARM64/x86_64) |
| **RAM** | 8 GB |
| **GPU** | OpenGL 3.3+ or Vulkan support |
| **Disk Space** | 2 GB |
| **Runtime** | .NET 8.0 Runtime |

### Recommended Specifications

| Component | Recommendation |
|-----------|----------------|
| **RAM** | 16 GB+ (for large CT stacks) |
| **GPU** | Dedicated graphics with OpenCL 1.2+ support |
| **Disk Space** | 10 GB+ (for large datasets) |
| **CPU** | Multi-core processor for parallel operations |

### Optional Components

These components enhance functionality but are not required:

| Component | Purpose |
|-----------|---------|
| OpenCL Runtime | GPU-accelerated simulations |
| ONNX Runtime | AI model inference (SAM2, MicroSAM) |
| GDAL | Enhanced GIS data support |

---

## Installation Methods

### Method 1: Cross-Platform Installer (Recommended)

The `InstallerWizard` TUI provides the easiest installation experience.

#### Windows

1. Download `InstallerWizard.exe` from [Releases](https://github.com/mattemangia/GeoscientistToolkit/releases)
2. Run the installer
3. Follow the on-screen prompts
4. Launch from Start Menu or desktop shortcut

#### Linux

```bash
# Download the installer
wget https://github.com/mattemangia/GeoscientistToolkit/releases/latest/download/InstallerWizard-linux-x64

# Make executable
chmod +x InstallerWizard-linux-x64

# Run installer
./InstallerWizard-linux-x64
```

#### macOS

```bash
# Download the installer (Intel or Apple Silicon)
curl -LO https://github.com/mattemangia/GeoscientistToolkit/releases/latest/download/InstallerWizard-osx-arm64

# Make executable
chmod +x InstallerWizard-osx-arm64

# Run installer (may need to allow in Security & Privacy)
./InstallerWizard-osx-arm64
```

**Note:** On macOS, you may need to allow the application in `System Preferences → Security & Privacy`.

### Method 2: Pre-Built Releases

Download platform-specific releases from the [Releases](https://github.com/mattemangia/GeoscientistToolkit/releases) page.

1. Download the archive for your platform
2. Extract to a folder of your choice
3. Run the executable (no installation required)

### Method 3: Build from Source

For development or customization:

```bash
# Clone the repository
git clone https://github.com/mattemangia/GeoscientistToolkit.git
cd GeoscientistToolkit

# Build
dotnet build

# Run
dotnet run
```

#### Build Prerequisites

- .NET 8.0 SDK
- Git

---

## Post-Installation Setup

### Graphics Backend Configuration

On first launch, the application automatically selects the best graphics backend:

| Platform | Default Backend | Alternatives |
|----------|-----------------|--------------|
| Windows | Direct3D 11 | Vulkan, OpenGL |
| macOS | Metal | - |
| Linux | Vulkan | OpenGL |

To change the backend:
1. Go to `Tools → Settings`
2. Navigate to the Graphics section
3. Select preferred backend
4. Restart the application

### GPU Acceleration (Optional)

To enable GPU-accelerated simulations:

1. Install OpenCL drivers for your GPU:
   - **NVIDIA**: Included with NVIDIA drivers
   - **AMD**: Included with AMD drivers
   - **Intel**: Install Intel OpenCL Runtime

2. Enable in application:
   - Go to `Tools → Settings`
   - Check "Enable OpenCL"
   - Select your GPU device
   - Restart the application

### AI Models Setup (Optional)

For AI-powered segmentation (SAM2, MicroSAM):

1. Models are downloaded automatically on first use
2. Or manually download from the ONNX models page
3. Place in the `ONNX/` folder
4. Restart the application

See [CT Imaging and Segmentation](CT-Imaging-and-Segmentation) for detailed AI setup.

---

## Platform-Specific Notes

### Windows

- **Preferred graphics**: Direct3D 11
- **OpenCL**: Typically from NVIDIA/AMD drivers
- No additional dependencies required

### macOS

- **Graphics backend**: Metal (required on Apple Silicon)
- **OpenCL**: Support varies by macOS version
- Unsigned binary may require security exception:
  ```bash
  xattr -d com.apple.quarantine ./GeoscientistToolkit
  ```

### Linux

- **Graphics**: Vulkan (preferred) or OpenGL
- **Dependencies**: May require additional libraries:
  ```bash
  # Ubuntu/Debian
  sudo apt install libskia-dev libgdal-dev

  # Fedora
  sudo dnf install skia gdal
  ```
- **OpenCL**: Install appropriate drivers (Intel/NVIDIA/AMD)

---

## Diagnostic Tools

The application includes diagnostic flags for troubleshooting:

```bash
# Test AI model setup
dotnet run -- --ai-diagnostic

# Test graphics/GPU setup
dotnet run -- --gui-diagnostic

# Run automated tests
dotnet run -- --test=all
dotnet run -- --test=test1,test2
```

Diagnostics open a full-screen log window with error highlighting.

---

## Troubleshooting

### Graphics Initialization Failure

**Symptom:** Application crashes on startup

**Solutions:**
1. Delete `settings.json` to reset graphics backend
2. Run with failsafe mode:
   ```bash
   ./GeoscientistToolkit --failsafe
   ```
3. Update graphics drivers

### Out of Memory

**Symptom:** Application crashes when loading large datasets

**Solutions:**
- Close unnecessary datasets
- Reduce volume resolution
- Increase system RAM or swap space
- Use 64-bit version

### OpenCL Not Available

**Symptom:** GPU options grayed out

**Solutions:**
- Install OpenCL drivers for your GPU
- Check `Tools → System Info` for detected devices
- Update graphics drivers

### Permission Denied (Linux/macOS)

**Symptom:** Cannot run executable

**Solution:**
```bash
chmod +x ./GeoscientistToolkit
```

### macOS Security Block

**Symptom:** "App cannot be opened because the developer cannot be verified"

**Solution:**
1. Open `System Preferences → Security & Privacy`
2. Click "Open Anyway" for the application
3. Or run: `xattr -d com.apple.quarantine ./GeoscientistToolkit`

---

## CI/CD

This repository uses GitHub Actions for build validation. See `.github/workflows/ci.yml` for the .NET 8 build pipeline.

---

## Updating

### Using Installer Wizard

Run the installer wizard again - it will detect the existing installation and offer to update.

### Manual Update

1. Download the new release
2. Extract to the same location (or a new one)
3. Your project files (`.gtp`) remain compatible

---

## Uninstallation

### Windows
- Use Add/Remove Programs if installed via wizard
- Or simply delete the installation folder

### Linux/macOS
- Delete the installation folder
- Remove any desktop entries if created

---

## Next Steps

- [Getting Started](Getting-Started) - Begin using the application
- [User Guide](User-Guide) - Complete documentation
- [Home](Home) - Return to wiki home
