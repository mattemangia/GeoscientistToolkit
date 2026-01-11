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
- [Simulation Modules](Simulation-Modules.md)
