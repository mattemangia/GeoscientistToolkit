# Start Here: Geoscientist's Toolkit

This page provides a single linear onboarding flow and points to the most relevant documentation so you can go from installation to results without hopping across many guides.

---

## 1) Install

Choose one of the following paths:

- **Cross-platform installer (recommended)**: the TUI wizard downloads and updates the toolkit automatically.
  - Guide: [docs/installers.md](docs/installers.md)
- **Build from source**: for development or local customization.
  - Quick steps: [README.md](README.md#installation)

**Core requirements**
- .NET 8 SDK/Runtime
- GPU with OpenGL 3.3+ or Vulkan for 3D visualization
- 8–16 GB RAM (16+ recommended for large CT stacks)

---

## 2) Create your first project

1. Launch the app (`./GeoscientistToolkit` or the release executable).
2. Go to **File → New Project**.
3. Choose a folder and project `.gtp` name.

More details on projects and datasets:
- [GUIDE.md](GUIDE.md#projects)

---

## 3) Import a dataset

From **File → Import**, choose your data type:

- **CT / Volumes**: DICOM, TIFF stack, `.ctstack`
- **Seismic**: SEG-Y
- **Well logs**: LAS, `.bhb`
- **Meshes**: OBJ, STL
- **Images**: PNG, JPG, BMP, TIFF
- **Tables**: CSV, Excel

Useful guides:
- AI segmentation: [docs/AI_SEGMENTATION_GUIDE.md](docs/AI_SEGMENTATION_GUIDE.md)
- GeoScript image pipelines: [docs/GEOSCRIPT_IMAGE_OPERATIONS.md](docs/GEOSCRIPT_IMAGE_OPERATIONS.md)

---

## 4) Run an analysis (example workflows)

### CT segmentation (quick)
1. Open a CT stack.
2. Define materials in **Tools → Segmentation → Material Manager**.
3. Segment with Brush/Lasso/Wand or AI models.
4. Export a mesh in **Tools → Export → Mesh Extraction**.

More on analysis modules:
- [GUIDE.md](GUIDE.md#analysis-modules)

### Seismic + borehole (quick)
1. Load a SEG-Y file.
2. Load a borehole with Vp and Density.
3. Generate synthetic seismic in **Borehole Tools → Seismic**.
4. Tie wells in **Seismic Tools → Borehole Integration**.

Seismic simulation quickstart:
- [docs/EARTHQUAKE_SIM_QUICKSTART.md](docs/EARTHQUAKE_SIM_QUICKSTART.md)

---

## 5) Produce outputs and export

- **Meshes/3D**: export OBJ/STL
- **Images**: capture high-res screenshots
- **Tables**: export CSV/Excel
- **Projects**: save the `.gtp` to reopen everything in one click

If you are working with reactions/thermochemistry:
- [PHYSICOCHEM_GUIDE.md](PHYSICOCHEM_GUIDE.md)
- [QUICK_START_REACTIONS.md](QUICK_START_REACTIONS.md)

---

## 6) Find the right documentation fast

- **Complete user guide**: [GUIDE.md](GUIDE.md)
- **GeoScript language**: [GEOSCRIPT_MANUAL.md](GEOSCRIPT_MANUAL.md)
- **Thermodynamics & geochemistry**: [docs/THERMODYNAMICS_ENHANCEMENTS.md](docs/THERMODYNAMICS_ENHANCEMENTS.md)
- **Pore network modeling**: [docs/DUAL_PNM_IMPLEMENTATION.md](docs/DUAL_PNM_IMPLEMENTATION.md)
- **API usage**: [Api/README.md](Api/README.md)
- **Documentation index**: [docs/README.md](docs/README.md)

If you want a workflow-specific path (CT, seismic, geothermal, reactors, etc.), open an issue with the dataset and goal so we can add a dedicated “start here” path.
