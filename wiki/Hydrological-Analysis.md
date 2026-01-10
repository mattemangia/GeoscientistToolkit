# Hydrological Analysis

Guide for GIS-scale hydrological analysis, including flow routing, rainfall simulation, and water body tracking.

---

## Overview

The Hydrological module performs raster-based hydrological analysis on GIS datasets. It supports GPU-accelerated flow routing, rainfall-driven water depth simulation, and watershed delineation.

**Key capabilities:**
- Flow direction and flow accumulation
- Stream network detection with thresholding
- Watershed delineation from a clicked outlet
- Rainfall simulation with seasonal curves
- Water body tracking and volume history
- GPU acceleration (OpenCL) when available

---

## Entry Point

1. Load a **GIS Dataset** with a raster elevation layer.
2. Open **GIS Tools → Hydrological Analysis**.
3. Choose the elevation layer and run analysis.

---

## Setup & Flow Analysis

1. **Select Elevation Raster** (DEM/DTM).
2. **Compute Flow Direction** and **Flow Accumulation**.
3. **Set Stream Threshold** to extract channel networks.
4. Click a point on the map to trace **flow paths** and **watersheds**.

---

## Rainfall Simulation

The rainfall tool simulates surface water depth over time:

- Annual rainfall curve editor (monthly multipliers)
- Base rainfall (mm/day)
- Drainage and infiltration rates
- Time-step controls and animation playback

The output includes water depth maps and time-series of total water volume.

---

## Water Bodies

Enable water body tracking to:

- Detect contiguous water regions
- Track volume evolution across time steps
- Visualize water extents on the GIS map

---

## Visualization

Hydrological visualization overlays include:

- Flow paths and watersheds
- Stream network layers
- Water depth raster overlays
- Animated rainfall playback

---

## Export

Export analysis results as GIS layers or raster outputs (e.g., GeoTIFF). Use the Export tab to select layers and formats.

---

## Related Pages

- [Seismic Analysis](Seismic-Analysis.md)
- [Geothermal Simulation](Geothermal-Simulation.md)
- [User Guide](User-Guide.md)
