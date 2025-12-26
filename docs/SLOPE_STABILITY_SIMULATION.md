# Slope Stability Simulation (3D + 2D)

This guide covers the slope stability simulation workflows for both 3D terrain-based analysis and 2D cross-section analysis. The 3D engine uses a Discrete Element Method (DEM) with block generation from joint sets, while the 2D workflow builds rigid blocks from a geological profile for section-based stability studies.

## 3D Slope Stability (DEM)

### Capabilities
- Automatic block generation from 3D meshes using joint sets.
- Material presets and custom material properties for friction, cohesion, and stiffness.
- Dynamic, quasi-static, and static (strength-reduction) simulation modes.
- Earthquake loading and pore-pressure effects.
- Export results to CSV, JSON, VTK, or binary (`.ssr`) for reimport.

### Typical Workflow (GUI)
1. **Import a terrain mesh** (OBJ/STL or derived from LIDAR/DTM).
2. **Create a Slope Stability dataset** from the mesh.
3. **Define joint sets** (dip, dip direction, spacing, friction, cohesion).
4. **Generate blocks** using the DFN block generator.
5. **Assign materials** (presets like Granite, Limestone, Sandstone, etc.).
6. **Configure simulation parameters** (time step, damping, mode).
7. **Run the simulation** and inspect results in the 3D viewer.
8. **Export results** to CSV/JSON/VTK or binary `.ssr`.

### GeoScript Example (3D)
```geoscript
# Load mesh and create slope analysis
mesh.obj |> SLOPE_GENERATE_BLOCKS target_size=1.5 remove_small=true

# Add joint sets
|> SLOPE_ADD_JOINT_SET dip=45 dip_dir=90 spacing=1.0 friction=30 cohesion=0.5 name="Main Fracture"
|> SLOPE_ADD_JOINT_SET dip=60 dip_dir=270 spacing=2.0 friction=35 cohesion=1.0 name="Secondary"

# Assign material properties
|> SLOPE_SET_MATERIAL preset=granite

# Optional: add earthquake trigger
|> SLOPE_ADD_EARTHQUAKE magnitude=6.0 epicenter_x=100 epicenter_y=50 start_time=2.0

# Run simulation
|> SLOPE_SIMULATE time=20.0 timestep=0.0005 mode=dynamic threads=8

# Export results
|> SLOPE_EXPORT path="landslide_results.csv" format=csv
```

## 2D Slope Stability (Cross-Section)

The 2D workflow creates a slope stability dataset from a **TwoDGeology** profile and runs a rigid-block simulation. This is ideal for section-based studies where you need to understand stability along a specific profile.

### Capabilities
- Build 2D slope stability datasets from geological cross-sections.
- Generate rigid blocks with joint sets and optional formation boundaries.
- Run dynamic, quasi-static, or static simulations.
- Review displacement history and block stability from results.

### Typical Workflow (GUI)
1. **Load a 2D geology profile** (TwoDGeology dataset).
2. **Create a 2D Slope Stability dataset** from the profile.
3. **Add joint sets** for discontinuities.
4. **Generate blocks** with area limits.
5. **Run the 2D simulation** and inspect the cross-section results.

### GeoScript Example (2D)
```geoscript
# Convert a 2D geology profile into a slope stability dataset
geology_profile.2dg |> SLOPE2D_FROM_GEOLOGY thickness=1.5 name="Valley Slope 2D"

# Add joint sets
|> SLOPE2D_ADD_JOINT_SET name="Bedding" dip=10 dip_dir=90 spacing=2.0 friction=30

# Generate blocks
|> SLOPE2D_GENERATE_BLOCKS min_area=0.5 max_area=50 remove_small=true use_formations=true

# Run simulation
|> SLOPE2D_SIMULATE time=20.0 timestep=0.0005 mode=quasistatic
```

## Related References
- Full 3D system deep-dive: [`Analysis/SlopeStability/README.md`](../Analysis/SlopeStability/README.md)
- GeoScript command implementations:
  - 3D: `Business/GeoScript/Commands/Slope/`
  - 2D: `Business/GeoScript/Commands/Slope/` (SLOPE2D_* commands)
