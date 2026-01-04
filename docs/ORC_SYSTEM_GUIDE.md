# ORC (Organic Rankine Cycle) System Guide

This guide explains how to design, configure, and simulate ORC geothermal power systems in GeoscientistToolkit.

## Overview

The ORC module provides a complete workflow for designing binary geothermal power plants:
1. **Heat Exchanger Placement** - Position coaxial or shell-tube exchangers in the reservoir
2. **Circuit Design** - Build ORC cycles with visual node editor
3. **Component Configuration** - Configure condenser, evaporator, turbine, pump parameters
4. **Cycle Visualization** - Analyze P-h and T-s diagrams
5. **Simulation** - Run coupled thermal-hydraulic simulations

---

## 1. Heat Exchanger Placement

### Using GTK Dialog
1. Select a PhysicoChem dataset
2. Menu: **Tools → Add Heat Exchanger / Object...**
3. Configure:
   - **Name**: Identifier for the exchanger
   - **Type**: `HeatExchanger`, `Condenser`, `Evaporator`
   - **Geometry**: Box or Cylinder mode
   - **Position**: X, Y, Z coordinates
   - **Dimensions**: Size or Radius/Height

### Surface Contact Requirement
Heat exchangers **must contact the surface** (top of mesh) for fluid inlet/outlet access. The system validates this automatically:
- If validation fails, an error dialog shows the required adjustment
- Use `TryEmbedObject()` for programmatic placement with validation

### Programmatic Placement
```csharp
var heatExchanger = new ReactorObject
{
    Name = "Coaxial HX",
    Type = "HeatExchanger",
    MaterialID = "Steel",
    IsCylinder = true,
    Center = (50.0, 50.0, 25.0),  // X, Y, Z
    Radius = 0.15,                 // meters
    Height = 50.0                  // extends to surface
};

if (mesh.TryEmbedObject(heatExchanger, out string error))
{
    Console.WriteLine("Heat exchanger placed successfully");
}
else
{
    Console.WriteLine($"Placement failed: {error}");
}
```

---

## 2. ORC Circuit Builder

### Opening the Builder
- Menu: **Windows → ORC Circuit Builder**
- Or instantiate: `new ORCCircuitBuilder()`

### Component Palette
| Component | Description | Ports |
|-----------|-------------|-------|
| **Evaporator** | Heats working fluid to vapor | 1 in (liquid), 1 out (vapor) |
| **Turbine** | Expands vapor, generates power | 1 in (vapor), 1 out (exhaust) |
| **Condenser** | Cools vapor to liquid | 1 in (vapor), 1 out (liquid) |
| **Pump** | Pressurizes liquid | 1 in (liquid), 1 out (pressurized) |
| **Recuperator** | Heat recovery between streams | 2 in, 2 out |
| **Separator** | Phase separation | 1 in, 2 out |
| **Accumulator** | Buffer tank | 1 in, 1 out |
| **Valve** | Throttling device | 1 in, 1 out |

### Building a Circuit
1. **Add Components**: Click component buttons in palette
2. **Connect Ports**: Drag from output port (right) to input port (left)
3. **Configure Parameters**: Select node, edit in properties panel
4. **Validate**: Click "Validate Circuit" to check connections

### Templates
- **Basic ORC**: Evaporator → Turbine → Condenser → Pump
- **Recuperated ORC**: Adds recuperator for heat recovery
- **Two-Stage ORC**: High and low pressure turbines

### Connection Colors
- **Blue**: Subcooled/saturated liquid
- **Red**: Superheated vapor
- **Purple**: Two-phase mixture

---

## 3. Condenser Configuration

### GTK Dialog (CondenserConfigDialog)
Access via: **Tools → Configure Condenser...**

#### Tab 1: Basic Settings
| Parameter | Description | Typical Range |
|-----------|-------------|---------------|
| Name | Identifier | - |
| Type | Water-Cooled, Air-Cooled, Evaporative, Hybrid | - |
| Temperature | Condensing temperature | 25-40°C |
| Pressure | Condensing pressure | 1-5 bar |
| Effectiveness | Heat transfer effectiveness | 0.80-0.95 |
| UA Value | Overall HT coefficient × Area | 10-500 kW/K |

#### Tab 2: Cooling System
| Parameter | Description | Typical Range |
|-----------|-------------|---------------|
| Flow Rate | Cooling water mass flow | 1-50 kg/s |
| Inlet Temp | Cooling water inlet | 10-25°C |
| Outlet Temp | Cooling water outlet | 20-35°C |
| Pressure Drop | Total pressure loss | 10-50 kPa |

**Heat Rejection Calculation:**
```
Q = ṁ × Cp × ΔT
```
Click "Calculate Heat Rejection" for automatic calculation.

#### Tab 3: Geometry
| Parameter | Description | Typical Range |
|-----------|-------------|---------------|
| Tube Count | Number of tubes | 50-500 |
| Tube Length | Single tube length | 1-6 m |
| Tube Diameter | Outer diameter | 15-25 mm |
| Material | Copper, SS304, SS316, Titanium | - |
| Fouling Factor | Thermal resistance from deposits | 0-0.5 m²K/kW |

---

## 4. ORC Cycle Visualizer

### Opening the Visualizer
- Menu: **Windows → ORC Cycle Diagram**
- Or instantiate: `new ORCCycleVisualizer()`

### Diagram Types

#### P-h Diagram (Pressure vs. Enthalpy)
- Shows saturation dome with liquid and vapor lines
- Cycle processes visible as connected line segments
- Useful for analyzing compression/expansion work

#### T-s Diagram (Temperature vs. Entropy)
- Shows saturation curve
- Heat addition/rejection visible as horizontal shifts
- Useful for analyzing heat transfer and efficiency

### Working Fluids
| Fluid | T_crit (°C) | P_crit (bar) | Best For |
|-------|-------------|--------------|----------|
| Isobutane (R600a) | 135 | 36.4 | Low-temp geothermal |
| R134a | 101 | 40.6 | Waste heat recovery |
| R245fa | 154 | 36.5 | Medium-temp sources |
| n-Pentane | 197 | 33.7 | Higher temp sources |
| Toluene | 319 | 41.0 | High-temp industrial |

### State Points
The visualizer shows 4 state points:
1. **Pump Inlet**: Subcooled liquid from condenser
2. **Pump Outlet**: Compressed liquid entering evaporator
3. **Turbine Inlet**: Superheated vapor leaving evaporator
4. **Turbine Outlet**: Low-pressure exhaust to condenser

### Cycle Metrics
| Metric | Formula | Description |
|--------|---------|-------------|
| Turbine Work | w_t = h_3 - h_4 | Specific work output |
| Pump Work | w_p = h_2 - h_1 | Specific work input |
| Heat Input | q_in = h_3 - h_2 | Evaporator heat per kg |
| Net Work | w_net = w_t - w_p | Net power per kg |
| Thermal Efficiency | η = w_net / q_in | First law efficiency |
| Back-Work Ratio | BWR = w_p / w_t | Parasitic load fraction |

---

## 5. ORC Component Parameters

### ORCComponentParameters Class
Located in: `Data/PhysicoChem/PhysicoChemMesh.cs`

```csharp
var turbineParams = new ORCComponentParameters
{
    ComponentType = "Turbine",
    Temperature = 100,              // °C
    Pressure = 15.0,                // bar
    MassFlowRate = 2.0,             // kg/s
    IsentropicEfficiency = 0.82,    // 82%
    MechanicalEfficiency = 0.95,    // 95%
    TurbineType = "Radial",
    RotationalSpeed = 3000          // RPM
};

// Calculate power output
double power = turbineParams.CalculateTurbinePower(
    enthalpyIn: 520,                // kJ/kg
    enthalpyOutIsentropic: 450      // kJ/kg
);
```

### Component-Specific Parameters

#### Evaporator
- `PinchPoint`: Minimum temperature approach (°C)
- `Superheat`: Degrees of superheat (°C)
- `Effectiveness`: Heat transfer effectiveness (0-1)

#### Turbine
- `TurbineType`: Radial, Axial, Screw
- `IsentropicEfficiency`: Typically 0.75-0.85
- `RotationalSpeed`: RPM
- `PowerOutput`: Calculated output (kW)

#### Condenser
- `CondenserType`: WaterCooled, AirCooled, Evaporative, Hybrid
- `CoolingInletTemp`, `CoolingOutletTemp`: Cooling circuit temps
- `Subcooling`: Degrees of subcooling (°C)

#### Pump
- `PumpType`: Centrifugal, PositiveDisplacement
- `PumpHead`: Head in meters
- `IsentropicEfficiency`: Typically 0.65-0.80
- `PowerConsumption`: Calculated input (kW)

---

## 6. Integration with PhysicoChem Simulation

### Adding ORC to Reactor Mesh
```csharp
// Create heat exchanger with ORC parameters
var evaporator = new ReactorObject
{
    Name = "ORC Evaporator",
    Type = "Evaporator",
    MaterialID = "SS316",
    IsCylinder = true,
    Center = (centerX, centerY, meshTop - height/2),
    Radius = 0.3,
    Height = depth,
    ORCParams = new ORCComponentParameters
    {
        ComponentType = "Evaporator",
        Pressure = 15.0,
        PinchPoint = 5.0,
        Superheat = 5.0,
        MassFlowRate = 2.0
    }
};

mesh.EmbedObject(evaporator);
```

### Running Coupled Simulation
1. Configure PhysicoChem solver with heat sources/sinks
2. Set `HeatSourceFunction` for heat extraction at exchanger location
3. Run time-stepping simulation
4. Extract outlet temperatures for ORC cycle calculation

---

## 7. Best Practices

### Design Guidelines
1. **Pinch Point**: Keep ≥5°C to ensure heat transfer driving force
2. **Superheat**: 5-10°C prevents liquid droplets in turbine
3. **Subcooling**: 2-5°C ensures no vapor enters pump
4. **Pressure Ratio**: Optimize for maximum net work

### Efficiency Optimization
- Higher evaporator pressure → Higher cycle efficiency (limited by source temp)
- Lower condenser temperature → Higher efficiency (limited by ambient)
- Recuperator improves efficiency by 5-15% for dry fluids

### Common Issues
| Issue | Cause | Solution |
|-------|-------|----------|
| Low efficiency | Pinch point too large | Increase HX area (UA) |
| Wet expansion | Insufficient superheat | Increase superheat or use dry fluid |
| Pump cavitation | Low NPSH | Increase subcooling or condenser elevation |
| Scaling/Fouling | High geo-fluid temp | Regular cleaning, material selection |

---

## References

1. Quoilin, S., et al. (2013). *Techno-economic survey of ORC systems*. Renewable and Sustainable Energy Reviews.
2. DiPippo, R. (2015). *Geothermal Power Plants* (4th ed.). Butterworth-Heinemann.
3. Lemmon, E.W., et al. (2018). *REFPROP 10.0*. NIST Standard Reference Database 23.
