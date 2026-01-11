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

---

## Structural Restoration Workflow

Structural restoration tools allow you to unfault and unfold the section to visualize pre-deformation geometry.

1. In the 2D Geology tools panel, click **Restore Section...**.
2. Choose **Start Interactive Restoration** to enable a live slider.
3. Adjust the **Restoration %** slider to interpolate from the current state (0%) to fully restored (100%).
4. Use **Apply Restoration** to commit changes or **Clear Restoration** to exit overlay mode.

Additional options:
- **Quick Restore (100%)** and **Quick Restore (50%)** for one-click restoration levels.
- **Create Flat Reference** to generate a fully flattened reference state.

When restoration is active, the original formations and faults are hidden and a restoration overlay is drawn for clarity.

---

## Integration With Slope Stability (2D)

A 2D geology profile can be converted into a 2D slope stability dataset via GeoScript:

```geoscript
SLOPE2D_FROM_GEOLOGY thickness=1.5 name="Valley Slope Analysis"
```

This generates a slope-stability dataset using the cross-section geometry, enabling rapid stability workflows tied to structural interpretations.

---

## Related Pages

- [Slope Stability](Slope-Stability.md)
- [GeoScript Manual](GeoScript-Manual.md)
- [Simulation Modules](Simulation-Modules.md)
