# GeoScript Language Reference Manual

## Table of Contents

1. [Introduction](#introduction)
2. [Getting Started](#getting-started)
3. [Language Syntax](#language-syntax)
4. [Dataset Types](#dataset-types)
5. [Command Reference](#command-reference)
6. [Examples and Workflows](#examples-and-workflows)
7. [Best Practices](#best-practices)
8. [Advanced Topics](#advanced-topics)

---

## Introduction

### What is GeoScript?

GeoScript is a domain-specific scripting language designed for geoscientific data analysis and processing within the GeoscientistToolkit application. It provides a unified interface for manipulating diverse geoscientific datasets including images, CT scans, seismic data, GIS layers, tables, pore networks, and more.

### Key Features

- **Pipeline-oriented syntax** using the `|>` operator for operation chaining
- **Dataset-centric operations** that work seamlessly across different data types
- **Non-destructive processing** - original datasets are preserved
- **Type-aware command dispatch** - operations automatically adapt to dataset types
- **Integrated scientific computing** - thermodynamics, image processing, GIS analysis in one language
- **REPL and script support** - interactive terminal and script file execution

### Design Philosophy

GeoScript follows a functional programming paradigm where:
- Each operation takes a dataset and produces a new dataset
- Operations can be chained together to create complex workflows
- All transformations are explicit and reproducible
- The system automatically handles type checking and compatibility

---

## Getting Started

### Opening the GeoScript Editor

There are two ways to work with GeoScript:

**1. GeoScript Editor (Script Files)**
- Navigate to **File → GeoScript Editor...**
- Select a dataset from the dropdown
- Write your script
- Click **Run Script** to execute

**2. GeoScript Terminal (Interactive REPL)**
- Navigate to **Tools → GeoScript Terminal...**
- Type commands interactively
- Use Arrow Up/Down for command history
- Use Tab for autocompletion

### Your First GeoScript Command

```geoscript
# Display information about the current dataset
INFO
```

### Basic Pipeline

```geoscript
# Apply a Gaussian filter to an image
FILTER type=gaussian size=5
```

### Chained Operations

```geoscript
# Multi-step image processing pipeline
FILTER type=median size=3 |> GRAYSCALE |> NORMALIZE
```

---

## Language Syntax

### Token Types

GeoScript recognizes the following tokens:

**Keywords:**
- `WITH`, `DO`, `TO`, `THEN` - Statement control keywords
- `LISTOPS`, `DISPTYPE`, `UNLOAD`, `USE` - Utility keywords

**Operators:**
- `|>` - Pipeline operator (chains operations)
- `,` - Parameter separator

**Literals:**
- `"string"` - String literals (dataset names, field names)
- `123.45` - Numeric literals (integers and floats)
- `IDENTIFIER` - Operation names and parameter names

**Comments:**
- `#` - Single-line comment
- `//` - Alternative single-line comment

### Syntax Forms

#### 1. Pipeline Syntax (Recommended)

The pipeline syntax is the preferred way to chain multiple operations:

```geoscript
# Single operation
OPERATION param1=value1 param2=value2

# Chained operations
OPERATION1 param=value |> OPERATION2 param=value |> OPERATION3
```

**Examples:**
```geoscript
GRAYSCALE |> THRESHOLD min=100 max=200 |> INVERT

FILTER type=gaussian size=5 |> BRIGHTNESS_CONTRAST brightness=10 contrast=1.2

SELECT WHERE 'Temperature' > 25 |> SORTBY 'Value' DESC |> TAKE 10
```

#### 2. WITH-DO-TO Syntax

The classic syntax for explicit dataset input/output:

```geoscript
WITH "dataset_name" DO OPERATION params TO "output_name"
```

**Multiple operations:**
```geoscript
WITH "dataset_name" DO
  OPERATION1 params TO "output1"
  THEN OPERATION2 params TO "output2"
```

**Example:**
```geoscript
WITH "my_image" DO FILTER type=gaussian size=5 TO "filtered_image"
```

#### 3. Utility Command Syntax

Special syntax for utility commands:

```geoscript
WITH "dataset_name" LISTOPS    # List available operations
WITH "dataset_name" DISPTYPE   # Display dataset type information
WITH "dataset_name" UNLOAD     # Unload dataset from memory
```

Or in pipeline mode:
```geoscript
LISTOPS
DISPTYPE
UNLOAD
INFO
USE @'OtherDataset'
```

### Parameter Syntax

#### Key-Value Parameters (Preferred)

```geoscript
FILTER type=gaussian size=5 sigma=1.5
BRIGHTNESS_CONTRAST brightness=10 contrast=1.2
THRESHOLD min=100 max=200
```

#### Positional Parameters (Legacy)

Some commands support positional parameters:

```geoscript
BRIGHTNESS_CONTRAST 128, 256    # brightness, contrast
THRESHOLD 100, 200              # min, max
```

#### Empty Parameters

Use empty values to skip optional parameters:

```geoscript
BRIGHTNESS_CONTRAST 128,        # brightness only
BRIGHTNESS_CONTRAST ,256        # contrast only
```

### String Literals

Strings must be enclosed in double quotes:

```geoscript
CALCULATE 'NewField' = 'OldField' * 2
SELECT WHERE 'Value' > 100
RENAME 'OldName' TO 'NewName'
```

### Dataset and Property References

Reference other loaded datasets using the `@'DatasetName'` syntax, or read dataset properties with dot notation. When you enter a property reference by itself, GeoScript prints the resolved value. These references are resolved from the current project context (all datasets loaded in the GeoScript editor or terminal).

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

### Expression Evaluation

Many commands support expression evaluation using NCalc syntax:

```geoscript
CALCULATE 'Area' = AREA                           # Geometry property
CALCULATE 'Ratio' = 'Field1' / 'Field2'          # Arithmetic
SELECT WHERE 'Temperature' > 25 AND 'Pressure' < 100  # Boolean logic
```

Available geometric properties:
- `AREA` - Feature area (for polygons)
- `LENGTH` - Feature length (for lines)
- `X`, `Y` - Centroid coordinates

---

## Dataset Types

GeoScript supports the following dataset types:

| Dataset Type | Description | Example Use Cases |
|-------------|-------------|-------------------|
| **ImageDataset** | Single 2D images | Microscopy, photographs, scanned images |
| **CtImageStack** | 3D CT scan volumes | X-ray CT, micro-CT, medical imaging |
| **TableDataset** | Tabular data | CSV files, Excel spreadsheets, data tables |
| **GISDataset** | Geographic data | Shapefiles, vector/raster GIS layers |
| **SeismicDataset** | Seismic survey data | SEG-Y files, seismic traces |
| **BoreholeDataset** | Well log data | LAS files, borehole measurements |
| **PNMDataset** | Pore network models | Extracted from CT scans, network simulations |
| **Mesh3D** | 3D mesh data | OBJ, STL surface meshes |
| **AcousticVolume** | 3D acoustic data | Sonar, ultrasound volumes |
| **VideoDataset** | Video files | MP4, AVI video sequences |
| **AudioDataset** | Audio files | WAV, MP3 audio recordings |
| **TextDataset** | Text documents | TXT, log files, documents |

---

## Command Reference

### Table Operations (9 commands)

#### SELECT

Filters rows or features based on attribute or spatial conditions.

**Syntax:**
```geoscript
SELECT WHERE <attribute_condition>
SELECT <spatial_condition> @'OtherLayer'
```

**Examples:**
```geoscript
# Attribute query
SELECT WHERE 'Temperature' > 25

# Attribute query with boolean logic
SELECT WHERE 'Value' > 100 AND 'Type' = 'Active'

# Spatial query (GIS)
SELECT INTERSECTS @'PolygonLayer'
SELECT CONTAINS @'PointLayer'
SELECT WITHIN @'BoundaryLayer'
```

**Spatial operators:**
- `INTERSECTS` - Features that intersect with another layer
- `CONTAINS` - Features that contain another layer
- `WITHIN` - Features within another layer

---

#### CALCULATE

Creates a new column/attribute from an expression.

**Syntax:**
```geoscript
CALCULATE 'NewField' = <expression>
```

**Examples:**
```geoscript
# Simple calculation
CALCULATE 'DoubleValue' = 'Value' * 2

# Using built-in functions (NCalc)
CALCULATE 'TempF' = 'TempC' * 1.8 + 32

# Geometry-based calculation (GIS)
CALCULATE 'AreaKm2' = AREA / 1000000

# Complex expression
CALCULATE 'Ratio' = 'Field1' / ('Field2' + 0.001)
```

---

#### SORTBY

Sorts a table dataset by a specific column.

**Syntax:**
```geoscript
SORTBY 'ColumnName' <ASC|DESC>
```

**Examples:**
```geoscript
# Sort ascending (default)
SORTBY 'Name' ASC

# Sort descending
SORTBY 'Value' DESC
```

---

#### GROUPBY

Groups rows and calculates aggregate values.

**Syntax:**
```geoscript
GROUPBY 'GroupCol' AGGREGATE <FUNC('ValCol') AS 'Alias', ...>
```

**Aggregate Functions:**
- `COUNT` - Count rows in group
- `SUM` - Sum values
- `AVG` - Average values
- `MIN` - Minimum value
- `MAX` - Maximum value

**Examples:**
```geoscript
# Simple grouping with count
GROUPBY 'Category' AGGREGATE COUNT('ID') AS 'Count'

# Multiple aggregations
GROUPBY 'Location' AGGREGATE SUM('Volume') AS 'TotalVolume', AVG('Temperature') AS 'AvgTemp'

# Complex aggregation
GROUPBY 'Type' AGGREGATE COUNT('ID') AS 'N', AVG('Value') AS 'Mean', MAX('Value') AS 'Max'
```

---

#### RENAME

Renames a column in a table dataset.

**Syntax:**
```geoscript
RENAME 'OldName' TO 'NewName'
```

**Example:**
```geoscript
RENAME 'Temp' TO 'Temperature'
```

---

#### DROP

Removes one or more columns from a table.

**Syntax:**
```geoscript
DROP 'Column1', 'Column2', ...
```

**Examples:**
```geoscript
# Drop single column
DROP 'UnusedField'

# Drop multiple columns
DROP 'Field1', 'Field2', 'Field3'
```

---

#### TAKE

Selects the top N rows from a table.

**Syntax:**
```geoscript
TAKE <count>
```

**Example:**
```geoscript
# Get first 10 rows
TAKE 10

# Pipeline: sort then take top 5
SORTBY 'Value' DESC |> TAKE 5
```

---

#### UNIQUE

Creates a table with unique values from a specified column.

**Syntax:**
```geoscript
UNIQUE 'ColumnName'
```

**Example:**
```geoscript
UNIQUE 'Category'
```

---

#### JOIN

Merges attributes from another dataset based on a key.

**Syntax:**
```geoscript
JOIN @'OtherDataset' ON 'LeftKey' = 'RightKey'
```

**Example:**
```geoscript
# Join table to GIS layer
JOIN @'AttributeTable' ON 'ID' = 'FeatureID'
```

---

### GIS Vector Operations (4 commands)

#### BUFFER

Creates a buffer zone around vector features.

**Syntax:**
```geoscript
BUFFER <distance>
```

**Example:**
```geoscript
# Create 100-unit buffer
BUFFER 100

# Buffer then dissolve
BUFFER 50 |> DISSOLVE 'Type'
```

---

#### DISSOLVE

Merges adjacent features based on a common attribute (GIS operation).

**Syntax:**
```geoscript
DISSOLVE 'FieldName'
```

**Example:**
```geoscript
DISSOLVE 'LandUse'
```

---

#### EXPLODE

Converts multi-part features into single-part features.

**Syntax:**
```geoscript
EXPLODE
```

**Example:**
```geoscript
EXPLODE |> CALCULATE 'PartArea' = AREA
```

---

#### CLEAN

Fixes invalid geometries (e.g., self-intersections).

**Syntax:**
```geoscript
CLEAN
```

**Example:**
```geoscript
CLEAN |> BUFFER 0.1
```

---

### GIS Raster Operations (5 commands)

#### RECLASSIFY

Reclassifies raster values into new categories.

**Syntax:**
```geoscript
RECLASSIFY INTO 'NewLayer' RANGES(min-max: new_val, ...)
```

**Example:**
```geoscript
RECLASSIFY INTO 'Classes' RANGES(0-50: 1, 50-100: 2, 100-255: 3)
```

---

#### RASTER_CALCULATE

Calculates a new raster layer from an expression applied across raster layers.

**Syntax:**
```geoscript
RASTER_CALCULATE EXPR 'A + B * 2' AS 'NewLayerName'
```

**Example:**
```geoscript
RASTER_CALCULATE EXPR '(A + B) / 2' AS 'MeanElevation'
```

---

#### SLOPE

Calculates slope from a Digital Elevation Model (DEM).

**Syntax:**
```geoscript
SLOPE AS 'NewLayerName'
```

**Example:**
```geoscript
SLOPE AS 'SlopeMap'
```

---

#### ASPECT

Calculates aspect (slope direction) from a DEM.

**Syntax:**
```geoscript
ASPECT AS 'NewLayerName'
```

---

#### CONTOUR

Generates vector contour lines from a DEM raster.

**Syntax:**
```geoscript
CONTOUR INTERVAL <value> AS 'NewLayerName'
```

---

### GIS Extended Operations (8 commands)

#### GIS_ADD_LAYER
Adds a new layer to a GIS dataset.

#### GIS_REMOVE_LAYER
Removes a layer from a GIS dataset.

#### GIS_INTERSECT
Performs spatial intersection operation.

#### GIS_UNION
Performs spatial union operation.

#### GIS_CLIP
Clips features to a boundary.

#### GIS_CALCULATE_AREA
Calculates area for polygon features.

#### GIS_CALCULATE_LENGTH
Calculates length for line features.

#### GIS_REPROJECT
Changes coordinate reference system.

---

### Thermodynamics Commands (7 commands)

#### CREATE_DIAGRAM

Generates thermodynamic phase diagrams.

**Syntax:**
```geoscript
CREATE_DIAGRAM BINARY FROM '<c1>' AND '<c2>' TEMP <val> K PRES <val> BAR
CREATE_DIAGRAM TERNARY FROM '<c1>', '<c2>', '<c3>' TEMP <val> K PRES <val> BAR
CREATE_DIAGRAM PT FOR COMP('<c1>'=<m1>,...) T_RANGE(<min>-<max>) K P_RANGE(<min>-<max>) BAR
CREATE_DIAGRAM ENERGY FROM '<c1>' AND '<c2>' TEMP <val> K PRES <val> BAR
CREATE_DIAGRAM COMPOSITION FROM '<col1>', '<col2>', '<col3>'
```

**Examples:**
```geoscript
# Binary phase diagram
CREATE_DIAGRAM BINARY FROM 'NaCl' AND 'H2O' TEMP 298 K PRES 1 BAR

# Ternary diagram
CREATE_DIAGRAM TERNARY FROM 'SiO2', 'Al2O3', 'CaO' TEMP 1500 K PRES 1 BAR

# Pressure-Temperature diagram
CREATE_DIAGRAM PT FOR COMP('H2O'=1.0,'CO2'=0.1) T_RANGE(273-373) K P_RANGE(1-100) BAR
```

---

#### THERMO_SWEEP

Runs a thermodynamic sweep over temperature and pressure and outputs a table dataset.

**Syntax:**
```geoscript
THERMO_SWEEP composition="'H2O'=55.5,'CO2'=1.0" minT=273 maxT=473 minP=1 maxP=1000 grid=25 name=ThermoSweep
```

**Notes:**
- `composition` uses `'<species>'=<moles>` pairs, comma-separated.
- `grid` controls the resolution of the P–T grid.

---

#### EQUILIBRATE

Solves for aqueous speciation for each row in a table.

**Syntax:**
```geoscript
EQUILIBRATE
```

**Description:**
Calculates pH, ionic strength, and species activities for water chemistry data.
Automatically converts concentration units (mg/L, ppm, mol/L) to moles.

**Output columns:**
- `pH` - Calculated pH
- `IonicStrength_molkg` - Ionic strength
- `act_<species>` - Activity coefficients for all species

**Example:**
```geoscript
EQUILIBRATE |> SATURATION MINERALS 'Calcite', 'Dolomite'
```

---

#### SATURATION

Calculates mineral saturation indices (Log Q/K).

**Syntax:**
```geoscript
SATURATION MINERALS 'Mineral1', 'Mineral2', ...
```

**Example:**
```geoscript
SATURATION MINERALS 'Calcite', 'Dolomite', 'Gypsum', 'Halite'
```

**Output:**
Adds `SI_<mineral>` columns where:
- SI > 0: Supersaturated (mineral will precipitate)
- SI = 0: At equilibrium
- SI < 0: Undersaturated (mineral will dissolve)

---

#### BALANCE_REACTION

Generates a balanced dissolution reaction for a mineral.

**Syntax:**
```geoscript
BALANCE_REACTION 'MineralName'
```

**Example:**
```geoscript
BALANCE_REACTION 'Calcite'
```

**Output:**
Creates a table showing the balanced reaction equation and equilibrium constant.

---

#### EVAPORATE

Simulates evaporation and mineral precipitation sequence.

**Syntax:**
```geoscript
EVAPORATE UPTO <factor>x STEPS <count> MINERALS 'Mineral1', 'Mineral2', ...
```

**Example:**
```geoscript
# Simulate 10x evaporation in 50 steps
EVAPORATE UPTO 10x STEPS 50 MINERALS 'Halite', 'Gypsum', 'Calcite'
```

**Output:**
Table showing evaporation factor, remaining volume, pH, ionic strength, and precipitated mineral amounts.

---

#### REACT

Calculates equilibrium products of reactants at given T and P.

**Syntax:**
```geoscript
REACT <Reactants> [TEMP <val> C|K] [PRES <val> BAR|ATM]
```

**Examples:**
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

**Output:**
Table showing products organized by phase (solid, aqueous, gas) with moles, mass, and mole fractions.

---

#### SPECIATE

Shows the dissociation and speciation products when compounds dissolve in water. For gases, uses Henry's Law to calculate dissolution based on pressure.

**Syntax:**
```geoscript
SPECIATE <Compounds> [TEMP <val> C|K] [PRES <val> BAR|ATM]
```

**Examples:**
```geoscript
# Dissolve salt in water
SPECIATE H2O + NaCl

# Dissolve multiple compounds
SPECIATE H2O + NaCl + CaCl2

# Dissolve gas at specific pressure (uses Henry's Law)
SPECIATE H2O + CO2 TEMP 25 C PRES 2 BAR

# Dissolve gas at high pressure
SPECIATE H2O + O2 TEMP 300 K PRES 10 ATM
```

**Note:**
- For **gases**, the command uses Henry's Law (C = K_H × P) to calculate dissolved concentration
- For **salts/minerals**, assumes 1 mole unless it's water (solvent)
- Chemical formulas work with plain numbers (H2O, CO2, etc.) or subscripts (H₂O, CO₂)

**Output:**
Table showing all dissolved species with moles, concentration (M), and activity

---

#### DIAGNOSE_SPECIATE

Runs the `SPECIATE` workflow but prints **every intermediate step** to the console so you can troubleshoot missing products or parsing issues.

**Syntax:**
```geoscript
DIAGNOSE_SPECIATE <Compounds> [TEMP <val> C|K] [PRES <val> BAR|ATM]
```

**When to use it:**
- The regular `SPECIATE` command returns no rows or unexpected values.
- You need to verify which dissociation reactions, moles, or solver inputs are being created.
- You want to confirm temperature/pressure parsing and water initialization.

**Output:**
- Same result table as `SPECIATE`.
- Additional console trace showing parsing, normalization, dissociation products, and solver status.

---

### PhysicoChem Commands (2 commands)

#### PHYSICOCHEM_SWEEP

Adds a parameter sweep curve that is applied during PhysicoChem simulations.

**Syntax:**
```geoscript
PHYSICOCHEM_SWEEP name=Temperature target=Domains[0].InitialConditions.Temperature min=273 max=373 mode=Temporal
```

---

#### RUN_SIMULATION

Runs a PhysicoChem reactor simulation on the current dataset.

---

### 2D Geomechanics Commands (2 commands)

#### GEOMECH_SWEEP

Configures a parameter sweep for 2D geomechanics simulations.

**Syntax:**
```geoscript
GEOMECH_SWEEP name=LoadFactor target=LoadFactor min=0.5 max=1.5 mode=Step
```

---

#### GEOMECH_RUN

Runs a 2D geomechanical simulation for a TwoDGeology dataset.

**Syntax:**
```geoscript
GEOMECH_RUN analysis=quasistatic steps=10 solver=PCG
```

---

### Image Processing Commands (7 commands)

#### BRIGHTNESS_CONTRAST

Adjust brightness and contrast of an image.

**Syntax:**
```geoscript
BRIGHTNESS_CONTRAST brightness=<-100 to 100> contrast=<0.1 to 3.0>
```

**Parameters:**
- `brightness`: Brightness offset (-100 to +100)
- `contrast`: Contrast multiplier (0.1 to 3.0, where 1.0 = no change)

**Examples:**
```geoscript
# Increase brightness
BRIGHTNESS_CONTRAST brightness=20

# Increase contrast
BRIGHTNESS_CONTRAST contrast=1.5

# Adjust both
BRIGHTNESS_CONTRAST brightness=10 contrast=1.2
```

---

#### FILTER

Apply various image filters.

**Syntax:**
```geoscript
FILTER type=<filterType> [size=<kernelSize>] [sigma=<value>]
```

**Filter Types:**
- `gaussian` - Gaussian blur (smooth averaging)
- `median` - Median filter (noise reduction, preserves edges)
- `mean` / `box` - Box filter (simple averaging)
- `sobel` - Sobel edge detection

**Parameters:**
- `type`: Filter type (required)
- `size`: Kernel size in pixels (default: 5)
- `sigma`: Sigma value for Gaussian (optional)

**Examples:**
```geoscript
# Denoise with median filter
FILTER type=median size=3

# Gaussian blur
FILTER type=gaussian size=7

# Edge detection
FILTER type=sobel

# Chained filtering
FILTER type=median size=3 |> FILTER type=gaussian size=5
```

---

#### THRESHOLD

Apply threshold segmentation to create a binary mask.

**Syntax:**
```geoscript
THRESHOLD min=<0-255> max=<0-255>
```

**Parameters:**
- `min`: Minimum threshold value (0-255)
- `max`: Maximum threshold value (0-255)

**Examples:**
```geoscript
# Threshold mid-range values
THRESHOLD min=100 max=200

# Segment bright regions
THRESHOLD min=180 max=255

# Pipeline with preprocessing
GRAYSCALE |> THRESHOLD min=128 max=255
```

---

#### BINARIZE

Convert image to binary (black/white) using a threshold.

**Syntax:**
```geoscript
BINARIZE threshold=<0-255 or 'auto'>
```

**Parameters:**
- `threshold`: Threshold value (0-255) or 'auto' for Otsu's automatic thresholding

**Examples:**
```geoscript
# Manual threshold
BINARIZE threshold=128

# Automatic Otsu thresholding
BINARIZE threshold=auto

# Pipeline with preprocessing
FILTER type=gaussian size=3 |> BINARIZE threshold=auto
```

---

#### GRAYSCALE

Convert image to grayscale.

**Syntax:**
```geoscript
GRAYSCALE
```

**Example:**
```geoscript
# Convert to grayscale
GRAYSCALE

# Grayscale then process
GRAYSCALE |> THRESHOLD min=100 max=200
```

---

#### INVERT

Invert image colors (create negative).

**Syntax:**
```geoscript
INVERT
```

**Examples:**
```geoscript
# Invert colors
INVERT

# Create negative of binary mask
BINARIZE threshold=128 |> INVERT
```

---

#### NORMALIZE

Normalize image to full intensity range (0-255).

**Syntax:**
```geoscript
NORMALIZE
```

**Description:**
Stretches the histogram to use the full dynamic range.

**Example:**
```geoscript
# Enhance low-contrast image
NORMALIZE

# Pipeline with normalization
FILTER type=median size=3 |> NORMALIZE
```

---

### CT Image Stack Commands (12 commands)

#### CT_SEGMENT
3D segmentation of CT volumes.

**Syntax:**
```geoscript
CT_SEGMENT method=<threshold|otsu|watershed> [min=<value>] [max=<value>] material=<id>
```

#### CT_FILTER3D
Apply 3D filters to CT stacks.

**Syntax:**
```geoscript
CT_FILTER3D type=<gaussian|median|mean|nlm|bilateral> [size=<value>]
```

#### CT_ADD_MATERIAL
Define material properties for segmented phases.

**Syntax:**
```geoscript
CT_ADD_MATERIAL name='<name>' color=<r,g,b>
```

#### CT_REMOVE_MATERIAL
Remove material definitions.

**Syntax:**
```geoscript
CT_REMOVE_MATERIAL id=<materialId>
```

#### CT_ANALYZE_POROSITY
Calculate porosity from segmented volumes.

**Syntax:**
```geoscript
CT_ANALYZE_POROSITY void_material=<materialId>
```

#### CT_CROP
Crop a sub-volume from the CT stack.

#### CT_EXTRACT_SLICE
Extract a 2D slice from the 3D volume.

#### CT_LABEL_ANALYSIS
Analyze connected components in labeled volumes.

#### SIMULATE_ACOUSTIC
Run acoustic wave simulation on the current CT dataset.

**Syntax:**
```geoscript
SIMULATE_ACOUSTIC [materials=1,2] [tx=0.1,0.5,0.5] [rx=0.9,0.5,0.5] [time_steps=1000] [use_gpu=true]
```

#### SIMULATE_NMR
Run random-walk NMR simulation on the current CT dataset.

**Syntax:**
```geoscript
SIMULATE_NMR [pore_material_id=1] [steps=1000] [timestep_ms=0.01] [use_opencl=false]
```

#### SIMULATE_THERMAL_CONDUCTIVITY
Run steady-state thermal conductivity simulation on the current CT dataset.

**Syntax:**
```geoscript
SIMULATE_THERMAL_CONDUCTIVITY [direction=z] [temperature_hot=373.15] [temperature_cold=293.15]
```

#### SIMULATE_GEOMECH
Run geomechanical stress/strain simulation on the current CT dataset.

**Syntax:**
```geoscript
SIMULATE_GEOMECH [sigma1=100] [sigma2=50] [sigma3=20] [use_gpu=true] [porosity=examplepnmdataset.porosity]
```

**Tip:** parameter values can reference dataset fields:
```geoscript
SIMULATE_GEOMECH porosity=examplepnmdataset.porosity sigma1=120 sigma2=60 sigma3=40
```

---

### Pore Network Model (PNM) Commands (7 commands)

#### PNM_FILTER_PORES

Filter pores based on geometric criteria.

**Syntax:**
```geoscript
PNM_FILTER_PORES [min_radius=<value>] [max_radius=<value>] [min_coord=<value>]
```

**Parameters:**
- `min_radius`: Minimum pore radius (micrometers)
- `max_radius`: Maximum pore radius (micrometers)
- `min_coord`: Minimum coordination number

**Example:**
```geoscript
PNM_FILTER_PORES min_radius=1.0 max_radius=100.0 min_coord=2
```

---

#### PNM_FILTER_THROATS

Filter throats based on geometric criteria.

**Syntax:**
```geoscript
PNM_FILTER_THROATS [min_radius=<value>] [max_radius=<value>] [max_length=<value>]
```

**Example:**
```geoscript
PNM_FILTER_THROATS min_radius=0.5 max_length=50.0
```

---

#### PNM_CALCULATE_PERMEABILITY

Calculate absolute permeability using network simulation.

**Syntax:**
```geoscript
PNM_CALCULATE_PERMEABILITY direction=<x|y|z|all>
```

**Example:**
```geoscript
PNM_CALCULATE_PERMEABILITY direction=x
```

---

#### PNM_DRAINAGE_SIMULATION

Run drainage capillary pressure simulation.

**Syntax:**
```geoscript
PNM_DRAINAGE_SIMULATION [contact_angle=<degrees>] [interfacial_tension=<N/m>]
```

**Example:**
```geoscript
PNM_DRAINAGE_SIMULATION contact_angle=30 interfacial_tension=0.03
```

---

#### PNM_IMBIBITION_SIMULATION

Run imbibition capillary pressure simulation.

**Syntax:**
```geoscript
PNM_IMBIBITION_SIMULATION [contact_angle=<degrees>] [interfacial_tension=<N/m>]
```

**Example:**
```geoscript
PNM_IMBIBITION_SIMULATION contact_angle=60 interfacial_tension=0.03
```

---

#### PNM_EXTRACT_LARGEST_CLUSTER

Extract the largest connected cluster of pores.

**Syntax:**
```geoscript
PNM_EXTRACT_LARGEST_CLUSTER
```

---

#### PNM_STATISTICS

Calculate comprehensive network statistics.

**Syntax:**
```geoscript
PNM_STATISTICS
```

**Output:**
- Total pores and throats
- Average coordination number
- Pore and throat size distributions
- Network porosity

---

### PNM Reactive Transport Commands (4 commands)

#### RUN_PNM_REACTIVE_TRANSPORT

Execute reactive transport simulation on pore network.

**Syntax:**
```geoscript
RUN_PNM_REACTIVE_TRANSPORT <time> <timestep> <temp> <inlet_P> <outlet_P>
```

**Example:**
```geoscript
RUN_PNM_REACTIVE_TRANSPORT 3600 1.0 298.15 2.0 0.0
```

---

#### SET_PNM_SPECIES

Set chemical species concentrations for reactive transport.

**Syntax:**
```geoscript
SET_PNM_SPECIES <species> <inlet_conc> <initial_conc>
```

**Example:**
```geoscript
SET_PNM_SPECIES Ca2+ 0.01 0.005
```

---

#### SET_PNM_MINERALS

Set initial mineral content in pores.

**Syntax:**
```geoscript
SET_PNM_MINERALS <mineral> <volume_fraction>
```

**Example:**
```geoscript
SET_PNM_MINERALS Calcite 0.05
```

---

#### EXPORT_PNM_RESULTS

Export reactive transport results.

**Syntax:**
```geoscript
EXPORT_PNM_RESULTS <directory_path>
```

---

### Seismic Data Commands (7 commands)

#### SEIS_FILTER

Apply frequency filters to seismic data.

**Syntax:**
```geoscript
SEIS_FILTER type=<bandpass|lowpass|highpass|fxdecon> [low=<Hz>] [high=<Hz>]
```

**Examples:**
```geoscript
# Bandpass filter
SEIS_FILTER type=bandpass low=10 high=80

# Lowpass filter
SEIS_FILTER type=lowpass high=60

# Highpass filter
SEIS_FILTER type=highpass low=15
```

---

#### SEIS_AGC

Apply automatic gain control to balance amplitudes.

**Syntax:**
```geoscript
SEIS_AGC window=<milliseconds>
```

**Example:**
```geoscript
SEIS_AGC window=500
```

---

#### SEIS_VELOCITY_ANALYSIS

Perform velocity analysis for NMO correction.

**Syntax:**
```geoscript
SEIS_VELOCITY_ANALYSIS method=<semblance|cvs|cmp>
```

**Example:**
```geoscript
SEIS_VELOCITY_ANALYSIS method=semblance
```

---

#### SEIS_NMO_CORRECTION

Apply normal moveout correction.

**Syntax:**
```geoscript
SEIS_NMO_CORRECTION [velocity_file=<path>] [velocity=<constant>]
```

**Example:**
```geoscript
SEIS_NMO_CORRECTION velocity=2000
```

---

#### SEIS_STACK

Stack seismic traces.

**Syntax:**
```geoscript
SEIS_STACK method=<mean|median|weighted>
```

**Example:**
```geoscript
SEIS_STACK method=mean
```

---

#### SEIS_MIGRATION

Perform seismic migration.

**Syntax:**
```geoscript
SEIS_MIGRATION method=<kirchhoff|fk|rtm> [aperture=<meters>]
```

**Example:**
```geoscript
SEIS_MIGRATION method=kirchhoff aperture=1000
```

---

#### SEIS_PICK_HORIZON

Pick seismic horizons.

**Syntax:**
```geoscript
SEIS_PICK_HORIZON name=<horizonName> method=<auto|manual|tracking>
```

**Example:**
```geoscript
SEIS_PICK_HORIZON name=Top_Reservoir method=auto
```

---

### Seismic Cube Commands (10 commands)

#### CUBE_CREATE

Create a seismic cube dataset.

**Syntax:**
```geoscript
CUBE_CREATE name="Survey_Cube" [survey="Field_2024"] [project="Project_X"]
```

---

#### CUBE_ADD_LINE

Add a seismic line to a cube with geometry or header-derived coordinates.

**Syntax:**
```geoscript
CUBE_ADD_LINE cube="Survey_Cube" line="Line_001"
  start_x=0 start_y=0 end_x=5000 end_y=0 trace_spacing=25

CUBE_ADD_LINE cube="Survey_Cube" line="Line_002" use_headers=true
```

---

#### CUBE_ADD_PERPENDICULAR

Add a perpendicular line at a trace index of an existing line.

**Syntax:**
```geoscript
CUBE_ADD_PERPENDICULAR cube="Survey_Cube" base="Line_001" trace=100 line="Crossline_001"
```

---

#### CUBE_DETECT_INTERSECTIONS

Detect intersections between cube lines.

**Syntax:**
```geoscript
CUBE_DETECT_INTERSECTIONS cube="Survey_Cube"
```

---

#### CUBE_SET_NORMALIZATION

Configure line normalization settings.

**Syntax:**
```geoscript
CUBE_SET_NORMALIZATION cube="Survey_Cube"
  normalize_amplitude=true amplitude_method=balanced
  match_frequency=true frequency_low=10 frequency_high=80
  match_phase=true smooth_transitions=true
  transition_traces=5 window_traces=10 window_ms=500
```

---

#### CUBE_NORMALIZE

Apply normalization at intersections.

**Syntax:**
```geoscript
CUBE_NORMALIZE cube="Survey_Cube"
```

---

#### CUBE_BUILD_VOLUME

Build the regularized 3D volume.

**Syntax:**
```geoscript
CUBE_BUILD_VOLUME cube="Survey_Cube"
  inline_count=200 crossline_count=200 sample_count=1500
  inline_spacing=25 crossline_spacing=25 sample_interval=4
```

---

#### CUBE_EXPORT_GIS

Export the cube as a Subsurface GIS dataset.

**Syntax:**
```geoscript
CUBE_EXPORT_GIS cube="Survey_Cube" output="Subsurface_Model"
```

---

#### CUBE_EXPORT_SLICE

Export a time slice as a GIS raster dataset.

**Syntax:**
```geoscript
CUBE_EXPORT_SLICE cube="Survey_Cube" time=1500 output="Slice_1500ms"
```

---

#### CUBE_STATISTICS

Generate a summary table for the cube.

**Syntax:**
```geoscript
CUBE_STATISTICS cube="Survey_Cube" output="Cube_Stats"
```

---

### Borehole Data Commands (7 commands)

#### BH_ADD_LITHOLOGY
Add lithology interval to borehole.

#### BH_REMOVE_LITHOLOGY
Remove lithology from borehole.

#### BH_ADD_LOG
Add a well log curve.

**Syntax:**
```geoscript
BH_ADD_LOG type=<logType> name=<displayName> [unit=<unit>] [min=<value>] [max=<value>] [log=<true|false>] [color=<r,g,b[,a]>]
```

**Notes:**
- `min`/`max` set display ranges for the track.
- `log=true` enables logarithmic scaling.
- `color` uses 0-255 RGBA values.

**Example:**
```geoscript
BH_ADD_LOG type=GR name=GammaRay unit=API min=0 max=150 color=255,200,80
```

#### BH_CALCULATE_POROSITY
Calculate porosity from density or sonic logs.

#### BH_CALCULATE_SATURATION
Calculate fluid saturation using Archie's equation.

#### BH_DEPTH_SHIFT
Apply depth shift to align logs.

#### BH_CORRELATION
Correlate lithology between boreholes.

---

### Miscellaneous Dataset Commands (12 commands)

#### ACOUSTIC_THRESHOLD
Threshold acoustic volumes.

#### ACOUSTIC_EXTRACT_TARGETS
Extract acoustic targets from volume.

#### MESH_SMOOTH
Smooth 3D mesh surfaces.

#### MESH_DECIMATE
Reduce mesh complexity.

#### MESH_REPAIR
Repair mesh topology issues.

#### MESH_CALCULATE_VOLUME
Calculate enclosed volume of mesh.

#### VIDEO_EXTRACT_FRAME
Extract frame from video.

#### VIDEO_STABILIZE
Stabilize video footage.

#### AUDIO_TRIM
Trim audio clip.

#### AUDIO_NORMALIZE
Normalize audio levels.

#### TEXT_SEARCH
Search within text datasets.

#### TEXT_REPLACE
Replace text patterns.

#### TEXT_STATISTICS
Calculate text statistics.

---

### Utility Commands (8 commands)

#### LOAD

Load a dataset from disk.

**Syntax:**
```geoscript
LOAD "path/to/file" [AS "DatasetName"] [TYPE=<DatasetType>] [PIXELSIZE=<value>] [UNIT="um|mm"]
```

**Description:**
Loads a dataset using the appropriate loader. Use `TYPE` to disambiguate formats and `PIXELSIZE`/`UNIT`
to override image/CT spacing metadata.

---

#### USE

Switch the active dataset to another loaded dataset by name.

**Syntax:**
```geoscript
USE @'DatasetName'
```

**Description:**
Pulls a dataset from the current project context so it can be used in the pipeline.

---

#### SAVE

Save the current dataset to disk.

**Syntax:**
```geoscript
SAVE "path/to/file" [FORMAT="fmt"]
```

**Description:**
Exports datasets using format-specific writers (e.g., CSV, LAS, BHB, PNG, GeoTIFF).

---

#### SET_PIXEL_SIZE

Update pixel size metadata for image or CT datasets.

**Syntax:**
```geoscript
SET_PIXEL_SIZE value=<size> [UNIT="um|mm"]
```

---

#### LISTOPS

List all available operations for the current dataset type.

**Syntax:**
```geoscript
LISTOPS
```

**Description:**
Displays a list of all commands that can be applied to the current dataset.

---

#### DISPTYPE

Display detailed dataset type information.

**Syntax:**
```geoscript
DISPTYPE
```

**Description:**
Shows the dataset type, supported operations, and metadata.

---

#### INFO

Show dataset summary information.

**Syntax:**
```geoscript
INFO
```

**Description:**
Quick summary of dataset properties (dimensions, size, type, etc.)

---

#### UNLOAD

Unload dataset from memory to free resources.

**Syntax:**
```geoscript
UNLOAD
```

**Description:**
Removes dataset from memory while keeping it in the project.

---

## Examples and Workflows

### Image Processing Workflows

#### Basic Image Enhancement

```geoscript
# Denoise and enhance a noisy image
FILTER type=median size=3 |>
  BRIGHTNESS_CONTRAST brightness=10 contrast=1.3 |>
  NORMALIZE
```

#### Edge Detection Pipeline

```geoscript
# Prepare image for edge analysis
GRAYSCALE |>
  FILTER type=gaussian size=3 |>
  FILTER type=sobel
```

#### Complete Segmentation Workflow

```geoscript
# Full segmentation pipeline
FILTER type=median size=3 |>
  GRAYSCALE |>
  NORMALIZE |>
  BINARIZE threshold=auto |>
  INFO
```

### Table Data Analysis

#### Data Filtering and Aggregation

```geoscript
# Filter, aggregate, and sort data
SELECT WHERE 'Temperature' > 25 |>
  CALCULATE 'TempF' = 'Temperature' * 1.8 + 32 |>
  GROUPBY 'Location' AGGREGATE AVG('TempF') AS 'AvgTempF', COUNT('ID') AS 'Count' |>
  SORTBY 'AvgTempF' DESC |>
  TAKE 10
```

#### Statistical Summary

```geoscript
# Create summary statistics by category
GROUPBY 'Category' AGGREGATE
  COUNT('ID') AS 'N',
  SUM('Volume') AS 'TotalVol',
  AVG('Concentration') AS 'MeanConc',
  MIN('Value') AS 'Min',
  MAX('Value') AS 'Max'
```

### GIS Spatial Analysis

#### Buffer and Dissolve

```geoscript
# Create 100m buffer zones and dissolve by type
BUFFER 100 |>
  DISSOLVE 'LandUseType' |>
  GIS_CALCULATE_AREA
```

#### Spatial Query and Calculation

```geoscript
# Find features in polygon and calculate properties
SELECT WITHIN @'StudyArea' |>
  CALCULATE 'AreaHectares' = AREA / 10000 |>
  SORTBY 'AreaHectares' DESC
```

### Thermodynamic Modeling

#### Water Chemistry Analysis

```geoscript
# Complete water chemistry workflow
EQUILIBRATE |>
  SATURATION MINERALS 'Calcite', 'Dolomite', 'Gypsum', 'Halite'
```

#### Evaporation Sequence

```geoscript
# Simulate seawater evaporation
EVAPORATE UPTO 100x STEPS 100 MINERALS
  'Halite', 'Gypsum', 'Calcite', 'Dolomite'
```

#### Chemical Reaction

```geoscript
# Simulate mineral dissolution
REACT CaCO3 + HCl + H2O TEMP 25 C PRES 1 BAR
```

### Seismic Data Processing

#### Standard Processing Flow

```geoscript
# Complete seismic processing pipeline
SEIS_FILTER type=bandpass low=10 high=80 |>
  SEIS_AGC window=500 |>
  SEIS_NMO_CORRECTION velocity=2000 |>
  SEIS_STACK method=mean |>
  SEIS_MIGRATION method=kirchhoff aperture=1000
```

### Pore Network Analysis

#### Network Characterization

```geoscript
# Clean and analyze pore network
PNM_EXTRACT_LARGEST_CLUSTER |>
  PNM_FILTER_PORES min_radius=1.0 min_coord=2 |>
  PNM_STATISTICS
```

#### Capillary Pressure Curve

```geoscript
# Generate drainage and imbibition curves
PNM_DRAINAGE_SIMULATION contact_angle=30 interfacial_tension=0.03
```

#### Reactive Transport

```geoscript
# Complete reactive transport workflow
SET_PNM_SPECIES Ca2+ 0.01 0.005 |>
  SET_PNM_SPECIES CO3^2- 0.01 0.005 |>
  SET_PNM_MINERALS Calcite 0.05 |>
  RUN_PNM_REACTIVE_TRANSPORT 3600 1.0 298.15 2.0 0.0 |>
  EXPORT_PNM_RESULTS ./results
```

---

## Best Practices

### General Principles

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

### Image Processing

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

### Table Operations

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

### Thermodynamics

1. **Equilibrate before saturation calculations**
   ```geoscript
   EQUILIBRATE |> SATURATION MINERALS 'Calcite', 'Gypsum'
   ```

2. **Check units in column headers**
   - System auto-converts: mg/L, ppm, ug/L, mol/L, etc.
   - Format: "Ca (mg/L)" or "Na [ppm]"

3. **Verify mass balance**
   - Use INFO to check element totals

### Performance

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

## Advanced Topics

### Error Handling

GeoScript provides clear error messages:

- **Type mismatch:** Operation not supported for dataset type
- **Invalid parameters:** Parameter out of range or incorrect format
- **Missing datasets:** Referenced dataset not found (e.g., in JOIN)
- **Parse errors:** Syntax errors in command

Check the output log for detailed error information.

### Pipeline Execution

Pipelines are executed left-to-right:
```geoscript
A |> B |> C
# Equivalent to: C(B(A(input)))
```

Each operation receives the output of the previous operation as input.

### Dataset Naming

Output datasets are automatically named:
- Pattern: `<OriginalName>_<OperationName>`
- Example: `MyImage_filtered`, `MyTable_Grouped`

All outputs are automatically added to the project.

### Memory Management

- All operations create new datasets (non-destructive)
- Original datasets are never modified
- Use UNLOAD to free memory when datasets are no longer needed
- Large pipelines may consume significant memory

### Expression Evaluation

GeoScript uses NCalc for expression evaluation, supporting:
- Arithmetic: `+`, `-`, `*`, `/`, `%`, `^`
- Comparisons: `>`, `<`, `>=`, `<=`, `=`, `!=`
- Boolean: `AND`, `OR`, `NOT`
- Functions: `Abs()`, `Sqrt()`, `Pow()`, `Min()`, `Max()`, etc.

### Unit Conversion (Thermodynamics)

The system automatically converts common units:

| Unit | Type | Conversion |
|------|------|-----------|
| mg/L, ppm | Mass | → mol/L using molar mass |
| ug/L, ppb | Mass | → mol/L using molar mass |
| g/L | Mass | → mol/L using molar mass |
| mol/L, M | Molar | No conversion |
| mmol/L | Molar | ÷ 1000 |
| umol/L | Molar | ÷ 1,000,000 |

---

## Appendix

### Complete Command List

**Table Operations (9):**
SELECT, CALCULATE, SORTBY, GROUPBY, RENAME, DROP, TAKE, UNIQUE, JOIN

**GIS Vector (4):**
BUFFER, DISSOLVE, EXPLODE, CLEAN

**GIS Raster (5):**
RECLASSIFY, RASTER_CALCULATE, SLOPE, ASPECT, CONTOUR

**GIS Extended (8):**
GIS_ADD_LAYER, GIS_REMOVE_LAYER, GIS_INTERSECT, GIS_UNION, GIS_CLIP, GIS_CALCULATE_AREA, GIS_CALCULATE_LENGTH, GIS_REPROJECT

**Thermodynamics (6):**
CREATE_DIAGRAM, EQUILIBRATE, SATURATION, BALANCE_REACTION, EVAPORATE, REACT

**Image Processing (7):**
BRIGHTNESS_CONTRAST, FILTER, THRESHOLD, BINARIZE, GRAYSCALE, INVERT, NORMALIZE

**CT Image Stack (12):**
CT_SEGMENT, CT_FILTER3D, CT_ADD_MATERIAL, CT_REMOVE_MATERIAL, CT_ANALYZE_POROSITY, CT_CROP, CT_EXTRACT_SLICE, CT_LABEL_ANALYSIS, SIMULATE_ACOUSTIC, SIMULATE_NMR, SIMULATE_THERMAL_CONDUCTIVITY, SIMULATE_GEOMECH

**PNM (7):**
PNM_FILTER_PORES, PNM_FILTER_THROATS, PNM_CALCULATE_PERMEABILITY, PNM_DRAINAGE_SIMULATION, PNM_IMBIBITION_SIMULATION, PNM_EXTRACT_LARGEST_CLUSTER, PNM_STATISTICS

**PNM Reactive Transport (4):**
RUN_PNM_REACTIVE_TRANSPORT, SET_PNM_SPECIES, SET_PNM_MINERALS, EXPORT_PNM_RESULTS

**Seismic (7):**
SEIS_FILTER, SEIS_AGC, SEIS_VELOCITY_ANALYSIS, SEIS_NMO_CORRECTION, SEIS_STACK, SEIS_MIGRATION, SEIS_PICK_HORIZON

**Seismic Cube (10):**
CUBE_CREATE, CUBE_ADD_LINE, CUBE_ADD_PERPENDICULAR, CUBE_DETECT_INTERSECTIONS, CUBE_SET_NORMALIZATION, CUBE_NORMALIZE, CUBE_BUILD_VOLUME, CUBE_EXPORT_GIS, CUBE_EXPORT_SLICE, CUBE_STATISTICS

**Borehole (7):**
BH_ADD_LITHOLOGY, BH_REMOVE_LITHOLOGY, BH_ADD_LOG, BH_CALCULATE_POROSITY, BH_CALCULATE_SATURATION, BH_DEPTH_SHIFT, BH_CORRELATION

**Miscellaneous (12):**
ACOUSTIC_THRESHOLD, ACOUSTIC_EXTRACT_TARGETS, MESH_SMOOTH, MESH_DECIMATE, MESH_REPAIR, MESH_CALCULATE_VOLUME, VIDEO_EXTRACT_FRAME, VIDEO_STABILIZE, AUDIO_TRIM, AUDIO_NORMALIZE, TEXT_SEARCH, TEXT_REPLACE, TEXT_STATISTICS

**Utility (7):**
LOAD, USE, SAVE, SET_PIXEL_SIZE, LISTOPS, DISPTYPE, INFO, UNLOAD

**Total: 98+ Commands**

---

## Quick Reference Card

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
BUFFER 100 |> DISSOLVE 'Type' |> GIS_CALCULATE_AREA

# Water chemistry
EQUILIBRATE |> SATURATION MINERALS 'Calcite', 'Dolomite'

# Seismic processing
SEIS_FILTER type=bandpass low=10 high=80 |> SEIS_AGC window=500
```

---

## Version History

**Version 1.0** - Initial comprehensive manual
- Complete command reference for all 85+ commands
- Detailed syntax documentation
- Extensive examples and workflows
- Best practices guide

---

## License and Attribution

GeoscientistToolkit GeoScript Language
Copyright (c) 2026 GeoscientistToolkit Project

---

*End of GeoScript Language Reference Manual*
