// GeoscientistToolkit/Data/TwoDGeology/Geomechanics/FEMMesh2D.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Data.TwoDGeology.Geomechanics;

/// <summary>
/// Element types for 2D FEM analysis
/// </summary>
public enum ElementType2D
{
    Triangle3,      // 3-node linear triangle (CST)
    Triangle6,      // 6-node quadratic triangle (LST)
    Quad4,          // 4-node bilinear quadrilateral
    Quad8,          // 8-node serendipity quadrilateral
    Quad9,          // 9-node Lagrangian quadrilateral
    Interface2,     // 2-node interface/joint element
    Interface4,     // 4-node interface element
    Beam2,          // 2-node beam element
    Truss2          // 2-node truss element
}

/// <summary>
/// Node for 2D FEM mesh
/// </summary>
public class FEMNode2D
{
    public int Id { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 InitialPosition { get; set; }

    // Degrees of freedom (displacement)
    public double Ux { get; set; }
    public double Uy { get; set; }

    // Velocity (for dynamic analysis)
    public double Vx { get; set; }
    public double Vy { get; set; }

    // Acceleration (for dynamic analysis)
    public double Ax { get; set; }
    public double Ay { get; set; }

    // Forces
    public double Fx { get; set; }
    public double Fy { get; set; }

    // Boundary conditions
    public bool FixedX { get; set; }
    public bool FixedY { get; set; }
    public double? PrescribedUx { get; set; }
    public double? PrescribedUy { get; set; }

    // Pore pressure (for coupled analysis)
    public double PorePressure { get; set; }

    // Temperature (for thermo-mechanical)
    public double Temperature { get; set; }

    // Global DOF indices
    public int GlobalDofX { get; set; } = -1;
    public int GlobalDofY { get; set; } = -1;

    public FEMNode2D(int id, Vector2 position)
    {
        Id = id;
        Position = position;
        InitialPosition = position;
    }

    public Vector2 GetDisplacement() => new((float)Ux, (float)Uy);
    public Vector2 GetVelocity() => new((float)Vx, (float)Vy);
    public Vector2 GetCurrentPosition() => InitialPosition + GetDisplacement();

    public void ApplyDisplacement(double ux, double uy)
    {
        Ux = ux;
        Uy = uy;
        Position = InitialPosition + new Vector2((float)ux, (float)uy);
    }

    public void Reset()
    {
        Ux = Uy = 0;
        Vx = Vy = 0;
        Ax = Ay = 0;
        Fx = Fy = 0;
        Position = InitialPosition;
    }
}

/// <summary>
/// Base class for 2D FEM elements
/// </summary>
public abstract class FEMElement2D
{
    public int Id { get; set; }
    public ElementType2D Type { get; protected set; }
    public int MaterialId { get; set; }
    public List<int> NodeIds { get; set; } = new();
    public double Thickness { get; set; } = 1.0; // Out-of-plane thickness

    // State variables
    public double[,] Stress { get; set; }           // Stress at integration points
    public double[,] Strain { get; set; }           // Strain at integration points
    public double[] PlasticStrain { get; set; }     // Equivalent plastic strain at IPs
    public double[] Damage { get; set; }            // Damage at integration points
    public bool[] HasYielded { get; set; }          // Yield flag at IPs
    public bool HasFailed { get; set; }

    // Integration points (Gauss quadrature)
    protected abstract (double xi, double eta, double weight)[] GaussPoints { get; }
    public int NumIntegrationPoints => GaussPoints.Length;

    // Geometry cache
    protected double _area;
    protected Vector2 _centroid;
    protected bool _geometryCached;

    /// <summary>
    /// Calculate element stiffness matrix [K] (local)
    /// </summary>
    public abstract double[,] GetStiffnessMatrix(FEMNode2D[] nodes, GeomechanicalMaterial2D material);

    /// <summary>
    /// Calculate element mass matrix [M] (consistent or lumped)
    /// </summary>
    public abstract double[,] GetMassMatrix(FEMNode2D[] nodes, double density);

    /// <summary>
    /// Calculate strain at a point from nodal displacements
    /// </summary>
    public abstract double[] GetStrain(FEMNode2D[] nodes, double xi, double eta);

    /// <summary>
    /// Calculate stress at a point from strain
    /// </summary>
    public double[] GetStress(double[] strain, GeomechanicalMaterial2D material, int ipIndex)
    {
        var D = material.GetPlaneStrainElasticityMatrix();
        var stress = new double[3];

        for (int i = 0; i < 3; i++)
        {
            stress[i] = 0;
            for (int j = 0; j < 3; j++)
            {
                stress[i] += D[i, j] * strain[j];
            }
        }

        // Apply plasticity if enabled
        if (material.EnableSoftening || material.HardeningLaw != HardeningSofteningLaw.None)
        {
            double plasticStrain = PlasticStrain?[ipIndex] ?? 0;
            var (sx, sy, txy, dEps) = material.PlasticReturn(stress[0], stress[1], stress[2], plasticStrain);
            stress[0] = sx;
            stress[1] = sy;
            stress[2] = txy;

            if (PlasticStrain != null && dEps > 0)
            {
                PlasticStrain[ipIndex] += dEps;
                HasYielded[ipIndex] = true;
            }
        }

        return stress;
    }

    /// <summary>
    /// Calculate element body force vector
    /// </summary>
    public abstract double[] GetBodyForceVector(FEMNode2D[] nodes, Vector2 bodyForce);

    /// <summary>
    /// Calculate internal force vector from current stress state
    /// </summary>
    public abstract double[] GetInternalForceVector(FEMNode2D[] nodes);

    /// <summary>
    /// Get shape function values at natural coordinates (xi, eta)
    /// </summary>
    protected abstract double[] GetShapeFunctions(double xi, double eta);

    /// <summary>
    /// Get shape function derivatives w.r.t. natural coordinates
    /// </summary>
    protected abstract double[,] GetShapeFunctionDerivatives(double xi, double eta);

    /// <summary>
    /// Get Jacobian matrix and its determinant
    /// </summary>
    protected (double[,] J, double detJ) GetJacobian(FEMNode2D[] nodes, double xi, double eta)
    {
        var dN = GetShapeFunctionDerivatives(xi, eta);
        var J = new double[2, 2];

        for (int i = 0; i < NodeIds.Count; i++)
        {
            var node = nodes[NodeIds[i]];
            J[0, 0] += dN[i, 0] * node.InitialPosition.X;
            J[0, 1] += dN[i, 0] * node.InitialPosition.Y;
            J[1, 0] += dN[i, 1] * node.InitialPosition.X;
            J[1, 1] += dN[i, 1] * node.InitialPosition.Y;
        }

        double detJ = J[0, 0] * J[1, 1] - J[0, 1] * J[1, 0];
        return (J, detJ);
    }

    /// <summary>
    /// Get B matrix (strain-displacement) at natural coordinates
    /// </summary>
    protected double[,] GetBMatrix(FEMNode2D[] nodes, double xi, double eta)
    {
        var dN = GetShapeFunctionDerivatives(xi, eta);
        var (J, detJ) = GetJacobian(nodes, xi, eta);

        // Inverse of Jacobian
        double invDetJ = 1.0 / detJ;
        var Jinv = new double[2, 2]
        {
            { J[1, 1] * invDetJ, -J[0, 1] * invDetJ },
            { -J[1, 0] * invDetJ, J[0, 0] * invDetJ }
        };

        // Transform derivatives to physical coordinates
        var dNdx = new double[NodeIds.Count, 2];
        for (int i = 0; i < NodeIds.Count; i++)
        {
            dNdx[i, 0] = Jinv[0, 0] * dN[i, 0] + Jinv[0, 1] * dN[i, 1];
            dNdx[i, 1] = Jinv[1, 0] * dN[i, 0] + Jinv[1, 1] * dN[i, 1];
        }

        // Build B matrix [3 x 2*nNodes]
        int nDof = NodeIds.Count * 2;
        var B = new double[3, nDof];

        for (int i = 0; i < NodeIds.Count; i++)
        {
            B[0, 2 * i] = dNdx[i, 0];         // ∂Ni/∂x
            B[1, 2 * i + 1] = dNdx[i, 1];     // ∂Ni/∂y
            B[2, 2 * i] = dNdx[i, 1];         // ∂Ni/∂y
            B[2, 2 * i + 1] = dNdx[i, 0];     // ∂Ni/∂x
        }

        return B;
    }

    /// <summary>
    /// Calculate element area
    /// </summary>
    public double GetArea(FEMNode2D[] nodes)
    {
        if (_geometryCached) return _area;
        CacheGeometry(nodes);
        return _area;
    }

    /// <summary>
    /// Calculate element centroid
    /// </summary>
    public Vector2 GetCentroid(FEMNode2D[] nodes)
    {
        if (_geometryCached) return _centroid;
        CacheGeometry(nodes);
        return _centroid;
    }

    protected virtual void CacheGeometry(FEMNode2D[] nodes)
    {
        // Numerical integration for area
        _area = 0;
        _centroid = Vector2.Zero;

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var (_, detJ) = GetJacobian(nodes, xi, eta);
            var N = GetShapeFunctions(xi, eta);

            _area += w * detJ;

            for (int i = 0; i < NodeIds.Count; i++)
            {
                _centroid += nodes[NodeIds[i]].InitialPosition * (float)(N[i] * w * detJ);
            }
        }

        if (_area > 0) _centroid /= (float)_area;
        _geometryCached = true;
    }

    public void InvalidateCache()
    {
        _geometryCached = false;
    }

    public void InitializeState()
    {
        int nIP = NumIntegrationPoints;
        Stress = new double[nIP, 3];
        Strain = new double[nIP, 3];
        PlasticStrain = new double[nIP];
        Damage = new double[nIP];
        HasYielded = new bool[nIP];
        HasFailed = false;
    }
}

/// <summary>
/// 3-node triangular element (Constant Strain Triangle - CST)
/// </summary>
public class TriangleElement3 : FEMElement2D
{
    public TriangleElement3()
    {
        Type = ElementType2D.Triangle3;
    }

    protected override (double xi, double eta, double weight)[] GaussPoints => new[]
    {
        (1.0/3.0, 1.0/3.0, 0.5) // Single point at centroid
    };

    protected override double[] GetShapeFunctions(double xi, double eta)
    {
        // Area coordinates: L1 = 1-xi-eta, L2 = xi, L3 = eta
        return new[] { 1 - xi - eta, xi, eta };
    }

    protected override double[,] GetShapeFunctionDerivatives(double xi, double eta)
    {
        // dN/dξ, dN/dη
        return new double[,]
        {
            { -1, -1 },  // dN1/dξ, dN1/dη
            {  1,  0 },  // dN2/dξ, dN2/dη
            {  0,  1 }   // dN3/dξ, dN3/dη
        };
    }

    public override double[,] GetStiffnessMatrix(FEMNode2D[] nodes, GeomechanicalMaterial2D material)
    {
        int nDof = 6; // 3 nodes × 2 DOF
        var K = new double[nDof, nDof];
        var D = material.GetPlaneStrainElasticityMatrix();

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var B = GetBMatrix(nodes, xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            // K += B^T * D * B * detJ * w * thickness
            for (int i = 0; i < nDof; i++)
            {
                for (int j = 0; j < nDof; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        for (int l = 0; l < 3; l++)
                        {
                            sum += B[k, i] * D[k, l] * B[l, j];
                        }
                    }
                    K[i, j] += sum * detJ * w * Thickness;
                }
            }
        }

        return K;
    }

    public override double[,] GetMassMatrix(FEMNode2D[] nodes, double density)
    {
        int nDof = 6;
        var M = new double[nDof, nDof];

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var N = GetShapeFunctions(xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            // Consistent mass matrix: M_ij = ρ * ∫ Ni * Nj dV
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double mass = density * N[i] * N[j] * detJ * w * Thickness;
                    M[2 * i, 2 * j] += mass;
                    M[2 * i + 1, 2 * j + 1] += mass;
                }
            }
        }

        return M;
    }

    public override double[] GetStrain(FEMNode2D[] nodes, double xi, double eta)
    {
        var B = GetBMatrix(nodes, xi, eta);
        var strain = new double[3];

        for (int i = 0; i < 3; i++) // 3 nodes
        {
            int nodeId = NodeIds[i];
            strain[0] += B[0, 2 * i] * nodes[nodeId].Ux + B[0, 2 * i + 1] * nodes[nodeId].Uy;
            strain[1] += B[1, 2 * i] * nodes[nodeId].Ux + B[1, 2 * i + 1] * nodes[nodeId].Uy;
            strain[2] += B[2, 2 * i] * nodes[nodeId].Ux + B[2, 2 * i + 1] * nodes[nodeId].Uy;
        }

        return strain;
    }

    public override double[] GetBodyForceVector(FEMNode2D[] nodes, Vector2 bodyForce)
    {
        var f = new double[6];
        double area = GetArea(nodes);

        // For CST, body force is distributed equally to all nodes
        double fx = bodyForce.X * area * Thickness / 3;
        double fy = bodyForce.Y * area * Thickness / 3;

        for (int i = 0; i < 3; i++)
        {
            f[2 * i] = fx;
            f[2 * i + 1] = fy;
        }

        return f;
    }

    public override double[] GetInternalForceVector(FEMNode2D[] nodes)
    {
        var f = new double[6];

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var B = GetBMatrix(nodes, xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            // Get stress at this integration point
            double[] stress = { Stress[0, 0], Stress[0, 1], Stress[0, 2] };

            // f = B^T * σ * detJ * w * thickness
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    f[i] += B[j, i] * stress[j] * detJ * w * Thickness;
                }
            }
        }

        return f;
    }
}

/// <summary>
/// 6-node quadratic triangular element (Linear Strain Triangle - LST)
/// </summary>
public class TriangleElement6 : FEMElement2D
{
    public TriangleElement6()
    {
        Type = ElementType2D.Triangle6;
    }

    // 3-point Gauss quadrature for triangle
    protected override (double xi, double eta, double weight)[] GaussPoints => new[]
    {
        (1.0/6.0, 1.0/6.0, 1.0/6.0),
        (2.0/3.0, 1.0/6.0, 1.0/6.0),
        (1.0/6.0, 2.0/3.0, 1.0/6.0)
    };

    protected override double[] GetShapeFunctions(double xi, double eta)
    {
        double L1 = 1 - xi - eta;
        double L2 = xi;
        double L3 = eta;

        return new[]
        {
            L1 * (2 * L1 - 1),      // N1 - corner
            L2 * (2 * L2 - 1),      // N2 - corner
            L3 * (2 * L3 - 1),      // N3 - corner
            4 * L1 * L2,            // N4 - midside
            4 * L2 * L3,            // N5 - midside
            4 * L3 * L1             // N6 - midside
        };
    }

    protected override double[,] GetShapeFunctionDerivatives(double xi, double eta)
    {
        double L1 = 1 - xi - eta;

        return new double[,]
        {
            { 4*xi + 4*eta - 3, 4*xi + 4*eta - 3 },  // dN1
            { 4*xi - 1, 0 },                          // dN2
            { 0, 4*eta - 1 },                         // dN3
            { 4 - 8*xi - 4*eta, -4*xi },              // dN4
            { 4*eta, 4*xi },                          // dN5
            { -4*eta, 4 - 4*xi - 8*eta }              // dN6
        };
    }

    public override double[,] GetStiffnessMatrix(FEMNode2D[] nodes, GeomechanicalMaterial2D material)
    {
        int nDof = 12; // 6 nodes × 2 DOF
        var K = new double[nDof, nDof];
        var D = material.GetPlaneStrainElasticityMatrix();

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var B = GetBMatrix(nodes, xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            for (int i = 0; i < nDof; i++)
            {
                for (int j = 0; j < nDof; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        for (int l = 0; l < 3; l++)
                        {
                            sum += B[k, i] * D[k, l] * B[l, j];
                        }
                    }
                    K[i, j] += sum * detJ * w * Thickness;
                }
            }
        }

        return K;
    }

    public override double[,] GetMassMatrix(FEMNode2D[] nodes, double density)
    {
        int nDof = 12;
        var M = new double[nDof, nDof];

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var N = GetShapeFunctions(xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    double mass = density * N[i] * N[j] * detJ * w * Thickness;
                    M[2 * i, 2 * j] += mass;
                    M[2 * i + 1, 2 * j + 1] += mass;
                }
            }
        }

        return M;
    }

    public override double[] GetStrain(FEMNode2D[] nodes, double xi, double eta)
    {
        var B = GetBMatrix(nodes, xi, eta);
        var strain = new double[3];

        for (int i = 0; i < 6; i++)
        {
            int nodeId = NodeIds[i];
            strain[0] += B[0, 2 * i] * nodes[nodeId].Ux + B[0, 2 * i + 1] * nodes[nodeId].Uy;
            strain[1] += B[1, 2 * i] * nodes[nodeId].Ux + B[1, 2 * i + 1] * nodes[nodeId].Uy;
            strain[2] += B[2, 2 * i] * nodes[nodeId].Ux + B[2, 2 * i + 1] * nodes[nodeId].Uy;
        }

        return strain;
    }

    public override double[] GetBodyForceVector(FEMNode2D[] nodes, Vector2 bodyForce)
    {
        var f = new double[12];

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var N = GetShapeFunctions(xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            for (int i = 0; i < 6; i++)
            {
                f[2 * i] += N[i] * bodyForce.X * detJ * w * Thickness;
                f[2 * i + 1] += N[i] * bodyForce.Y * detJ * w * Thickness;
            }
        }

        return f;
    }

    public override double[] GetInternalForceVector(FEMNode2D[] nodes)
    {
        var f = new double[12];

        for (int ip = 0; ip < GaussPoints.Length; ip++)
        {
            var (xi, eta, w) = GaussPoints[ip];
            var B = GetBMatrix(nodes, xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            double[] stress = { Stress[ip, 0], Stress[ip, 1], Stress[ip, 2] };

            for (int i = 0; i < 12; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    f[i] += B[j, i] * stress[j] * detJ * w * Thickness;
                }
            }
        }

        return f;
    }
}

/// <summary>
/// 4-node bilinear quadrilateral element (Q4)
/// </summary>
public class QuadElement4 : FEMElement2D
{
    public QuadElement4()
    {
        Type = ElementType2D.Quad4;
    }

    // 2×2 Gauss quadrature
    private static readonly double GP = 1.0 / Math.Sqrt(3);
    protected override (double xi, double eta, double weight)[] GaussPoints => new[]
    {
        (-GP, -GP, 1.0),
        ( GP, -GP, 1.0),
        ( GP,  GP, 1.0),
        (-GP,  GP, 1.0)
    };

    protected override double[] GetShapeFunctions(double xi, double eta)
    {
        return new[]
        {
            0.25 * (1 - xi) * (1 - eta),
            0.25 * (1 + xi) * (1 - eta),
            0.25 * (1 + xi) * (1 + eta),
            0.25 * (1 - xi) * (1 + eta)
        };
    }

    protected override double[,] GetShapeFunctionDerivatives(double xi, double eta)
    {
        return new double[,]
        {
            { -0.25 * (1 - eta), -0.25 * (1 - xi) },
            {  0.25 * (1 - eta), -0.25 * (1 + xi) },
            {  0.25 * (1 + eta),  0.25 * (1 + xi) },
            { -0.25 * (1 + eta),  0.25 * (1 - xi) }
        };
    }

    public override double[,] GetStiffnessMatrix(FEMNode2D[] nodes, GeomechanicalMaterial2D material)
    {
        int nDof = 8; // 4 nodes × 2 DOF
        var K = new double[nDof, nDof];
        var D = material.GetPlaneStrainElasticityMatrix();

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var B = GetBMatrix(nodes, xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            for (int i = 0; i < nDof; i++)
            {
                for (int j = 0; j < nDof; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        for (int l = 0; l < 3; l++)
                        {
                            sum += B[k, i] * D[k, l] * B[l, j];
                        }
                    }
                    K[i, j] += sum * detJ * w * Thickness;
                }
            }
        }

        return K;
    }

    public override double[,] GetMassMatrix(FEMNode2D[] nodes, double density)
    {
        int nDof = 8;
        var M = new double[nDof, nDof];

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var N = GetShapeFunctions(xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    double mass = density * N[i] * N[j] * detJ * w * Thickness;
                    M[2 * i, 2 * j] += mass;
                    M[2 * i + 1, 2 * j + 1] += mass;
                }
            }
        }

        return M;
    }

    public override double[] GetStrain(FEMNode2D[] nodes, double xi, double eta)
    {
        var B = GetBMatrix(nodes, xi, eta);
        var strain = new double[3];

        for (int i = 0; i < 4; i++)
        {
            int nodeId = NodeIds[i];
            strain[0] += B[0, 2 * i] * nodes[nodeId].Ux + B[0, 2 * i + 1] * nodes[nodeId].Uy;
            strain[1] += B[1, 2 * i] * nodes[nodeId].Ux + B[1, 2 * i + 1] * nodes[nodeId].Uy;
            strain[2] += B[2, 2 * i] * nodes[nodeId].Ux + B[2, 2 * i + 1] * nodes[nodeId].Uy;
        }

        return strain;
    }

    public override double[] GetBodyForceVector(FEMNode2D[] nodes, Vector2 bodyForce)
    {
        var f = new double[8];

        foreach (var (xi, eta, w) in GaussPoints)
        {
            var N = GetShapeFunctions(xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            for (int i = 0; i < 4; i++)
            {
                f[2 * i] += N[i] * bodyForce.X * detJ * w * Thickness;
                f[2 * i + 1] += N[i] * bodyForce.Y * detJ * w * Thickness;
            }
        }

        return f;
    }

    public override double[] GetInternalForceVector(FEMNode2D[] nodes)
    {
        var f = new double[8];

        for (int ip = 0; ip < GaussPoints.Length; ip++)
        {
            var (xi, eta, w) = GaussPoints[ip];
            var B = GetBMatrix(nodes, xi, eta);
            var (_, detJ) = GetJacobian(nodes, xi, eta);

            double[] stress = { Stress[ip, 0], Stress[ip, 1], Stress[ip, 2] };

            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    f[i] += B[j, i] * stress[j] * detJ * w * Thickness;
                }
            }
        }

        return f;
    }
}

/// <summary>
/// Interface/Joint element for discontinuities and contacts
/// </summary>
public class InterfaceElement4 : FEMElement2D
{
    public InterfaceElement4()
    {
        Type = ElementType2D.Interface4;
    }

    // Joint properties
    public double NormalStiffness { get; set; } = 1e10;
    public double ShearStiffness { get; set; } = 1e9;
    public double JointCohesion { get; set; } = 0;
    public double JointFriction { get; set; } = 30; // degrees
    public double JointTensileStrength { get; set; } = 0;

    // State
    public double NormalGap { get; set; }
    public double ShearSlip { get; set; }
    public bool IsOpen { get; set; }
    public bool IsSliding { get; set; }

    protected override (double xi, double eta, double weight)[] GaussPoints => new[]
    {
        (-1.0 / Math.Sqrt(3), 0.0, 1.0),
        ( 1.0 / Math.Sqrt(3), 0.0, 1.0)
    };

    protected override double[] GetShapeFunctions(double xi, double eta)
    {
        // 1D shape functions along interface
        return new[]
        {
            0.5 * (1 - xi), // N1 (bottom-left)
            0.5 * (1 + xi), // N2 (bottom-right)
            0.5 * (1 + xi), // N3 (top-right)
            0.5 * (1 - xi)  // N4 (top-left)
        };
    }

    protected override double[,] GetShapeFunctionDerivatives(double xi, double eta)
    {
        return new double[,]
        {
            { -0.5, 0 },
            {  0.5, 0 },
            {  0.5, 0 },
            { -0.5, 0 }
        };
    }

    public override double[,] GetStiffnessMatrix(FEMNode2D[] nodes, GeomechanicalMaterial2D material)
    {
        // Interface stiffness matrix considering opening and sliding
        int nDof = 8; // 4 nodes × 2 DOF
        var K = new double[nDof, nDof];

        double kn = NormalStiffness;
        double ks = ShearStiffness;

        // If interface is open, reduce normal stiffness
        if (IsOpen) kn = 0;

        // If sliding, reduce shear stiffness
        if (IsSliding) ks = 0;

        // Get interface direction
        var dir = nodes[NodeIds[1]].InitialPosition - nodes[NodeIds[0]].InitialPosition;
        double length = dir.Length();
        if (length < 1e-10) return K;
        dir = Vector2.Normalize(dir);

        // Normal direction
        var normal = new Vector2(-dir.Y, dir.X);

        // Build stiffness matrix for relative displacement across interface
        foreach (var (xi, _, w) in GaussPoints)
        {
            var N = GetShapeFunctions(xi, 0);

            // For each pair of opposing nodes
            for (int i = 0; i < 2; i++)
            {
                int bot = i;
                int top = 3 - i; // Opposing node

                // Normal contribution
                K[2 * bot, 2 * bot] += kn * normal.X * normal.X * N[bot] * N[bot] * length / 2 * w;
                K[2 * bot + 1, 2 * bot + 1] += kn * normal.Y * normal.Y * N[bot] * N[bot] * length / 2 * w;
                K[2 * bot, 2 * bot + 1] += kn * normal.X * normal.Y * N[bot] * N[bot] * length / 2 * w;
                K[2 * bot + 1, 2 * bot] += kn * normal.X * normal.Y * N[bot] * N[bot] * length / 2 * w;

                K[2 * top, 2 * top] += kn * normal.X * normal.X * N[top] * N[top] * length / 2 * w;
                K[2 * top + 1, 2 * top + 1] += kn * normal.Y * normal.Y * N[top] * N[top] * length / 2 * w;

                // Cross terms (negative for relative displacement)
                K[2 * bot, 2 * top] -= kn * normal.X * normal.X * N[bot] * N[top] * length / 2 * w;
                K[2 * top, 2 * bot] -= kn * normal.X * normal.X * N[bot] * N[top] * length / 2 * w;

                // Shear contribution (similar structure)
                K[2 * bot, 2 * bot] += ks * dir.X * dir.X * N[bot] * N[bot] * length / 2 * w;
                K[2 * bot + 1, 2 * bot + 1] += ks * dir.Y * dir.Y * N[bot] * N[bot] * length / 2 * w;

                K[2 * top, 2 * top] += ks * dir.X * dir.X * N[top] * N[top] * length / 2 * w;
                K[2 * top + 1, 2 * top + 1] += ks * dir.Y * dir.Y * N[top] * N[top] * length / 2 * w;

                K[2 * bot, 2 * top] -= ks * dir.X * dir.X * N[bot] * N[top] * length / 2 * w;
                K[2 * top, 2 * bot] -= ks * dir.X * dir.X * N[bot] * N[top] * length / 2 * w;
            }
        }

        return K;
    }

    public override double[,] GetMassMatrix(FEMNode2D[] nodes, double density)
    {
        // Interface elements typically have no mass
        return new double[8, 8];
    }

    public override double[] GetStrain(FEMNode2D[] nodes, double xi, double eta)
    {
        // Return relative displacement across interface
        var N = GetShapeFunctions(xi, 0);
        double deltaUx = 0, deltaUy = 0;

        for (int i = 0; i < 2; i++)
        {
            int bot = i;
            int top = 3 - i;
            deltaUx += N[top] * nodes[NodeIds[top]].Ux - N[bot] * nodes[NodeIds[bot]].Ux;
            deltaUy += N[top] * nodes[NodeIds[top]].Uy - N[bot] * nodes[NodeIds[bot]].Uy;
        }

        // Transform to normal/tangential components
        var dir = Vector2.Normalize(nodes[NodeIds[1]].InitialPosition - nodes[NodeIds[0]].InitialPosition);
        var normal = new Vector2(-dir.Y, dir.X);

        double normalGap = deltaUx * normal.X + deltaUy * normal.Y;
        double shearSlip = deltaUx * dir.X + deltaUy * dir.Y;

        return new[] { normalGap, shearSlip, 0 };
    }

    public override double[] GetBodyForceVector(FEMNode2D[] nodes, Vector2 bodyForce)
    {
        return new double[8]; // No body force for interface
    }

    public override double[] GetInternalForceVector(FEMNode2D[] nodes)
    {
        var f = new double[8];

        // Calculate relative displacement and forces
        var dir = Vector2.Normalize(nodes[NodeIds[1]].InitialPosition - nodes[NodeIds[0]].InitialPosition);
        var normal = new Vector2(-dir.Y, dir.X);
        double length = (nodes[NodeIds[1]].InitialPosition - nodes[NodeIds[0]].InitialPosition).Length();

        foreach (var (xi, _, w) in GaussPoints)
        {
            var strain = GetStrain(nodes, xi, 0);
            double normalGap = strain[0];
            double shearSlip = strain[1];

            // Normal traction
            double tn = 0;
            if (normalGap < 0) // Compression
            {
                tn = NormalStiffness * normalGap;
                IsOpen = false;
            }
            else if (normalGap > 0 && JointTensileStrength > 0 && NormalStiffness * normalGap < JointTensileStrength)
            {
                tn = NormalStiffness * normalGap;
                IsOpen = false;
            }
            else
            {
                IsOpen = true;
            }

            // Shear traction with friction
            double ts = ShearStiffness * shearSlip;
            double maxShear = JointCohesion - tn * Math.Tan(JointFriction * Math.PI / 180); // tn is negative in compression
            if (Math.Abs(ts) > maxShear && maxShear > 0)
            {
                ts = Math.Sign(ts) * maxShear;
                IsSliding = true;
            }
            else
            {
                IsSliding = false;
            }

            // Distribute forces to nodes
            var N = GetShapeFunctions(xi, 0);
            for (int i = 0; i < 4; i++)
            {
                int sign = i < 2 ? -1 : 1; // Bottom vs top side
                f[2 * i] += sign * (tn * normal.X + ts * dir.X) * N[i] * length / 2 * w * Thickness;
                f[2 * i + 1] += sign * (tn * normal.Y + ts * dir.Y) * N[i] * length / 2 * w * Thickness;
            }
        }

        return f;
    }
}

/// <summary>
/// Complete 2D FEM mesh with nodes, elements, and generation capabilities
/// </summary>
public class FEMMesh2D
{
    public List<FEMNode2D> Nodes { get; } = new();
    public List<FEMElement2D> Elements { get; } = new();
    public GeomechanicalMaterialLibrary2D Materials { get; } = new();

    // Mesh generation settings
    public double DefaultElementSize { get; set; } = 1.0;
    public ElementType2D DefaultElementType { get; set; } = ElementType2D.Triangle3;

    // Boundary conditions storage
    public List<BoundaryCondition2D> BoundaryConditions { get; } = new();
    public List<LoadCondition2D> Loads { get; } = new();

    // Global system matrices (assembled)
    public double[] GlobalDisplacements { get; set; }
    public double[] GlobalForces { get; set; }
    public int TotalDOF { get; private set; }

    #region Node Management

    public FEMNode2D AddNode(Vector2 position)
    {
        var node = new FEMNode2D(Nodes.Count, position);
        Nodes.Add(node);
        return node;
    }

    public FEMNode2D FindNearestNode(Vector2 position, double tolerance = 1e-6)
    {
        FEMNode2D nearest = null;
        double minDist = tolerance;

        foreach (var node in Nodes)
        {
            double dist = Vector2.Distance(position, node.InitialPosition);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = node;
            }
        }

        return nearest;
    }

    public FEMNode2D GetOrCreateNode(Vector2 position, double tolerance = 1e-6)
    {
        var existing = FindNearestNode(position, tolerance);
        return existing ?? AddNode(position);
    }

    #endregion

    #region Element Creation

    public FEMElement2D AddTriangle(int n1, int n2, int n3, int materialId)
    {
        var element = new TriangleElement3
        {
            Id = Elements.Count,
            MaterialId = materialId,
            NodeIds = new List<int> { n1, n2, n3 }
        };
        element.InitializeState();
        Elements.Add(element);
        return element;
    }

    public FEMElement2D AddQuad(int n1, int n2, int n3, int n4, int materialId)
    {
        var element = new QuadElement4
        {
            Id = Elements.Count,
            MaterialId = materialId,
            NodeIds = new List<int> { n1, n2, n3, n4 }
        };
        element.InitializeState();
        Elements.Add(element);
        return element;
    }

    public FEMElement2D AddInterface(int n1, int n2, int n3, int n4, double kn, double ks, double cohesion, double friction)
    {
        var element = new InterfaceElement4
        {
            Id = Elements.Count,
            NodeIds = new List<int> { n1, n2, n3, n4 },
            NormalStiffness = kn,
            ShearStiffness = ks,
            JointCohesion = cohesion,
            JointFriction = friction
        };
        element.InitializeState();
        Elements.Add(element);
        return element;
    }

    #endregion

    #region Mesh Generation

    /// <summary>
    /// Generate a rectangular mesh
    /// </summary>
    public void GenerateRectangularMesh(Vector2 origin, double width, double height, int nx, int ny, int materialId)
    {
        double dx = width / nx;
        double dy = height / ny;

        // Create nodes
        int nodeStart = Nodes.Count;
        for (int j = 0; j <= ny; j++)
        {
            for (int i = 0; i <= nx; i++)
            {
                var pos = origin + new Vector2((float)(i * dx), (float)(j * dy));
                AddNode(pos);
            }
        }

        // Create elements (quads or triangles)
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                int n1 = nodeStart + j * (nx + 1) + i;
                int n2 = n1 + 1;
                int n3 = n2 + nx + 1;
                int n4 = n1 + nx + 1;

                if (DefaultElementType == ElementType2D.Quad4)
                {
                    AddQuad(n1, n2, n3, n4, materialId);
                }
                else
                {
                    // Two triangles
                    AddTriangle(n1, n2, n3, materialId);
                    AddTriangle(n1, n3, n4, materialId);
                }
            }
        }
    }

    /// <summary>
    /// Generate mesh for a polygon region using constrained Delaunay triangulation
    /// </summary>
    public void GeneratePolygonMesh(List<Vector2> polygon, int materialId, double elementSize = -1)
    {
        if (elementSize < 0) elementSize = DefaultElementSize;

        // Simple triangulation using ear clipping for convex/simple polygons
        var nodes = new List<int>();
        foreach (var p in polygon)
        {
            nodes.Add(GetOrCreateNode(p, elementSize * 0.1).Id);
        }

        // Ear clipping triangulation
        var remaining = new List<int>(nodes);
        while (remaining.Count > 3)
        {
            bool earFound = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int prev = remaining[(i + remaining.Count - 1) % remaining.Count];
                int curr = remaining[i];
                int next = remaining[(i + 1) % remaining.Count];

                if (IsEar(prev, curr, next, remaining))
                {
                    AddTriangle(prev, curr, next, materialId);
                    remaining.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }

            if (!earFound)
            {
                // Fallback for complex polygons
                break;
            }
        }

        if (remaining.Count == 3)
        {
            AddTriangle(remaining[0], remaining[1], remaining[2], materialId);
        }
    }

    private bool IsEar(int prevId, int currId, int nextId, List<int> polygon)
    {
        var prev = Nodes[prevId].InitialPosition;
        var curr = Nodes[currId].InitialPosition;
        var next = Nodes[nextId].InitialPosition;

        // Check if convex (CCW)
        double cross = (curr.X - prev.X) * (next.Y - prev.Y) - (curr.Y - prev.Y) * (next.X - prev.X);
        if (cross <= 0) return false;

        // Check no other vertices inside
        foreach (int id in polygon)
        {
            if (id == prevId || id == currId || id == nextId) continue;

            var p = Nodes[id].InitialPosition;
            if (PointInTriangle(p, prev, curr, next))
                return false;
        }

        return true;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        double Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        }

        double d1 = Sign(p, a, b);
        double d2 = Sign(p, b, c);
        double d3 = Sign(p, c, a);

        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(hasNeg && hasPos);
    }

    /// <summary>
    /// Generate mesh for a circle/disk
    /// </summary>
    public void GenerateCircleMesh(Vector2 center, double radius, int nRadial, int nCircumferential, int materialId)
    {
        // Center node
        int centerNode = AddNode(center).Id;

        // Radial layers
        for (int r = 1; r <= nRadial; r++)
        {
            double rad = radius * r / nRadial;
            for (int c = 0; c < nCircumferential; c++)
            {
                double angle = 2 * Math.PI * c / nCircumferential;
                var pos = center + new Vector2((float)(rad * Math.Cos(angle)), (float)(rad * Math.Sin(angle)));
                AddNode(pos);
            }
        }

        // Create triangles for innermost ring
        for (int c = 0; c < nCircumferential; c++)
        {
            int n1 = centerNode;
            int n2 = centerNode + 1 + c;
            int n3 = centerNode + 1 + (c + 1) % nCircumferential;
            AddTriangle(n1, n2, n3, materialId);
        }

        // Create quads for outer rings
        for (int r = 1; r < nRadial; r++)
        {
            int ringStart = centerNode + 1 + (r - 1) * nCircumferential;
            int nextRingStart = ringStart + nCircumferential;

            for (int c = 0; c < nCircumferential; c++)
            {
                int n1 = ringStart + c;
                int n2 = ringStart + (c + 1) % nCircumferential;
                int n3 = nextRingStart + (c + 1) % nCircumferential;
                int n4 = nextRingStart + c;
                AddQuad(n1, n2, n3, n4, materialId);
            }
        }
    }

    #endregion

    #region DOF Numbering

    /// <summary>
    /// Number the degrees of freedom, accounting for fixed nodes
    /// </summary>
    public void NumberDOF()
    {
        int dof = 0;

        foreach (var node in Nodes)
        {
            node.GlobalDofX = node.FixedX ? -1 : dof++;
            node.GlobalDofY = node.FixedY ? -1 : dof++;
        }

        TotalDOF = dof;
        GlobalDisplacements = new double[dof];
        GlobalForces = new double[dof];
    }

    #endregion

    #region Boundary Conditions

    public void FixNode(int nodeId, bool fixX = true, bool fixY = true)
    {
        if (nodeId >= 0 && nodeId < Nodes.Count)
        {
            Nodes[nodeId].FixedX = fixX;
            Nodes[nodeId].FixedY = fixY;
        }
    }

    public void FixBottom(double tolerance = 1e-6)
    {
        if (Nodes.Count == 0) return;
        double minY = Nodes.Min(n => n.InitialPosition.Y);
        foreach (var node in Nodes.Where(n => Math.Abs(n.InitialPosition.Y - minY) < tolerance))
        {
            node.FixedX = true;
            node.FixedY = true;
        }
    }

    public void FixLeft(double tolerance = 1e-6)
    {
        if (Nodes.Count == 0) return;
        double minX = Nodes.Min(n => n.InitialPosition.X);
        foreach (var node in Nodes.Where(n => Math.Abs(n.InitialPosition.X - minX) < tolerance))
        {
            node.FixedX = true;
        }
    }

    public void FixRight(double tolerance = 1e-6)
    {
        if (Nodes.Count == 0) return;
        double maxX = Nodes.Max(n => n.InitialPosition.X);
        foreach (var node in Nodes.Where(n => Math.Abs(n.InitialPosition.X - maxX) < tolerance))
        {
            node.FixedX = true;
        }
    }

    public void ApplyPrescribedDisplacement(int nodeId, double? ux, double? uy)
    {
        if (nodeId >= 0 && nodeId < Nodes.Count)
        {
            var node = Nodes[nodeId];
            if (ux.HasValue)
            {
                node.PrescribedUx = ux;
                node.FixedX = true;
            }
            if (uy.HasValue)
            {
                node.PrescribedUy = uy;
                node.FixedY = true;
            }
        }
    }

    public void ApplyNodalForce(int nodeId, double fx, double fy)
    {
        if (nodeId >= 0 && nodeId < Nodes.Count)
        {
            Nodes[nodeId].Fx += fx;
            Nodes[nodeId].Fy += fy;
        }
    }

    public void ApplyDistributedLoad(List<int> nodeIds, Vector2 traction)
    {
        // Distribute load to nodes along boundary
        if (nodeIds.Count < 2) return;

        for (int i = 0; i < nodeIds.Count - 1; i++)
        {
            var n1 = Nodes[nodeIds[i]];
            var n2 = Nodes[nodeIds[i + 1]];
            double length = Vector2.Distance(n1.InitialPosition, n2.InitialPosition);

            // Half to each node
            double fx = traction.X * length / 2;
            double fy = traction.Y * length / 2;

            n1.Fx += fx;
            n1.Fy += fy;
            n2.Fx += fx;
            n2.Fy += fy;
        }
    }

    #endregion

    #region Utilities

    public void Reset()
    {
        foreach (var node in Nodes)
        {
            node.Reset();
        }

        foreach (var element in Elements)
        {
            element.InitializeState();
        }
    }

    public void Clear()
    {
        Nodes.Clear();
        Elements.Clear();
        BoundaryConditions.Clear();
        Loads.Clear();
    }

    public (Vector2 min, Vector2 max) GetBoundingBox()
    {
        if (Nodes.Count == 0) return (Vector2.Zero, Vector2.Zero);

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        foreach (var node in Nodes)
        {
            min.X = Math.Min(min.X, node.InitialPosition.X);
            min.Y = Math.Min(min.Y, node.InitialPosition.Y);
            max.X = Math.Max(max.X, node.InitialPosition.X);
            max.Y = Math.Max(max.Y, node.InitialPosition.Y);
        }

        return (min, max);
    }

    /// <summary>
    /// Get nodes on the specified boundary
    /// </summary>
    public List<int> GetBoundaryNodes(BoundarySide side, double tolerance = 1e-6)
    {
        if (Nodes.Count == 0) return new List<int>();

        var (min, max) = GetBoundingBox();
        var result = new List<int>();

        foreach (var node in Nodes)
        {
            bool onBoundary = side switch
            {
                BoundarySide.Bottom => Math.Abs(node.InitialPosition.Y - min.Y) < tolerance,
                BoundarySide.Top => Math.Abs(node.InitialPosition.Y - max.Y) < tolerance,
                BoundarySide.Left => Math.Abs(node.InitialPosition.X - min.X) < tolerance,
                BoundarySide.Right => Math.Abs(node.InitialPosition.X - max.X) < tolerance,
                _ => false
            };

            if (onBoundary) result.Add(node.Id);
        }

        // Sort by position along boundary
        if (side == BoundarySide.Bottom || side == BoundarySide.Top)
            result.Sort((a, b) => Nodes[a].InitialPosition.X.CompareTo(Nodes[b].InitialPosition.X));
        else
            result.Sort((a, b) => Nodes[a].InitialPosition.Y.CompareTo(Nodes[b].InitialPosition.Y));

        return result;
    }

    #endregion
}

/// <summary>
/// Boundary side enumeration
/// </summary>
public enum BoundarySide
{
    Bottom,
    Top,
    Left,
    Right
}

/// <summary>
/// Boundary condition specification
/// </summary>
public class BoundaryCondition2D
{
    public int NodeId { get; set; }
    public bool FixX { get; set; }
    public bool FixY { get; set; }
    public double? PrescribedUx { get; set; }
    public double? PrescribedUy { get; set; }
}

/// <summary>
/// Load condition specification
/// </summary>
public class LoadCondition2D
{
    public enum LoadType { NodalForce, DistributedLoad, Pressure, BodyForce }

    public LoadType Type { get; set; }
    public List<int> NodeIds { get; set; } = new();
    public Vector2 Value { get; set; }  // Force or traction
    public double Magnitude { get; set; }
}
