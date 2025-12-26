using System;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Parameters for slope stability simulation.
    ///
    /// <para><b>Academic References:</b></para>
    ///
    /// <para>DEM time step stability (critical time step):</para>
    /// <para>O'Sullivan, C., &amp; Bray, J. D. (2004). Selecting a suitable time step for discrete
    /// element simulations that use the central difference time integration scheme.
    /// Engineering Computations, 21(2/3/4), 278-303.
    /// https://doi.org/10.1108/02644400410519794</para>
    ///
    /// <para>Damping in DEM simulations:</para>
    /// <para>Cundall, P. A., &amp; Hart, R. D. (1992). Numerical modelling of discontinua.
    /// Engineering Computations, 9(2), 101-113.
    /// https://doi.org/10.1108/eb023851</para>
    ///
    /// <para>Seismic slope stability (pseudo-static method):</para>
    /// <para>Seed, H. B. (1979). Considerations in the earthquake-resistant design of earth and
    /// rockfill dams. Géotechnique, 29(3), 215-263.
    /// https://doi.org/10.1680/geot.1979.29.3.215</para>
    ///
    /// <para>Factor of safety by strength reduction:</para>
    /// <para>Dawson, E. M., Roth, W. H., &amp; Drescher, A. (1999). Slope stability analysis by
    /// strength reduction. Géotechnique, 49(6), 835-840.
    /// https://doi.org/10.1680/geot.1999.49.6.835</para>
    /// </summary>
    public class SlopeStabilityParameters
    {
        // Simulation control
        public float TimeStep { get; set; }             // seconds
        public float TotalTime { get; set; }            // seconds
        public int MaxIterations { get; set; }
        public float ConvergenceThreshold { get; set; } // displacement tolerance

        // Gravity and slope angle
        public Vector3 Gravity { get; set; }            // m/s²
        public float SlopeAngle { get; set; }           // degrees (0-90), auto-rotates gravity direction
        public bool UseCustomGravityDirection { get; set; }  // if true, uses Gravity vector directly

        // Damping (for quasi-static analysis)
        public float LocalDamping { get; set; }         // 0-1, local non-viscous damping
        public bool UseAdaptiveDamping { get; set; }
        public float ViscousDamping { get; set; }       // viscous damping coefficient

        // Contact detection
        public float ContactSearchRadius { get; set; }  // meters
        public int SpatialHashGridSize { get; set; }    // cells per dimension

        // Boundary conditions
        public BoundaryConditionMode BoundaryMode { get; set; }
        public Vector3 AllowedDisplacementDOF { get; set; }  // 1.0 = free, 0.0 = fixed for X,Y,Z

        // Simulation mode
        public SimulationMode Mode { get; set; }

        // Output control
        public int OutputFrequency { get; set; }        // save results every N steps
        public bool SaveIntermediateStates { get; set; }
        public bool ComputeFinalState { get; set; }     // run until blocks settle

        // Solver options
        public bool UseMultithreading { get; set; }
        public int NumThreads { get; set; }             // 0 = auto-detect
        public bool UseSIMD { get; set; }

        // Advanced options
        public bool IncludeRotation { get; set; }       // enable rotational dynamics
        public bool IncludeFluidPressure { get; set; }  // water pressure in joints
        public float WaterTableZ { get; set; }          // elevation of water table
        public float WaterDensity { get; set; }         // kg/m³

        // Joint set identification
        public float JointOrientationTolerance { get; set; }  // degrees, tolerance for matching contact normal to joint set

        // Earthquake loading
        public List<EarthquakeLoad> EarthquakeLoads { get; set; }
        public bool EnableEarthquakeLoading { get; set; }

        // External results import
        public string ExternalStressFieldPath { get; set; }
        public string ExternalDisplacementFieldPath { get; set; }
        public string ExternalVelocityFieldPath { get; set; }
        public bool UseExternalInitialConditions { get; set; }

        public SlopeStabilityParameters()
        {
            TimeStep = 0.001f;              // 1 ms
            TotalTime = 10.0f;              // 10 seconds
            MaxIterations = 100000;
            ConvergenceThreshold = 1e-6f;   // 1 micron
            Gravity = new Vector3(0, 0, -9.81f);  // Standard gravity downward
            SlopeAngle = 0.0f;              // Horizontal by default
            UseCustomGravityDirection = false;
            LocalDamping = 0.05f;
            UseAdaptiveDamping = true;
            ViscousDamping = 0.0f;
            ContactSearchRadius = 0.1f;     // 10 cm
            SpatialHashGridSize = 100;
            BoundaryMode = BoundaryConditionMode.Free;
            AllowedDisplacementDOF = Vector3.One;  // All DOFs free
            Mode = SimulationMode.Dynamic;
            OutputFrequency = 100;
            SaveIntermediateStates = true;
            ComputeFinalState = true;
            UseMultithreading = true;
            NumThreads = 0;  // Auto-detect
            UseSIMD = true;
            IncludeRotation = true;
            IncludeFluidPressure = false;
            WaterTableZ = 0.0f;
            WaterDensity = 1000.0f;
            JointOrientationTolerance = 5.0f;  // 5 degrees default
            EarthquakeLoads = new List<EarthquakeLoad>();
            EnableEarthquakeLoading = false;
            ExternalStressFieldPath = "";
            ExternalDisplacementFieldPath = "";
            ExternalVelocityFieldPath = "";
            UseExternalInitialConditions = false;
        }

        /// <summary>
        /// Validates parameters and adjusts if necessary.
        /// </summary>
        public void Validate()
        {
            // Ensure time step is stable
            if (TimeStep <= 0.0f)
                TimeStep = 0.001f;

            if (TimeStep > 0.1f)
            {
                Console.WriteLine($"Warning: TimeStep {TimeStep} is very large. Reducing to 0.1s for stability.");
                TimeStep = 0.1f;
            }

            // Ensure total time is positive
            if (TotalTime <= 0.0f)
                TotalTime = 10.0f;

            // Clamp damping values
            LocalDamping = Math.Clamp(LocalDamping, 0.0f, 1.0f);
            ViscousDamping = Math.Max(ViscousDamping, 0.0f);

            // Ensure contact search radius is positive
            if (ContactSearchRadius <= 0.0f)
                ContactSearchRadius = 0.1f;

            // Ensure spatial hash grid is reasonable
            if (SpatialHashGridSize < 10)
                SpatialHashGridSize = 10;
            if (SpatialHashGridSize > 1000)
                SpatialHashGridSize = 1000;

            // Ensure output frequency is valid
            if (OutputFrequency < 1)
                OutputFrequency = 1;

            // Auto-detect thread count
            if (UseMultithreading && NumThreads == 0)
                NumThreads = Environment.ProcessorCount;

            // Update gravity direction based on slope angle if not using custom
            UpdateGravityFromSlopeAngle();
        }

        /// <summary>
        /// Updates gravity direction based on slope angle.
        /// Slope angle tilts the gravity vector to simulate inclined terrain.
        /// </summary>
        public void UpdateGravityFromSlopeAngle()
        {
            if (!UseCustomGravityDirection && Math.Abs(SlopeAngle) > 0.01f)
            {
                float angleRad = SlopeAngle * MathF.PI / 180.0f;
                float gravityMagnitude = 9.81f;

                // Rotate gravity vector in X-Z plane (tilt forward)
                // Positive angle = slope going down in +X direction
                Gravity = new Vector3(
                    gravityMagnitude * MathF.Sin(angleRad),   // Downslope component
                    0.0f,
                    -gravityMagnitude * MathF.Cos(angleRad)   // Vertical component
                );
            }
            else if (!UseCustomGravityDirection)
            {
                Gravity = new Vector3(0, 0, -9.81f);
            }
        }

        /// <summary>
        /// Calculates the number of simulation steps.
        /// </summary>
        public int GetNumSteps()
        {
            return Math.Min((int)(TotalTime / TimeStep), MaxIterations);
        }

        /// <summary>
        /// Estimates critical time step based on block properties (for stability).
        /// </summary>
        public static float EstimateCriticalTimeStep(float minBlockSize, float maxStiffness, float maxDensity)
        {
            // Critical time step for explicit integration: Δt_crit = 2 * sqrt(m/k)
            // Use simplified estimate based on smallest block
            float estimatedMass = maxDensity * minBlockSize * minBlockSize * minBlockSize;
            float dtCrit = 2.0f * MathF.Sqrt(estimatedMass / maxStiffness);

            // Apply safety factor
            return dtCrit * 0.1f;  // Use 10% of critical time step
        }
    }

    /// <summary>
    /// Simulation mode.
    /// </summary>
    public enum SimulationMode
    {
        Dynamic,        // Full dynamic analysis with inertia
        QuasiStatic,    // Quasi-static with strong damping
        Static          // Static equilibrium (strength reduction method)
    }

    /// <summary>
    /// Boundary condition mode.
    /// </summary>
    public enum BoundaryConditionMode
    {
        Free,           // All boundaries free
        FixedBase,      // Bottom fixed
        FixedSides,     // Sides fixed
        Custom          // User-defined DOF constraints
    }
}
