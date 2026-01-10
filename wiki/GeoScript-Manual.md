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

Reference other datasets using `@`:
```geoscript
JOIN @'OtherDataset' ON 'LeftKey' = 'RightKey'
SELECT INTERSECTS @'PolygonLayer'
```

---

## Data Types and Datasets

### Supported Dataset Types

| Dataset Type | Description | Common Operations |
|--------------|-------------|-------------------|
| **ImageDataset** | Single 2D raster images | FILTER, THRESHOLD, GRAYSCALE |
| **CtImageStack** | 3D CT scan volumes | CT_SEGMENT, CT_FILTER3D |
| **TableDataset** | Tabular data | SELECT, GROUPBY, CALCULATE |
| **GISDataset** | Geographic data | BUFFER, DISSOLVE, SELECT |
| **SeismicDataset** | Seismic survey data | SEIS_FILTER, SEIS_STACK |
| **BoreholeDataset** | Well log data | BH_ADD_LOG, BH_CALCULATE_POROSITY |
| **PNMDataset** | Pore network models | PNM_CALCULATE_PERMEABILITY |
| **Mesh3D** | 3D surface meshes | MESH_SMOOTH, MESH_DECIMATE |

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
| `SLOPE` | Calculate slope | `SLOPE AS 'SlopeMap'` |
| `ASPECT` | Calculate aspect | `ASPECT AS 'AspectMap'` |
| `CONTOUR` | Generate contours | `CONTOUR INTERVAL 10 AS 'Contours'` |

### Thermodynamics Operations

| Command | Description | Example |
|---------|-------------|---------|
| `CREATE_DIAGRAM` | Generate phase diagrams | `CREATE_DIAGRAM BINARY FROM 'NaCl' AND 'H2O' TEMP 298 K PRES 1 BAR` |
| `EQUILIBRATE` | Calculate equilibrium | `EQUILIBRATE` |
| `SATURATION` | Calculate saturation indices | `SATURATION MINERALS 'Calcite', 'Gypsum'` |
| `BALANCE_REACTION` | Balance reactions | `BALANCE_REACTION 'Calcite'` |
| `EVAPORATE` | Simulate evaporation | `EVAPORATE UPTO 10x STEPS 50 MINERALS 'Halite', 'Gypsum'` |
| `REACT` | Calculate reaction products | `REACT CaCO3 + HCl TEMP 25 C` |

### CT Image Stack Operations

| Command | Description | Example |
|---------|-------------|---------|
| `CT_SEGMENT` | 3D segmentation | `CT_SEGMENT method=threshold` |
| `CT_FILTER3D` | 3D filters | `CT_FILTER3D type=gaussian3d size=5` |
| `CT_ADD_MATERIAL` | Define material | `CT_ADD_MATERIAL name='Pore' density=1.0` |
| `CT_ANALYZE_POROSITY` | Calculate porosity | `CT_ANALYZE_POROSITY material='Pore'` |
| `CT_CROP` | Crop sub-volume | `CT_CROP x=0 y=0 z=0 width=100 height=100 depth=100` |
| `CT_EXTRACT_SLICE` | Extract 2D slice | `CT_EXTRACT_SLICE index=50 axis=z` |

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

### Seismic Operations

| Command | Description | Example |
|---------|-------------|---------|
| `SEIS_FILTER` | Frequency filtering | `SEIS_FILTER type=bandpass low=10 high=80` |
| `SEIS_AGC` | Automatic gain control | `SEIS_AGC window=500` |
| `SEIS_VELOCITY_ANALYSIS` | Velocity analysis | `SEIS_VELOCITY_ANALYSIS method=semblance` |
| `SEIS_NMO_CORRECTION` | NMO correction | `SEIS_NMO_CORRECTION velocity=2000` |
| `SEIS_STACK` | Stack traces | `SEIS_STACK method=mean` |
| `SEIS_MIGRATION` | Seismic migration | `SEIS_MIGRATION method=kirchhoff aperture=1000` |

### Utility Operations

| Command | Description | Example |
|---------|-------------|---------|
| `LOAD` | Load dataset | `LOAD "file.csv" AS "MyData"` |
| `SAVE` | Save dataset | `SAVE "output.csv" FORMAT="csv"` |
| `COPY` | Duplicate dataset | `COPY AS "Backup"` |
| `DELETE` | Remove from project | `DELETE` |
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

- [GeoScript Image Operations](GeoScript-Image-Operations) - Detailed image processing reference
- [User Guide](User-Guide) - Complete application documentation
- [Home](Home) - Wiki home page

---

**Total Commands:** 88+

**Document Version:** 1.0
**Last Updated:** January 2026
