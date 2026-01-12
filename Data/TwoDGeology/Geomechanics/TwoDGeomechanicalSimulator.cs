// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/TwoDGeomechanicalSimulator.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// Analysis type for 2D geomechanical simulation
/// </summary>
public enum AnalysisType2D
{
    Static,                 // Static equilibrium
    QuasiStatic,           // Load stepping with equilibrium at each step
    Dynamic,               // Explicit dynamic analysis
    ImplicitDynamic,       // Implicit Newmark integration
    LargeDeformation,      // Updated Lagrangian for large strains
    Consolidation,         // Coupled poromechanics
    ThermoMechanical       // Coupled thermal-mechanical
}

/// <summary>
/// Solver type for linear system
/// </summary>
public enum SolverType2D
{
    DirectLU,              // LU decomposition
    DirectCholesky,        // Cholesky for SPD systems
    ConjugateGradient,     // PCG iterative
    GMRES,                 // Generalized minimal residual
    ExplicitCentral        // Central difference explicit
}

/// <summary>
/// Simulation state for visualization
/// </summary>
public class SimulationState2D
{
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public double CurrentTime { get; set; }
    public double TotalTime { get; set; }
    public double CurrentLoad { get; set; }
    public bool IsRunning { get; set; }
    public bool IsConverged { get; set; }
    public double ResidualNorm { get; set; }
    public int NumPlasticElements { get; set; }
    public int NumFailedElements { get; set; }
    public double MaxDisplacement { get; set; }
    public double MaxStress { get; set; }
    public string StatusMessage { get; set; }

    /// <summary>
    /// Number of auto-generated faults during simulation
    /// </summary>
    public int NumGeneratedFaults { get; set; }

    /// <summary>
    /// Total length of all generated faults (m)
    /// </summary>
    public double TotalFaultLength { get; set; }

    /// <summary>
    /// Number of detected rupture sites awaiting fault nucleation
    /// </summary>
    public int NumRuptureSites { get; set; }
}

/// <summary>
/// Results data for post-processing and visualization
/// </summary>
public class SimulationResults2D
{
    // Per-node results
    public double[] DisplacementX { get; set; }
    public double[] DisplacementY { get; set; }
    public double[] DisplacementMagnitude { get; set; }
    public double[] VelocityX { get; set; }
    public double[] VelocityY { get; set; }
    public double[] ReactionForceX { get; set; }
    public double[] ReactionForceY { get; set; }
    public double[] PorePressure { get; set; }
    public double[] Temperature { get; set; }

    // Per-element results (at centroids or integration points)
    public double[] StressXX { get; set; }
    public double[] StressYY { get; set; }
    public double[] StressXY { get; set; }
    public double[] StressZZ { get; set; }  // Out-of-plane for plane strain
    public double[] StrainXX { get; set; }
    public double[] StrainYY { get; set; }
    public double[] StrainXY { get; set; }
    public double[] VolumetricStrain { get; set; }
    public double[] ShearStrain { get; set; }

    // Principal stresses
    public double[] Sigma1 { get; set; }
    public double[] Sigma2 { get; set; }
    public double[] Sigma3 { get; set; }
    public double[] PrincipalAngle { get; set; }

    // Derived quantities
    public double[] MeanStress { get; set; }
    public double[] DeviatoricStress { get; set; }
    public double[] VonMisesStress { get; set; }
    public double[] MaxShearStress { get; set; }
    public double[] OctahedralStress { get; set; }

    // Failure/plasticity indicators
    public double[] YieldIndex { get; set; }        // f/f_yield - safety factor
    public double[] PlasticStrain { get; set; }
    public double[] DamageVariable { get; set; }
    public bool[] HasYielded { get; set; }
    public bool[] HasFailed { get; set; }

    // History data
    public List<(double time, double load, double maxDisp, double maxStress)> History { get; set; } = new();

    public void Initialize(int numNodes, int numElements)
    {
        DisplacementX = new double[numNodes];
        DisplacementY = new double[numNodes];
        DisplacementMagnitude = new double[numNodes];
        VelocityX = new double[numNodes];
        VelocityY = new double[numNodes];
        ReactionForceX = new double[numNodes];
        ReactionForceY = new double[numNodes];
        PorePressure = new double[numNodes];
        Temperature = new double[numNodes];

        StressXX = new double[numElements];
        StressYY = new double[numElements];
        StressXY = new double[numElements];
        StressZZ = new double[numElements];
        StrainXX = new double[numElements];
        StrainYY = new double[numElements];
        StrainXY = new double[numElements];
        VolumetricStrain = new double[numElements];
        ShearStrain = new double[numElements];

        Sigma1 = new double[numElements];
        Sigma2 = new double[numElements];
        Sigma3 = new double[numElements];
        PrincipalAngle = new double[numElements];

        MeanStress = new double[numElements];
        DeviatoricStress = new double[numElements];
        VonMisesStress = new double[numElements];
        MaxShearStress = new double[numElements];
        OctahedralStress = new double[numElements];

        YieldIndex = new double[numElements];
        PlasticStrain = new double[numElements];
        DamageVariable = new double[numElements];
        HasYielded = new bool[numElements];
        HasFailed = new bool[numElements];
    }
}

/// <summary>
/// Full 2D geomechanical simulator with FEM solver.
/// Supports static, dynamic, and coupled analyses with
/// various constitutive models and failure criteria.
/// </summary>
public class TwoDGeomechanicalSimulator
{
    #region Properties

    public FEMMesh2D Mesh { get; private set; }
    public SimulationState2D State { get; } = new();
    public SimulationResults2D Results { get; private set; }

    /// <summary>
    /// Fault propagation engine for automatic fault generation during simulation
    /// </summary>
    public FaultPropagationEngine FaultEngine { get; } = new();

    /// <summary>
    /// Settings for automatic fault generation based on rupture criteria
    /// </summary>
    public AutoFaultSettings AutoFaultSettings => FaultEngine.Settings;

    // Analysis settings
    public AnalysisType2D AnalysisType { get; set; } = AnalysisType2D.Static;
    public SolverType2D SolverType { get; set; } = SolverType2D.ConjugateGradient;
    public bool PlaneStrain { get; set; } = true;  // vs plane stress

    // Loading parameters
    public Vector2 Gravity { get; set; } = new(0, -9.81f);
    public bool ApplyGravity { get; set; } = true;
    public int NumLoadSteps { get; set; } = 10;
    public double LoadFactor { get; set; } = 1.0;

    // Dynamic parameters
    public double TimeStep { get; set; } = 0.001;
    public double TotalTime { get; set; } = 1.0;
    public double MassDamping { get; set; } = 0.0;   // Rayleigh α
    public double StiffnessDamping { get; set; } = 0.0; // Rayleigh β

    // Convergence parameters
    public int MaxIterations { get; set; } = 100;
    public double ConvergenceTolerance { get; set; } = 1e-6;
    public double ForceResidualTolerance { get; set; } = 1e-4;

    // Large deformation settings
    public bool UpdatedLagrangian { get; set; } = false;
    public bool GeometricNonlinearity { get; set; } = false;

    // Coupled analysis
    public bool EnablePorePressure { get; set; } = false;
    public bool EnableThermal { get; set; } = false;

    // Events
    public event Action<SimulationState2D> OnStepCompleted;
    public event Action<SimulationResults2D> OnSimulationCompleted;
    public event Action<string> OnMessage;

    /// <summary>
    /// Event fired when a new fault is generated during simulation
    /// </summary>
    public event EventHandler<FaultGeneratedEventArgs> OnFaultGenerated;

    /// <summary>
    /// Event fired when rupture is detected (before fault forms)
    /// </summary>
    public event EventHandler<FaultNucleationSite> OnRuptureDetected;

    #endregion

    #region Private Fields

    private double[,] _globalK;        // Global stiffness matrix
    private double[,] _globalM;        // Global mass matrix
    private double[] _globalF;         // Global force vector
    private double[] _globalU;         // Global displacement vector
    private double[] _globalV;         // Global velocity vector
    private double[] _globalA;         // Global acceleration vector
    private double[] _internalForce;   // Internal force vector

    private CancellationTokenSource _cts;
    private bool _isRunning;

    #endregion

    #region Initialization

    public TwoDGeomechanicalSimulator()
    {
        Mesh = new FEMMesh2D();
        InitializeFaultEngine();
    }

    /// <summary>
    /// Initialize fault propagation engine and wire up events
    /// </summary>
    private void InitializeFaultEngine()
    {
        FaultEngine.OnFaultNucleated += (sender, args) =>
        {
            OnFaultGenerated?.Invoke(this, args);
            OnMessage?.Invoke($"Fault nucleated at step {args.SimulationStep}: " +
                $"Mode={args.Fault.Mode}, Length={args.Fault.Length:F2}m");
        };

        FaultEngine.OnFaultPropagated += (sender, args) =>
        {
            OnFaultGenerated?.Invoke(this, args);
            OnMessage?.Invoke($"Fault {args.Fault.Id} propagated to length {args.Fault.Length:F2}m");
        };

        FaultEngine.OnRuptureDetected += (sender, site) =>
        {
            OnRuptureDetected?.Invoke(this, site);
        };
    }

    public void SetMesh(FEMMesh2D mesh)
    {
        Mesh = mesh;
        InitializeResults();
    }

    public void InitializeResults()
    {
        Results = new SimulationResults2D();
        Results.Initialize(Mesh.Nodes.Count, Mesh.Elements.Count);
    }

    #endregion

    #region Main Simulation Methods

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;
        State.IsRunning = true;

        try
        {
            // Number DOFs
            Mesh.NumberDOF();
            if (Mesh.TotalDOF == 0)
            {
                OnMessage?.Invoke("Error: No free degrees of freedom");
                return;
            }

            // Initialize arrays
            InitializeGlobalArrays();

            // Run appropriate analysis
            switch (AnalysisType)
            {
                case AnalysisType2D.Static:
                    await Task.Run(() => RunStaticAnalysis(_cts.Token), _cts.Token);
                    break;

                case AnalysisType2D.QuasiStatic:
                    await Task.Run(() => RunQuasiStaticAnalysis(_cts.Token), _cts.Token);
                    break;

                case AnalysisType2D.Dynamic:
                    await Task.Run(() => RunExplicitDynamicAnalysis(_cts.Token), _cts.Token);
                    break;

                case AnalysisType2D.ImplicitDynamic:
                    await Task.Run(() => RunImplicitDynamicAnalysis(_cts.Token), _cts.Token);
                    break;

                case AnalysisType2D.LargeDeformation:
                    UpdatedLagrangian = true;
                    await Task.Run(() => RunQuasiStaticAnalysis(_cts.Token), _cts.Token);
                    break;
            }

            State.IsRunning = false;
            OnSimulationCompleted?.Invoke(Results);
        }
        catch (OperationCanceledException)
        {
            OnMessage?.Invoke("Simulation cancelled");
        }
        catch (Exception ex)
        {
            OnMessage?.Invoke($"Simulation error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
            State.IsRunning = false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    #endregion

    #region Analysis Methods

    private void RunStaticAnalysis(CancellationToken ct)
    {
        State.TotalSteps = 1;
        State.CurrentStep = 0;
        State.StatusMessage = "Running static analysis...";

        // Assemble global stiffness matrix
        AssembleStiffnessMatrix();

        // Apply body forces (gravity)
        if (ApplyGravity)
        {
            ApplyBodyForces();
        }

        // Assemble force vector
        AssembleForceVector();

        // Solve the system
        SolveLinearSystem();

        // Extract results
        ExtractDisplacements();
        UpdateStressStrain();
        ComputeDerivedQuantities();

        // Process automatic fault generation based on rupture criteria
        // (for static analysis, only check once at the end)
        ProcessAutoFaultGeneration(1, 0);

        State.CurrentStep = 1;
        State.IsConverged = true;
        OnStepCompleted?.Invoke(State);
    }

    private void RunQuasiStaticAnalysis(CancellationToken ct)
    {
        State.TotalSteps = NumLoadSteps;
        State.StatusMessage = "Running quasi-static analysis...";

        for (int step = 1; step <= NumLoadSteps; step++)
        {
            ct.ThrowIfCancellationRequested();

            State.CurrentStep = step;
            State.CurrentLoad = (double)step / NumLoadSteps;
            State.StatusMessage = $"Load step {step}/{NumLoadSteps}";

            // Scale forces
            double loadFactor = (double)step / NumLoadSteps;

            // Newton-Raphson iteration for nonlinear solution
            bool converged = false;
            for (int iter = 0; iter < MaxIterations; iter++)
            {
                ct.ThrowIfCancellationRequested();

                // Assemble tangent stiffness
                AssembleStiffnessMatrix();

                // Compute residual
                ComputeInternalForces();
                AssembleForceVector(loadFactor);

                double residualNorm = ComputeResidual();
                State.ResidualNorm = residualNorm;

                if (residualNorm < ConvergenceTolerance)
                {
                    converged = true;
                    break;
                }

                // Solve for displacement increment
                SolveLinearSystem();
                ExtractDisplacements();

                // Update geometry if large deformation
                if (UpdatedLagrangian)
                {
                    UpdateNodePositions();
                }

                // Update stress/strain with plasticity
                UpdateStressStrain();
            }

            State.IsConverged = converged;
            if (!converged)
            {
                OnMessage?.Invoke($"Warning: Step {step} did not converge");
            }

            ComputeDerivedQuantities();

            // Process automatic fault generation based on rupture criteria
            ProcessAutoFaultGeneration(step, step * TimeStep);

            RecordHistory(step * TimeStep);
            OnStepCompleted?.Invoke(State);
        }
    }

    private void RunExplicitDynamicAnalysis(CancellationToken ct)
    {
        int numSteps = (int)(TotalTime / TimeStep);
        State.TotalSteps = numSteps;
        State.TotalTime = TotalTime;

        // Compute lumped mass matrix
        AssembleLumpedMassMatrix();

        // Initial conditions
        Array.Clear(_globalV, 0, _globalV.Length);
        Array.Clear(_globalA, 0, _globalA.Length);

        State.StatusMessage = "Running explicit dynamic analysis...";

        for (int step = 0; step < numSteps; step++)
        {
            ct.ThrowIfCancellationRequested();

            double t = step * TimeStep;
            State.CurrentStep = step;
            State.CurrentTime = t;

            // Central difference scheme
            // u(n+1) = u(n) + dt*v(n) + 0.5*dt²*a(n)
            for (int i = 0; i < Mesh.TotalDOF; i++)
            {
                _globalU[i] += TimeStep * _globalV[i] + 0.5 * TimeStep * TimeStep * _globalA[i];
            }

            ExtractDisplacements();

            // Update stress/strain
            UpdateStressStrain();

            // Compute internal forces
            ComputeInternalForces();

            // Compute external forces
            AssembleForceVector();

            // Compute acceleration: a = M^(-1) * (F_ext - F_int - C*v)
            for (int i = 0; i < Mesh.TotalDOF; i++)
            {
                double mass = _globalM[i, i];
                if (mass > 1e-20)
                {
                    double damping = MassDamping * mass * _globalV[i];
                    _globalA[i] = (_globalF[i] - _internalForce[i] - damping) / mass;
                }
            }

            // Update velocity: v(n+1) = v(n) + 0.5*dt*(a(n) + a(n+1))
            for (int i = 0; i < Mesh.TotalDOF; i++)
            {
                _globalV[i] += TimeStep * _globalA[i];
            }

            // Apply damping to velocity
            if (StiffnessDamping > 0)
            {
                for (int i = 0; i < Mesh.TotalDOF; i++)
                {
                    _globalV[i] *= (1.0 - StiffnessDamping * TimeStep);
                }
            }

            if (step % 100 == 0)
            {
                ComputeDerivedQuantities();

                // Process automatic fault generation based on rupture criteria
                ProcessAutoFaultGeneration(step, t);

                RecordHistory(t);
                OnStepCompleted?.Invoke(State);
            }
        }

        ComputeDerivedQuantities();
        State.IsConverged = true;
    }

    private void RunImplicitDynamicAnalysis(CancellationToken ct)
    {
        int numSteps = (int)(TotalTime / TimeStep);
        State.TotalSteps = numSteps;
        State.TotalTime = TotalTime;

        // Newmark parameters (average acceleration)
        double gamma = 0.5;
        double beta = 0.25;

        // Assemble mass matrix
        AssembleMassMatrix();

        State.StatusMessage = "Running implicit dynamic analysis...";

        for (int step = 0; step < numSteps; step++)
        {
            ct.ThrowIfCancellationRequested();

            double t = step * TimeStep;
            State.CurrentStep = step;
            State.CurrentTime = t;

            // Predictor
            var uPred = new double[Mesh.TotalDOF];
            var vPred = new double[Mesh.TotalDOF];
            for (int i = 0; i < Mesh.TotalDOF; i++)
            {
                uPred[i] = _globalU[i] + TimeStep * _globalV[i] + (0.5 - beta) * TimeStep * TimeStep * _globalA[i];
                vPred[i] = _globalV[i] + (1 - gamma) * TimeStep * _globalA[i];
            }

            // Effective stiffness: K_eff = K + a0*M + a1*C
            double a0 = 1.0 / (beta * TimeStep * TimeStep);
            double a1 = gamma / (beta * TimeStep);

            AssembleStiffnessMatrix();
            for (int i = 0; i < Mesh.TotalDOF; i++)
            {
                _globalK[i, i] += a0 * _globalM[i, i];
            }

            // Effective force
            AssembleForceVector();
            for (int i = 0; i < Mesh.TotalDOF; i++)
            {
                _globalF[i] += _globalM[i, i] * (a0 * uPred[i] + a1 * vPred[i]);
            }

            // Solve
            SolveLinearSystem();
            ExtractDisplacements();

            // Corrector
            for (int i = 0; i < Mesh.TotalDOF; i++)
            {
                _globalA[i] = a0 * (_globalU[i] - uPred[i]);
                _globalV[i] = vPred[i] + gamma * TimeStep * _globalA[i];
            }

            UpdateStressStrain();

            if (step % 10 == 0)
            {
                ComputeDerivedQuantities();

                // Process automatic fault generation based on rupture criteria
                ProcessAutoFaultGeneration(step, t);

                RecordHistory(t);
                OnStepCompleted?.Invoke(State);
            }
        }

        ComputeDerivedQuantities();
        State.IsConverged = true;
    }

    #endregion

    #region Automatic Fault Generation

    /// <summary>
    /// Process automatic fault generation based on rupture criteria
    /// </summary>
    /// <param name="step">Current simulation step</param>
    /// <param name="time">Current simulation time</param>
    private void ProcessAutoFaultGeneration(int step, double time)
    {
        if (!AutoFaultSettings.Enabled) return;
        if (step < AutoFaultSettings.StartAtLoadStep) return;
        if (step % AutoFaultSettings.CheckInterval != 0) return;

        // Generate faults from detected rupture sites
        var newFaults = FaultEngine.GenerateFaults(Mesh, Results, step, time);

        // Update state with fault information
        State.NumGeneratedFaults = FaultEngine.GeneratedFaults.Count;
        State.TotalFaultLength = FaultEngine.GeneratedFaults.Sum(f => f.Length);
        State.NumRuptureSites = FaultEngine.PotentialNucleationSites.Count;

        // If new faults were generated, need to update the system
        if (newFaults.Count > 0)
        {
            OnMessage?.Invoke($"Generated {newFaults.Count} new fault(s) at step {step}. " +
                $"Total faults: {State.NumGeneratedFaults}, Total length: {State.TotalFaultLength:F2}m");

            // Re-number DOFs to account for new nodes from interface elements
            Mesh.NumberDOF();

            // Reinitialize arrays if DOF count changed
            if (Mesh.TotalDOF != _globalK.GetLength(0))
            {
                InitializeGlobalArrays();
                Results.Initialize(Mesh.Nodes.Count, Mesh.Elements.Count);
            }
        }
    }

    /// <summary>
    /// Enable automatic fault generation with default settings
    /// </summary>
    public void EnableAutoFaultGeneration()
    {
        AutoFaultSettings.Enabled = true;
    }

    /// <summary>
    /// Enable automatic fault generation with custom settings
    /// </summary>
    /// <param name="ruptureThreshold">Minimum yield index to trigger fault nucleation</param>
    /// <param name="minClusterSize">Minimum number of failed elements to nucleate a fault</param>
    /// <param name="strategy">Fault propagation direction strategy</param>
    public void EnableAutoFaultGeneration(double ruptureThreshold, int minClusterSize,
        PropagationStrategy strategy = PropagationStrategy.ConjugateAngle)
    {
        AutoFaultSettings.Enabled = true;
        AutoFaultSettings.RuptureThreshold = ruptureThreshold;
        AutoFaultSettings.MinFailedClusterSize = minClusterSize;
        AutoFaultSettings.PropagationStrategy = strategy;
    }

    /// <summary>
    /// Disable automatic fault generation
    /// </summary>
    public void DisableAutoFaultGeneration()
    {
        AutoFaultSettings.Enabled = false;
    }

    /// <summary>
    /// Get all generated faults from the simulation
    /// </summary>
    public List<GeneratedFault> GetGeneratedFaults()
    {
        return FaultEngine.GeneratedFaults.ToList();
    }

    /// <summary>
    /// Get generated faults converted to Discontinuity2D objects
    /// </summary>
    public List<Discontinuity2D> GetGeneratedFaultsAsDiscontinuities()
    {
        return FaultEngine.ConvertToDiscontinuities();
    }

    /// <summary>
    /// Clear all generated faults and reset fault engine
    /// </summary>
    public void ClearGeneratedFaults()
    {
        FaultEngine.Reset();
    }

    #endregion

    #region Assembly Methods

    private void InitializeGlobalArrays()
    {
        int n = Mesh.TotalDOF;
        _globalK = new double[n, n];
        _globalM = new double[n, n];
        _globalF = new double[n];
        _globalU = new double[n];
        _globalV = new double[n];
        _globalA = new double[n];
        _internalForce = new double[n];
    }

    private void AssembleStiffnessMatrix()
    {
        Array.Clear(_globalK, 0, _globalK.Length);
        var nodes = Mesh.Nodes.ToArray();

        foreach (var element in Mesh.Elements)
        {
            var material = Mesh.Materials.GetMaterial(element.MaterialId);
            if (material == null) continue;

            var Ke = element.GetStiffnessMatrix(nodes, material);
            int nDof = element.NodeIds.Count * 2;

            for (int i = 0; i < element.NodeIds.Count; i++)
            {
                int nodeI = element.NodeIds[i];
                int gDofIx = Mesh.Nodes[nodeI].GlobalDofX;
                int gDofIy = Mesh.Nodes[nodeI].GlobalDofY;

                for (int j = 0; j < element.NodeIds.Count; j++)
                {
                    int nodeJ = element.NodeIds[j];
                    int gDofJx = Mesh.Nodes[nodeJ].GlobalDofX;
                    int gDofJy = Mesh.Nodes[nodeJ].GlobalDofY;

                    // Assemble if both DOFs are free
                    if (gDofIx >= 0 && gDofJx >= 0)
                        _globalK[gDofIx, gDofJx] += Ke[2 * i, 2 * j];
                    if (gDofIx >= 0 && gDofJy >= 0)
                        _globalK[gDofIx, gDofJy] += Ke[2 * i, 2 * j + 1];
                    if (gDofIy >= 0 && gDofJx >= 0)
                        _globalK[gDofIy, gDofJx] += Ke[2 * i + 1, 2 * j];
                    if (gDofIy >= 0 && gDofJy >= 0)
                        _globalK[gDofIy, gDofJy] += Ke[2 * i + 1, 2 * j + 1];
                }
            }
        }
    }

    private void AssembleMassMatrix()
    {
        Array.Clear(_globalM, 0, _globalM.Length);
        var nodes = Mesh.Nodes.ToArray();

        foreach (var element in Mesh.Elements)
        {
            var material = Mesh.Materials.GetMaterial(element.MaterialId);
            if (material == null) continue;

            var Me = element.GetMassMatrix(nodes, material.Density);

            for (int i = 0; i < element.NodeIds.Count; i++)
            {
                int nodeI = element.NodeIds[i];
                int gDofIx = Mesh.Nodes[nodeI].GlobalDofX;
                int gDofIy = Mesh.Nodes[nodeI].GlobalDofY;

                for (int j = 0; j < element.NodeIds.Count; j++)
                {
                    int nodeJ = element.NodeIds[j];
                    int gDofJx = Mesh.Nodes[nodeJ].GlobalDofX;
                    int gDofJy = Mesh.Nodes[nodeJ].GlobalDofY;

                    if (gDofIx >= 0 && gDofJx >= 0)
                        _globalM[gDofIx, gDofJx] += Me[2 * i, 2 * j];
                    if (gDofIy >= 0 && gDofJy >= 0)
                        _globalM[gDofIy, gDofJy] += Me[2 * i + 1, 2 * j + 1];
                }
            }
        }
    }

    private void AssembleLumpedMassMatrix()
    {
        Array.Clear(_globalM, 0, _globalM.Length);
        var nodes = Mesh.Nodes.ToArray();

        foreach (var element in Mesh.Elements)
        {
            var material = Mesh.Materials.GetMaterial(element.MaterialId);
            if (material == null) continue;

            double area = element.GetArea(nodes);
            double elementMass = material.Density * area * element.Thickness;
            double massPerNode = elementMass / element.NodeIds.Count;

            foreach (int nodeId in element.NodeIds)
            {
                int gDofX = Mesh.Nodes[nodeId].GlobalDofX;
                int gDofY = Mesh.Nodes[nodeId].GlobalDofY;

                if (gDofX >= 0) _globalM[gDofX, gDofX] += massPerNode;
                if (gDofY >= 0) _globalM[gDofY, gDofY] += massPerNode;
            }
        }
    }

    private void ApplyBodyForces()
    {
        var nodes = Mesh.Nodes.ToArray();

        foreach (var element in Mesh.Elements)
        {
            var material = Mesh.Materials.GetMaterial(element.MaterialId);
            if (material == null) continue;

            var bodyForce = new Vector2(
                (float)(Gravity.X * material.Density),
                (float)(Gravity.Y * material.Density));

            var fe = element.GetBodyForceVector(nodes, bodyForce);

            for (int i = 0; i < element.NodeIds.Count; i++)
            {
                int nodeId = element.NodeIds[i];
                Mesh.Nodes[nodeId].Fx += fe[2 * i];
                Mesh.Nodes[nodeId].Fy += fe[2 * i + 1];
            }
        }
    }

    private void AssembleForceVector(double loadFactor = 1.0)
    {
        Array.Clear(_globalF, 0, _globalF.Length);

        foreach (var node in Mesh.Nodes)
        {
            if (node.GlobalDofX >= 0)
                _globalF[node.GlobalDofX] = node.Fx * loadFactor * LoadFactor;
            if (node.GlobalDofY >= 0)
                _globalF[node.GlobalDofY] = node.Fy * loadFactor * LoadFactor;
        }
    }

    private void ComputeInternalForces()
    {
        Array.Clear(_internalForce, 0, _internalForce.Length);
        var nodes = Mesh.Nodes.ToArray();

        foreach (var element in Mesh.Elements)
        {
            var fInt = element.GetInternalForceVector(nodes);

            for (int i = 0; i < element.NodeIds.Count; i++)
            {
                int nodeId = element.NodeIds[i];
                int gDofX = Mesh.Nodes[nodeId].GlobalDofX;
                int gDofY = Mesh.Nodes[nodeId].GlobalDofY;

                if (gDofX >= 0) _internalForce[gDofX] += fInt[2 * i];
                if (gDofY >= 0) _internalForce[gDofY] += fInt[2 * i + 1];
            }
        }
    }

    private double ComputeResidual()
    {
        double norm = 0;
        for (int i = 0; i < Mesh.TotalDOF; i++)
        {
            double r = _globalF[i] - _internalForce[i];
            norm += r * r;
        }
        return Math.Sqrt(norm);
    }

    #endregion

    #region Solver Methods

    private void SolveLinearSystem()
    {
        switch (SolverType)
        {
            case SolverType2D.DirectLU:
                SolveLU();
                break;
            case SolverType2D.ConjugateGradient:
                SolvePCG();
                break;
            default:
                SolvePCG();
                break;
        }
    }

    private void SolveLU()
    {
        int n = Mesh.TotalDOF;
        var A = new double[n, n];
        var b = new double[n];
        Array.Copy(_globalK, A, _globalK.Length);
        Array.Copy(_globalF, b, n);

        // LU decomposition with partial pivoting
        var perm = new int[n];
        for (int i = 0; i < n; i++) perm[i] = i;

        for (int k = 0; k < n - 1; k++)
        {
            // Find pivot
            int maxIdx = k;
            double maxVal = Math.Abs(A[k, k]);
            for (int i = k + 1; i < n; i++)
            {
                if (Math.Abs(A[i, k]) > maxVal)
                {
                    maxVal = Math.Abs(A[i, k]);
                    maxIdx = i;
                }
            }

            if (maxVal < 1e-20)
            {
                OnMessage?.Invoke($"Warning: Near-singular matrix at row {k}");
                continue;
            }

            // Swap rows
            if (maxIdx != k)
            {
                (perm[k], perm[maxIdx]) = (perm[maxIdx], perm[k]);
                for (int j = 0; j < n; j++)
                    (A[k, j], A[maxIdx, j]) = (A[maxIdx, j], A[k, j]);
                (b[k], b[maxIdx]) = (b[maxIdx], b[k]);
            }

            // Elimination
            for (int i = k + 1; i < n; i++)
            {
                double factor = A[i, k] / A[k, k];
                for (int j = k + 1; j < n; j++)
                    A[i, j] -= factor * A[k, j];
                b[i] -= factor * b[k];
            }
        }

        // Back substitution
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = b[i];
            for (int j = i + 1; j < n; j++)
                sum -= A[i, j] * _globalU[j];
            _globalU[i] = sum / A[i, i];
        }
    }

    private void SolvePCG()
    {
        int n = Mesh.TotalDOF;
        var x = new double[n];
        var r = new double[n];
        var p = new double[n];
        var Ap = new double[n];
        var z = new double[n];

        // Diagonal preconditioner
        var M = new double[n];
        for (int i = 0; i < n; i++)
            M[i] = Math.Abs(_globalK[i, i]) > 1e-20 ? 1.0 / _globalK[i, i] : 1.0;

        // Initial residual r = b - Ax
        Array.Copy(_globalF, r, n);

        // Preconditioned residual z = M^(-1) * r
        for (int i = 0; i < n; i++)
            z[i] = M[i] * r[i];

        Array.Copy(z, p, n);

        double rzOld = 0;
        for (int i = 0; i < n; i++)
            rzOld += r[i] * z[i];

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            // Ap = K * p
            Array.Clear(Ap, 0, n);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    Ap[i] += _globalK[i, j] * p[j];

            // alpha = (r·z) / (p·Ap)
            double pAp = 0;
            for (int i = 0; i < n; i++)
                pAp += p[i] * Ap[i];

            if (Math.Abs(pAp) < 1e-30) break;
            double alpha = rzOld / pAp;

            // x = x + alpha * p
            // r = r - alpha * Ap
            for (int i = 0; i < n; i++)
            {
                x[i] += alpha * p[i];
                r[i] -= alpha * Ap[i];
            }

            // Check convergence
            double rnorm = 0;
            for (int i = 0; i < n; i++)
                rnorm += r[i] * r[i];
            if (Math.Sqrt(rnorm) < ConvergenceTolerance)
                break;

            // z = M^(-1) * r
            for (int i = 0; i < n; i++)
                z[i] = M[i] * r[i];

            // beta = (r_new·z_new) / (r_old·z_old)
            double rzNew = 0;
            for (int i = 0; i < n; i++)
                rzNew += r[i] * z[i];

            double beta = rzNew / rzOld;
            rzOld = rzNew;

            // p = z + beta * p
            for (int i = 0; i < n; i++)
                p[i] = z[i] + beta * p[i];
        }

        Array.Copy(x, _globalU, n);
    }

    #endregion

    #region Post-Processing

    private void ExtractDisplacements()
    {
        foreach (var node in Mesh.Nodes)
        {
            double ux = node.GlobalDofX >= 0 ? _globalU[node.GlobalDofX] : (node.PrescribedUx ?? 0);
            double uy = node.GlobalDofY >= 0 ? _globalU[node.GlobalDofY] : (node.PrescribedUy ?? 0);
            node.ApplyDisplacement(ux, uy);

            Results.DisplacementX[node.Id] = ux;
            Results.DisplacementY[node.Id] = uy;
            Results.DisplacementMagnitude[node.Id] = Math.Sqrt(ux * ux + uy * uy);
        }

        State.MaxDisplacement = Results.DisplacementMagnitude.Max();
    }

    private void UpdateStressStrain()
    {
        var nodes = Mesh.Nodes.ToArray();
        int numPlastic = 0;
        int numFailed = 0;

        for (int e = 0; e < Mesh.Elements.Count; e++)
        {
            var element = Mesh.Elements[e];
            var material = Mesh.Materials.GetMaterial(element.MaterialId);
            if (material == null) continue;

            // Average stress/strain at element centroid
            double sxx = 0, syy = 0, sxy = 0;
            double exx = 0, eyy = 0, exy = 0;
            double plasticStrain = 0;
            bool yielded = false;

            for (int ip = 0; ip < element.NumIntegrationPoints; ip++)
            {
                var strain = element.GetStrain(nodes, 0.333, 0.333);  // Centroid
                var stress = element.GetStress(strain, material, ip);

                exx += strain[0];
                eyy += strain[1];
                exy += strain[2];

                sxx += stress[0];
                syy += stress[1];
                sxy += stress[2];

                if (element.PlasticStrain != null)
                    plasticStrain += element.PlasticStrain[ip];
                if (element.HasYielded != null && element.HasYielded[ip])
                    yielded = true;

                // Store in element state
                element.Stress[ip, 0] = stress[0];
                element.Stress[ip, 1] = stress[1];
                element.Stress[ip, 2] = stress[2];
                element.Strain[ip, 0] = strain[0];
                element.Strain[ip, 1] = strain[1];
                element.Strain[ip, 2] = strain[2];
            }

            int nIP = element.NumIntegrationPoints;
            Results.StressXX[e] = sxx / nIP;
            Results.StressYY[e] = syy / nIP;
            Results.StressXY[e] = sxy / nIP;
            Results.StrainXX[e] = exx / nIP;
            Results.StrainYY[e] = eyy / nIP;
            Results.StrainXY[e] = exy / nIP;
            Results.PlasticStrain[e] = plasticStrain / nIP;
            Results.HasYielded[e] = yielded;

            if (yielded) numPlastic++;
            if (element.HasFailed)
            {
                numFailed++;
                Results.HasFailed[e] = true;
            }
        }

        State.NumPlasticElements = numPlastic;
        State.NumFailedElements = numFailed;
    }

    private void ComputeDerivedQuantities()
    {
        for (int e = 0; e < Mesh.Elements.Count; e++)
        {
            double sxx = Results.StressXX[e];
            double syy = Results.StressYY[e];
            double sxy = Results.StressXY[e];

            // Principal stresses
            double p = (sxx + syy) / 2;
            double R = Math.Sqrt(Math.Pow((sxx - syy) / 2, 2) + sxy * sxy);
            Results.Sigma1[e] = p + R;
            Results.Sigma2[e] = p - R;
            Results.Sigma3[e] = PlaneStrain ? 0.3 * (sxx + syy) : 0;  // σz for plane strain
            Results.PrincipalAngle[e] = 0.5 * Math.Atan2(2 * sxy, sxx - syy) * 180 / Math.PI;

            // Mean stress
            Results.MeanStress[e] = (sxx + syy + Results.Sigma3[e]) / 3;

            // Deviatoric stress
            double s1d = sxx - Results.MeanStress[e];
            double s2d = syy - Results.MeanStress[e];
            double s3d = Results.Sigma3[e] - Results.MeanStress[e];
            Results.DeviatoricStress[e] = Math.Sqrt(0.5 * (s1d * s1d + s2d * s2d + s3d * s3d + 2 * sxy * sxy));

            // Von Mises stress
            Results.VonMisesStress[e] = Math.Sqrt(sxx * sxx + syy * syy - sxx * syy + 3 * sxy * sxy);

            // Max shear stress
            Results.MaxShearStress[e] = R;

            // Octahedral shear stress
            Results.OctahedralStress[e] = Math.Sqrt(2) / 3 * Results.VonMisesStress[e];

            // Volumetric and shear strains
            Results.VolumetricStrain[e] = Results.StrainXX[e] + Results.StrainYY[e];
            Results.ShearStrain[e] = Results.StrainXY[e];

            // Yield index (safety factor)
            var material = Mesh.Materials.GetMaterial(Mesh.Elements[e].MaterialId);
            if (material != null)
            {
                double yieldFunc = material.EvaluateYieldFunction(Results.Sigma1[e], Results.Sigma2[e], 0);
                Results.YieldIndex[e] = yieldFunc;
            }
        }

        State.MaxStress = Results.VonMisesStress.Max();
    }

    private void UpdateNodePositions()
    {
        foreach (var node in Mesh.Nodes)
        {
            node.Position = node.InitialPosition + node.GetDisplacement();
        }

        // Invalidate element caches
        foreach (var element in Mesh.Elements)
        {
            element.InvalidateCache();
        }
    }

    private void RecordHistory(double time)
    {
        Results.History.Add((
            time,
            State.CurrentLoad,
            State.MaxDisplacement,
            State.MaxStress
        ));
    }

    #endregion

    #region Mesh Quality and Validation

    public void CheckMeshQuality()
    {
        var nodes = Mesh.Nodes.ToArray();
        int badElements = 0;
        double minArea = double.MaxValue;
        double maxAspect = 0;

        foreach (var element in Mesh.Elements)
        {
            double area = element.GetArea(nodes);
            if (area < 0)
            {
                badElements++;
                OnMessage?.Invoke($"Element {element.Id} has negative area (inverted)");
            }

            minArea = Math.Min(minArea, Math.Abs(area));

            // Check aspect ratio for triangles
            if (element is TriangleElement3 || element is TriangleElement6)
            {
                var p1 = nodes[element.NodeIds[0]].InitialPosition;
                var p2 = nodes[element.NodeIds[1]].InitialPosition;
                var p3 = nodes[element.NodeIds[2]].InitialPosition;

                double a = Vector2.Distance(p1, p2);
                double b = Vector2.Distance(p2, p3);
                double c = Vector2.Distance(p3, p1);
                double s = (a + b + c) / 2;
                double triArea = Math.Sqrt(s * (s - a) * (s - b) * (s - c));
                double aspect = Math.Max(a, Math.Max(b, c)) / (4 * triArea / (a + b + c));
                maxAspect = Math.Max(maxAspect, aspect);
            }
        }

        OnMessage?.Invoke($"Mesh quality: {badElements} bad elements, min area: {minArea:E2}, max aspect: {maxAspect:F2}");
    }

    #endregion
}
