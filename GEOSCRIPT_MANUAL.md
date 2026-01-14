# GEOSCRIPT Language Manual

**Version 1.0**
**Official Documentation for Geoscientist's Toolkit**

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Language Overview](#2-language-overview)
3. [Syntax and Grammar](#3-syntax-and-grammar)
4. [Data Types and Datasets](#4-data-types-and-datasets)
5. [Command Reference](#5-command-reference)
6. [Advanced Features](#6-advanced-features)
7. [Practical Examples](#7-practical-examples)
8. [Best Practices](#8-best-practices)
9. [Troubleshooting](#9-troubleshooting)
10. [API Reference](#10-api-reference)

---

## 1. Introduction

### 1.1 What is GEOSCRIPT?

GEOSCRIPT is a domain-specific scripting language (DSL) designed specifically for geoscientific data analysis and manipulation within the Geoscientist's Toolkit application. It provides a powerful, intuitive syntax for automating complex workflows involving CT scans, seismic data, well logs, GIS datasets, and more.

### 1.2 Key Features

- **Pipeline-based syntax** using the `|>` operator for elegant operation chaining
- **Type-aware operations** that automatically adapt to different dataset types
- **Non-destructive processing** preserving original data integrity
- **Integrated scientific computing** combining image processing, thermodynamics, and GIS analysis
- **Interactive REPL** (Read-Eval-Print-Loop) for rapid prototyping
- **Script file support** for reproducible workflows

### 1.3 Design Philosophy

GEOSCRIPT follows a functional programming paradigm where:

- Each operation receives a dataset as input and produces a new dataset as output
- Operations can be composed into pipelines for complex transformations
- All operations are explicit, reproducible, and auditable
- The system handles type checking and compatibility automatically

### 1.4 Getting Started

Access GEOSCRIPT through two interfaces:

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

## 2. Language Overview

### 2.1 Basic Concepts

#### 2.1.1 Datasets

All GEOSCRIPT operations work on datasets. A dataset is any data structure loaded into the Geoscientist's Toolkit, including:

- Images (2D raster data)
- CT image stacks (3D volumetric data)
- Tables (tabular data from CSV, Excel, etc.)
- GIS layers (vector and raster geographic data)
- Seismic data (SEG-Y files)
- Borehole data (well logs)
- Pore network models
- 3D meshes

#### 2.1.2 Operations

Operations are functions that transform datasets. Each operation:

- Takes zero or more parameters
- Receives an input dataset
- Produces an output dataset
- Preserves the original input (non-destructive)

#### 2.1.3 Pipeline Execution

The pipeline operator `|>` chains operations together:

```geoscript
INPUT |> OPERATION1 |> OPERATION2 |> OPERATION3
```

This is equivalent to: `OPERATION3(OPERATION2(OPERATION1(INPUT)))`

### 2.2 Language Paradigm

GEOSCRIPT is:

- **Declarative**: You specify what you want, not how to do it
- **Functional**: Operations are pure functions without side effects
- **Type-safe**: Operations validate dataset types at execution time
- **Expression-oriented**: Everything is an expression that returns a value

---

## 3. Syntax and Grammar

### 3.1 Lexical Elements

#### 3.1.1 Comments

```geoscript
# This is a single-line comment
// This is also a comment
```

#### 3.1.2 Keywords

Reserved words in GEOSCRIPT:

- `WITH` - Specify input dataset (classic syntax)
- `DO` - Begin operation sequence (classic syntax)
- `TO` - Specify output dataset name
- `THEN` - Chain multiple operations (classic syntax)
- `LISTOPS` - List available operations
- `DISPTYPE` - Display dataset type information
- `UNLOAD` - Remove dataset from memory

#### 3.1.3 Operators

- `|>` - Pipeline operator (operation chaining)
- `,` - Parameter separator
- `=` - Parameter assignment or equality test

#### 3.1.4 Literals

**String Literals**:
```geoscript
"dataset_name"
'column_name'
```

**Numeric Literals**:
```geoscript
42          # Integer
3.14159     # Float
-273.15     # Negative number
1.5e-10     # Scientific notation
```

**Identifiers**:
```geoscript
FILTER      # Operation name
gaussian    # Parameter value
size        # Parameter name
```

### 3.2 Syntax Forms

#### 3.2.1 Pipeline Syntax (Recommended)

The modern, recommended syntax for GEOSCRIPT:

```geoscript
# Single operation
OPERATION param1=value1 param2=value2

# Chained operations
OPERATION1 param=value |> OPERATION2 param=value |> OPERATION3
```

**Examples**:
```geoscript
# Image processing
GRAYSCALE |> THRESHOLD min=100 max=200 |> INVERT

# Multi-step filtering
FILTER type=gaussian size=5 |> BRIGHTNESS_CONTRAST brightness=10 contrast=1.2

# Table analysis
SELECT WHERE 'Temperature' > 25 |> SORTBY 'Value' DESC |> TAKE 10
```

#### 3.2.2 Classic WITH-DO-TO Syntax

The original syntax, still supported for explicit dataset specification:

```geoscript
WITH "dataset_name" DO OPERATION params TO "output_name"
```

**Multiple operations**:
```geoscript
WITH "dataset_name" DO
  OPERATION1 params TO "output1"
  THEN OPERATION2 params TO "output2"
```

**Example**:
```geoscript
WITH "my_image" DO FILTER type=gaussian size=5 TO "filtered_image"
```

#### 3.2.3 Utility Command Syntax

Special commands for metadata and management:

```geoscript
# In classic syntax
WITH "dataset_name" LISTOPS
WITH "dataset_name" DISPTYPE
WITH "dataset_name" UNLOAD

# In pipeline syntax
LISTOPS
DISPTYPE
INFO
UNLOAD
```

#### 3.2.4 Dataset References

GeoScript can reference other loaded datasets by name using the `@'DatasetName'` syntax. These dataset references come from the current project context (all loaded datasets in the GeoScript editor/terminal).

Common usage patterns:
```geoscript
# Switch the active dataset to another loaded dataset
USE @'WellLogs'

# Join a GIS layer with a table dataset by key
SELECT WHERE 'Zone' = 'A' |>
  JOIN @'LithologyTable' ON 'WellID' = 'WellID'

# Spatial selection against another GIS layer
SELECT INTERSECTS @'FaultPolygons'
```

### 3.3 Parameter Syntax

#### 3.3.1 Named Parameters (Recommended)

```geoscript
FILTER type=gaussian size=5 sigma=1.5
BRIGHTNESS_CONTRAST brightness=10 contrast=1.2
THRESHOLD min=100 max=200
```

#### 3.3.2 Positional Parameters (Legacy)

Some commands accept positional parameters:

```geoscript
BRIGHTNESS_CONTRAST 128, 256    # brightness, contrast
THRESHOLD 100, 200              # min, max
```

#### 3.3.3 Optional Parameters

Use empty values to skip optional parameters:

```geoscript
BRIGHTNESS_CONTRAST 128,        # brightness only
BRIGHTNESS_CONTRAST ,256        # contrast only
```

### 3.4 String Handling

#### 3.4.1 Field and Column References

Use single quotes for field/column names:

```geoscript
CALCULATE 'NewField' = 'OldField' * 2
SELECT WHERE 'Value' > 100
RENAME 'OldName' TO 'NewName'
```

#### 3.4.2 Dataset and Property References

GeoScript can reference datasets and dataset properties using dot notation. When you enter a property reference by itself, GeoScript prints the resolved value.

Rules:
- `DatasetName.Property.SubProperty` resolves from any loaded dataset by name.
- `Property.SubProperty` resolves from the current context dataset.
- If the reference is just a dataset name, GeoScript prints the dataset summary.
- Lists can be indexed with numbers (e.g., `Materials.0`) and lists of named objects can be resolved by name (e.g., `Materials.Basalt`).

```geoscript
# Use another dataset directly
JOIN @'OtherDataset' ON 'LeftKey' = 'RightKey'
SELECT INTERSECTS @'PolygonLayer'

# Print a property from the current dataset
permeability

# Print a property from another dataset
PNM.permeability

# Traverse deeper properties
CT.Materials.Basalt.VoxelCount
```

### 3.5 Expression Evaluation

GEOSCRIPT uses NCalc for mathematical and logical expressions:

#### 3.5.1 Arithmetic Expressions

```geoscript
CALCULATE 'Result' = 'Field1' + 'Field2'
CALCULATE 'Ratio' = 'Numerator' / ('Denominator' + 0.001)
CALCULATE 'Power' = Pow('Base', 2)
```

#### 3.5.2 Logical Expressions

```geoscript
SELECT WHERE 'Temperature' > 25 AND 'Pressure' < 100
SELECT WHERE 'Status' = 'Active' OR 'Priority' > 5
SELECT WHERE NOT ('Flag' = 1)
```

#### 3.5.3 Geometric Properties (GIS)

For GIS datasets, special geometric properties are available:

```geoscript
CALCULATE 'AreaKm2' = AREA / 1000000
CALCULATE 'LengthMiles' = LENGTH * 0.000621371
CALCULATE 'CentroidX' = X
CALCULATE 'CentroidY' = Y
```

Available geometric properties:
- `AREA` - Feature area (for polygons)
- `LENGTH` - Feature length (for lines)
- `X` - Centroid X coordinate
- `Y` - Centroid Y coordinate

---

## 4. Data Types and Datasets

### 4.1 Supported Dataset Types

| Dataset Type | Description | File Formats | Common Operations |
|--------------|-------------|--------------|-------------------|
| **SingleImage** | Single 2D raster images | PNG, JPG, BMP, TIFF | BRIGHTNESS_CONTRAST, FILTER, THRESHOLD |
| **CtImageStack** | 3D CT scan volumes | DICOM, TIFF stack, .ctstack | CT_SEGMENT, CT_FILTER3D |
| **CtBinaryFile** | CT binary volume files | .bin, .raw | LOAD, DISPTYPE |
| **MicroXrf** | Micro-XRF maps | Custom format | FILTER, INFO |
| **PointCloud** | Point clouds | LAS, LAZ, PLY | INFO, SAVE |
| **Mesh** | Legacy mesh datasets | OBJ, STL | INFO, SAVE |
| **Group** | Dataset collections | Project group | INFO, UNLOAD |
| **Mesh3D** | 3D surface meshes | OBJ, STL | MESH_SMOOTH, MESH_DECIMATE |
| **Table** | Tabular data | CSV, Excel, TXT | SELECT, GROUPBY, CALCULATE |
| **GIS** | Geographic data | Shapefile, GeoJSON, GeoTIFF | BUFFER, DISSOLVE, GIS_* |
| **AcousticVolume** | 3D acoustic data | Custom format | ACOUSTIC_THRESHOLD |
| **PNM** | Pore network models | Custom format | PNM_CALCULATE_PERMEABILITY |
| **DualPNM** | Dual pore networks | Custom format | PNM_* (where applicable) |
| **Borehole** | Well log data | LAS, .bhb | BH_ADD_LOG, BH_CALCULATE_POROSITY |
| **TwoDGeology** | 2D geology datasets | Custom format | SAVE, INFO |
| **SubsurfaceGIS** | Subsurface GIS layers | Custom format | GIS_REPROJECT |
| **Earthquake** | Earthquake datasets | Custom format | INFO, SAVE |
| **Seismic** | Seismic survey data | SEG-Y | SEIS_FILTER, SEIS_STACK |
| **SeismicCube** | Seismic cube datasets | .seiscube | CUBE_CREATE, CUBE_BUILD_VOLUME |
| **Video** | Video files | MP4, AVI | VIDEO_EXTRACT_FRAME |
| **Audio** | Audio files | WAV, MP3 | AUDIO_NORMALIZE |
| **Text** | Text documents | TXT | TEXT_SEARCH, TEXT_REPLACE |
| **Nerf** | NeRF datasets | Custom format | INFO, SAVE |
| **SlopeStability** | Slope stability datasets | Custom format | SLOPE_GENERATE_BLOCKS, SLOPE_SIMULATE |

### 4.2 Type Checking

GEOSCRIPT automatically validates that operations are compatible with dataset types:

```geoscript
# This works - GRAYSCALE supports images
GRAYSCALE

# This fails if dataset is not an image
CT_SEGMENT   # Error: Operation not supported for this dataset type
```

### 4.3 Type Conversion

Some operations automatically convert between types:

```geoscript
# CT stack to image (extracts single slice)
CT_EXTRACT_SLICE index=50

# Image to table (histogram)
CALCULATE_HISTOGRAM
```

---

## 5. Command Reference

### 5.1 Table Operations

#### 5.1.1 SELECT

Filters rows based on attribute or spatial conditions.

**Syntax**:
```geoscript
SELECT WHERE <expression>
SELECT <spatial_operator> @'OtherLayer'
```

**Parameters**:
- `WHERE` - Attribute query expression
- Spatial operators: `INTERSECTS`, `CONTAINS`, `WITHIN`

**Examples**:
```geoscript
# Simple attribute filter
SELECT WHERE 'Temperature' > 25

# Complex boolean logic
SELECT WHERE 'Value' > 100 AND 'Type' = 'Active'

# Spatial query (GIS only)
SELECT INTERSECTS @'PolygonLayer'
SELECT CONTAINS @'PointLayer'
SELECT WITHIN @'BoundaryLayer'
```

**Returns**: Filtered dataset with matching rows/features

---

#### 5.1.2 CALCULATE

Creates a new column from an expression.

**Syntax**:
```geoscript
CALCULATE 'NewColumn' = <expression>
```

**Parameters**:
- Column name (string literal)
- Expression using existing columns

**Examples**:
```geoscript
# Simple arithmetic
CALCULATE 'DoubleValue' = 'Value' * 2

# Temperature conversion
CALCULATE 'TempF' = 'TempC' * 1.8 + 32

# Geometric calculation (GIS)
CALCULATE 'AreaKm2' = AREA / 1000000

# Complex expression
CALCULATE 'Ratio' = 'Field1' / ('Field2' + 0.001)
```

**Supported Functions**:
- Arithmetic: `+`, `-`, `*`, `/`, `%`, `^`
- Math: `Abs()`, `Sqrt()`, `Pow()`, `Log()`, `Exp()`
- Trigonometry: `Sin()`, `Cos()`, `Tan()`, `Asin()`, `Acos()`, `Atan()`
- Rounding: `Round()`, `Floor()`, `Ceiling()`
- Min/Max: `Min()`, `Max()`

**Returns**: Dataset with new calculated column

---

#### 5.1.3 SORTBY

Sorts table rows by a column.

**Syntax**:
```geoscript
SORTBY 'ColumnName' [ASC|DESC]
```

**Parameters**:
- Column name (string literal)
- Sort direction: `ASC` (ascending, default) or `DESC` (descending)

**Examples**:
```geoscript
# Sort ascending (default)
SORTBY 'Name' ASC

# Sort descending
SORTBY 'Value' DESC

# Pipeline: filter then sort
SELECT WHERE 'Active' = 1 |> SORTBY 'Score' DESC
```

**Returns**: Sorted dataset

---

#### 5.1.4 GROUPBY

Groups rows and calculates aggregate values.

**Syntax**:
```geoscript
GROUPBY 'GroupColumn' AGGREGATE <aggregations>
```

**Aggregate Functions**:
- `COUNT('Column')` - Count non-null values
- `SUM('Column')` - Sum of values
- `AVG('Column')` - Average of values
- `MIN('Column')` - Minimum value
- `MAX('Column')` - Maximum value

**Examples**:
```geoscript
# Simple count
GROUPBY 'Category' AGGREGATE COUNT('ID') AS 'Count'

# Multiple aggregations
GROUPBY 'Location' AGGREGATE
  SUM('Volume') AS 'TotalVolume',
  AVG('Temperature') AS 'AvgTemp'

# Statistical summary
GROUPBY 'Type' AGGREGATE
  COUNT('ID') AS 'N',
  AVG('Value') AS 'Mean',
  MIN('Value') AS 'Min',
  MAX('Value') AS 'Max'
```

**Returns**: Grouped dataset with aggregate columns

---

#### 5.1.5 RENAME

Renames a column.

**Syntax**:
```geoscript
RENAME 'OldName' TO 'NewName'
```

**Examples**:
```geoscript
RENAME 'Temp' TO 'Temperature'
RENAME 'Val' TO 'Value'
```

**Returns**: Dataset with renamed column

---

#### 5.1.6 DROP

Removes one or more columns.

**Syntax**:
```geoscript
DROP 'Column1', 'Column2', ...
```

**Examples**:
```geoscript
# Drop single column
DROP 'UnusedField'

# Drop multiple columns
DROP 'Field1', 'Field2', 'Field3'
```

**Returns**: Dataset without dropped columns

---

#### 5.1.7 TAKE

Selects the first N rows.

**Syntax**:
```geoscript
TAKE <count>
```

**Examples**:
```geoscript
# Get first 10 rows
TAKE 10

# Pipeline: sort then take top 5
SORTBY 'Value' DESC |> TAKE 5
```

**Returns**: Dataset with first N rows

---

#### 5.1.8 UNIQUE

Extracts unique values from a column.

**Syntax**:
```geoscript
UNIQUE 'ColumnName'
```

**Examples**:
```geoscript
UNIQUE 'Category'
UNIQUE 'Location'
```

**Returns**: New dataset with unique values

---

#### 5.1.9 JOIN

Merges attributes from another dataset based on a key.

**Syntax**:
```geoscript
JOIN @'OtherDataset' ON 'LeftKey' = 'RightKey'
```

**Examples**:
```geoscript
# Join table to GIS layer
JOIN @'AttributeTable' ON 'ID' = 'FeatureID'

# Join with different key names
JOIN @'LookupTable' ON 'Code' = 'LookupCode'
```

**Returns**: Joined dataset with combined attributes

---

### 5.2 GIS Vector Operations

#### 5.2.1 BUFFER

Creates a buffer zone around features.

**Syntax**:
```geoscript
BUFFER <distance>
```

**Parameters**:
- Distance in map units

**Examples**:
```geoscript
# Create 100-unit buffer
BUFFER 100

# Buffer then dissolve
BUFFER 50 |> DISSOLVE 'Type'
```

**Returns**: Buffered features

---

#### 5.2.2 DISSOLVE

Merges adjacent features with common attributes.

**Syntax**:
```geoscript
DISSOLVE 'FieldName'
```

**Examples**:
```geoscript
DISSOLVE 'LandUse'
DISSOLVE 'District'
```

**Returns**: Dissolved features

---

#### 5.2.3 EXPLODE

Converts multi-part features to single-part features.

**Syntax**:
```geoscript
EXPLODE
```

**Examples**:
```geoscript
EXPLODE |> CALCULATE 'PartArea' = AREA
```

**Returns**: Single-part features

---

#### 5.2.4 CLEAN

Fixes invalid geometries.

**Syntax**:
```geoscript
CLEAN
```

**Examples**:
```geoscript
CLEAN |> BUFFER 0.1
```

**Returns**: Cleaned geometries

---

#### 5.2.5 GIS_ADD_LAYER

Adds a layer to a GIS dataset.

**Syntax**:
```geoscript
GIS_ADD_LAYER path="<path>" name="<layerName>"
```

---

#### 5.2.6 GIS_REMOVE_LAYER

Removes a layer by name.

**Syntax**:
```geoscript
GIS_REMOVE_LAYER name="<layerName>"
```

---

#### 5.2.7 GIS_INTERSECT

Intersects two GIS layers.

**Syntax**:
```geoscript
GIS_INTERSECT layer="<layerA>" with="<layerB>"
```

---

#### 5.2.8 GIS_UNION

Unions two GIS layers.

**Syntax**:
```geoscript
GIS_UNION layer="<layerA>" with="<layerB>"
```

---

#### 5.2.9 GIS_CLIP

Clips a layer by a polygon mask.

**Syntax**:
```geoscript
GIS_CLIP layer="<layer>" clip="<maskLayer>"
```

---

#### 5.2.10 GIS_CALCULATE_AREA

Calculates polygon areas.

**Syntax**:
```geoscript
GIS_CALCULATE_AREA
```

---

#### 5.2.11 GIS_CALCULATE_LENGTH

Calculates line lengths.

**Syntax**:
```geoscript
GIS_CALCULATE_LENGTH
```

---

#### 5.2.12 GIS_REPROJECT

Reprojects a GIS dataset to a new EPSG code.

**Syntax**:
```geoscript
GIS_REPROJECT epsg=<code>
```

---

### 5.3 GIS Raster Operations

#### 5.3.1 RECLASSIFY

Reclassifies raster values into new categories.

**Syntax**:
```geoscript
RECLASSIFY INTO 'NewLayer' RANGES(min-max: new_val, ...)
```

**Examples**:
```geoscript
RECLASSIFY INTO 'Classes' RANGES(0-50: 1, 50-100: 2, 100-255: 3)
```

**Returns**: Reclassified raster layer

---

#### 5.3.2 RASTER_CALCULATE

Calculates a new raster layer from an expression applied across raster layers.

**Syntax**:
```geoscript
RASTER_CALCULATE EXPR 'A + B * 2' AS 'NewLayerName'
```

**Examples**:
```geoscript
RASTER_CALCULATE EXPR '(A + B) / 2' AS 'MeanElevation'
RASTER_CALCULATE EXPR 'A > 100 ? 255 : 0' AS 'Thresholded'
```

**Notes**:
- Raster layers are referenced by letter in dataset order: A, B, C, ...
- All raster layers must have the same dimensions.

**Returns**: Calculated raster layer

---

#### 5.3.3 SLOPE

Calculates slope from a Digital Elevation Model.

**Syntax**:
```geoscript
SLOPE AS 'NewLayerName'
```

**Examples**:
```geoscript
SLOPE AS 'SlopeMap'
```

**Returns**: Slope raster in degrees

---

#### 5.3.4 ASPECT

Calculates aspect (slope direction) from a DEM.

**Syntax**:
```geoscript
ASPECT AS 'NewLayerName'
```

**Returns**: Aspect raster in degrees (0-360)

---

#### 5.3.5 CONTOUR

Generates contour lines from a raster.

**Syntax**:
```geoscript
CONTOUR INTERVAL <value> AS 'NewLayerName'
```

**Examples**:
```geoscript
CONTOUR INTERVAL 10 AS 'Contours10m'
```

**Returns**: Vector contour lines

---

### 5.4 Thermodynamics Operations

#### 5.4.1 CREATE_DIAGRAM

Generates thermodynamic phase diagrams.

**Syntax**:
```geoscript
CREATE_DIAGRAM BINARY FROM '<c1>' AND '<c2>' TEMP <val> K PRES <val> BAR
CREATE_DIAGRAM TERNARY FROM '<c1>', '<c2>', '<c3>' TEMP <val> K PRES <val> BAR
CREATE_DIAGRAM PT FOR COMP('<c1>'=<m1>,...) T_RANGE(<min>-<max>) K P_RANGE(<min>-<max>) BAR
CREATE_DIAGRAM ENERGY FROM '<c1>' AND '<c2>' TEMP <val> K PRES <val> BAR
```

**Examples**:
```geoscript
# Binary phase diagram
CREATE_DIAGRAM BINARY FROM 'NaCl' AND 'H2O' TEMP 298 K PRES 1 BAR

# Ternary diagram
CREATE_DIAGRAM TERNARY FROM 'SiO2', 'Al2O3', 'CaO' TEMP 1500 K PRES 1 BAR

# Pressure-Temperature diagram
CREATE_DIAGRAM PT FOR COMP('H2O'=1.0,'CO2'=0.1) T_RANGE(273-373) K P_RANGE(1-100) BAR
```

**Returns**: Phase diagram dataset

---

#### 5.4.2 EQUILIBRATE

Calculates aqueous speciation equilibrium.

**Syntax**:
```geoscript
EQUILIBRATE
```

**Description**:
Solves for pH, ionic strength, and species activities for each row in a water chemistry table. Automatically converts concentration units (mg/L, ppm, mol/L) to moles.

**Output Columns**:
- `pH` - Calculated pH
- `IonicStrength_molkg` - Ionic strength
- `act_<species>` - Activity coefficients for all species

**Examples**:
```geoscript
EQUILIBRATE |> SATURATION MINERALS 'Calcite', 'Dolomite'
```

**Returns**: Table with equilibrium speciation

---

#### 5.4.3 SATURATION

Calculates mineral saturation indices.

**Syntax**:
```geoscript
SATURATION MINERALS 'Mineral1', 'Mineral2', ...
```

**Examples**:
```geoscript
SATURATION MINERALS 'Calcite', 'Dolomite', 'Gypsum', 'Halite'
```

**Output**: Adds `SI_<mineral>` columns where:
- SI > 0: Supersaturated (mineral will precipitate)
- SI = 0: At equilibrium
- SI < 0: Undersaturated (mineral will dissolve)

**Returns**: Table with saturation indices

---

#### 5.4.4 BALANCE_REACTION

Generates a balanced dissolution reaction.

**Syntax**:
```geoscript
BALANCE_REACTION 'MineralName'
```

**Examples**:
```geoscript
BALANCE_REACTION 'Calcite'
BALANCE_REACTION 'Dolomite'
```

**Returns**: Table showing balanced reaction equation and equilibrium constant

---

#### 5.4.5 EVAPORATE

Simulates evaporation and mineral precipitation.

**Syntax**:
```geoscript
EVAPORATE UPTO <factor>x STEPS <count> MINERALS 'Mineral1', 'Mineral2', ...
```

**Examples**:
```geoscript
# Simulate 10x evaporation in 50 steps
EVAPORATE UPTO 10x STEPS 50 MINERALS 'Halite', 'Gypsum', 'Calcite'

# Seawater evaporation
EVAPORATE UPTO 100x STEPS 100 MINERALS 'Halite', 'Gypsum', 'Calcite', 'Dolomite'
```

**Output**: Table showing evaporation factor, remaining volume, pH, ionic strength, and precipitated mineral amounts

**Returns**: Evaporation sequence dataset

---

#### 5.4.6 REACT

Calculates equilibrium products of reactants.

**Syntax**:
```geoscript
REACT <Reactants> [TEMP <val> C|K] [PRES <val> BAR|ATM]
```

**Examples**:
```geoscript
# Water equilibrium (automatically uses 55.5 moles for 1 L)
REACT H2O

# Simple salts (plain number notation works!)
REACT NaCl
REACT CaCl2 + Na2CO3

# Minerals and acids
REACT CaCO3 + HCl TEMP 25 C PRES 1 BAR
REACT Fe2O3 + H2SO4 + H2O TEMP 350 K PRES 2 BAR

# Complex species
REACT Al2Si2O5(OH)4 + H2SO4 TEMP 350 K
```

**Note**: Chemical formulas can be entered with plain numbers (e.g., "H2O", "NaCl", "CaCO3", "H2SO4") or subscripts (e.g., "H₂O", "NaCl", "CaCO₃", "H₂SO₄"). The system automatically normalizes ALL formulas to match the database.

**Output**: Table showing products organized by phase (solid, aqueous, gas) with moles, mass, and mole fractions

**Returns**: Reaction products dataset

---

#### 5.4.7 SATURATION_INDEX

Calculates saturation indices for minerals using a compact output format.

**Syntax**:
```geoscript
SATURATION_INDEX MINERALS 'Mineral1', 'Mineral2', ...
```

**Examples**:
```geoscript
SATURATION_INDEX MINERALS 'Calcite', 'Dolomite'
```

**Returns**: Table with saturation index values per mineral

---

#### 5.4.8 SPECIATE

Runs aqueous speciation for the current chemistry dataset.

**Syntax**:
```geoscript
SPECIATE
```

**Examples**:
```geoscript
SPECIATE
```

**Returns**: Speciation results with phases grouped by type

---

#### 5.4.9 DIAGNOSE_SPECIATE

Outputs diagnostic information for speciation runs.

**Syntax**:
```geoscript
DIAGNOSE_SPECIATE
```

**Returns**: Diagnostic speciation report

---

#### 5.4.10 DIAGNOSTIC_THERMODYNAMIC

Runs thermodynamic diagnostics for the current dataset.

**Syntax**:
```geoscript
DIAGNOSTIC_THERMODYNAMIC
```

**Returns**: Diagnostic report with species and phase checks

---

#### 5.4.11 CALCULATE_PHASES

Generates phase-separated outputs from thermodynamic calculations.

**Syntax**:
```geoscript
CALCULATE_PHASES
```

**Returns**: Dataset grouped by phase (solid, aqueous, gas)

---

#### 5.4.12 CALCULATE_CARBONATE_ALKALINITY

Calculates carbonate alkalinity and pH adjustments.

**Syntax**:
```geoscript
CALCULATE_CARBONATE_ALKALINITY
```

**Returns**: Carbonate alkalinity results

---

### 5.5 Petrology Operations

#### 5.5.1 FRACTIONATE_MAGMA

Models fractional crystallization for igneous systems.

**Syntax**:
```geoscript
FRACTIONATE_MAGMA
```

---

#### 5.5.2 LIQUIDUS_SOLIDUS

Computes liquidus and solidus curves for a composition.

**Syntax**:
```geoscript
LIQUIDUS_SOLIDUS
```

---

#### 5.5.3 METAMORPHIC_PT

Builds metamorphic P-T paths from input composition data.

**Syntax**:
```geoscript
METAMORPHIC_PT
```

---

### 5.6 PhysicoChem Reactor Operations

#### 5.6.1 CREATE_REACTOR

Creates a PhysicoChem reactor grid.

**Syntax**:
```geoscript
CREATE_REACTOR
```

---

#### 5.6.2 RUN_SIMULATION

Runs the PhysicoChem simulation.

**Syntax**:
```geoscript
RUN_SIMULATION <total_time_s> <time_step_s> [convergence_tolerance=1e-6]
```

---

#### 5.6.3 PHYSICOCHEM_ADD_FORCE

Adds a force field (gravity/vortex/centrifugal) to a PhysicoChem dataset.

**Syntax**:
```geoscript
PHYSICOCHEM_ADD_FORCE name=<name> type=<gravity|vortex|centrifugal> [options]
```

**Gravity Options**:
- `gravity` (x,y,z)
- `gravity_x`, `gravity_y`, `gravity_z`
- `gravity_preset` (earth, moon, mars, venus, jupiter, saturn, mercury)
- `gravity_magnitude`

**Vortex/Centrifugal Options**:
- `center` (x,y,z)
- `axis` (x,y,z)
- `strength`, `radius`

**Examples**:
```geoscript
PHYSICOCHEM_ADD_FORCE name=MoonGravity type=gravity gravity=0,0,-1.62
PHYSICOCHEM_ADD_FORCE name=Vortex type=vortex center=0,0,0 axis=0,1,0 strength=2.5 radius=5
```

---

#### 5.6.4 PHYSICOCHEM_ADD_NUCLEATION_SITE

Adds a nucleation site (point) to a PhysicoChem dataset.

**Syntax**:
```geoscript
PHYSICOCHEM_ADD_NUCLEATION_SITE name=Site1 x=0 y=0 z=0 mineral=Calcite material_id=ReactorFluid rate=1e6 active=true
```

---

#### 5.6.5 ADD_CELL

Adds a cell to the reactor grid.

**Syntax**:
```geoscript
ADD_CELL x=<int> y=<int> z=<int> material='<name>'
```

---

#### 5.6.6 SET_CELL_MATERIAL

Sets a material for an existing reactor cell.

**Syntax**:
```geoscript
SET_CELL_MATERIAL id=<int> material='<name>'
```

---

### 5.7 Image Processing Operations

#### 5.7.1 BRIGHTNESS_CONTRAST

Adjusts image brightness and contrast.

**Syntax**:
```geoscript
BRIGHTNESS_CONTRAST brightness=<-100 to 100> contrast=<0.1 to 3.0>
```

**Parameters**:
- `brightness`: Brightness offset (-100 to +100)
- `contrast`: Contrast multiplier (0.1 to 3.0, where 1.0 = no change)

**Examples**:
```geoscript
# Increase brightness
BRIGHTNESS_CONTRAST brightness=20

# Increase contrast
BRIGHTNESS_CONTRAST contrast=1.5

# Adjust both
BRIGHTNESS_CONTRAST brightness=10 contrast=1.2
```

**Returns**: Adjusted image

---

#### 5.7.2 FILTER

Applies image filters.

**Syntax**:
```geoscript
FILTER type=<filterType> [size=<kernelSize>] [sigma=<value>]
```

**Filter Types**:
- `gaussian` - Gaussian blur (smooth averaging)
- `median` - Median filter (noise reduction, preserves edges)
- `mean` / `box` - Box filter (simple averaging)
- `sobel` - Sobel edge detection

**Parameters**:
- `type`: Filter type (required)
- `size`: Kernel size in pixels (default: 5)
- `sigma`: Sigma value for Gaussian (optional)

**Examples**:
```geoscript
# Denoise with median filter
FILTER type=median size=3

# Gaussian blur
FILTER type=gaussian size=7 sigma=2.0

# Edge detection
FILTER type=sobel

# Chained filtering
FILTER type=median size=3 |> FILTER type=gaussian size=5
```

**Returns**: Filtered image

---

#### 5.7.3 THRESHOLD

Applies threshold segmentation.

**Syntax**:
```geoscript
THRESHOLD min=<0-255> max=<0-255>
```

**Parameters**:
- `min`: Minimum threshold value (0-255)
- `max`: Maximum threshold value (0-255)

**Examples**:
```geoscript
# Threshold mid-range values
THRESHOLD min=100 max=200

# Segment bright regions
THRESHOLD min=180 max=255

# Pipeline with preprocessing
GRAYSCALE |> THRESHOLD min=128 max=255
```

**Returns**: Binary mask image

---

#### 5.7.4 BINARIZE

Converts image to binary using a threshold.

**Syntax**:
```geoscript
BINARIZE threshold=<0-255 or 'auto'>
```

**Parameters**:
- `threshold`: Threshold value (0-255) or 'auto' for Otsu's automatic thresholding

**Examples**:
```geoscript
# Manual threshold
BINARIZE threshold=128

# Automatic Otsu thresholding
BINARIZE threshold=auto

# Pipeline with preprocessing
FILTER type=gaussian size=3 |> BINARIZE threshold=auto
```

**Returns**: Binary image (black/white)

---

#### 5.7.5 GRAYSCALE

Converts image to grayscale.

**Syntax**:
```geoscript
GRAYSCALE
```

**Examples**:
```geoscript
# Convert to grayscale
GRAYSCALE

# Grayscale then process
GRAYSCALE |> THRESHOLD min=100 max=200
```

**Returns**: Grayscale image

---

#### 5.7.6 INVERT

Inverts image colors (creates negative).

**Syntax**:
```geoscript
INVERT
```

**Examples**:
```geoscript
# Invert colors
INVERT

# Create negative of binary mask
BINARIZE threshold=128 |> INVERT
```

**Returns**: Inverted image

---

#### 5.7.7 NORMALIZE

Normalizes image to full intensity range.

**Syntax**:
```geoscript
NORMALIZE
```

**Description**: Stretches the histogram to use the full dynamic range (0-255).

**Examples**:
```geoscript
# Enhance low-contrast image
NORMALIZE

# Pipeline with normalization
FILTER type=median size=3 |> NORMALIZE
```

**Returns**: Normalized image

---

### 5.8 CT Image Stack Operations

#### 5.8.1 CT_SEGMENT

Performs 3D segmentation of CT volumes.

**Syntax**:
```geoscript
CT_SEGMENT method=<method> [parameters]
```

**Methods**:
- `threshold` - Threshold-based segmentation
- `otsu` - Otsu automatic thresholding
- `watershed` - Watershed segmentation

**Parameters**:
- `min`, `max` (for `threshold`): Intensity bounds
- `material`: Material ID to assign

**Returns**: Segmented CT volume

---

#### 5.8.2 CT_FILTER3D

Applies 3D filters to CT stacks.

**Syntax**:
```geoscript
CT_FILTER3D type=<filterType> [size=<value>]
```

**Filter Types**:
- `gaussian` - 3D Gaussian blur
- `median` - 3D median filter
- `mean` - 3D mean filter
- `nlm` - Non-local means
- `bilateral` - Bilateral filter

**Returns**: Filtered CT volume

---

#### 5.8.3 CT_ADD_MATERIAL

Defines material properties for segmented phases.

**Syntax**:
```geoscript
CT_ADD_MATERIAL name='<name>' color=<r,g,b>
```

**Returns**: Updated CT volume with material definition

---

#### 5.8.4 CT_REMOVE_MATERIAL

Removes a material definition from a CT stack.

**Syntax**:
```geoscript
CT_REMOVE_MATERIAL id=<materialId>
```

**Returns**: Updated CT volume without the specified material

---

#### 5.8.5 CT_ANALYZE_POROSITY

Calculates porosity from segmented volumes.

**Syntax**:
```geoscript
CT_ANALYZE_POROSITY void_material=<materialId>
```

**Returns**: Porosity analysis results

---

#### 5.8.6 CT_CROP

Crops a sub-volume from the CT stack.

**Syntax**:
```geoscript
CT_CROP x=<start> y=<start> z=<start> width=<w> height=<h> depth=<d>
```

**Returns**: Cropped CT volume

---

#### 5.8.7 CT_EXTRACT_SLICE

Extracts a 2D slice from the 3D volume.

**Syntax**:
```geoscript
CT_EXTRACT_SLICE index=<sliceNumber> [axis=<x|y|z>]
```

**Returns**: 2D image slice

---

#### 5.8.8 CT_LABEL_ANALYSIS

Summarizes label connectivity and size statistics.

**Syntax**:
```geoscript
CT_LABEL_ANALYSIS
```

**Returns**: Label analysis report

---

### 5.9 Pore Network Model (PNM) Operations

#### 5.9.1 PNM_FILTER_PORES

Filters pores based on geometric criteria.

**Syntax**:
```geoscript
PNM_FILTER_PORES [min_radius=<value>] [max_radius=<value>] [min_coord=<value>]
```

**Parameters**:
- `min_radius`: Minimum pore radius (micrometers)
- `max_radius`: Maximum pore radius (micrometers)
- `min_coord`: Minimum coordination number

**Examples**:
```geoscript
PNM_FILTER_PORES min_radius=1.0 max_radius=100.0 min_coord=2
```

**Returns**: Filtered pore network

---

#### 5.9.2 PNM_FILTER_THROATS

Filters throats based on geometric criteria.

**Syntax**:
```geoscript
PNM_FILTER_THROATS [min_radius=<value>] [max_radius=<value>] [max_length=<value>]
```

**Examples**:
```geoscript
PNM_FILTER_THROATS min_radius=0.5 max_length=50.0
```

**Returns**: Filtered pore network

---

#### 5.9.3 PNM_CALCULATE_PERMEABILITY

Calculates absolute permeability using network simulation.

**Syntax**:
```geoscript
PNM_CALCULATE_PERMEABILITY direction=<x|y|z|all>
```

**Examples**:
```geoscript
PNM_CALCULATE_PERMEABILITY direction=x
PNM_CALCULATE_PERMEABILITY direction=all
```

**Returns**: Permeability results table

---

#### 5.9.4 PNM_DRAINAGE_SIMULATION

Runs drainage capillary pressure simulation.

**Syntax**:
```geoscript
PNM_DRAINAGE_SIMULATION [contact_angle=<degrees>] [interfacial_tension=<N/m>]
```

**Examples**:
```geoscript
PNM_DRAINAGE_SIMULATION contact_angle=30 interfacial_tension=0.03
```

**Returns**: Drainage curve data

---

#### 5.9.5 PNM_IMBIBITION_SIMULATION

Runs imbibition capillary pressure simulation.

**Syntax**:
```geoscript
PNM_IMBIBITION_SIMULATION [contact_angle=<degrees>] [interfacial_tension=<N/m>]
```

**Examples**:
```geoscript
PNM_IMBIBITION_SIMULATION contact_angle=60 interfacial_tension=0.03
```

**Returns**: Imbibition curve data

---

#### 5.9.6 PNM_EXTRACT_LARGEST_CLUSTER

Extracts the largest connected cluster of pores.

**Syntax**:
```geoscript
PNM_EXTRACT_LARGEST_CLUSTER
```

**Returns**: Largest cluster pore network

---

#### 5.9.7 PNM_STATISTICS

Calculates comprehensive network statistics.

**Syntax**:
```geoscript
PNM_STATISTICS
```

**Output**:
- Total pores and throats
- Average coordination number
- Pore and throat size distributions
- Network porosity

**Returns**: Statistics table

---

#### 5.9.8 SET_PNM_SPECIES

Sets reactive species concentrations for a pore network.

**Syntax**:
```geoscript
SET_PNM_SPECIES <species> <inlet_conc_mol_L> <initial_conc_mol_L>
```

**Examples**:
```geoscript
SET_PNM_SPECIES Ca2+ 0.01 0.005
```

---

#### 5.9.9 SET_PNM_MINERALS

Sets initial mineral volume fractions for reactive transport.

**Syntax**:
```geoscript
SET_PNM_MINERALS <mineral> <volume_fraction>
```

**Examples**:
```geoscript
SET_PNM_MINERALS Calcite 0.02
```

---

#### 5.9.10 RUN_PNM_REACTIVE_TRANSPORT

Runs reactive transport simulation through the pore network.

**Syntax**:
```geoscript
RUN_PNM_REACTIVE_TRANSPORT <total_time_s> <time_step_s> <inlet_temp_K> <inlet_pressure_Pa> <outlet_pressure_Pa> [convergence_tolerance=1e-6]
```

**Examples**:
```geoscript
RUN_PNM_REACTIVE_TRANSPORT 1000 0.01 298 1.5e7 1.0e7 convergence_tolerance=1e-6
```

---

#### 5.9.11 EXPORT_PNM_RESULTS

Exports reactive transport results to CSV.

**Syntax**:
```geoscript
EXPORT_PNM_RESULTS "results.csv"
```

---

### 5.10 Seismic Data Operations

#### 5.10.1 SEIS_FILTER

Applies frequency filters to seismic data.

**Syntax**:
```geoscript
SEIS_FILTER type=<bandpass|lowpass|highpass|fxdecon> [low=<Hz>] [high=<Hz>]
```

**Examples**:
```geoscript
# Bandpass filter
SEIS_FILTER type=bandpass low=10 high=80

# Lowpass filter
SEIS_FILTER type=lowpass high=60

# Highpass filter
SEIS_FILTER type=highpass low=15
```

**Returns**: Filtered seismic data

---

#### 5.10.2 SEIS_AGC

Applies automatic gain control.

**Syntax**:
```geoscript
SEIS_AGC window=<milliseconds>
```

**Examples**:
```geoscript
SEIS_AGC window=500
```

**Returns**: Gain-controlled seismic data

---

#### 5.10.3 SEIS_VELOCITY_ANALYSIS

Performs velocity analysis for NMO correction.

**Syntax**:
```geoscript
SEIS_VELOCITY_ANALYSIS method=<semblance|cvs|cmp>
```

**Examples**:
```geoscript
SEIS_VELOCITY_ANALYSIS method=semblance
```

**Returns**: Velocity model

---

#### 5.10.4 SEIS_NMO_CORRECTION

Applies normal moveout correction.

**Syntax**:
```geoscript
SEIS_NMO_CORRECTION [velocity_file=<path>] [velocity=<constant>]
```

**Examples**:
```geoscript
SEIS_NMO_CORRECTION velocity=2000
```

**Returns**: NMO-corrected seismic data

---

#### 5.10.5 SEIS_STACK

Stacks seismic traces.

**Syntax**:
```geoscript
SEIS_STACK method=<mean|median|weighted>
```

**Examples**:
```geoscript
SEIS_STACK method=mean
```

**Returns**: Stacked seismic data

---

#### 5.10.6 SEIS_MIGRATION

Performs seismic migration.

**Syntax**:
```geoscript
SEIS_MIGRATION method=<kirchhoff|fk|rtm> [aperture=<meters>]
```

**Examples**:
```geoscript
SEIS_MIGRATION method=kirchhoff aperture=1000
```

**Returns**: Migrated seismic data

---

#### 5.10.7 SEIS_PICK_HORIZON

Picks seismic horizons.

**Syntax**:
```geoscript
SEIS_PICK_HORIZON name=<horizonName> method=<auto|manual|tracking>
```

**Examples**:
```geoscript
SEIS_PICK_HORIZON name=Top_Reservoir method=auto
```

**Returns**: Horizon pick dataset

---

### 5.11 Seismic Cube Operations

#### 5.11.1 CUBE_CREATE

Creates a seismic cube dataset.

**Syntax**:
```geoscript
CUBE_CREATE name="Survey_Cube" [survey="Field_2024"] [project="Project_X"]
```

**Returns**: Seismic cube dataset

---

#### 5.11.2 CUBE_ADD_LINE

Adds a seismic line to a cube using geometry or header-derived coordinates.

**Syntax**:
```geoscript
CUBE_ADD_LINE cube="Survey_Cube" line="Line_001" start_x=0 start_y=0 end_x=5000 end_y=0 trace_spacing=25
CUBE_ADD_LINE cube="Survey_Cube" line="Line_002" use_headers=true
```

**Returns**: Updated cube dataset

---

#### 5.11.3 CUBE_ADD_PERPENDICULAR

Adds a perpendicular line at a trace index of an existing line.

**Syntax**:
```geoscript
CUBE_ADD_PERPENDICULAR cube="Survey_Cube" base="Line_001" trace=100 line="Crossline_001"
```

**Returns**: Updated cube dataset

---

#### 5.11.4 CUBE_DETECT_INTERSECTIONS

Detects intersections between cube lines.

**Syntax**:
```geoscript
CUBE_DETECT_INTERSECTIONS cube="Survey_Cube"
```

---

#### 5.11.5 CUBE_SET_NORMALIZATION

Configures normalization settings.

**Syntax**:
```geoscript
CUBE_SET_NORMALIZATION cube="Survey_Cube" amplitude_method=balanced match_phase=true
```

---

#### 5.11.6 CUBE_NORMALIZE

Applies normalization at intersections.

**Syntax**:
```geoscript
CUBE_NORMALIZE cube="Survey_Cube"
```

---

#### 5.11.7 CUBE_BUILD_VOLUME

Builds the regularized cube volume.

**Syntax**:
```geoscript
CUBE_BUILD_VOLUME cube="Survey_Cube" inline_count=200 crossline_count=200 sample_count=1500 inline_spacing=25 crossline_spacing=25 sample_interval=4
```

---

#### 5.11.8 CUBE_EXPORT_GIS

Exports the cube to a Subsurface GIS dataset.

**Syntax**:
```geoscript
CUBE_EXPORT_GIS cube="Survey_Cube" output="Subsurface_Model"
```

---

#### 5.11.9 CUBE_EXPORT_SLICE

Exports a time slice as a GIS raster dataset.

**Syntax**:
```geoscript
CUBE_EXPORT_SLICE cube="Survey_Cube" time=1500 output="Slice_1500ms"
```

---

#### 5.11.10 CUBE_STATISTICS

Generates a summary table of cube statistics.

**Syntax**:
```geoscript
CUBE_STATISTICS cube="Survey_Cube" output="Cube_Stats"
```

---

### 5.12 Utility Operations

#### 5.12.1 LISTOPS

Lists all available operations for the current dataset type.

**Syntax**:
```geoscript
LISTOPS
```

**Description**: Displays all commands applicable to the current dataset.

**Returns**: Operation list (printed to console)

---

#### 5.12.2 DISPTYPE

Displays detailed dataset type information.

**Syntax**:
```geoscript
DISPTYPE
```

**Description**: Shows dataset type, supported operations, and metadata.

**Returns**: Type information (printed to console)

---

#### 5.12.3 LOAD

Loads a dataset from a file.

**Syntax**:
```geoscript
LOAD "path/to/file" [AS "DatasetName"] [TYPE=<DatasetType>] [PIXELSIZE=<value>] [UNIT="um|mm"]
```

**Parameters**:
- `path`: Source file or folder path.
- `AS`: Optional dataset name override.
- `TYPE`: Optional dataset type override for ambiguous file types.
- `PIXELSIZE`: Optional pixel size for image/CT datasets.
- `UNIT`: Optional unit for `PIXELSIZE` (default: µm).

**Examples**:
```geoscript
LOAD "scan.ctstack" AS "CT Volume" TYPE=CtImageStack PIXELSIZE=2.5 UNIT="um"
LOAD "image.tif" AS "ThinSection" PIXELSIZE=0.8 UNIT="mm"
```

**Returns**: The loaded dataset.

---

#### 5.11.4 USE

Switches the active dataset to another loaded dataset by name.

**Syntax**:
```geoscript
USE @'DatasetName'
```

**Examples**:
```geoscript
USE @'WellLogs' |> SELECT WHERE 'Depth' > 2500
USE @'InterpretationPolygons'
```

**Returns**: The referenced dataset.

---

#### 5.11.5 INFO

Shows dataset summary information.

**Syntax**:
```geoscript
INFO
```

**Description**: Quick summary of dataset properties (dimensions, size, type, etc.)

**Returns**: Dataset information (printed to console)

---

#### 5.11.6 UNLOAD

Unloads dataset from memory.

**Syntax**:
```geoscript
UNLOAD
```

**Description**: Removes dataset from memory while keeping it in the project.

**Returns**: None (frees memory)

---

#### 5.11.7 SAVE

Saves a dataset to a file.

**Syntax**:
```geoscript
SAVE "path/to/file" [FORMAT="fmt"]
```

**Parameters**:
- `path`: Destination file path.
- `FORMAT`: Optional format specifier (e.g., "png", "csv", "shp", "las", "bhb").

**Examples**:
```geoscript
SAVE "output_image.png"
SAVE "results.csv" FORMAT="csv"
SAVE "borehole.las" FORMAT="las"
```

**Returns**: The saved dataset.

---

#### 5.11.8 SET_PIXEL_SIZE

Updates pixel size metadata on image or CT datasets.

**Syntax**:
```geoscript
SET_PIXEL_SIZE value=<size> [UNIT="um|mm"]
```

**Examples**:
```geoscript
SET_PIXEL_SIZE value=1.2 UNIT="um"
```

**Returns**: The updated dataset.

---

#### 5.11.9 COPY

Duplicates the current dataset.

**Syntax**:
```geoscript
COPY [AS "NewName"]
```

**Parameters**:
- `AS`: Optional new name for the copy. If omitted, appends "_Copy" to the original name.

**Examples**:
```geoscript
COPY AS "BackupData"
```

**Returns**: The duplicated dataset.

---

#### 5.11.10 DELETE

Removes the dataset from the project.

**Syntax**:
```geoscript
DELETE
```

**Description**: Removes the dataset from the project manager. It remains in memory if referenced by variables but is removed from the UI project list.

**Returns**: The removed dataset.

---

### 5.12 Borehole Operations

#### 5.12.1 BH_ADD_LITHOLOGY

Adds lithology intervals to a borehole dataset.

**Syntax**:
```geoscript
BH_ADD_LITHOLOGY depth=<value> lith='<name>'
```

---

#### 5.12.2 BH_REMOVE_LITHOLOGY

Removes a lithology interval.

**Syntax**:
```geoscript
BH_REMOVE_LITHOLOGY depth=<value>
```

---

#### 5.12.3 BH_ADD_LOG

Adds a log curve definition.

**Syntax**:
```geoscript
BH_ADD_LOG name='<logName>' unit='<unit>'
```

---

#### 5.12.4 BH_CALCULATE_POROSITY

Calculates porosity from borehole logs.

**Syntax**:
```geoscript
BH_CALCULATE_POROSITY method=<neutron|density>
```

---

#### 5.12.5 BH_CALCULATE_SATURATION

Calculates saturation using Archie-style methods.

**Syntax**:
```geoscript
BH_CALCULATE_SATURATION method=archie
```

---

#### 5.12.6 BH_DEPTH_SHIFT

Applies a depth shift to borehole data.

**Syntax**:
```geoscript
BH_DEPTH_SHIFT shift=<value>
```

---

#### 5.12.7 BH_CORRELATION

Runs borehole correlation tools.

**Syntax**:
```geoscript
BH_CORRELATION method=<dtw|manual>
```

---

### 5.13 Acoustic Volume Operations

#### 5.13.1 ACOUSTIC_THRESHOLD

Thresholds an acoustic volume.

**Syntax**:
```geoscript
ACOUSTIC_THRESHOLD min=<value> max=<value>
```

---

#### 5.13.2 ACOUSTIC_EXTRACT_TARGETS

Extracts targets from acoustic data.

**Syntax**:
```geoscript
ACOUSTIC_EXTRACT_TARGETS threshold=<value>
```

---

### 5.14 Mesh Operations

#### 5.14.1 MESH_SMOOTH

Smooths mesh surfaces.

**Syntax**:
```geoscript
MESH_SMOOTH iterations=<count>
```

---

#### 5.14.2 MESH_DECIMATE

Decimates mesh geometry.

**Syntax**:
```geoscript
MESH_DECIMATE target=<ratio>
```

---

#### 5.14.3 MESH_REPAIR

Repairs mesh topology.

**Syntax**:
```geoscript
MESH_REPAIR
```

---

#### 5.14.4 MESH_CALCULATE_VOLUME

Calculates mesh volume.

**Syntax**:
```geoscript
MESH_CALCULATE_VOLUME
```

---

### 5.15 Video Operations

#### 5.15.1 VIDEO_EXTRACT_FRAME

Extracts a frame from a video dataset.

**Syntax**:
```geoscript
VIDEO_EXTRACT_FRAME index=<frameNumber>
```

---

#### 5.15.2 VIDEO_STABILIZE

Stabilizes a video dataset.

**Syntax**:
```geoscript
VIDEO_STABILIZE smoothing=<value>
```

---

### 5.16 Audio Operations

#### 5.16.1 AUDIO_TRIM

Trims audio to a time window.

**Syntax**:
```geoscript
AUDIO_TRIM start=<seconds> end=<seconds>
```

---

#### 5.16.2 AUDIO_NORMALIZE

Normalizes audio volume.

**Syntax**:
```geoscript
AUDIO_NORMALIZE target=<dB>
```

---

### 5.17 Text Operations

#### 5.17.1 TEXT_SEARCH

Searches text datasets.

**Syntax**:
```geoscript
TEXT_SEARCH pattern="<text>"
```

---

#### 5.17.2 TEXT_REPLACE

Replaces text in datasets.

**Syntax**:
```geoscript
TEXT_REPLACE pattern="<text>" with="<replacement>"
```

---

#### 5.17.3 TEXT_STATISTICS

Computes text statistics.

**Syntax**:
```geoscript
TEXT_STATISTICS
```

---

### 5.18 Slope Stability Operations

#### 5.18.1 SLOPE_GENERATE_BLOCKS

Generates slope stability blocks.

**Syntax**:
```geoscript
SLOPE_GENERATE_BLOCKS
```

---

#### 5.18.2 SLOPE_ADD_JOINT_SET

Adds a joint set to the slope model.

**Syntax**:
```geoscript
SLOPE_ADD_JOINT_SET dip=<degrees> dip_dir=<degrees> spacing=<meters> friction=<degrees> cohesion=<MPa>
```

---

#### 5.18.3 SLOPE_SET_MATERIAL

Sets material properties for slope blocks.

**Syntax**:
```geoscript
SLOPE_SET_MATERIAL preset=<name>
```

---

#### 5.18.4 SLOPE_SET_ANGLE

Sets slope angle parameters.

**Syntax**:
```geoscript
SLOPE_SET_ANGLE degrees=<value>
```

---

#### 5.18.5 SLOPE_ADD_EARTHQUAKE

Adds earthquake loading to the model.

**Syntax**:
```geoscript
SLOPE_ADD_EARTHQUAKE magnitude=<value> depth=<value>
```

---

#### 5.18.6 SLOPE_SET_WATER

Sets groundwater parameters.

**Syntax**:
```geoscript
SLOPE_SET_WATER level=<value>
```

---

#### 5.18.7 SLOPE_FILTER_BLOCKS

Filters blocks based on criteria.

**Syntax**:
```geoscript
SLOPE_FILTER_BLOCKS min_volume=<value>
```

---

#### 5.18.8 SLOPE_TRACK_BLOCKS

Tracks block motion through time.

**Syntax**:
```geoscript
SLOPE_TRACK_BLOCKS
```

---

#### 5.18.9 SLOPE_CALCULATE_FOS

Calculates factor of safety.

**Syntax**:
```geoscript
SLOPE_CALCULATE_FOS
```

---

#### 5.18.10 SLOPE_SIMULATE

Runs slope simulation.

**Syntax**:
```geoscript
SLOPE_SIMULATE mode=<static|dynamic> time=<seconds>
```

---

#### 5.18.11 SLOPE_EXPORT

Exports slope stability results.

**Syntax**:
```geoscript
SLOPE_EXPORT path="<outputPath>"
```

---

## 6. Advanced Features

### 6.1 Pipeline Composition

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

### 6.2 Conditional Execution

Use SELECT to conditionally process data:

```geoscript
# Process only high-temperature samples
SELECT WHERE 'Temperature' > 100 |>
  CALCULATE 'Converted' = 'Temperature' * 1.8 + 32 |>
  SORTBY 'Converted' DESC
```

### 6.3 Cross-Dataset Operations

Reference multiple datasets in a single operation:

```geoscript
# Spatial join
SELECT INTERSECTS @'BufferZone' |>
  JOIN @'AttributeData' ON 'ID' = 'FeatureID' |>
  CALCULATE 'Density' = 'Population' / AREA
```

### 6.4 Unit Conversion (Thermodynamics)

The thermodynamics engine automatically converts concentration units:

| Unit | Type | Conversion |
|------|------|-----------|
| mg/L, ppm | Mass | to mol/L using molar mass |
| ug/L, ppb | Mass | to mol/L using molar mass |
| g/L | Mass | to mol/L using molar mass |
| mol/L, M | Molar | No conversion |
| mmol/L | Molar | / 1000 |
| umol/L | Molar | / 1,000,000 |

Column headers must include units:
```geoscript
"Ca (mg/L)" or "Na [ppm]" or "Cl (mol/L)"
```

### 6.5 Expression Functions (NCalc)

Full list of supported mathematical functions:

**Arithmetic**:
- `Abs(x)` - Absolute value
- `Sign(x)` - Sign of number (-1, 0, 1)
- `Sqrt(x)` - Square root
- `Pow(x, y)` - x raised to power y

**Trigonometry**:
- `Sin(x)`, `Cos(x)`, `Tan(x)`
- `Asin(x)`, `Acos(x)`, `Atan(x)`, `Atan2(y, x)`

**Logarithms**:
- `Log(x)` - Natural logarithm
- `Log10(x)` - Base-10 logarithm
- `Exp(x)` - e raised to power x

**Rounding**:
- `Round(x)` - Round to nearest integer
- `Floor(x)` - Round down
- `Ceiling(x)` - Round up
- `Truncate(x)` - Remove decimal part

**Min/Max**:
- `Min(x, y, ...)` - Minimum value
- `Max(x, y, ...)` - Maximum value

**Conditional**:
- `if(condition, true_value, false_value)`

---

## 7. Practical Examples

### 7.1 Image Processing Workflows

#### 7.1.1 Basic Image Enhancement

```geoscript
# Denoise and enhance a noisy image
FILTER type=median size=3 |>
  BRIGHTNESS_CONTRAST brightness=10 contrast=1.3 |>
  NORMALIZE
```

#### 7.1.2 Edge Detection Pipeline

```geoscript
# Prepare image for edge analysis
GRAYSCALE |>
  FILTER type=gaussian size=3 |>
  FILTER type=sobel
```

#### 7.1.3 Complete Segmentation Workflow

```geoscript
# Full segmentation pipeline
FILTER type=median size=3 |>
  GRAYSCALE |>
  NORMALIZE |>
  BINARIZE threshold=auto |>
  INFO
```

### 7.2 Table Data Analysis

#### 7.2.1 Data Filtering and Aggregation

```geoscript
# Filter, aggregate, and sort data
SELECT WHERE 'Temperature' > 25 |>
  CALCULATE 'TempF' = 'Temperature' * 1.8 + 32 |>
  GROUPBY 'Location' AGGREGATE AVG('TempF') AS 'AvgTempF', COUNT('ID') AS 'Count' |>
  SORTBY 'AvgTempF' DESC |>
  TAKE 10
```

#### 7.2.2 Statistical Summary

```geoscript
# Create summary statistics by category
GROUPBY 'Category' AGGREGATE
  COUNT('ID') AS 'N',
  SUM('Volume') AS 'TotalVol',
  AVG('Concentration') AS 'MeanConc',
  MIN('Value') AS 'Min',
  MAX('Value') AS 'Max'
```

### 7.3 GIS Spatial Analysis

#### 7.3.1 Buffer and Dissolve

```geoscript
# Create 100m buffer zones and dissolve by type
BUFFER 100 |>
  DISSOLVE 'LandUseType' |>
  CALCULATE 'AreaHectares' = AREA / 10000
```

#### 7.3.2 Spatial Query and Calculation

```geoscript
# Find features in polygon and calculate properties
SELECT WITHIN @'StudyArea' |>
  CALCULATE 'AreaHectares' = AREA / 10000 |>
  SORTBY 'AreaHectares' DESC
```

### 7.4 Thermodynamic Modeling

#### 7.4.1 Water Chemistry Analysis

```geoscript
# Complete water chemistry workflow
EQUILIBRATE |>
  SATURATION MINERALS 'Calcite', 'Dolomite', 'Gypsum', 'Halite'
```

#### 7.4.2 Evaporation Sequence

```geoscript
# Simulate seawater evaporation
EVAPORATE UPTO 100x STEPS 100 MINERALS
  'Halite', 'Gypsum', 'Calcite', 'Dolomite'
```

#### 7.4.3 Chemical Reaction

```geoscript
# Simulate mineral dissolution
REACT CaCO3 + HCl + H2O TEMP 25 C PRES 1 BAR
```

### 7.5 Seismic Data Processing

#### 7.5.1 Standard Processing Flow

```geoscript
# Complete seismic processing pipeline
SEIS_FILTER type=bandpass low=10 high=80 |>
  SEIS_AGC window=500 |>
  SEIS_NMO_CORRECTION velocity=2000 |>
  SEIS_STACK method=mean |>
  SEIS_MIGRATION method=kirchhoff aperture=1000
```

### 7.6 Pore Network Analysis

#### 7.6.1 Network Characterization

```geoscript
# Clean and analyze pore network
PNM_EXTRACT_LARGEST_CLUSTER |>
  PNM_FILTER_PORES min_radius=1.0 min_coord=2 |>
  PNM_STATISTICS
```

#### 7.6.2 Capillary Pressure Curve

```geoscript
# Generate drainage and imbibition curves
PNM_DRAINAGE_SIMULATION contact_angle=30 interfacial_tension=0.03
```

---

## 8. Best Practices

### 8.1 General Principles

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

   # Avoid: Hard-to-read nested operations
   ```

### 8.2 Image Processing

1. **Always preprocess before segmentation**
   ```geoscript
   FILTER type=median size=3 |> BINARIZE threshold=auto
   ```

2. **Use grayscale for most analysis**
   ```geoscript
   GRAYSCALE |> THRESHOLD min=100 max=200
   ```

3. **Normalize low-contrast images**
   ```geoscript
   NORMALIZE |> BINARIZE threshold=128
   ```

4. **Chain filters for better results**
   ```geoscript
   FILTER type=median size=3 |> FILTER type=gaussian size=5
   ```

### 8.3 Table Operations

1. **Filter before aggregation**
   ```geoscript
   SELECT WHERE 'Valid' = 1 |> GROUPBY 'Category' AGGREGATE COUNT('ID')
   ```

2. **Sort after aggregation**
   ```geoscript
   GROUPBY 'Type' AGGREGATE SUM('Value') AS 'Total' |> SORTBY 'Total' DESC
   ```

3. **Use TAKE for large datasets**
   ```geoscript
   SORTBY 'Score' DESC |> TAKE 100
   ```

### 8.4 Thermodynamics

1. **Equilibrate before saturation calculations**
   ```geoscript
   EQUILIBRATE |> SATURATION MINERALS 'Calcite', 'Gypsum'
   ```

2. **Check units in column headers**
   - System auto-converts: mg/L, ppm, ug/L, mol/L, etc.
   - Format: "Ca (mg/L)" or "Na [ppm]"

3. **Verify mass balance**
   - Use INFO to check element totals

### 8.5 Performance

1. **Unload unused datasets**
   ```geoscript
   UNLOAD
   ```

2. **Process large images in steps**
   ```geoscript
   # Better: Two simple operations
   FILTER type=median size=3
   GRAYSCALE

   # Avoid: One complex operation on very large images
   ```

3. **Use appropriate filter sizes**
   ```geoscript
   # For noise: small kernel
   FILTER type=median size=3

   # For smoothing: larger kernel
   FILTER type=gaussian size=7
   ```

---

## 9. Troubleshooting

### 9.1 Common Errors

#### 9.1.1 Type Mismatch

**Error**: "Operation not supported for this dataset type"

**Solution**: Check dataset type with `DISPTYPE` and use `LISTOPS` to see available operations.

```geoscript
DISPTYPE    # Check current dataset type
LISTOPS     # See what operations are available
```

#### 9.1.2 Invalid Parameters

**Error**: "Parameter out of range" or "Invalid parameter value"

**Solution**: Check parameter ranges in command reference. Common issues:
- Brightness: must be -100 to 100
- Contrast: must be 0.1 to 3.0
- Threshold: must be 0 to 255

#### 9.1.3 Missing Dataset Reference

**Error**: "Referenced dataset not found"

**Solution**: Ensure dataset exists before referencing with `@`:

```geoscript
# This will fail if 'OtherDataset' doesn't exist
JOIN @'OtherDataset' ON 'ID' = 'Key'
```

#### 9.1.4 Parse Errors

**Error**: "Invalid command syntax"

**Solution**: Check syntax:
- Ensure operation name is spelled correctly
- Use quotes for string literals
- Check parameter format (name=value)

### 9.2 Debugging Tips

1. **Test operations incrementally**
   ```geoscript
   # Test each step separately
   FILTER type=median size=3
   # Then add next operation
   FILTER type=median size=3 |> GRAYSCALE
   ```

2. **Use INFO to inspect intermediate results**
   ```geoscript
   OPERATION1 |> INFO |> OPERATION2
   ```

3. **Check the output log**
   All errors and warnings are logged to the output window.

---

## 10. API Reference

### 10.1 Core Classes

#### 10.1.1 GeoScriptEngine

Main execution engine for GEOSCRIPT.

**Methods**:
- `ExecuteAsync(string script, Dataset inputDataset, Dictionary<string, Dataset> contextDatasets)` - Execute script

#### 10.1.2 GeoScriptParser

Parses GEOSCRIPT syntax into Abstract Syntax Tree (AST).

**Methods**:
- `Parse(string script)` - Parse script into AST

#### 10.1.3 GeoScriptContext

Execution context containing datasets and environment.

**Properties**:
- `InputDataset` - Current dataset being processed
- `AvailableDatasets` - Dictionary of datasets accessible by name

### 10.2 AST Nodes

#### 10.2.1 CommandNode

Represents a single operation.

**Properties**:
- `CommandName` - Name of the operation
- `FullText` - Full command text with parameters

#### 10.2.2 PipelineNode

Represents a pipeline of operations.

**Properties**:
- `Left` - Left-hand operation
- `Right` - Right-hand operation

### 10.3 Command Interface

All operations implement `IGeoScriptCommand`:

```csharp
public interface IGeoScriptCommand
{
    string Name { get; }
    string HelpText { get; }
    string Usage { get; }
    Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node);
}
```

### 10.4 Operation Registry

#### 10.4.1 OperationRegistry

Manages available operations for each dataset type.

**Methods**:
- `GetOperationsForType(DatasetType type)` - Get operations for a dataset type
- `GetOperation(string name)` - Get operation by name
- `HasOperation(string name)` - Check if operation exists
- `GetAllOperationNames()` - Get all operation names

### 10.5 Token Types

GEOSCRIPT lexer recognizes the following token types:

- `WITH`, `DO`, `TO`, `THEN` - Statement keywords
- `LISTOPS`, `DISPTYPE`, `INFO`, `UNLOAD`, `USE` - Utility keywords
- `PIPE` (`|>`) - Pipeline operator
- `COMMA` (`,`) - Parameter separator
- `STRING` - String literal
- `NUMBER` - Numeric literal
- `IDENTIFIER` - Operation/parameter name
- `NEWLINE` - Line terminator
- `EOF` - End of file

---

## Appendix A: Complete Command List

### Table Operations (9 commands)
SELECT, CALCULATE, SORTBY, GROUPBY, RENAME, DROP, TAKE, UNIQUE, JOIN

### GIS Vector (4 commands)
BUFFER, DISSOLVE, EXPLODE, CLEAN

### GIS Raster (5 commands)
RECLASSIFY, RASTER_CALCULATE, SLOPE, ASPECT, CONTOUR

### GIS Extended (8 commands)
GIS_ADD_LAYER, GIS_REMOVE_LAYER, GIS_INTERSECT, GIS_UNION, GIS_CLIP, GIS_CALCULATE_AREA, GIS_CALCULATE_LENGTH, GIS_REPROJECT

### Thermodynamics (12 commands)
CREATE_DIAGRAM, EQUILIBRATE, SATURATION, SATURATION_INDEX, BALANCE_REACTION, EVAPORATE, REACT, SPECIATE, DIAGNOSE_SPECIATE, DIAGNOSTIC_THERMODYNAMIC, CALCULATE_PHASES, CALCULATE_CARBONATE_ALKALINITY

### Petrology (3 commands)
FRACTIONATE_MAGMA, LIQUIDUS_SOLIDUS, METAMORPHIC_PT

### PhysicoChem Reactor (5 commands)
CREATE_REACTOR, RUN_SIMULATION, PHYSICOCHEM_ADD_FORCE, ADD_CELL, SET_CELL_MATERIAL

### Image Processing (7 commands)
BRIGHTNESS_CONTRAST, FILTER, THRESHOLD, BINARIZE, GRAYSCALE, INVERT, NORMALIZE

### CT Image Stack (8 commands)
CT_SEGMENT, CT_FILTER3D, CT_ADD_MATERIAL, CT_REMOVE_MATERIAL, CT_ANALYZE_POROSITY, CT_CROP, CT_EXTRACT_SLICE, CT_LABEL_ANALYSIS

### PNM (7 commands)
PNM_FILTER_PORES, PNM_FILTER_THROATS, PNM_CALCULATE_PERMEABILITY, PNM_DRAINAGE_SIMULATION, PNM_IMBIBITION_SIMULATION, PNM_EXTRACT_LARGEST_CLUSTER, PNM_STATISTICS

### PNM Reactive Transport (4 commands)
RUN_PNM_REACTIVE_TRANSPORT, SET_PNM_SPECIES, SET_PNM_MINERALS, EXPORT_PNM_RESULTS

### Seismic (7 commands)
SEIS_FILTER, SEIS_AGC, SEIS_VELOCITY_ANALYSIS, SEIS_NMO_CORRECTION, SEIS_STACK, SEIS_MIGRATION, SEIS_PICK_HORIZON

**Seismic Cube (10):**
CUBE_CREATE, CUBE_ADD_LINE, CUBE_ADD_PERPENDICULAR, CUBE_DETECT_INTERSECTIONS, CUBE_SET_NORMALIZATION, CUBE_NORMALIZE, CUBE_BUILD_VOLUME, CUBE_EXPORT_GIS, CUBE_EXPORT_SLICE, CUBE_STATISTICS

### Borehole (7 commands)
BH_ADD_LITHOLOGY, BH_REMOVE_LITHOLOGY, BH_ADD_LOG, BH_CALCULATE_POROSITY, BH_CALCULATE_SATURATION, BH_DEPTH_SHIFT, BH_CORRELATION

### Acoustic (2 commands)
ACOUSTIC_THRESHOLD, ACOUSTIC_EXTRACT_TARGETS

### Mesh (4 commands)
MESH_SMOOTH, MESH_DECIMATE, MESH_REPAIR, MESH_CALCULATE_VOLUME

### Video (2 commands)
VIDEO_EXTRACT_FRAME, VIDEO_STABILIZE

### Audio (2 commands)
AUDIO_TRIM, AUDIO_NORMALIZE

### Text (3 commands)
TEXT_SEARCH, TEXT_REPLACE, TEXT_STATISTICS

### Slope Stability (11 commands)
SLOPE_GENERATE_BLOCKS, SLOPE_ADD_JOINT_SET, SLOPE_SET_MATERIAL, SLOPE_SET_ANGLE, SLOPE_ADD_EARTHQUAKE, SLOPE_SET_WATER, SLOPE_FILTER_BLOCKS, SLOPE_TRACK_BLOCKS, SLOPE_CALCULATE_FOS, SLOPE_SIMULATE, SLOPE_EXPORT

### Utility (10 commands)
LOAD, SAVE, COPY, DELETE, SET_PIXEL_SIZE, LISTOPS, DISPTYPE, INFO, UNLOAD, USE

**Total: 118 Commands**

---

## Appendix B: Quick Reference Card

### Common Patterns

```geoscript
# Image enhancement
FILTER type=median size=3 |> NORMALIZE |> BRIGHTNESS_CONTRAST contrast=1.2

# Segmentation
GRAYSCALE |> NORMALIZE |> BINARIZE threshold=auto

# Edge detection
GRAYSCALE |> FILTER type=gaussian size=3 |> FILTER type=sobel

# Table analysis
SELECT WHERE 'Value' > 100 |> SORTBY 'Value' DESC |> TAKE 10

# Aggregation
GROUPBY 'Category' AGGREGATE COUNT('ID') AS 'Count', AVG('Value') AS 'Mean'

# GIS buffer
BUFFER 100 |> DISSOLVE 'Type' |> CALCULATE 'AreaHectares' = AREA / 10000

# Water chemistry
EQUILIBRATE |> SATURATION MINERALS 'Calcite', 'Dolomite'

# Seismic processing
SEIS_FILTER type=bandpass low=10 high=80 |> SEIS_AGC window=500
```

---

## Appendix C: Glossary

**AST** - Abstract Syntax Tree, the internal representation of parsed code

**Dataset** - Any data structure loaded into Geoscientist's Toolkit

**DSL** - Domain-Specific Language

**NCalc** - Expression evaluation library used for calculations

**Non-destructive** - Operations that preserve original data

**Pipeline** - Chain of operations connected by `|>` operator

**REPL** - Read-Eval-Print-Loop, interactive command interface

**Saturation Index** - Log(Q/K), measure of mineral supersaturation

---

## License and Copyright

GEOSCRIPT Language Manual
Copyright (c) 2026 The Geoscientist's Toolkit Project

This documentation is part of the Geoscientist's Toolkit application, licensed under the MIT License.

---

## Version History

**Version 1.0** (2026)
- Complete language specification
- Comprehensive command reference
- Detailed examples and workflows
- Best practices guide
- Full API documentation

---

For any help or information please contact the author.

**End of GEOSCRIPT Language Manual**
