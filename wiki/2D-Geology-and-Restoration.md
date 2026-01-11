# 2D Geology & Restoration

Work with 2D geologic cross-sections: draft formations and faults, edit topography, and perform structural restoration with real-time overlays.

---

## Overview

The **2D Geology** workflow is centered on `.2dgeo` datasets that store a cross-section profile (formations, faults, and topography). The viewer provides editing tools, measurements, and structural restoration options to help interpret pre-deformation geometry.

---

## Create or Import a 2D Geology Profile

**Create a new profile:**
1. Go to **File → New 2D Geology Profile...**
2. Choose a name and location for the `.2dgeo` file.

**Import an existing profile:**
1. Go to **File → Import → 2D Geology Profile (.2dgeo)**
2. Select the profile file to load.

---

## Core Editing Tools

When a 2D Geology dataset is selected, use the **Tools panel** to access:

- **Formation and fault drafting** (draw formations/fault traces, set thickness, color, dip, and displacement)
- **Selection and editing** (move vertices, edit boundaries, delete features)
- **Topography editing** (drag control points to reshape the surface)
- **Measurements** (distance and angle tools)
- **Geological presets** (apply fault and stratigraphy scenarios)
- **Snapping** (toggle vertex snapping for precise alignment)

The viewer supports interactive drawing with a dedicated **Interactive Drawing Tool** for freehand drafting and snapping.

### Keyboard Shortcuts

| Key | Action |
|-----|--------|
| Q | Select mode |
| W | Draw formation |
| E | Draw fault |
| T | Edit topography |
| M | Measure distance |
| DEL | Delete selected |
| ESC | Cancel operation |

---

## Geological Presets

The **Load Preset...** button opens a menu of pre-built geological scenarios. Each preset generates a complete cross-section with properly stacked formations, faults, and appropriate topography.

### Available Presets

| Preset | Description |
|--------|-------------|
| **Simple Horizontal Layers** | Horizontal sedimentary layers - ideal for testing restoration |
| **Eroded Anticline** | Anticline with erosion exposing older rocks at core |
| **Eroded Syncline** | Syncline preserving younger rocks in trough |
| **Jura Mountains Style** | Multiple folds with thrust faults (thin-skinned tectonics) |
| **Normal Faults (Graben)** | Graben structure bounded by normal faults |
| **Thrust Fault System** | Thrust system with older rocks over younger |
| **Anticline-Syncline Pair** | Complete fold train with anticline and syncline |
| **Angular Unconformity** | Tilted sequence overlain by horizontal beds |
| **Sedimentary Basin** | Basin with layers thickening toward center |
| **Channel Fill** | Incised valley with lens-shaped channel sand |

All presets include a standard stratigraphic column with geologically appropriate colors and realistic layer thicknesses. Formations are automatically validated to prevent overlaps.

---

## Structural Restoration Workflow

Structural restoration tools allow you to unfault and unfold the section to visualize pre-deformation geometry using flexural slip unfolding mechanics.

### Starting Restoration

1. In the 2D Geology tools panel, click **Restore Section...**.
2. Choose **Start Interactive Restoration** to enable a live slider.
3. Adjust the **Restoration %** slider to interpolate from the current state (0%) to fully restored (100%).

### Restoration Controls

When restoration is active, the toolbar displays:

- **Restoration Slider**: Drag from 0% (original deformed) to 100% (fully restored flat layers)
- **Quick Buttons**: 0%, 50%, 100% for common restoration levels
- **Ghost Toggle**: Show original geology as semi-transparent overlay for comparison
- **Apply**: Commit the current restoration state to the cross-section
- **Cancel**: Exit restoration mode and return to original

### Visual Feedback

- **Green indicator**: Shows "RESTORED: XX%" on the viewport
- **Ghost overlay**: Original formations shown in semi-transparent when Ghost is enabled
- **Restoration progress bar**: Displayed in the Layers panel

### Restoration Methods

The restoration algorithm uses:

- **Flexural slip unfolding**: Preserves bed length (arc length) as the fundamental constraint
- **Pin point detection**: Uses minimum curvature points for stable restoration
- **Fault displacement**: Removes fault offsets based on fault type (normal, reverse, thrust)
- **SIMD optimization**: AVX2 and NEON acceleration for real-time performance

### Quick Actions

- **Quick Restore (100%)**: Immediately restore to completely undeformed state
- **Quick Restore (50%)**: Partially restore the section
- **Create Flat Reference**: Generate a completely flattened reference state

---

## Understanding the Restoration Display

| State | Description |
|-------|-------------|
| 0% | Original deformed geology as interpreted |
| 50% | Intermediate state - halfway to restoration |
| 100% | Fully restored - layers are horizontal/flat |

When restoration is active:
- The **restored** geology is shown in full color
- The **original** geology (when Ghost is enabled) is shown semi-transparent
- The **RESTORED** indicator shows current percentage

---

## Overlap Detection and Auto-Fix

Geological formations must never overlap in cross-sections. The system provides:

1. **Check Overlaps** button: Detects all overlapping formations
2. **Auto-Fix**: Automatically rearranges formations to eliminate overlaps while maintaining relative positions
3. **Validation**: All presets and operations are validated to prevent overlaps

---

## Integration With Slope Stability (2D)

A 2D geology profile can be converted into a 2D slope stability dataset via GeoScript:

```geoscript
SLOPE2D_FROM_GEOLOGY thickness=1.5 name="Valley Slope Analysis"
```

This generates a slope-stability dataset using the cross-section geometry, enabling rapid stability workflows tied to structural interpretations.

---

## 2D Geomechanical Simulation

The TwoDGeologyDataset includes a comprehensive 2D finite element geomechanical simulation system for analyzing stresses, strains, and failure in geological cross-sections.

### Features

- **Finite Element Analysis**: Triangle and quadrilateral elements with linear and quadratic shape functions
- **Multiple Failure Criteria**: Mohr-Coulomb, Curved Mohr-Coulomb (τ = A(σₙ + T)^B), Hoek-Brown, Drucker-Prager, Von Mises, Tresca
- **Joint Sets**: Automatic and manual discontinuity generation with Barton-Bandis shear strength
- **Engineering Primitives**: Foundations, tunnels, dams, retaining walls, indenters
- **Dynamic Analysis**: Static, quasi-static, explicit dynamic, and implicit dynamic solvers

### Accessing Geomechanics Tools

1. Select a 2D Geology dataset
2. In the Tools panel, expand **Geomechanics** section
3. Use the tool buttons to:
   - Create geometric primitives (rectangles, circles, foundations, tunnels)
   - Draw joints and joint sets
   - Apply boundary conditions (fixed, roller, prescribed displacement)
   - Apply loads (point forces, distributed pressure, gravity)
   - Run simulations and visualize results

### Tool Modes

| Tool | Description |
|------|-------------|
| **Select** | Select and transform primitives with resize/rotate handles |
| **Rectangle/Circle/Polygon** | Draw basic geometric shapes |
| **Foundation/Tunnel/Dam** | Place engineering structures |
| **Joint/Joint Set** | Create single joints or automatic joint sets |
| **Force/Pressure/Displacement** | Apply loads to nodes/edges |
| **Fix Boundary** | Constrain node degrees of freedom |
| **Probe Results** | Query stress/strain at elements |

### Snapping System

Enable snapping for precise placement:

| Snap Mode | Description | Shortcut |
|-----------|-------------|----------|
| Grid | Snap to regular grid | G |
| Node | Snap to mesh nodes | N |
| Vertex | Snap to primitive vertices | V |
| Edge | Snap to edges | - |
| Center | Snap to shape centers | - |
| Midpoint | Snap to edge midpoints | - |
| Angle | Constrain angles to increments | A |

Toggle all snapping with **S** key.

### Material Properties

Define materials with these properties:

| Property | Symbol | Description |
|----------|--------|-------------|
| Young's Modulus | E | Elastic stiffness (Pa) |
| Poisson's Ratio | ν | Lateral strain ratio |
| Density | ρ | Mass density (kg/m³) |
| Cohesion | c | Shear strength intercept (Pa) |
| Friction Angle | φ | Internal friction (degrees) |
| Tensile Strength | T | Maximum tensile stress (Pa) |
| Dilation Angle | ψ | Plastic volumetric strain ratio |

#### Curved Mohr-Coulomb Criterion

The curved envelope τ = A(σₙ + T)^B models the nonlinear strength of rocks at low confining stress:

- **A coefficient**: Controls overall strength level (typically 0.5-2.0)
- **B exponent**: Controls curvature (0.5-0.9, where 1.0 = linear)

#### Hoek-Brown Criterion

For rock masses with joints and fractures:

- **m_i**: Intact rock parameter (from 4 for mudstone to 35 for granite)
- **GSI**: Geological Strength Index (10-100)
- **D**: Disturbance factor (0 = undisturbed, 1 = heavily blasted)

### Running Simulations

1. Create mesh from primitives or generate from geology
2. Assign materials to regions
3. Apply boundary conditions
4. Apply loads
5. Click **Run Simulation**

Monitor progress with:
- Current step / total steps
- Residual norm (convergence indicator)
- Maximum displacement
- Number of plastic/failed elements

### Visualization Options

| Display Field | Description |
|---------------|-------------|
| Stress XX/YY/XY | Stress tensor components |
| Sigma 1/2 | Principal stresses |
| Von Mises | Equivalent stress |
| Displacement | Nodal displacements |
| Strain | Element strains |
| Yield Index | Proximity to failure (0-1) |

Additional options:
- **Color maps**: Jet, Viridis, Plasma, Rainbow, Grayscale
- **Deformation scale**: Exaggerate displacements for visualization
- **Show mesh**: Display element boundaries
- **Show BCs**: Display boundary condition symbols
- **Mohr Circle**: Interactive Mohr circle for selected elements

### Undo/Redo

All editing operations support undo/redo:
- **Ctrl+Z**: Undo last operation
- **Ctrl+Y** or **Ctrl+Shift+Z**: Redo

### GeoScript Commands

```geoscript
# Create mesh
GEOMECH_CREATE_MESH type=rectangular width=100 height=50 nx=20 ny=10

# Set material
GEOMECH_SET_MATERIAL name=Sandstone E=25e9 nu=0.25 cohesion=5e6 friction=35

# Add foundation
GEOMECH_ADD_PRIMITIVE type=foundation x=50 y=50 width=10 height=2

# Add joint set
GEOMECH_ADD_JOINTSET dip=45 spacing=2 friction=30

# Apply boundary conditions
GEOMECH_FIX_BOUNDARY side=bottom
GEOMECH_APPLY_LOAD type=pressure pressure=100000

# Run simulation
GEOMECH_RUN analysis=static steps=10

# Visualize
GEOMECH_SET_DISPLAY field=vonmises colormap=jet deformation_scale=100
```

---

## Exporting

- **Export SVG**: Creates publication-quality vector graphics of the cross-section with grid, labels, and legend
- **Animation Export**: Export restoration sequences as frame sequences (PNG/BMP) for animations

---

## Troubleshooting

### Restoration shows same geology as original
Ensure you have formations with varying geometry (folds, tilted layers). Simple horizontal layers will appear unchanged when restored.

### Presets have overlapping layers
Click **Check Overlaps** and use **Auto-Fix** to resolve. All built-in presets are validated but manual edits can introduce overlaps.

### Ghost overlay not visible
Enable the **Ghost** checkbox in the restoration toolbar. Adjust opacity if needed.

---

## Related Pages

- [Slope Stability](Slope-Stability.md)
- [Stratigraphy Correlation](Stratigraphy-Correlation.md)
- [GeoScript Manual](GeoScript-Manual.md)
- [GeoScript Image Operations](GeoScript-Image-Operations.md)
- [Simulation Modules](Simulation-Modules.md)
- [Rock Mechanics](Rock-Mechanics.md)
