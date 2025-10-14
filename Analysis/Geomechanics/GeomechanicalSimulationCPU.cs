// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulatorCPU.cs
// Proper Finite Element Method implementation for geomechanical analysis
// 
// References:
// [1] Zienkiewicz, O.C. & Taylor, R.L. (2000). The Finite Element Method, Volume 1: The Basis. Butterworth-Heinemann.
// [2] Hughes, T.J.R. (2000). The Finite Element Method: Linear Static and Dynamic Finite Element Analysis. Dover.
// [3] Bathe, K.J. (1996). Finite Element Procedures. Prentice Hall.
// [4] Beer, G. et al. (2010). The Boundary Element Method with Programming. Springer.
// [5] Bower, A.F. (2009). Applied Mechanics of Solids. CRC Press.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public class GeomechanicalSimulatorCPU
{
    private readonly GeomechanicalParameters _params;
    private List<int> _colIdx;
    private float[] _dirichletValue;

    // Global degrees of freedom (3 DOFs per node: ux, uy, uz)
    private float[] _displacement;

    // Material properties per element
    private float[] _elementE; // Young's modulus

    // Element connectivity (8 nodes per hexahedral element)
    private int[] _elementNodes;
    private float[] _elementNu; // Poisson's ratio
    private float[] _force;

    // Boundary condition flags
    private bool[] _isDirichletDOF;

    // Node to DOF mapping
    private int[] _nodeToDOF;

    // Node coordinates
    private float[] _nodeX, _nodeY, _nodeZ;
    private int _numDOFs;
    private int _numElements;

    // FEM mesh data
    private int _numNodes;

    // Sparse matrix storage (CSR format)
    private List<int> _rowPtr;
    private List<float> _values;

    public GeomechanicalSimulatorCPU(GeomechanicalParameters parameters)
    {
        _params = parameters;
    }

    public GeomechanicalResults Simulate(byte[,,] labels, float[,,] density,
        IProgress<float> progress, CancellationToken token)
    {
        var extent = _params.SimulationExtent;
        var startTime = DateTime.Now;

        try
        {
            // Step 1: Generate FEM mesh from voxel data
            progress?.Report(0.05f);
            GenerateMeshFromVoxels(labels, density);
            token.ThrowIfCancellationRequested();

            // Step 2: Assemble global stiffness matrix K
            progress?.Report(0.15f);
            AssembleGlobalStiffnessMatrix();
            token.ThrowIfCancellationRequested();

            // Step 3: Apply boundary conditions and loading
            progress?.Report(0.25f);
            ApplyBoundaryConditionsAndLoading();
            token.ThrowIfCancellationRequested();

            // Step 4: Solve linear system K*u = F using Conjugate Gradient
            progress?.Report(0.35f);
            var converged = SolveDisplacements(progress, token);
            token.ThrowIfCancellationRequested();

            // Step 5: Calculate strains from displacement gradients
            progress?.Report(0.75f);
            var results = CalculateStrainsAndStresses(labels, extent);
            results.Converged = converged;
            token.ThrowIfCancellationRequested();

            // Step 6: Calculate principal stresses
            progress?.Report(0.85f);
            CalculatePrincipalStresses(results, labels);
            token.ThrowIfCancellationRequested();

            // Step 7: Evaluate failure criteria
            progress?.Report(0.90f);
            EvaluateFailure(results, labels);
            token.ThrowIfCancellationRequested();

            // Step 8: Generate Mohr circles
            progress?.Report(0.95f);
            GenerateMohrCircles(results);

            results.ComputationTime = DateTime.Now - startTime;
            progress?.Report(1.0f);

            return results;
        }
        catch (Exception ex)
        {
            throw new Exception($"Simulation failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Generate hexahedral finite element mesh from voxel data.
    ///     Each solid voxel becomes one 8-node hexahedral element.
    ///     Reference: [1] Zienkiewicz & Taylor, Chapter 8 - 3D elements
    /// </summary>
    private void GenerateMeshFromVoxels(byte[,,] labels, float[,,] density)
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var dx = _params.PixelSize / 1e6f; // Convert to meters

        // Count solid voxels (elements)
        var elementCount = 0;
        for (var z = 0; z < d - 1; z++)
        for (var y = 0; y < h - 1; y++)
        for (var x = 0; x < w - 1; x++)
            // Check if this voxel and its neighbors form a solid element
            // Element exists if center voxel is solid
            if (labels[x, y, z] != 0)
                elementCount++;

        _numElements = elementCount;

        // Create node grid (nodes at voxel corners)
        _numNodes = w * h * d;
        _nodeX = new float[_numNodes];
        _nodeY = new float[_numNodes];
        _nodeZ = new float[_numNodes];

        var nodeIdx = 0;
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            _nodeX[nodeIdx] = x * dx;
            _nodeY[nodeIdx] = y * dx;
            _nodeZ[nodeIdx] = z * dx;
            nodeIdx++;
        }

        // Create element connectivity
        // Hexahedral element node ordering (right-hand rule):
        //     7-------6
        //    /|      /|
        //   4-+-----5 |
        //   | |     | |
        //   | 3-----+-2
        //   |/      |/
        //   0-------1
        // Reference: [1] Zienkiewicz, Fig 8.2

        _elementNodes = new int[_numElements * 8];
        _elementE = new float[_numElements];
        _elementNu = new float[_numElements];

        var elemIdx = 0;
        for (var z = 0; z < d - 1; z++)
        for (var y = 0; y < h - 1; y++)
        for (var x = 0; x < w - 1; x++)
        {
            if (labels[x, y, z] == 0) continue;

            // Node indices for this element
            var n0 = (z * h + y) * w + x;
            var n1 = (z * h + y) * w + x + 1;
            var n2 = (z * h + y + 1) * w + x + 1;
            var n3 = (z * h + y + 1) * w + x;
            var n4 = ((z + 1) * h + y) * w + x;
            var n5 = ((z + 1) * h + y) * w + x + 1;
            var n6 = ((z + 1) * h + y + 1) * w + x + 1;
            var n7 = ((z + 1) * h + y + 1) * w + x;

            _elementNodes[elemIdx * 8 + 0] = n0;
            _elementNodes[elemIdx * 8 + 1] = n1;
            _elementNodes[elemIdx * 8 + 2] = n2;
            _elementNodes[elemIdx * 8 + 3] = n3;
            _elementNodes[elemIdx * 8 + 4] = n4;
            _elementNodes[elemIdx * 8 + 5] = n5;
            _elementNodes[elemIdx * 8 + 6] = n6;
            _elementNodes[elemIdx * 8 + 7] = n7;

            // Assign material properties
            // Get material properties from library or defaults
            _elementE[elemIdx] = _params.YoungModulus * 1e6f; // Pa
            _elementNu[elemIdx] = _params.PoissonRatio;

            elemIdx++;
        }

        // Initialize DOF arrays
        _numDOFs = _numNodes * 3; // 3 DOFs per node (ux, uy, uz)
        _displacement = new float[_numDOFs];
        _force = new float[_numDOFs];
        _isDirichletDOF = new bool[_numDOFs];
        _dirichletValue = new float[_numDOFs];
        _nodeToDOF = new int[_numNodes];

        for (var i = 0; i < _numNodes; i++)
            _nodeToDOF[i] = i * 3;
    }

    /// <summary>
    ///     Assemble global stiffness matrix in CSR sparse format.
    ///     Uses standard FEM assembly procedure with element stiffness matrices.
    ///     Reference: [2] Hughes, Chapter 3 - Assembly of discrete equations
    ///     [3] Bathe, Section 6.2 - Formulation of FE equations
    /// </summary>
    private void AssembleGlobalStiffnessMatrix()
    {
        // Initialize sparse matrix in COO format (easier for assembly)
        var cooRow = new List<int>();
        var cooCol = new List<int>();
        var cooVal = new List<float>();

        // Gauss quadrature points for 2x2x2 integration
        // Reference: [1] Zienkiewicz, Table 8.1
        var gp = 1.0f / MathF.Sqrt(3.0f); // ±1/√3
        var gaussPoints = new (float xi, float eta, float zeta, float weight)[]
        {
            (-gp, -gp, -gp, 1.0f), (+gp, -gp, -gp, 1.0f),
            (+gp, +gp, -gp, 1.0f), (-gp, +gp, -gp, 1.0f),
            (-gp, -gp, +gp, 1.0f), (+gp, -gp, +gp, 1.0f),
            (+gp, +gp, +gp, 1.0f), (-gp, +gp, +gp, 1.0f)
        };

        // Assemble element by element
        for (var e = 0; e < _numElements; e++)
        {
            // Get element nodes
            var nodes = new int[8];
            for (var i = 0; i < 8; i++)
                nodes[i] = _elementNodes[e * 8 + i];

            // Get element coordinates
            float[] ex = new float[8], ey = new float[8], ez = new float[8];
            for (var i = 0; i < 8; i++)
            {
                ex[i] = _nodeX[nodes[i]];
                ey[i] = _nodeY[nodes[i]];
                ez[i] = _nodeZ[nodes[i]];
            }

            // Material properties
            var E = _elementE[e];
            var nu = _elementNu[e];

            // Compute elasticity matrix D (6x6 for 3D)
            // Reference: [5] Bower, Section 3.2.5 - Linear elastic constitutive equations
            var D = ComputeElasticityMatrix(E, nu);

            // Element stiffness matrix (24x24: 8 nodes × 3 DOFs)
            var Ke = new float[24, 24];

            // Numerical integration using Gauss quadrature
            foreach (var (xi, eta, zeta, w) in gaussPoints)
            {
                // Shape function derivatives in natural coordinates
                // Reference: [1] Zienkiewicz, Eq 8.12
                var dN_dxi = ComputeShapeFunctionDerivatives(xi, eta, zeta);

                // Jacobian matrix J = ∂x/∂ξ
                var J = ComputeJacobian(dN_dxi, ex, ey, ez);
                var detJ = Determinant3x3(J);

                if (detJ <= 0)
                    throw new Exception($"Negative Jacobian in element {e}: detJ = {detJ}");

                // Inverse Jacobian
                var Jinv = Inverse3x3(J);

                // Shape function derivatives in physical coordinates: ∂N/∂x = J⁻¹ · ∂N/∂ξ
                var dN_dx = MatrixMultiply(Jinv, dN_dxi);

                // Strain-displacement matrix B (6x24)
                // Reference: [3] Bathe, Eq 6.6
                var B = ComputeStrainDisplacementMatrix(dN_dx);

                // Element stiffness: Ke += Bᵀ D B det(J) w
                // Reference: [2] Hughes, Eq 3.2.20
                AddToElementStiffness(Ke, B, D, detJ * w);
            }

            // Assemble into global matrix
            for (var i = 0; i < 8; i++)
            for (var j = 0; j < 8; j++)
            for (var di = 0; di < 3; di++)
            for (var dj = 0; dj < 3; dj++)
            {
                var globalI = _nodeToDOF[nodes[i]] + di;
                var globalJ = _nodeToDOF[nodes[j]] + dj;
                var localI = i * 3 + di;
                var localJ = j * 3 + dj;

                var value = Ke[localI, localJ];
                if (MathF.Abs(value) > 1e-12f)
                {
                    cooRow.Add(globalI);
                    cooCol.Add(globalJ);
                    cooVal.Add(value);
                }
            }
        }

        // Convert COO to CSR format for efficient matrix-vector products
        ConvertCOOtoCSR(cooRow, cooCol, cooVal);
    }

    /// <summary>
    ///     Compute elasticity matrix D for 3D linear elasticity.
    ///     Reference: [5] Bower, Eq 3.2.19 - Isotropic elastic constitutive matrix
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[,] ComputeElasticityMatrix(float E, float nu)
    {
        // D matrix relates stresses to strains: {σ} = [D]{ε}
        // For 3D: {σ} = {σxx, σyy, σzz, τxy, τxz, τyz}ᵀ
        //         {ε} = {εxx, εyy, εzz, γxy, γxz, γyz}ᵀ

        var lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
        var mu = E / (2.0f * (1.0f + nu));
        var lambda_plus_2mu = lambda + 2.0f * mu;

        var D = new float[6, 6];

        // Normal stress-strain coupling
        D[0, 0] = lambda_plus_2mu;
        D[0, 1] = lambda;
        D[0, 2] = lambda;
        D[1, 0] = lambda;
        D[1, 1] = lambda_plus_2mu;
        D[1, 2] = lambda;
        D[2, 0] = lambda;
        D[2, 1] = lambda;
        D[2, 2] = lambda_plus_2mu;

        // Shear components
        D[3, 3] = mu;
        D[4, 4] = mu;
        D[5, 5] = mu;

        return D;
    }

    /// <summary>
    ///     Compute shape function derivatives for 8-node hexahedral element.
    ///     Reference: [1] Zienkiewicz, Eq 8.11 - Hexahedral shape functions
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[,] ComputeShapeFunctionDerivatives(float xi, float eta, float zeta)
    {
        // Shape functions: Ni = 1/8 (1 + ξiξ)(1 + ηiη)(1 + ζiζ)
        // where (ξi, ηi, ζi) are node coordinates in natural space (±1, ±1, ±1)

        var dN = new float[3, 8]; // [dN/dxi, dN/deta, dN/dzeta] for 8 nodes

        // Node coordinates in natural space
        var naturalCoords = new float[8, 3]
        {
            { -1, -1, -1 }, { +1, -1, -1 }, { +1, +1, -1 }, { -1, +1, -1 },
            { -1, -1, +1 }, { +1, -1, +1 }, { +1, +1, +1 }, { -1, +1, +1 }
        };

        for (var i = 0; i < 8; i++)
        {
            var xi_i = naturalCoords[i, 0];
            var eta_i = naturalCoords[i, 1];
            var zeta_i = naturalCoords[i, 2];

            // ∂Ni/∂ξ = 1/8 ξi(1 + ηiη)(1 + ζiζ)
            dN[0, i] = 0.125f * xi_i * (1.0f + eta_i * eta) * (1.0f + zeta_i * zeta);

            // ∂Ni/∂η = 1/8 (1 + ξiξ) ηi (1 + ζiζ)
            dN[1, i] = 0.125f * (1.0f + xi_i * xi) * eta_i * (1.0f + zeta_i * zeta);

            // ∂Ni/∂ζ = 1/8 (1 + ξiξ)(1 + ηiη) ζi
            dN[2, i] = 0.125f * (1.0f + xi_i * xi) * (1.0f + eta_i * eta) * zeta_i;
        }

        return dN;
    }

    /// <summary>
    ///     Compute Jacobian matrix J = ∂x/∂ξ.
    ///     Reference: [2] Hughes, Eq 3.2.8
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[,] ComputeJacobian(float[,] dN_dxi, float[] ex, float[] ey, float[] ez)
    {
        var J = new float[3, 3];

        for (var i = 0; i < 8; i++)
        {
            // J11 = ∂x/∂ξ, J12 = ∂y/∂ξ, J13 = ∂z/∂ξ
            J[0, 0] += dN_dxi[0, i] * ex[i];
            J[0, 1] += dN_dxi[0, i] * ey[i];
            J[0, 2] += dN_dxi[0, i] * ez[i];

            // J21 = ∂x/∂η, J22 = ∂y/∂η, J23 = ∂z/∂η
            J[1, 0] += dN_dxi[1, i] * ex[i];
            J[1, 1] += dN_dxi[1, i] * ey[i];
            J[1, 2] += dN_dxi[1, i] * ez[i];

            // J31 = ∂x/∂ζ, J32 = ∂y/∂ζ, J33 = ∂z/∂ζ
            J[2, 0] += dN_dxi[2, i] * ex[i];
            J[2, 1] += dN_dxi[2, i] * ey[i];
            J[2, 2] += dN_dxi[2, i] * ez[i];
        }

        return J;
    }

    /// <summary>
    ///     Compute strain-displacement matrix B.
    ///     Reference: [3] Bathe, Eq 6.6 - Strain-displacement transformation
    ///     Strain-displacement relations:
    ///     εxx = ∂ux/∂x,  εyy = ∂uy/∂y,  εzz = ∂uz/∂z
    ///     γxy = ∂ux/∂y + ∂uy/∂x
    ///     γxz = ∂ux/∂z + ∂uz/∂x
    ///     γyz = ∂uy/∂z + ∂uz/∂y
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float[,] ComputeStrainDisplacementMatrix(float[,] dN_dx)
    {
        var B = new float[6, 24]; // 6 strain components, 24 DOFs (8 nodes × 3)

        for (var i = 0; i < 8; i++)
        {
            var dNi_dx = dN_dx[0, i];
            var dNi_dy = dN_dx[1, i];
            var dNi_dz = dN_dx[2, i];

            var col = i * 3;

            // εxx = ∂ux/∂x
            B[0, col + 0] = dNi_dx;

            // εyy = ∂uy/∂y
            B[1, col + 1] = dNi_dy;

            // εzz = ∂uz/∂z
            B[2, col + 2] = dNi_dz;

            // γxy = ∂ux/∂y + ∂uy/∂x
            B[3, col + 0] = dNi_dy;
            B[3, col + 1] = dNi_dx;

            // γxz = ∂ux/∂z + ∂uz/∂x
            B[4, col + 0] = dNi_dz;
            B[4, col + 2] = dNi_dx;

            // γyz = ∂uy/∂z + ∂uz/∂y
            B[5, col + 1] = dNi_dz;
            B[5, col + 2] = dNi_dy;
        }

        return B;
    }

    /// <summary>
    ///     Add Bᵀ D B contribution to element stiffness matrix.
    ///     Reference: [2] Hughes, Eq 3.2.20
    /// </summary>
    private void AddToElementStiffness(float[,] Ke, float[,] B, float[,] D, float factor)
    {
        // Compute DB (6x24 matrix)
        var DB = new float[6, 24];
        for (var i = 0; i < 6; i++)
        for (var j = 0; j < 24; j++)
        {
            float sum = 0;
            for (var k = 0; k < 6; k++)
                sum += D[i, k] * B[k, j];
            DB[i, j] = sum;
        }

        // Compute Bᵀ(DB) and add to Ke
        for (var i = 0; i < 24; i++)
        for (var j = 0; j < 24; j++)
        {
            float sum = 0;
            for (var k = 0; k < 6; k++)
                sum += B[k, i] * DB[k, j];
            Ke[i, j] += sum * factor;
        }
    }

    /// <summary>
    ///     Apply boundary conditions and loading.
    ///     Boundary conditions:
    ///     1. Free surface: natural BC (traction-free), no constraint needed
    ///     2. Voids (non-material voxels): nodes adjacent to void are free unless loaded
    ///     3. Loading faces: apply distributed load converted to nodal forces
    ///     4. Constraint: fix minimum displacement in each direction to prevent rigid body motion
    ///     Reference: [3] Bathe, Section 8.2 - Boundary conditions
    /// </summary>
    private void ApplyBoundaryConditionsAndLoading()
    {
        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;

        // Identify loading direction based on loading mode
        // Convention: Z-direction (depth) is typically σ1 (maximum principal stress)
        // Reference: [4] Beer et al., Section 2.3 - Stress conventions in geomechanics

        var sigma1_Pa = _params.Sigma1 * 1e6f;
        var sigma2_Pa = _params.Sigma2 * 1e6f;
        var sigma3_Pa = _params.Sigma3 * 1e6f;

        // Apply effective stress if pore pressure is enabled
        // Reference: Terzaghi effective stress principle
        if (_params.UsePorePressure)
        {
            var pp_Pa = _params.PorePressure * 1e6f;
            var alpha = _params.BiotCoefficient;
            sigma1_Pa -= alpha * pp_Pa;
            sigma2_Pa -= alpha * pp_Pa;
            sigma3_Pa -= alpha * pp_Pa;
        }

        var dx = _params.PixelSize / 1e6f;
        var faceArea = dx * dx; // Area per node on a face

        // Apply loads on external faces
        // Top face (z = d-1): σ1 in negative z-direction (compression)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var nodeIdx = ((d - 1) * h + y) * w + x;
            var dofZ = _nodeToDOF[nodeIdx] + 2;

            // Nodal force = stress × area (distributed to nodes)
            // Corner nodes get 1/4, edge nodes get 1/2, face nodes get full
            var loadFactor = 0.25f; // Simplified: assume all 1/4
            _force[dofZ] -= sigma1_Pa * faceArea * loadFactor;
        }

        // Bottom face (z = 0): constrain or apply counter-pressure
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var nodeIdx = (0 * h + y) * w + x;
            var dofZ = _nodeToDOF[nodeIdx] + 2;

            // Fix z-displacement to prevent rigid body motion
            _isDirichletDOF[dofZ] = true;
            _dirichletValue[dofZ] = 0.0f;
        }

        // Y-direction faces: σ2
        // Front face (y = 0)
        for (var z = 0; z < d; z++)
        for (var x = 0; x < w; x++)
        {
            var nodeIdx = (z * h + 0) * w + x;
            var dofY = _nodeToDOF[nodeIdx] + 1;

            if (!_isDirichletDOF[dofY])
            {
                var loadFactor = 0.25f;
                _force[dofY] += sigma2_Pa * faceArea * loadFactor;
            }
        }

        // Back face (y = h-1)
        for (var z = 0; z < d; z++)
        for (var x = 0; x < w; x++)
        {
            var nodeIdx = (z * h + (h - 1)) * w + x;
            var dofY = _nodeToDOF[nodeIdx] + 1;

            if (!_isDirichletDOF[dofY])
            {
                var loadFactor = 0.25f;
                _force[dofY] -= sigma2_Pa * faceArea * loadFactor;
            }
        }

        // X-direction faces: σ3
        // Left face (x = 0): constrain to prevent rigid body motion
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        {
            var nodeIdx = (z * h + y) * w + 0;
            var dofX = _nodeToDOF[nodeIdx] + 0;

            _isDirichletDOF[dofX] = true;
            _dirichletValue[dofX] = 0.0f;
        }

        // Right face (x = w-1): apply σ3
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        {
            var nodeIdx = (z * h + y) * w + (w - 1);
            var dofX = _nodeToDOF[nodeIdx] + 0;

            if (!_isDirichletDOF[dofX])
            {
                var loadFactor = 0.25f;
                _force[dofX] -= sigma3_Pa * faceArea * loadFactor;
            }
        }

        // Fix one corner completely to prevent rigid body rotation
        // Reference: [3] Bathe, Section 8.2.3 - Constraints to prevent rigid body modes
        var cornerNode = 0; // Node (0,0,0)
        for (var i = 0; i < 3; i++)
        {
            _isDirichletDOF[_nodeToDOF[cornerNode] + i] = true;
            _dirichletValue[_nodeToDOF[cornerNode] + i] = 0.0f;
        }
    }

    /// <summary>
    ///     Solve K*u = F using Preconditioned Conjugate Gradient method.
    ///     Modified to handle Dirichlet boundary conditions.
    ///     Reference: [2] Hughes, Section 8.7 - Iterative solution methods
    ///     Reference: Shewchuk, J.R. (1994). "An Introduction to the Conjugate Gradient Method
    ///     Without the Agonizing Pain" - Carnegie Mellon technical report
    /// </summary>
    private bool SolveDisplacements(IProgress<float> progress, CancellationToken token)
    {
        var maxIter = _params.MaxIterations;
        var tolerance = _params.Tolerance;

        // Apply Dirichlet boundary conditions by modifying system
        // Set rows corresponding to constrained DOFs to identity
        ApplyDirichletBC();

        // Initial guess: u = 0
        Array.Clear(_displacement, 0, _numDOFs);

        // Set Dirichlet values
        for (var i = 0; i < _numDOFs; i++)
            if (_isDirichletDOF[i])
                _displacement[i] = _dirichletValue[i];

        // Residual: r = F - K*u
        var r = new float[_numDOFs];
        var Ku = new float[_numDOFs];
        MatrixVectorMultiply(Ku, _displacement);

        for (var i = 0; i < _numDOFs; i++)
            r[i] = _force[i] - Ku[i];

        // Preconditioner: M = diag(K) (Jacobi preconditioner)
        var M_inv = new float[_numDOFs];
        for (var i = 0; i < _numDOFs; i++)
        {
            var diag = GetDiagonalElement(i);
            M_inv[i] = diag > 1e-12f ? 1.0f / diag : 1.0f;
        }

        // z = M^(-1) * r
        var z = new float[_numDOFs];
        for (var i = 0; i < _numDOFs; i++)
            z[i] = M_inv[i] * r[i];

        // p = z
        var p = new float[_numDOFs];
        Array.Copy(z, p, _numDOFs);

        // rho = r^T * z
        var rho = DotProduct(r, z);
        var rho0 = rho;

        var converged = false;
        var iter = 0;

        while (iter < maxIter && !converged)
        {
            token.ThrowIfCancellationRequested();

            // q = K * p
            var q = new float[_numDOFs];
            MatrixVectorMultiply(q, p);

            // alpha = rho / (p^T * q)
            var pq = DotProduct(p, q);
            if (MathF.Abs(pq) < 1e-20f)
                break;

            var alpha = rho / pq;

            // u = u + alpha * p
            for (var i = 0; i < _numDOFs; i++)
                if (!_isDirichletDOF[i])
                    _displacement[i] += alpha * p[i];

            // r = r - alpha * q
            for (var i = 0; i < _numDOFs; i++)
                r[i] -= alpha * q[i];

            // Check convergence: ||r|| / ||r0||
            var residualNorm = VectorNorm(r);
            var relativeResidual = residualNorm / MathF.Sqrt(rho0);

            if (relativeResidual < tolerance)
            {
                converged = true;
                break;
            }

            // z = M^(-1) * r
            for (var i = 0; i < _numDOFs; i++)
                z[i] = M_inv[i] * r[i];

            // rho_new = r^T * z
            var rho_new = DotProduct(r, z);

            // beta = rho_new / rho
            var beta = rho_new / rho;

            // p = z + beta * p
            for (var i = 0; i < _numDOFs; i++)
                p[i] = z[i] + beta * p[i];

            rho = rho_new;
            iter++;

            if (iter % 10 == 0)
            {
                var prog = 0.35f + 0.4f * iter / maxIter;
                progress?.Report(prog);
            }
        }

        return converged;
    }

    /// <summary>
    ///     Apply Dirichlet boundary conditions to the system.
    ///     For constrained DOF i: set row i to identity, column i to zero except diagonal.
    /// </summary>
    private void ApplyDirichletBC()
    {
        for (var i = 0; i < _numDOFs; i++)
            if (_isDirichletDOF[i])
                // Modify force vector
                _force[i] = _dirichletValue[i];
        // Modify matrix: will be handled during matrix-vector multiply
    }

    /// <summary>
    ///     Sparse matrix-vector multiplication: y = K * x
    ///     Handles Dirichlet BC by treating constrained rows as identity.
    /// </summary>
    private void MatrixVectorMultiply(float[] y, float[] x)
    {
        Array.Clear(y, 0, _numDOFs);

        for (var row = 0; row < _numDOFs; row++)
        {
            if (_isDirichletDOF[row])
            {
                y[row] = x[row]; // Identity for constrained DOF
                continue;
            }

            var rowStart = _rowPtr[row];
            var rowEnd = _rowPtr[row + 1];

            float sum = 0;
            for (var j = rowStart; j < rowEnd; j++)
            {
                var col = _colIdx[j];

                // Skip contribution from constrained DOF
                if (_isDirichletDOF[col])
                    continue;

                sum += _values[j] * x[col];
            }

            y[row] = sum;
        }
    }

    /// <summary>
    ///     Get diagonal element of stiffness matrix.
    /// </summary>
    private float GetDiagonalElement(int row)
    {
        var rowStart = _rowPtr[row];
        var rowEnd = _rowPtr[row + 1];

        for (var j = rowStart; j < rowEnd; j++)
            if (_colIdx[j] == row)
                return _values[j];

        return 1.0f; // Default if not found
    }

    /// <summary>
    ///     Calculate strains and stresses from displacement field.
    ///     Uses finite element interpolation within each element.
    ///     Reference: [3] Bathe, Section 6.2.3 - Calculation of element strains and stresses
    /// </summary>
    private GeomechanicalResults CalculateStrainsAndStresses(byte[,,] labels, BoundingBox extent)
    {
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;

        var results = new GeomechanicalResults
        {
            StressXX = new float[w, h, d],
            StressYY = new float[w, h, d],
            StressZZ = new float[w, h, d],
            StressXY = new float[w, h, d],
            StressXZ = new float[w, h, d],
            StressYZ = new float[w, h, d],
            StrainXX = new float[w, h, d],
            StrainYY = new float[w, h, d],
            StrainZZ = new float[w, h, d],
            StrainXY = new float[w, h, d],
            StrainXZ = new float[w, h, d],
            StrainYZ = new float[w, h, d],
            Sigma1 = new float[w, h, d],
            Sigma2 = new float[w, h, d],
            Sigma3 = new float[w, h, d],
            FailureIndex = new float[w, h, d],
            DamageField = new byte[w, h, d],
            FractureField = new bool[w, h, d],
            MaterialLabels = labels,
            Parameters = _params
        };

        // For each element, compute strains and stresses at element center
        Parallel.For(0, _numElements, e =>
        {
            // Get element nodes
            var nodes = new int[8];
            for (var i = 0; i < 8; i++)
                nodes[i] = _elementNodes[e * 8 + i];

            // Get nodal displacements
            var ue = new float[24];
            for (var i = 0; i < 8; i++)
            {
                var dofBase = _nodeToDOF[nodes[i]];
                ue[i * 3 + 0] = _displacement[dofBase + 0];
                ue[i * 3 + 1] = _displacement[dofBase + 1];
                ue[i * 3 + 2] = _displacement[dofBase + 2];
            }

            // Get element coordinates
            float[] ex = new float[8], ey = new float[8], ez = new float[8];
            for (var i = 0; i < 8; i++)
            {
                ex[i] = _nodeX[nodes[i]];
                ey[i] = _nodeY[nodes[i]];
                ez[i] = _nodeZ[nodes[i]];
            }

            // Evaluate at element center (ξ=0, η=0, ζ=0)
            var dN_dxi = ComputeShapeFunctionDerivatives(0, 0, 0);
            var J = ComputeJacobian(dN_dxi, ex, ey, ez);
            var Jinv = Inverse3x3(J);
            var dN_dx = MatrixMultiply(Jinv, dN_dxi);
            var B = ComputeStrainDisplacementMatrix(dN_dx);

            // Compute strain: {ε} = [B]{u}
            var strain = new float[6];
            for (var i = 0; i < 6; i++)
            {
                float sum = 0;
                for (var j = 0; j < 24; j++)
                    sum += B[i, j] * ue[j];
                strain[i] = sum;
            }

            // Compute stress: {σ} = [D]{ε}
            var E = _elementE[e];
            var nu = _elementNu[e];
            var D = ComputeElasticityMatrix(E, nu);

            var stress = new float[6];
            for (var i = 0; i < 6; i++)
            {
                float sum = 0;
                for (var j = 0; j < 6; j++)
                    sum += D[i, j] * strain[j];
                stress[i] = sum;
            }

            // Find voxel corresponding to element center
            float cx = 0, cy = 0, cz = 0;
            for (var i = 0; i < 8; i++)
            {
                cx += ex[i];
                cy += ey[i];
                cz += ez[i];
            }

            cx /= 8.0f;
            cy /= 8.0f;
            cz /= 8.0f;

            var dx = _params.PixelSize / 1e6f;
            var vx = (int)(cx / dx);
            var vy = (int)(cy / dx);
            var vz = (int)(cz / dx);

            if (vx >= 0 && vx < w && vy >= 0 && vy < h && vz >= 0 && vz < d)
            {
                results.StrainXX[vx, vy, vz] = strain[0];
                results.StrainYY[vx, vy, vz] = strain[1];
                results.StrainZZ[vx, vy, vz] = strain[2];
                results.StrainXY[vx, vy, vz] = strain[3];
                results.StrainXZ[vx, vy, vz] = strain[4];
                results.StrainYZ[vx, vy, vz] = strain[5];

                results.StressXX[vx, vy, vz] = stress[0];
                results.StressYY[vx, vy, vz] = stress[1];
                results.StressZZ[vx, vy, vz] = stress[2];
                results.StressXY[vx, vy, vz] = stress[3];
                results.StressXZ[vx, vy, vz] = stress[4];
                results.StressYZ[vx, vy, vz] = stress[5];
            }
        });

        return results;
    }

    // Principal stress calculation - keep existing robust implementation
    private void CalculatePrincipalStresses(GeomechanicalResults results, byte[,,] labels)
    {
        var w = results.StressXX.GetLength(0);
        var h = results.StressXX.GetLength(1);
        var d = results.StressXX.GetLength(2);

        Parallel.For(0, d, z =>
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (labels[x, y, z] == 0) continue;

                var sxx = results.StressXX[x, y, z];
                var syy = results.StressYY[x, y, z];
                var szz = results.StressZZ[x, y, z];
                var sxy = results.StressXY[x, y, z];
                var sxz = results.StressXZ[x, y, z];
                var syz = results.StressYZ[x, y, z];

                var principals = CalculatePrincipalValues(sxx, syy, szz, sxy, sxz, syz);

                results.Sigma1[x, y, z] = principals.sigma1;
                results.Sigma2[x, y, z] = principals.sigma2;
                results.Sigma3[x, y, z] = principals.sigma3;
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (float sigma1, float sigma2, float sigma3) CalculatePrincipalValues(
        float sxx, float syy, float szz, float sxy, float sxz, float syz)
    {
        var I1 = sxx + syy + szz;
        var I2 = sxx * syy + syy * szz + szz * sxx - sxy * sxy - sxz * sxz - syz * syz;
        var I3 = sxx * syy * szz + 2 * sxy * sxz * syz - sxx * syz * syz - syy * sxz * sxz - szz * sxy * sxy;

        var p = I2 - I1 * I1 / 3.0f;
        var q = I3 + (2.0f * I1 * I1 * I1 - 9.0f * I1 * I2) / 27.0f;

        float sigma1, sigma2, sigma3;

        if (MathF.Abs(p) < 1e-9f)
        {
            sigma1 = sigma2 = sigma3 = I1 / 3.0f;
        }
        else
        {
            var half_q = q * 0.5f;
            var term_under_sqrt = -p * p * p / 27.0f;

            if (term_under_sqrt < 0) term_under_sqrt = 0;

            var r = MathF.Sqrt(term_under_sqrt);
            var cos_phi = Math.Clamp(-half_q / r, -1.0f, 1.0f);
            var phi = MathF.Acos(cos_phi);

            var scale = 2.0f * MathF.Sqrt(-p / 3.0f);
            var offset = I1 / 3.0f;

            sigma1 = offset + scale * MathF.Cos(phi / 3.0f);
            sigma2 = offset + scale * MathF.Cos((phi + 2.0f * MathF.PI) / 3.0f);
            sigma3 = offset + scale * MathF.Cos((phi + 4.0f * MathF.PI) / 3.0f);
        }

        if (sigma1 < sigma2) (sigma1, sigma2) = (sigma2, sigma1);
        if (sigma1 < sigma3) (sigma1, sigma3) = (sigma3, sigma1);
        if (sigma2 < sigma3) (sigma2, sigma3) = (sigma3, sigma2);

        return (sigma1, sigma2, sigma3);
    }

    // Keep existing failure evaluation and Mohr circle generation
    private void EvaluateFailure(GeomechanicalResults results, byte[,,] labels)
    {
        var w = results.StressXX.GetLength(0);
        var h = results.StressXX.GetLength(1);
        var d = results.StressXX.GetLength(2);

        var cohesion_Pa = _params.Cohesion * 1e6f;
        var phi = _params.FrictionAngle * MathF.PI / 180f;
        var tensileStrength_Pa = _params.TensileStrength * 1e6f;

        var failedCount = 0;
        var totalCount = 0;

        Parallel.For(0, d, z =>
        {
            var localFailed = 0;
            var localTotal = 0;

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (labels[x, y, z] == 0) continue;

                localTotal++;

                var sigma1 = results.Sigma1[x, y, z];
                var sigma3 = results.Sigma3[x, y, z];

                if (_params.UsePorePressure)
                {
                    var pp = _params.PorePressure * 1e6f;
                    sigma1 -= _params.BiotCoefficient * pp;
                    sigma3 -= _params.BiotCoefficient * pp;
                }

                float failureIndex = 0;

                switch (_params.FailureCriterion)
                {
                    case FailureCriterion.MohrCoulomb:
                        var left = sigma1 - sigma3;
                        var right = 2 * cohesion_Pa * MathF.Cos(phi) + (sigma1 + sigma3) * MathF.Sin(phi);
                        failureIndex = right > 1e-9f ? left / right : left;
                        break;

                    case FailureCriterion.DruckerPrager:
                        var p = (sigma1 + sigma3) / 2;
                        var q = (sigma1 - sigma3) / 2;
                        var alpha = 2 * MathF.Sin(phi) / (3 - MathF.Sin(phi));
                        var k = 6 * cohesion_Pa * MathF.Cos(phi) / (3 - MathF.Sin(phi));
                        failureIndex = k > 1e-9f ? (q - alpha * p) / k : q - alpha * p;
                        break;

                    case FailureCriterion.HoekBrown:
                        var ucs_Pa = 2 * cohesion_Pa * MathF.Cos(phi) / (1 - MathF.Sin(phi));
                        var mb = _params.HoekBrown_mb;
                        var s = _params.HoekBrown_s;
                        var a = _params.HoekBrown_a;
                        var strength = ucs_Pa * MathF.Pow(mb * sigma3 / ucs_Pa + s, a);
                        var failure_stress = sigma3 + strength;
                        failureIndex = failure_stress > 1e-9f ? sigma1 / failure_stress : sigma1;
                        break;

                    case FailureCriterion.Griffith:
                        if (sigma3 < 0)
                            failureIndex = tensileStrength_Pa > 1e-9f ? -sigma3 / tensileStrength_Pa : -sigma3;
                        else
                            failureIndex = tensileStrength_Pa * 8 > 1e-9f
                                ? (sigma1 - sigma3) / (8 * tensileStrength_Pa)
                                : sigma1 - sigma3;
                        break;
                }

                results.FailureIndex[x, y, z] = failureIndex;

                if (failureIndex >= 1.0f)
                {
                    results.FractureField[x, y, z] = true;
                    results.DamageField[x, y, z] = 255;
                    localFailed++;
                }
                else if (failureIndex >= _params.DamageThreshold)
                {
                    var damage = (failureIndex - _params.DamageThreshold) / (1.0f - _params.DamageThreshold);
                    results.DamageField[x, y, z] = (byte)(damage * 255);
                }
            }

            lock (results)
            {
                failedCount += localFailed;
                totalCount += localTotal;
            }
        });

        results.FailedVoxels = failedCount;
        results.TotalVoxels = totalCount;
        results.FailedVoxelPercentage = totalCount > 0 ? 100f * failedCount / totalCount : 0;
    }

    private void GenerateMohrCircles(GeomechanicalResults results)
    {
        var w = results.Sigma1.GetLength(0);
        var h = results.Sigma1.GetLength(1);
        var d = results.Sigma1.GetLength(2);

        var locations = new List<(string name, int x, int y, int z)>
        {
            ("Center", w / 2, h / 2, d / 2),
            ("Top", w / 2, h / 2, d - 1),
            ("Bottom", w / 2, h / 2, 0)
        };

        var maxStressValue = float.MinValue;
        int maxX = 0, maxY = 0, maxZ = 0;
        var maxStressLocationFound = false;

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (results.MaterialLabels[x, y, z] == 0) continue;

            var stress = results.Sigma1[x, y, z];
            if (stress > maxStressValue)
            {
                maxStressValue = stress;
                maxX = x;
                maxY = y;
                maxZ = z;
                maxStressLocationFound = true;
            }
        }

        if (maxStressLocationFound) locations.Add(("Max Stress", maxX, maxY, maxZ));

        foreach (var (name, x, y, z) in locations)
        {
            if (x < 0 || x >= w || y < 0 || y >= h || z < 0 || z >= d || results.MaterialLabels[x, y, z] == 0)
                continue;

            var sigma1 = results.Sigma1[x, y, z];
            var sigma2 = results.Sigma2[x, y, z];
            var sigma3 = results.Sigma3[x, y, z];
            var hasFailed = results.FractureField[x, y, z];

            var circle = new MohrCircleData
            {
                Location = name,
                Position = new Vector3(x, y, z),
                Sigma1 = sigma1 / 1e6f,
                Sigma2 = sigma2 / 1e6f,
                Sigma3 = sigma3 / 1e6f,
                MaxShearStress = (sigma1 - sigma3) / (2 * 1e6f),
                HasFailed = hasFailed
            };

            var phi_rad = _params.FrictionAngle * MathF.PI / 180f;
            var failureAngle_rad = MathF.PI / 4 + phi_rad / 2;
            circle.FailureAngle = failureAngle_rad * 180f / MathF.PI;

            if (hasFailed)
            {
                var two_theta = 2 * failureAngle_rad;
                circle.NormalStressAtFailure =
                    ((sigma1 + sigma3) / 2 + (sigma1 - sigma3) / 2 * MathF.Cos(two_theta)) / 1e6f;
                circle.ShearStressAtFailure = (sigma1 - sigma3) / 2 * MathF.Sin(two_theta) / 1e6f;
            }

            results.MohrCircles.Add(circle);
        }
    }

    // Matrix utility functions
    private float[,] MatrixMultiply(float[,] A, float[,] B)
    {
        var m = A.GetLength(0);
        var n = B.GetLength(1);
        var k = A.GetLength(1);
        var C = new float[m, n];

        for (var i = 0; i < m; i++)
        for (var j = 0; j < n; j++)
        for (var p = 0; p < k; p++)
            C[i, j] += A[i, p] * B[p, j];

        return C;
    }

    private float Determinant3x3(float[,] m)
    {
        return m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
               - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
               + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
    }

    private float[,] Inverse3x3(float[,] m)
    {
        var det = Determinant3x3(m);
        if (MathF.Abs(det) < 1e-12f)
            throw new Exception("Singular matrix");

        var invDet = 1.0f / det;
        var inv = new float[3, 3];

        inv[0, 0] = (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) * invDet;
        inv[0, 1] = (m[0, 2] * m[2, 1] - m[0, 1] * m[2, 2]) * invDet;
        inv[0, 2] = (m[0, 1] * m[1, 2] - m[0, 2] * m[1, 1]) * invDet;
        inv[1, 0] = (m[1, 2] * m[2, 0] - m[1, 0] * m[2, 2]) * invDet;
        inv[1, 1] = (m[0, 0] * m[2, 2] - m[0, 2] * m[2, 0]) * invDet;
        inv[1, 2] = (m[0, 2] * m[1, 0] - m[0, 0] * m[1, 2]) * invDet;
        inv[2, 0] = (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]) * invDet;
        inv[2, 1] = (m[0, 1] * m[2, 0] - m[0, 0] * m[2, 1]) * invDet;
        inv[2, 2] = (m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]) * invDet;

        return inv;
    }

    private void ConvertCOOtoCSR(List<int> cooRow, List<int> cooCol, List<float> cooVal)
    {
        var nnz = cooRow.Count;

        // Sort by row, then column
        var indices = Enumerable.Range(0, nnz).ToArray();
        Array.Sort(indices, (a, b) =>
        {
            var cmp = cooRow[a].CompareTo(cooRow[b]);
            if (cmp == 0) cmp = cooCol[a].CompareTo(cooCol[b]);
            return cmp;
        });

        // Build CSR
        _rowPtr = new List<int>(_numDOFs + 1);
        _colIdx = new List<int>(nnz);
        _values = new List<float>(nnz);

        var currentRow = 0;
        _rowPtr.Add(0);

        for (var i = 0; i < nnz; i++)
        {
            var idx = indices[i];
            var row = cooRow[idx];
            var col = cooCol[idx];
            var val = cooVal[idx];

            // Fill row pointers for empty rows
            while (currentRow < row)
            {
                currentRow++;
                _rowPtr.Add(_colIdx.Count);
            }

            // Add element (combine duplicates)
            if (_colIdx.Count > 0 && _colIdx[_colIdx.Count - 1] == col)
            {
                _values[_values.Count - 1] += val;
            }
            else
            {
                _colIdx.Add(col);
                _values.Add(val);
            }
        }

        // Fill remaining row pointers
        while (currentRow < _numDOFs)
        {
            currentRow++;
            _rowPtr.Add(_colIdx.Count);
        }
    }

    private float DotProduct(float[] a, float[] b)
    {
        float sum = 0;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    private float VectorNorm(float[] v)
    {
        return MathF.Sqrt(DotProduct(v, v));
    }
}