# Acoustic Simulation

Guide to CT-scale acoustic wave simulation and tomography.

---

## Overview

The acoustic simulation module models elastic wave propagation in segmented CT volumes. It supports interactive transmitter/receiver placement, real-time visualization, and time-series output for tomography workflows.

**Key capabilities:**
- Elastic/plastic wave propagation
- Interactive TX/RX placement
- Time-series snapshot output
- Real-time tomography viewer
- Material-based property assignment

---

## Entry Point

1. Select a **CT Image Stack** dataset.
2. Open **CT Tools → Analysis → Acoustic Simulation**.
3. Configure material properties and run the solver.

---

## Typical Workflow

1. **Select materials** participating in the simulation.
2. **Set elastic properties** (Young’s modulus, Poisson ratio, density).
3. **Place transducers** (TX/RX) using the viewer overlays.
4. **Configure source parameters** (frequency, amplitude, wavelet).
5. **Run simulation** and inspect wavefields.
6. **Export** time-series or acoustic volumes.

---

## Solver Options

- **Wavelet source** (Ricker or continuous)
- **Elastic vs plastic** response
- **Chunked processing** for large datasets
- **GPU acceleration** where available
- **Real-time visualization** with update interval controls

---

## Outputs

- Wavefield snapshots over time
- Acoustic volume exports
- Travel-time and attenuation diagnostics
- Tomography-style reconstructions

---

## Acoustic Volume Dataset

Acoustic simulations can be exported to an **Acoustic Volume** dataset (`.acvol`). This package stores wavefield snapshots, metadata, and derived travel-time measurements for reuse in seismic-style visualization workflows.

**Typical workflow:**
1. Run the acoustic simulation on a CT dataset.
2. Use the Export option to generate an Acoustic Volume package.
3. Load the `.acvol` dataset for post-processing, waveform inspection, or integration with seismic tools.

---

## GeoScript Automation

Run acoustic simulations from GeoScript:

```geoscript
SIMULATE_ACOUSTIC materials=1,2 tx=0.1,0.5,0.5 rx=0.9,0.5,0.5 time_steps=1000 use_gpu=true
```

---

## Related Pages

- [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md)
- [Geomechanical Simulation](Geomechanical-Simulation.md)
- [Seismic Analysis](Seismic-Analysis.md)
