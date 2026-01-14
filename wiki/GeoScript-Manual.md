# GeoScript Manual

**Version 1.0** - Official Documentation for Geoscientist's Toolkit

---

## Table of Contents

1. [Introduction](#introduction)
2. [Language Overview](#language-overview)
3. [Syntax and Grammar](#syntax-and-grammar)
4. [Data Types and Datasets](#data-types-and-datasets)
5. [Command Reference](#command-reference)
6. [Advanced Features](#advanced-features)
7. [Practical Examples](#practical-examples)
8. [Best Practices](#best-practices)
9. [Troubleshooting](#troubleshooting)

---

## Introduction

### What is GeoScript?

GeoScript is a domain-specific scripting language (DSL) designed specifically for geoscientific data analysis and manipulation within the Geoscientist's Toolkit application. It provides a powerful, intuitive syntax for automating complex workflows involving CT scans, seismic data, well logs, GIS datasets, and more.

> **Note:** GeoScript in this project is an internal language for Geoscientist's Toolkit and is not affiliated with https://geoscript.net or any of its components.

### Key Features

- **Pipeline-based syntax** using the `|>` operator for elegant operation chaining
- **Type-aware operations** that automatically adapt to different dataset types
- **Non-destructive processing** preserving original data integrity
- **Integrated scientific computing** combining image processing, thermodynamics, and GIS analysis
- **Interactive REPL** (Read-Eval-Print-Loop) for rapid prototyping
- **Script file support** for reproducible workflows

### Getting Started

Access GeoScript through two interfaces:

**GeoScript Editor** (for script files):
- Navigate to `File → GeoScript Editor...`
- Select a dataset from the dropdown
- Write your script and click `Run Script`

**GeoScript Terminal** (for interactive use):
- Navigate to `Tools → GeoScript Terminal...`
- Type commands interactively
- Use Arrow keys for command history
- Use Tab for auto-completion

---

## Language Overview

### Basic Concepts

#### Datasets

All GeoScript operations work on datasets. A dataset is any data structure loaded into the Geoscientist's Toolkit, including:
- Images (2D raster data)
- CT image stacks (3D volumetric data)
- Tables (tabular data from CSV, Excel, etc.)
- GIS layers (vector and raster geographic data)
- Seismic data (SEG-Y files)
- Borehole data (well logs)
- Pore network models
- 3D meshes

#### Operations

Operations are functions that transform datasets. Each operation:
- Takes zero or more parameters
- Receives an input dataset
- Produces an output dataset
- Preserves the original input (non-destructive)

#### Pipeline Execution

The pipeline operator `|>` chains operations together:

```geoscript
INPUT |> OPERATION1 |> OPERATION2 |> OPERATION3
```

This is equivalent to: `OPERATION3(OPERATION2(OPERATION1(INPUT)))`

---

## Syntax and Grammar

### Lexical Elements

#### Comments

```geoscript
# This is a single-line comment
// This is also a comment
```

#### Keywords

Reserved words in GeoScript:
- `WITH`, `DO`, `TO`, `THEN` - Statement keywords (classic syntax)
- `LISTOPS` - List available operations
- `DISPTYPE` - Display dataset type information
- `UNLOAD` - Remove dataset from memory
- `USE` - Switch active dataset

#### Operators

- `|>` - Pipeline operator (operation chaining)
- `,` - Parameter separator
- `=` - Parameter assignment or equality test

### Syntax Forms

#### Pipeline Syntax (Recommended)

```geoscript
# Single operation
OPERATION param1=value1 param2=value2

# Chained operations
OPERATION1 param=value |> OPERATION2 param=value |> OPERATION3
```

**Examples:**
```geoscript
# Image processing
GRAYSCALE |> THRESHOLD min=100 max=200 |> INVERT

# Multi-step filtering
FILTER type=gaussian size=5 |> BRIGHTNESS_CONTRAST brightness=10 contrast=1.2

# Table analysis
SELECT WHERE 'Temperature' > 25 |> SORTBY 'Value' DESC |> TAKE 10
```

#### Classic WITH-DO-TO Syntax

```geoscript
WITH "dataset_name" DO OPERATION params TO "output_name"
```

**Multiple operations:**
```geoscript
WITH "dataset_name" DO
  OPERATION1 params TO "output1"
  THEN OPERATION2 params TO "output2"
```

### Parameter Syntax

#### Named Parameters (Recommended)

```geoscript
FILTER type=gaussian size=5 sigma=1.5
BRIGHTNESS_CONTRAST brightness=10 contrast=1.2
THRESHOLD min=100 max=200
```

#### String Handling

Use single quotes for field/column names:
```geoscript
CALCULATE 'NewField' = 'OldField' * 2
SELECT WHERE 'Value' > 100
```

Reference other loaded datasets using `@'DatasetName'`, or read dataset properties with dot notation. When you enter a property reference by itself, GeoScript prints the resolved value. These references are resolved from the current project context (all datasets loaded in the GeoScript editor or terminal):
```geoscript
USE @'WellLogs'
JOIN @'OtherDataset' ON 'LeftKey' = 'RightKey'
SELECT INTERSECTS @'PolygonLayer'

# Property access (current dataset)
permeability

# Property access (named dataset)
PNM.permeability

# Deep property access
CT.Materials.Basalt.VoxelCount
```

---

## Data Types and Datasets

### Supported Dataset Types

| Dataset Type | Description | Common Operations |
|--------------|-------------|-------------------|
| **SingleImage** | Single 2D raster images | BRIGHTNESS_CONTRAST, FILTER, THRESHOLD |
| **CtImageStack** | 3D CT scan volumes | CT_SEGMENT, CT_FILTER3D, SIMULATE_* |
| **CtBinaryFile** | CT binary volume files | LOAD, DISPTYPE |
| **MicroXrf** | Micro-XRF maps | FILTER, INFO |
| **PointCloud** | Point cloud datasets | INFO, SAVE |
| **Mesh** | Legacy mesh datasets | INFO, SAVE |
| **Group** | Dataset collections | INFO, UNLOAD |
| **Mesh3D** | 3D surface meshes | MESH_SMOOTH, MESH_DECIMATE |
| **Table** | Tabular data | SELECT, GROUPBY, CALCULATE |
| **GIS** | Geographic data | BUFFER, DISSOLVE, SELECT |
| **AcousticVolume** | Acoustic volume datasets | ACOUSTIC_THRESHOLD, ACOUSTIC_EXTRACT_TARGETS |
| **PNM** | Pore network models | PNM_CALCULATE_PERMEABILITY, RUN_PNM_REACTIVE_TRANSPORT |
| **DualPNM** | Dual pore network models | PNM_* (where applicable) |
| **Borehole** | Well log data | BH_ADD_LOG, BH_CALCULATE_POROSITY |
| **TwoDGeology** | 2D geology datasets | SAVE, INFO |
| **SubsurfaceGIS** | Subsurface GIS layers | GIS_ADD_LAYER, GIS_REPROJECT |
| **Earthquake** | Earthquake datasets | INFO, SAVE |
| **Seismic** | Seismic survey data | SEIS_FILTER, SEIS_STACK |
| **SeismicCube** | 3D seismic cubes | CUBE_CREATE, CUBE_BUILD_VOLUME |
| **Video** | Video datasets | VIDEO_EXTRACT_FRAME, VIDEO_STABILIZE |
| **Audio** | Audio datasets | AUDIO_TRIM, AUDIO_NORMALIZE |
| **Text** | Text datasets | TEXT_SEARCH, TEXT_STATISTICS |
| **Nerf** | NeRF datasets | INFO, SAVE |
| **SlopeStability** | Slope stability models | SLOPE_GENERATE_BLOCKS, SLOPE_SIMULATE |

### Type Checking

GeoScript automatically validates that operations are compatible with dataset types:

```geoscript
# This works - GRAYSCALE supports images
GRAYSCALE

# This fails if dataset is not an image
CT_SEGMENT   # Error: Operation not supported for this dataset type
```

---

## Command Reference

### Table Operations

| Command | Description | Example |
|---------|-------------|---------|
| `SELECT` | Filter rows | `SELECT WHERE 'Temp' > 25` |
| `CALCULATE` | Create new column | `CALCULATE 'TempF' = 'TempC' * 1.8 + 32` |
| `SORTBY` | Sort rows | `SORTBY 'Value' DESC` |
| `GROUPBY` | Group and aggregate | `GROUPBY 'Type' AGGREGATE COUNT('ID')` |
| `RENAME` | Rename column | `RENAME 'Old' TO 'New'` |
| `DROP` | Remove columns | `DROP 'UnusedField'` |
| `TAKE` | Select first N rows | `TAKE 10` |
| `UNIQUE` | Extract unique values | `UNIQUE 'Category'` |
| `JOIN` | Merge datasets | `JOIN @'Other' ON 'Key' = 'ID'` |

### Image Processing Operations

| Command | Description | Example |
|---------|-------------|---------|
| `BRIGHTNESS_CONTRAST` | Adjust brightness/contrast | `BRIGHTNESS_CONTRAST brightness=10 contrast=1.2` |
| `FILTER` | Apply filters | `FILTER type=gaussian size=5` |
| `THRESHOLD` | Threshold segmentation | `THRESHOLD min=100 max=200` |
| `BINARIZE` | Convert to binary | `BINARIZE threshold=auto` |
| `GRAYSCALE` | Convert to grayscale | `GRAYSCALE` |
| `INVERT` | Invert colors | `INVERT` |
| `NORMALIZE` | Normalize to full range | `NORMALIZE` |

**Filter types:** `gaussian`, `median`, `mean`, `box`, `sobel`

### GIS Operations

| Command | Description | Example |
|---------|-------------|---------|
| `BUFFER` | Create buffer zones | `BUFFER 100` |
| `DISSOLVE` | Merge features | `DISSOLVE 'LandUse'` |
| `EXPLODE` | Multi-part to single | `EXPLODE` |
| `CLEAN` | Fix invalid geometries | `CLEAN` |
| `RECLASSIFY` | Reclassify raster | `RECLASSIFY INTO 'Classes' RANGES(0-50: 1, 50-100: 2)` |
| `RASTER_CALCULATE` | Raster math expression | `RASTER_CALCULATE EXPR '(A + B) / 2' AS 'MeanElevation'` |
| `SLOPE` | Calculate slope | `SLOPE AS 'SlopeMap'` |
| `ASPECT` | Calculate aspect | `ASPECT AS 'AspectMap'` |
| `CONTOUR` | Generate contours | `CONTOUR INTERVAL 10 AS 'Contours'` |

### GIS Extended Operations

| Command | Description | Example |
|---------|-------------|---------|
| `GIS_ADD_LAYER` | Add a layer to a GIS dataset | `GIS_ADD_LAYER path=\"layer.shp\" name=\"NewLayer\"` |
| `GIS_REMOVE_LAYER` | Remove a layer by name | `GIS_REMOVE_LAYER name=\"OldLayer\"` |
| `GIS_INTERSECT` | Intersect two layers | `GIS_INTERSECT layer=\"Faults\" with=\"Wells\"` |
| `GIS_UNION` | Union two layers | `GIS_UNION layer=\"A\" with=\"B\"` |
| `GIS_CLIP` | Clip to polygon layer | `GIS_CLIP layer=\"Contours\" clip=\"Mask\"` |
| `GIS_CALCULATE_AREA` | Compute polygon areas | `GIS_CALCULATE_AREA` |
| `GIS_CALCULATE_LENGTH` | Compute feature lengths | `GIS_CALCULATE_LENGTH` |
| `GIS_REPROJECT` | Reproject to EPSG | `GIS_REPROJECT epsg=4326` |

### Thermodynamics Operations

| Command | Description | Example |
|---------|-------------|---------|
| `CREATE_DIAGRAM` | Generate phase diagrams | `CREATE_DIAGRAM BINARY FROM 'NaCl' AND 'H2O' TEMP 298 K PRES 1 BAR` |
| `THERMO_SWEEP` | Sweep equilibrium across T/P | `THERMO_SWEEP composition="'H2O'=55.5,'CO2'=1.0" minT=273 maxT=473 minP=1 maxP=1000 grid=25 name=ThermoSweep` |
| `EQUILIBRATE` | Calculate equilibrium | `EQUILIBRATE` |
| `SATURATION` | Calculate saturation indices | `SATURATION MINERALS 'Calcite', 'Gypsum'` |
| `BALANCE_REACTION` | Balance reactions | `BALANCE_REACTION 'Calcite'` |
| `EVAPORATE` | Simulate evaporation | `EVAPORATE UPTO 10x STEPS 50 MINERALS 'Halite', 'Gypsum'` |
| `REACT` | Calculate reaction products | `REACT CaCO3 + HCl TEMP 25 C` |
| `SPECIATE` | Compute speciation for the current aqueous system | `SPECIATE` |
| `DIAGNOSE_SPECIATE` | Speciation diagnostics output | `DIAGNOSE_SPECIATE` |
| `DIAGNOSTIC_THERMODYNAMIC` | Run thermodynamic diagnostics | `DIAGNOSTIC_THERMODYNAMIC` |
| `CALCULATE_PHASES` | Phase-separated outputs | `CALCULATE_PHASES` |
| `CALCULATE_CARBONATE_ALKALINITY` | Carbonate alkalinity / pH balance | `CALCULATE_CARBONATE_ALKALINITY` |

### Petrology Operations

| Command | Description | Example |
|---------|-------------|---------|
| `FRACTIONATE_MAGMA` | Fractional crystallization modeling | `FRACTIONATE_MAGMA` |
| `LIQUIDUS_SOLIDUS` | Liquidus/solidus curves | `LIQUIDUS_SOLIDUS` |
| `METAMORPHIC_PT` | Metamorphic P-T paths | `METAMORPHIC_PT` |

### PhysicoChem Reactor Operations

| Command | Description | Example |
|---------|-------------|---------|
| `CREATE_REACTOR` | Create a PhysicoChem reactor | `CREATE_REACTOR` |
| `RUN_SIMULATION` | Run PhysicoChem simulation | `RUN_SIMULATION` |
| `PHYSICOCHEM_ADD_FORCE` | Add force fields (gravity/vortex) | `PHYSICOCHEM_ADD_FORCE name=MoonGravity type=gravity gravity=0,0,-1.62` |
| `PHYSICOCHEM_SWEEP` | Configure PhysicoChem parameter sweep | `PHYSICOCHEM_SWEEP name=Temp target=Domains[0].InitialConditions.Temperature min=273 max=373 mode=Temporal` |
| `ADD_CELL` | Add cell to reactor grid | `ADD_CELL x=10 y=12 z=4 material='Sandstone'` |
| `SET_CELL_MATERIAL` | Set reactor cell material | `SET_CELL_MATERIAL id=12 material='Clay'` |

### 2D Geomechanics Operations

| Command | Description | Example |
|---------|-------------|---------|
| `GEOMECH_RUN` | Run 2D geomechanical simulation | `GEOMECH_RUN analysis=quasistatic steps=10 solver=PCG` |
| `GEOMECH_SWEEP` | Configure geomechanics parameter sweep | `GEOMECH_SWEEP name=LoadFactor target=LoadFactor min=0.5 max=1.5 mode=Step` |

### CT Image Stack Operations

| Command | Description | Example |
|---------|-------------|---------|
| `CT_SEGMENT` | 3D segmentation | `CT_SEGMENT method=threshold min=100 max=200 material=1` |
| `CT_FILTER3D` | 3D filters | `CT_FILTER3D type=gaussian size=5` |
| `CT_ADD_MATERIAL` | Define material | `CT_ADD_MATERIAL name='Pore' color=0,0,255` |
| `CT_REMOVE_MATERIAL` | Remove material | `CT_REMOVE_MATERIAL id=1` |
| `CT_ANALYZE_POROSITY` | Calculate porosity | `CT_ANALYZE_POROSITY void_material=1` |
| `CT_CROP` | Crop sub-volume | `CT_CROP x=0 y=0 z=0 width=100 height=100 depth=100` |
| `CT_EXTRACT_SLICE` | Extract 2D slice | `CT_EXTRACT_SLICE index=50 axis=z` |
| `CT_LABEL_ANALYSIS` | Label analysis summary | `CT_LABEL_ANALYSIS` |
| `SIMULATE_ACOUSTIC` | Acoustic wave simulation | `SIMULATE_ACOUSTIC materials=1,2 tx=0.1,0.5,0.5 rx=0.9,0.5,0.5` |
| `SIMULATE_NMR` | NMR random walk simulation | `SIMULATE_NMR pore_material_id=1 steps=1000` |
| `SIMULATE_THERMAL_CONDUCTIVITY` | Thermal conductivity solver | `SIMULATE_THERMAL_CONDUCTIVITY direction=z temperature_hot=373.15` |
| `SIMULATE_GEOMECH` | Geomechanical simulation | `SIMULATE_GEOMECH sigma1=100 sigma2=50 sigma3=20 apply_gravity=true gravity=0,0,-9.81` |

**Parameter values can reference dataset fields**, for example:

```geoscript
SIMULATE_GEOMECH porosity=examplepnmdataset.porosity sigma1=100 sigma2=50 sigma3=20
```

Gravity body forces can be enabled with `apply_gravity` and a full 3D vector:

```geoscript
SIMULATE_GEOMECH apply_gravity=true gravity=0,0,-9.81
```

### Borehole Operations

| Command | Description | Example |
|---------|-------------|---------|
| `BH_ADD_LITHOLOGY` | Add lithology intervals | `BH_ADD_LITHOLOGY depth=1200 lith='Sandstone'` |
| `BH_REMOVE_LITHOLOGY` | Remove lithology interval | `BH_REMOVE_LITHOLOGY depth=1200` |
| `BH_ADD_LOG` | Add log curve | `BH_ADD_LOG name='GR' unit='API'` |
| `BH_CALCULATE_POROSITY` | Compute porosity | `BH_CALCULATE_POROSITY method=neutron` |
| `BH_CALCULATE_SATURATION` | Compute saturation | `BH_CALCULATE_SATURATION method=archie` |
| `BH_DEPTH_SHIFT` | Shift depth reference | `BH_DEPTH_SHIFT shift=1.5` |
| `BH_CORRELATION` | Correlate boreholes | `BH_CORRELATION method=dtw` |

### PNM Operations

| Command | Description | Example |
|---------|-------------|---------|
| `PNM_FILTER_PORES` | Filter by size | `PNM_FILTER_PORES min_radius=1.0` |
| `PNM_FILTER_THROATS` | Filter throats | `PNM_FILTER_THROATS max_length=50.0` |
| `PNM_CALCULATE_PERMEABILITY` | Calculate permeability | `PNM_CALCULATE_PERMEABILITY direction=all` |
| `PNM_DRAINAGE_SIMULATION` | Drainage simulation | `PNM_DRAINAGE_SIMULATION contact_angle=30` |
| `PNM_IMBIBITION_SIMULATION` | Imbibition simulation | `PNM_IMBIBITION_SIMULATION contact_angle=60` |
| `PNM_EXTRACT_LARGEST_CLUSTER` | Extract main cluster | `PNM_EXTRACT_LARGEST_CLUSTER` |
| `PNM_STATISTICS` | Network statistics | `PNM_STATISTICS` |
| `SET_PNM_SPECIES` | Set reactive species concentrations | `SET_PNM_SPECIES Ca2+ 0.01 0.005` |
| `SET_PNM_MINERALS` | Set initial mineral content | `SET_PNM_MINERALS Calcite 0.02` |
| `RUN_PNM_REACTIVE_TRANSPORT` | Reactive transport simulation | `RUN_PNM_REACTIVE_TRANSPORT 1000 0.01 298 1.5e7 1.0e7` |
| `EXPORT_PNM_RESULTS` | Export reactive transport results | `EXPORT_PNM_RESULTS \"results.csv\"` |

### Seismic Operations

| Command | Description | Example |
|---------|-------------|---------|
| `SEIS_FILTER` | Frequency filtering | `SEIS_FILTER type=bandpass low=10 high=80` |
| `SEIS_AGC` | Automatic gain control | `SEIS_AGC window=500` |
| `SEIS_VELOCITY_ANALYSIS` | Velocity analysis | `SEIS_VELOCITY_ANALYSIS method=semblance` |
| `SEIS_NMO_CORRECTION` | NMO correction | `SEIS_NMO_CORRECTION velocity=2000` |
| `SEIS_STACK` | Stack traces | `SEIS_STACK method=mean` |
| `SEIS_MIGRATION` | Seismic migration | `SEIS_MIGRATION method=kirchhoff aperture=1000` |
| `SEIS_PICK_HORIZON` | Pick seismic horizons | `SEIS_PICK_HORIZON method=auto` |

### Seismic Cube Operations

| Command | Description | Example |
|---------|-------------|---------|
| `CUBE_CREATE` | Create a seismic cube | `CUBE_CREATE name="Survey_Cube" survey="Field_2024"` |
| `CUBE_ADD_LINE` | Add a seismic line | `CUBE_ADD_LINE cube="Survey_Cube" line="Line_001" start_x=0 start_y=0 end_x=5000 end_y=0` |
| `CUBE_ADD_PERPENDICULAR` | Add a perpendicular line | `CUBE_ADD_PERPENDICULAR cube="Survey_Cube" base="Line_001" trace=100 line="Crossline_001"` |
| `CUBE_DETECT_INTERSECTIONS` | Detect intersections | `CUBE_DETECT_INTERSECTIONS cube="Survey_Cube"` |
| `CUBE_SET_NORMALIZATION` | Configure normalization | `CUBE_SET_NORMALIZATION cube="Survey_Cube" amplitude_method=balanced` |
| `CUBE_NORMALIZE` | Apply normalization | `CUBE_NORMALIZE cube="Survey_Cube"` |
| `CUBE_BUILD_VOLUME` | Build regularized volume | `CUBE_BUILD_VOLUME cube="Survey_Cube" inline_count=200 crossline_count=200 sample_count=1500` |
| `CUBE_EXPORT_GIS` | Export to Subsurface GIS | `CUBE_EXPORT_GIS cube="Survey_Cube" output="Subsurface_Model"` |
| `CUBE_EXPORT_SLICE` | Export time slice | `CUBE_EXPORT_SLICE cube="Survey_Cube" time=1500 output="Slice_1500ms"` |
| `CUBE_STATISTICS` | Generate summary table | `CUBE_STATISTICS cube="Survey_Cube" output="Cube_Stats"` |

### Acoustic Volume Operations

| Command | Description | Example |
|---------|-------------|---------|
| `ACOUSTIC_THRESHOLD` | Threshold acoustic volume | `ACOUSTIC_THRESHOLD min=0.1 max=0.8` |
| `ACOUSTIC_EXTRACT_TARGETS` | Extract acoustic targets | `ACOUSTIC_EXTRACT_TARGETS threshold=0.7` |

### Mesh Operations

| Command | Description | Example |
|---------|-------------|---------|
| `MESH_SMOOTH` | Smooth mesh surfaces | `MESH_SMOOTH iterations=10` |
| `MESH_DECIMATE` | Decimate mesh | `MESH_DECIMATE target=0.5` |
| `MESH_REPAIR` | Repair mesh topology | `MESH_REPAIR` |
| `MESH_CALCULATE_VOLUME` | Compute mesh volume | `MESH_CALCULATE_VOLUME` |

### Video Operations

| Command | Description | Example |
|---------|-------------|---------|
| `VIDEO_EXTRACT_FRAME` | Extract frame | `VIDEO_EXTRACT_FRAME index=120` |
| `VIDEO_STABILIZE` | Stabilize video | `VIDEO_STABILIZE smoothing=0.8` |

### Audio Operations

| Command | Description | Example |
|---------|-------------|---------|
| `AUDIO_TRIM` | Trim audio | `AUDIO_TRIM start=2.5 end=10.0` |
| `AUDIO_NORMALIZE` | Normalize audio | `AUDIO_NORMALIZE target=-3` |

### Text Operations

| Command | Description | Example |
|---------|-------------|---------|
| `TEXT_SEARCH` | Search text datasets | `TEXT_SEARCH pattern=\"permeability\"` |
| `TEXT_REPLACE` | Replace text | `TEXT_REPLACE pattern=\"calcite\" with=\"dolomite\"` |
| `TEXT_STATISTICS` | Text statistics | `TEXT_STATISTICS` |

### Slope Stability Operations

| Command | Description | Example |
|---------|-------------|---------|
| `SLOPE_GENERATE_BLOCKS` | Generate blocks | `SLOPE_GENERATE_BLOCKS` |
| `SLOPE_ADD_JOINT_SET` | Add joint set | `SLOPE_ADD_JOINT_SET dip=45 dip_dir=180 spacing=2.0 friction=30 cohesion=0.5` |
| `SLOPE_SET_MATERIAL` | Set material properties | `SLOPE_SET_MATERIAL preset=granite` |
| `SLOPE_SET_ANGLE` | Set slope angle | `SLOPE_SET_ANGLE degrees=35` |
| `SLOPE_ADD_EARTHQUAKE` | Add earthquake load | `SLOPE_ADD_EARTHQUAKE magnitude=6.5 depth=10` |
| `SLOPE_SET_WATER` | Set groundwater parameters | `SLOPE_SET_WATER level=12` |
| `SLOPE_FILTER_BLOCKS` | Filter blocks | `SLOPE_FILTER_BLOCKS min_volume=1.0` |
| `SLOPE_TRACK_BLOCKS` | Track block motion | `SLOPE_TRACK_BLOCKS` |
| `SLOPE_CALCULATE_FOS` | Compute factor of safety | `SLOPE_CALCULATE_FOS` |
| `SLOPE_SIMULATE` | Run slope simulation | `SLOPE_SIMULATE mode=dynamic time=10` |
| `SLOPE_EXPORT` | Export slope results | `SLOPE_EXPORT path=\"results.json\"` |
| `SLOPE_SET_GRAVITY` | Set custom gravity | `SLOPE_SET_GRAVITY preset=mars` |
| `SLOPE2D_SET_GRAVITY` | Set 2D slope gravity | `SLOPE2D_SET_GRAVITY magnitude=1.62` |

### 2D Geomechanical Simulation Operations

These commands work with TwoDGeologyDataset for 2D finite element geomechanical analysis.

#### Mesh and Model Setup

| Command | Description | Example |
|---------|-------------|---------|
| `GEOMECH_CREATE_MESH` | Create 2D FEM mesh | `GEOMECH_CREATE_MESH type=rectangular width=100 height=50 nx=20 ny=10` |
| `GEOMECH_SET_MATERIAL` | Define material properties | `GEOMECH_SET_MATERIAL name=Sandstone E=25e9 nu=0.25 cohesion=5e6 friction=35` |
| `GEOMECH_ADD_PRIMITIVE` | Add geometric primitive | `GEOMECH_ADD_PRIMITIVE type=foundation x=50 y=50 width=10 height=2` |
| `GEOMECH_ADD_JOINTSET` | Add discontinuity joint set | `GEOMECH_ADD_JOINTSET dip=45 spacing=2 friction=30 cohesion=0` |

#### Material Assignment from Library

| Command | Description | Example |
|---------|-------------|---------|
| `GEOMECH_ASSIGN_MATERIAL` | Assign material from library to formation | `GEOMECH_ASSIGN_MATERIAL formation="Sandstone" material="Sandstone (quartz-rich)"` |
| `GEOMECH_AUTO_ASSIGN_MATERIALS` | Auto-assign materials by name matching | `GEOMECH_AUTO_ASSIGN_MATERIALS` |
| `GEOMECH_LIST_MATERIALS` | List available library materials | `GEOMECH_LIST_MATERIALS filter=sand` |
| `GEOMECH_VALIDATE_MATERIALS` | Validate material assignments | `GEOMECH_VALIDATE_MATERIALS` |

#### Boundary Conditions and Loads

| Command | Description | Example |
|---------|-------------|---------|
| `GEOMECH_FIX_BOUNDARY` | Apply fixed boundary conditions | `GEOMECH_FIX_BOUNDARY side=bottom` |
| `GEOMECH_APPLY_LOAD` | Apply loads (force, pressure, displacement) | `GEOMECH_APPLY_LOAD type=pressure pressure=100000` |
| `GEOMECH_SET_GRAVITY` | Set custom gravity for simulation | `GEOMECH_SET_GRAVITY preset=moon` |

#### Simulation and Results

| Command | Description | Example |
|---------|-------------|---------|
| `GEOMECH_RUN` | Run geomechanical simulation | `GEOMECH_RUN analysis=static steps=10 solver=PCG` |
| `GEOMECH_SET_DISPLAY` | Set visualization options | `GEOMECH_SET_DISPLAY field=vonmises colormap=jet deformation_scale=100` |
| `GEOMECH_EXPORT_RESULTS` | Export simulation results | `GEOMECH_EXPORT_RESULTS field=stress_xx format=csv path=results.csv` |

#### Material Properties

The `GEOMECH_SET_MATERIAL` command supports these parameters:

| Parameter | Description | Units |
|-----------|-------------|-------|
| `E` | Young's modulus | Pa |
| `nu` | Poisson's ratio | - |
| `density` | Material density | kg/m³ |
| `cohesion` | Cohesion (Mohr-Coulomb) | Pa |
| `friction` | Friction angle | degrees |
| `tensile` | Tensile strength | Pa |
| `criterion` | Failure criterion | MohrCoulomb, HoekBrown, CurvedMC |

For **Curved Mohr-Coulomb** (τ = A(σₙ + T)^B):
| Parameter | Description |
|-----------|-------------|
| `A` | A coefficient (typically 0.5-2.0) |
| `B` | B exponent (typically 0.5-0.9) |

For **Hoek-Brown**:
| Parameter | Description |
|-----------|-------------|
| `mi` | Intact rock parameter (1-35) |
| `GSI` | Geological Strength Index (10-100) |
| `D` | Disturbance factor (0-1) |

#### Primitive Types

| Type | Description |
|------|-------------|
| `rectangle` | Simple rectangle |
| `circle` | Circular region |
| `foundation` | Strip foundation |
| `tunnel` | Excavation tunnel |
| `dam` | Dam structure |
| `indenter` | Indenter for bearing capacity |
| `retainingwall` | Retaining wall structure |

#### Display Fields

| Field | Description |
|-------|-------------|
| `stress_xx`, `stress_yy`, `stress_xy` | Stress components |
| `sigma1`, `sigma2` | Principal stresses |
| `vonmises` | Von Mises equivalent stress |
| `displacement`, `displacement_x`, `displacement_y` | Displacements |
| `strain` | Strain magnitude |
| `yield` | Yield index (0-1) |

#### Custom Gravity Settings

The `GEOMECH_SET_GRAVITY` and `SLOPE_SET_GRAVITY` commands support custom gravitational acceleration:

**Planetary Presets:**
| Preset | Gravity (m/s²) |
|--------|---------------|
| `earth` | 9.81 |
| `moon` | 1.62 |
| `mars` | 3.72 |
| `venus` | 8.87 |
| `jupiter` | 24.79 |
| `saturn` | 10.44 |
| `mercury` | 3.70 |

**Usage Examples:**
```geoscript
# Use preset
GEOMECH_SET_GRAVITY preset=moon

# Set magnitude (direction is downward)
GEOMECH_SET_GRAVITY magnitude=1.62

# Set custom vector
GEOMECH_SET_GRAVITY x=0 y=-3.72

# For slope stability
SLOPE_SET_GRAVITY preset=mars
SLOPE2D_SET_GRAVITY magnitude=1.62
SLOPE_SET_GRAVITY x=0 y=0 z=-3.72 custom=true
```

### Utility Operations

| Command | Description | Example |
|---------|-------------|---------|
| `LOAD` | Load dataset | `LOAD "file.csv" AS "MyData"` |
| `USE` | Switch active dataset | `USE @'OtherDataset'` |
| `SAVE` | Save dataset | `SAVE "output.csv" FORMAT="csv"` |
| `COPY` | Duplicate dataset | `COPY AS "Backup"` |
| `DELETE` | Remove from project | `DELETE` |
| `SET_PIXEL_SIZE` | Update pixel size metadata | `SET_PIXEL_SIZE value=5 unit="µm"` |
| `INFO` | Show summary | `INFO` |
| `LISTOPS` | List operations | `LISTOPS` |
| `DISPTYPE` | Show type info | `DISPTYPE` |
| `UNLOAD` | Free memory | `UNLOAD` |

---

## Advanced Features

### Pipeline Composition

Complex pipelines can be built by chaining multiple operations:

```geoscript
# Multi-stage image processing
FILTER type=median size=3 |>
  GRAYSCALE |>
  NORMALIZE |>
  THRESHOLD min=100 max=200 |>
  INVERT |>
  INFO
```

### Cross-Dataset Operations

Reference multiple datasets in a single operation:

```geoscript
# Spatial join
SELECT INTERSECTS @'BufferZone' |>
  JOIN @'AttributeData' ON 'ID' = 'FeatureID' |>
  CALCULATE 'Density' = 'Population' / AREA
```

### Expression Functions

Supported mathematical functions:

**Arithmetic:** `Abs()`, `Sign()`, `Sqrt()`, `Pow()`
**Trigonometry:** `Sin()`, `Cos()`, `Tan()`, `Asin()`, `Acos()`, `Atan()`
**Logarithms:** `Log()`, `Log10()`, `Exp()`
**Rounding:** `Round()`, `Floor()`, `Ceiling()`, `Truncate()`
**Min/Max:** `Min()`, `Max()`
**Conditional:** `if(condition, true_value, false_value)`

---

## Practical Examples

### Image Processing Workflow

```geoscript
# Denoise and enhance a noisy image
FILTER type=median size=3 |>
  BRIGHTNESS_CONTRAST brightness=10 contrast=1.3 |>
  NORMALIZE

# Edge detection pipeline
GRAYSCALE |>
  FILTER type=gaussian size=3 |>
  FILTER type=sobel

# Complete segmentation workflow
FILTER type=median size=3 |>
  GRAYSCALE |>
  NORMALIZE |>
  BINARIZE threshold=auto |>
  INFO
```

### Table Data Analysis

```geoscript
# Filter, aggregate, and sort data
SELECT WHERE 'Temperature' > 25 |>
  CALCULATE 'TempF' = 'Temperature' * 1.8 + 32 |>
  GROUPBY 'Location' AGGREGATE AVG('TempF') AS 'AvgTempF', COUNT('ID') AS 'Count' |>
  SORTBY 'AvgTempF' DESC |>
  TAKE 10
```

### GIS Spatial Analysis

```geoscript
# Create 100m buffer zones and dissolve by type
BUFFER 100 |>
  DISSOLVE 'LandUseType' |>
  CALCULATE 'AreaHectares' = AREA / 10000
```

### Thermodynamic Modeling

```geoscript
# Complete water chemistry workflow
EQUILIBRATE |>
  SATURATION MINERALS 'Calcite', 'Dolomite', 'Gypsum', 'Halite'

# Simulate mineral dissolution
REACT CaCO3 + HCl + H2O TEMP 25 C PRES 1 BAR
```

### Seismic Data Processing

```geoscript
# Complete seismic processing pipeline
SEIS_FILTER type=bandpass low=10 high=80 |>
  SEIS_AGC window=500 |>
  SEIS_NMO_CORRECTION velocity=2000 |>
  SEIS_STACK method=mean |>
  SEIS_MIGRATION method=kirchhoff aperture=1000
```

### Seismic Cube Workflow

```geoscript
# Build a seismic cube from intersecting lines
CUBE_CREATE name="Survey_Cube" survey="Field_2024"
CUBE_ADD_LINE cube="Survey_Cube" line="Line_001" start_x=0 start_y=0 end_x=5000 end_y=0
CUBE_ADD_LINE cube="Survey_Cube" line="Line_002" start_x=2500 start_y=-2500 end_x=2500 end_y=2500
CUBE_ADD_PERPENDICULAR cube="Survey_Cube" base="Line_001" trace=100 line="Crossline_001"
CUBE_DETECT_INTERSECTIONS cube="Survey_Cube"
CUBE_SET_NORMALIZATION cube="Survey_Cube" amplitude_method=balanced match_phase=true
CUBE_NORMALIZE cube="Survey_Cube"
CUBE_BUILD_VOLUME cube="Survey_Cube" inline_count=200 crossline_count=200 sample_count=1500
```

### Pore Network Analysis

```geoscript
# Clean and analyze pore network
PNM_EXTRACT_LARGEST_CLUSTER |>
  PNM_FILTER_PORES min_radius=1.0 min_coord=2 |>
  PNM_STATISTICS
```

---

## Best Practices

1. **Always check dataset type first**
   ```geoscript
   DISPTYPE  # Check what operations are available
   ```

2. **Use LISTOPS to discover available operations**
   ```geoscript
   LISTOPS  # See all commands for this dataset type
   ```

3. **Verify results with INFO**
   ```geoscript
   OPERATION params |> INFO
   ```

4. **Prefer pipeline syntax for readability**
   ```geoscript
   # Good: Clear pipeline
   FILTER type=median |> GRAYSCALE |> THRESHOLD min=100
   ```

5. **Always preprocess before segmentation**
   ```geoscript
   FILTER type=median size=3 |> BINARIZE threshold=auto
   ```

6. **Filter before aggregation**
   ```geoscript
   SELECT WHERE 'Valid' = 1 |> GROUPBY 'Category' AGGREGATE COUNT('ID')
   ```

---

## Troubleshooting

### Common Errors

**Type Mismatch:**
```
Error: "Operation not supported for this dataset type"
Solution: Check dataset type with DISPTYPE and use LISTOPS
```

**Invalid Parameters:**
```
Error: "Parameter out of range"
Solution: Check parameter ranges (brightness: -100 to 100, contrast: 0.1 to 3.0)
```

**Missing Dataset Reference:**
```
Error: "Referenced dataset not found"
Solution: Ensure dataset exists before using @'DatasetName'
```

### Debugging Tips

1. Test operations incrementally
2. Use INFO to inspect intermediate results
3. Check the output log for warnings and errors

---

## Related Pages

- [GeoScript Image Operations](GeoScript-Image-Operations.md) - Detailed image processing reference
- [User Guide](User-Guide.md) - Complete application documentation
- [Home](Home.md) - Wiki home page

---

**Total Commands:** 124

**Document Version:** 1.0
**Last Updated:** January 2026
