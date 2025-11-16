// GeoscientistToolkit/OpenCL/TriaxialKernels.cl
// OpenCL kernels for triaxial simulation with GPU acceleration
//
// Includes:
// - Stress-strain computation for hex elements
// - Multiple failure criteria
// - Fracture detection
// - Time integration

// ========== HELPER FUNCTIONS ==========

// Compute 3D strain from displacement field using finite differences
void compute_strain_at_node(
    __global const float* nodeX,
    __global const float* nodeY,
    __global const float* nodeZ,
    __global const float* displacement,
    __global const int* elements,
    int elementIdx,
    int localNode,
    float* strain_out)  // 6 components: εxx, εyy, εzz, γxy, γyz, γxz
{
    // Get element nodes (8 nodes for hex)
    int n0 = elements[elementIdx * 8 + 0];
    int n1 = elements[elementIdx * 8 + 1];
    int n2 = elements[elementIdx * 8 + 2];
    int n3 = elements[elementIdx * 8 + 3];
    int n4 = elements[elementIdx * 8 + 4];
    int n5 = elements[elementIdx * 8 + 5];
    int n6 = elements[elementIdx * 8 + 6];
    int n7 = elements[elementIdx * 8 + 7];

    // Simplified: average strain in element
    // Production code: use proper shape function derivatives

    // ∂u/∂x ≈ Δu/Δx
    float ux0 = displacement[3*n0 + 0];
    float ux1 = displacement[3*n1 + 0];
    float dx = nodeX[n1] - nodeX[n0];
    float dudx = (dx > 1e-6f) ? (ux1 - ux0) / dx : 0.0f;

    float uy2 = displacement[3*n2 + 1];
    float uy1 = displacement[3*n1 + 1];
    float dy = nodeY[n2] - nodeY[n1];
    float dvdy = (dy > 1e-6f) ? (uy2 - uy1) / dy : 0.0f;

    float uz4 = displacement[3*n4 + 2];
    float uz0 = displacement[3*n0 + 2];
    float dz = nodeZ[n4] - nodeZ[n0];
    float dwdz = (dz > 1e-6f) ? (uz4 - uz0) / dz : 0.0f;

    // Normal strains
    strain_out[0] = dudx;  // εxx
    strain_out[1] = dvdy;  // εyy
    strain_out[2] = dwdz;  // εzz

    // Shear strains (engineering)
    strain_out[3] = 0.0f;  // γxy (simplified)
    strain_out[4] = 0.0f;  // γyz
    strain_out[5] = 0.0f;  // γxz
}

// Compute stress from strain using linear elasticity
void compute_stress_from_strain(
    const float* strain,  // 6 components
    float E,              // Young's modulus (MPa)
    float nu,             // Poisson's ratio
    float* stress_out)    // 6 components: σxx, σyy, σzz, τxy, τyz, τxz
{
    // Lamé parameters
    float lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
    float mu = E / (2.0f * (1.0f + nu));

    // Volumetric strain
    float eps_v = strain[0] + strain[1] + strain[2];

    // Normal stresses
    stress_out[0] = lambda * eps_v + 2.0f * mu * strain[0];  // σxx
    stress_out[1] = lambda * eps_v + 2.0f * mu * strain[1];  // σyy
    stress_out[2] = lambda * eps_v + 2.0f * mu * strain[2];  // σzz

    // Shear stresses
    stress_out[3] = mu * strain[3];  // τxy
    stress_out[4] = mu * strain[4];  // τyz
    stress_out[5] = mu * strain[5];  // τxz
}

// Compute principal stresses from stress tensor
void compute_principal_stresses(
    const float* stress,  // 6 components
    float* sigma1,
    float* sigma2,
    float* sigma3)
{
    float sxx = stress[0];
    float syy = stress[1];
    float szz = stress[2];
    float sxy = stress[3];
    float syz = stress[4];
    float sxz = stress[5];

    // Invariants
    float I1 = sxx + syy + szz;
    float I2 = sxx*syy + syy*szz + szz*sxx - sxy*sxy - syz*syz - sxz*sxz;
    float I3 = sxx*syy*szz + 2.0f*sxy*syz*sxz - sxx*syz*syz - syy*sxz*sxz - szz*sxy*sxy;

    // Characteristic equation: λ³ - I1·λ² + I2·λ - I3 = 0
    // Solve using Cardano's method (simplified for now)

    // Simplified: assume diagonal stress tensor (common in triaxial)
    *sigma1 = max(max(sxx, syy), szz);
    *sigma3 = min(min(sxx, syy), szz);
    *sigma2 = I1 - *sigma1 - *sigma3;

    // Ensure σ1 >= σ2 >= σ3
    if (*sigma2 > *sigma1) {
        float temp = *sigma1;
        *sigma1 = *sigma2;
        *sigma2 = temp;
    }
    if (*sigma3 > *sigma2) {
        float temp = *sigma2;
        *sigma2 = *sigma3;
        *sigma3 = temp;
    }
    if (*sigma2 > *sigma1) {
        float temp = *sigma1;
        *sigma1 = *sigma2;
        *sigma2 = temp;
    }
}

// Check Mohr-Coulomb failure criterion
int check_mohr_coulomb_failure(
    float sigma1,
    float sigma3,
    float cohesion,
    float frictionAngle_deg)
{
    float phi_rad = frictionAngle_deg * M_PI_F / 180.0f;
    float sin_phi = sin(phi_rad);
    float cos_phi = cos(phi_rad);

    // (σ1 - σ3) / 2 >= c·cos(φ) + (σ1 + σ3)/2 · sin(φ)
    float lhs = (sigma1 - sigma3) / 2.0f;
    float rhs = cohesion * cos_phi + (sigma1 + sigma3) / 2.0f * sin_phi;

    return (lhs >= rhs) ? 1 : 0;
}

// Check Drucker-Prager failure criterion
int check_drucker_prager_failure(
    float sigma1,
    float sigma2,
    float sigma3,
    float cohesion,
    float frictionAngle_deg)
{
    float phi_rad = frictionAngle_deg * M_PI_F / 180.0f;
    float sin_phi = sin(phi_rad);
    float cos_phi = cos(phi_rad);

    // Material constants
    float alpha = 2.0f * sin_phi / (3.0f - sin_phi);
    float k = 6.0f * cohesion * cos_phi / (3.0f - sin_phi);

    // Invariants
    float I1 = sigma1 + sigma2 + sigma3;
    float J2 = ((sigma1 - sigma2) * (sigma1 - sigma2) +
                (sigma2 - sigma3) * (sigma2 - sigma3) +
                (sigma3 - sigma1) * (sigma3 - sigma1)) / 6.0f;

    // sqrt(J2) >= k + α·I1/3
    return (sqrt(J2) >= k + alpha * I1 / 3.0f) ? 1 : 0;
}

// Check Hoek-Brown failure criterion
int check_hoek_brown_failure(
    float sigma1,
    float sigma3,
    float ucs,           // Uniaxial compressive strength
    float mb,            // Hoek-Brown parameter
    float s,             // Hoek-Brown parameter
    float a)             // Hoek-Brown parameter
{
    // σ1 = σ3 + σci·(mb·σ3/σci + s)^a
    float rhs = sigma3 + ucs * pow(mb * sigma3 / ucs + s, a);
    return (sigma1 >= rhs) ? 1 : 0;
}

// Check Griffith failure criterion
int check_griffith_failure(
    float sigma1,
    float sigma3,
    float tensileStrength)
{
    // τ² = 4·T0·(σn + T0)
    float tau = (sigma1 - sigma3) / 2.0f;
    float sigma_n = (sigma1 + sigma3) / 2.0f;

    return (tau * tau >= 4.0f * tensileStrength * (sigma_n + tensileStrength)) ? 1 : 0;
}

// ========== MAIN KERNELS ==========

// Apply triaxial boundary conditions
__kernel void apply_triaxial_load(
    __global const float* nodeX,
    __global const float* nodeY,
    __global const float* nodeZ,
    __global float* displacement,
    __global const int* topPlatenNodes,
    __global const int* bottomPlatenNodes,
    __global const int* lateralNodes,
    int nTopPlaten,
    int nBottomPlaten,
    int nLateral,
    float axialStrain,
    float radialStrain,
    float sampleHeight,
    int nNodes)
{
    int i = get_global_id(0);
    if (i >= nNodes) return;

    float x = nodeX[i];
    float y = nodeY[i];
    float z = nodeZ[i];

    // Apply axial displacement
    displacement[3*i + 2] = axialStrain * z;

    // Apply radial displacement
    float r = sqrt(x*x + y*y);
    if (r > 1e-6f) {
        displacement[3*i + 0] = radialStrain * x;
        displacement[3*i + 1] = radialStrain * y;
    }
}

// Compute stress and strain at all nodes
__kernel void compute_stress_strain(
    __global const float* nodeX,
    __global const float* nodeY,
    __global const float* nodeZ,
    __global const float* displacement,
    __global const int* elements,
    __global float* stress,
    __global float* strain,
    float E,
    float nu,
    int nElements)
{
    int elemIdx = get_global_id(0);
    if (elemIdx >= nElements) return;

    // Compute average strain in element
    float elem_strain[6];
    compute_strain_at_node(nodeX, nodeY, nodeZ, displacement, elements,
                          elemIdx, 0, elem_strain);

    // Compute stress from strain
    float elem_stress[6];
    compute_stress_from_strain(elem_strain, E, nu, elem_stress);

    // Store at element centroid (or distribute to nodes)
    // For simplicity, store at first node of element
    int nodeIdx = elements[elemIdx * 8];

    for (int i = 0; i < 6; i++) {
        stress[nodeIdx * 6 + i] = elem_stress[i];
        strain[nodeIdx * 6 + i] = elem_strain[i];
    }
}

// Detect failure in elements
__kernel void detect_failure(
    __global const float* stress,
    __global const int* elements,
    __global int* failed,
    int failureCriterion,  // 0=MC, 1=DP, 2=HB, 3=Griffith
    float cohesion,
    float frictionAngle,
    float tensileStrength,
    float ucs,
    float hoekBrown_mb,
    float hoekBrown_s,
    float hoekBrown_a,
    int nElements)
{
    int elemIdx = get_global_id(0);
    if (elemIdx >= nElements) return;

    // Get stress at element (use first node)
    int nodeIdx = elements[elemIdx * 8];
    float elem_stress[6];
    for (int i = 0; i < 6; i++) {
        elem_stress[i] = stress[nodeIdx * 6 + i];
    }

    // Compute principal stresses
    float sigma1, sigma2, sigma3;
    compute_principal_stresses(elem_stress, &sigma1, &sigma2, &sigma3);

    // Check failure criterion
    int hasFailed = 0;

    if (failureCriterion == 0) {
        // Mohr-Coulomb
        hasFailed = check_mohr_coulomb_failure(sigma1, sigma3, cohesion, frictionAngle);
    }
    else if (failureCriterion == 1) {
        // Drucker-Prager
        hasFailed = check_drucker_prager_failure(sigma1, sigma2, sigma3, cohesion, frictionAngle);
    }
    else if (failureCriterion == 2) {
        // Hoek-Brown
        hasFailed = check_hoek_brown_failure(sigma1, sigma3, ucs,
                                             hoekBrown_mb, hoekBrown_s, hoekBrown_a);
    }
    else if (failureCriterion == 3) {
        // Griffith
        hasFailed = check_griffith_failure(sigma1, sigma3, tensileStrength);
    }

    failed[elemIdx] = hasFailed;
}

// Update displacement using explicit time integration
__kernel void update_displacement(
    __global float* displacement,
    __global const float* velocity,
    __global const float* force,
    float dt,
    float mass,
    int nNodes)
{
    int i = get_global_id(0);
    if (i >= nNodes) return;

    // Explicit Euler: v^{n+1} = v^n + (F/m)·dt
    //                 u^{n+1} = u^n + v^{n+1}·dt

    // For quasi-static, we skip dynamics and use equilibrium
    // This kernel is for dynamic simulations
}

// Compute principal stresses at all nodes
__kernel void compute_principal_stress_field(
    __global const float* stress,
    __global float* sigma1_out,
    __global float* sigma2_out,
    __global float* sigma3_out,
    int nNodes)
{
    int i = get_global_id(0);
    if (i >= nNodes) return;

    float stress_tensor[6];
    for (int j = 0; j < 6; j++) {
        stress_tensor[j] = stress[i * 6 + j];
    }

    float s1, s2, s3;
    compute_principal_stresses(stress_tensor, &s1, &s2, &s3);

    sigma1_out[i] = s1;
    sigma2_out[i] = s2;
    sigma3_out[i] = s3;
}

// Compute Von Mises stress
__kernel void compute_von_mises_stress(
    __global const float* stress,
    __global float* vonMises_out,
    int nNodes)
{
    int i = get_global_id(0);
    if (i >= nNodes) return;

    float sxx = stress[i * 6 + 0];
    float syy = stress[i * 6 + 1];
    float szz = stress[i * 6 + 2];
    float sxy = stress[i * 6 + 3];
    float syz = stress[i * 6 + 4];
    float sxz = stress[i * 6 + 5];

    // σ_vm = sqrt(3·J2)
    // J2 = 1/6·[(σxx-σyy)² + (σyy-σzz)² + (σzz-σxx)²] + τxy² + τyz² + τxz²
    float diff_xx_yy = sxx - syy;
    float diff_yy_zz = syy - szz;
    float diff_zz_xx = szz - sxx;

    float J2 = (diff_xx_yy * diff_xx_yy + diff_yy_zz * diff_yy_zz + diff_zz_xx * diff_zz_xx) / 6.0f
             + sxy * sxy + syz * syz + sxz * sxz;

    vonMises_out[i] = sqrt(3.0f * J2);
}
