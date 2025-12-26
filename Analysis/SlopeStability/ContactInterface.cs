using System;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Represents a contact interface between two blocks or a block and a boundary.
    /// Handles contact detection, force calculation, and friction/cohesion.
    ///
    /// <para><b>Academic References:</b></para>
    ///
    /// <para>Mohr-Coulomb failure criterion:</para>
    /// <para>Coulomb, C. A. (1776). Essai sur une application des règles de maximis et minimis à
    /// quelques problèmes de statique relatifs à l'architecture. Mémoires de Mathématique et de
    /// Physique, Académie Royale des Sciences, 7, 343-382.</para>
    ///
    /// <para>Effective stress principle for pore pressure:</para>
    /// <para>Terzaghi, K. (1943). Theoretical soil mechanics. John Wiley &amp; Sons.</para>
    ///
    /// <para>Joint shear strength and roughness:</para>
    /// <para>Barton, N., &amp; Choubey, V. (1977). The shear strength of rock joints in theory and
    /// practice. Rock Mechanics, 10(1-2), 1-54. https://doi.org/10.1007/BF01261801</para>
    ///
    /// <para>Contact stiffness formulation:</para>
    /// <para>Itasca Consulting Group. (2016). 3DEC—Three-dimensional distinct element code,
    /// Version 5.2, Theory and background. Minneapolis: Itasca.</para>
    /// </summary>
    public class ContactInterface
    {
        // Contact identification
        public int BlockAId { get; set; }
        public int BlockBId { get; set; }  // -1 for boundary contact
        public bool IsBoundaryContact { get; set; }

        // Contact geometry
        public Vector3 ContactPoint { get; set; }
        public Vector3 ContactNormal { get; set; }  // Points from A to B
        public float PenetrationDepth { get; set; }
        public float ContactArea { get; set; }

        // Contact state
        public bool IsActive { get; set; }
        public ContactType Type { get; set; }

        // Forces
        public Vector3 NormalForce { get; set; }
        public Vector3 ShearForce { get; set; }
        public Vector3 TotalForce { get; set; }

        // Joint properties (if contact is along a joint)
        public int JointSetId { get; set; }  // -1 if not a joint contact
        public bool IsJointContact { get; set; }

        // Contact mechanics parameters
        public float NormalStiffness { get; set; }  // kn (Pa/m)
        public float ShearStiffness { get; set; }   // ks (Pa/m)
        public float FrictionCoefficient { get; set; }
        public float Cohesion { get; set; }
        public float TensileStrength { get; set; }
        public float DilationAngle { get; set; }

        // Water pressure parameters
        public float PorePressure { get; set; }     // u (Pa) - fluid pressure at contact
        public float WaterElevation { get; set; }   // Water table elevation at this contact
        public bool IsSubmerged { get; set; }       // Whether contact is below water table
        public float EffectiveNormalStress { get; set; }  // σ'_n = σ_n - u

        // Contact history (for incremental calculations)
        public Vector3 ShearDisplacementIncrement { get; set; }
        public Vector3 AccumulatedShearDisplacement { get; set; }
        public bool HasSlipped { get; set; }
        public bool HasOpened { get; set; }

        // Time tracking
        public float TimeOfFirstContact { get; set; }
        public float TimeOfLastContact { get; set; }

        public ContactInterface()
        {
            BlockAId = -1;
            BlockBId = -1;
            IsBoundaryContact = false;
            ContactPoint = Vector3.Zero;
            ContactNormal = Vector3.UnitZ;
            PenetrationDepth = 0.0f;
            ContactArea = 0.0f;
            IsActive = false;
            Type = ContactType.None;
            NormalForce = Vector3.Zero;
            ShearForce = Vector3.Zero;
            TotalForce = Vector3.Zero;
            JointSetId = -1;
            IsJointContact = false;
            NormalStiffness = 1e9f;  // Default: 1 GPa/m
            ShearStiffness = 1e8f;   // Default: 100 MPa/m
            FrictionCoefficient = 0.6f;  // tan(30°) ≈ 0.577
            Cohesion = 0.0f;
            TensileStrength = 0.0f;
            DilationAngle = 0.0f;
            PorePressure = 0.0f;
            WaterElevation = 0.0f;
            IsSubmerged = false;
            EffectiveNormalStress = 0.0f;
            ShearDisplacementIncrement = Vector3.Zero;
            AccumulatedShearDisplacement = Vector3.Zero;
            HasSlipped = false;
            HasOpened = false;
            TimeOfFirstContact = 0.0f;
            TimeOfLastContact = 0.0f;
        }

        /// <summary>
        /// Calculates contact forces based on penetration and displacement.
        /// Uses linear elastic contact model with Mohr-Coulomb friction.
        /// </summary>
        public void CalculateContactForces(float deltaTime)
        {
            if (!IsActive || PenetrationDepth <= 0.0f)
            {
                NormalForce = Vector3.Zero;
                ShearForce = Vector3.Zero;
                TotalForce = Vector3.Zero;
                Type = ContactType.None;
                return;
            }

            // Normal force (compressive is positive)
            float normalForceMagnitude = NormalStiffness * PenetrationDepth * ContactArea;

            // Check for tension
            if (normalForceMagnitude < -TensileStrength * ContactArea)
            {
                // Tensile failure - contact opens
                NormalForce = Vector3.Zero;
                ShearForce = Vector3.Zero;
                TotalForce = Vector3.Zero;
                IsActive = false;
                HasOpened = true;
                Type = ContactType.Separated;
                return;
            }

            NormalForce = normalForceMagnitude * ContactNormal;

            // Calculate effective normal stress accounting for pore water pressure
            // Effective stress principle: σ'_n = σ_n - u
            // This reduces frictional resistance when joints are saturated
            float effectiveNormalForceMagnitude = normalForceMagnitude;

            if (IsSubmerged && PorePressure > 0.0f)
            {
                // Reduce normal force by pore pressure
                float pressureForce = PorePressure * ContactArea;
                effectiveNormalForceMagnitude = Math.Max(0.0f, normalForceMagnitude - pressureForce);
                EffectiveNormalStress = effectiveNormalForceMagnitude / Math.Max(ContactArea, 1e-10f);
            }
            else
            {
                EffectiveNormalStress = normalForceMagnitude / Math.Max(ContactArea, 1e-10f);
            }

            // Shear force calculation (incremental)
            // Accumulate shear displacement
            AccumulatedShearDisplacement += ShearDisplacementIncrement;

            // Calculate trial shear force
            Vector3 trialShearForce = -ShearStiffness * ContactArea * AccumulatedShearDisplacement;

            // Mohr-Coulomb criterion: τ_max = c + σ'_n * tan(φ)
            // Note: Uses EFFECTIVE normal stress when water is present
            float maxShearForce = Cohesion * ContactArea + effectiveNormalForceMagnitude * FrictionCoefficient;

            float trialShearMagnitude = trialShearForce.Length();

            if (trialShearMagnitude > maxShearForce)
            {
                // Sliding occurs
                ShearForce = maxShearForce * Vector3.Normalize(trialShearForce);
                HasSlipped = true;
                Type = ContactType.Sliding;

                // Update accumulated displacement to match the maximum shear force
                AccumulatedShearDisplacement = -ShearForce / (ShearStiffness * ContactArea);
            }
            else
            {
                // Elastic contact (stick)
                ShearForce = trialShearForce;
                HasSlipped = false;
                Type = ContactType.Sticking;
            }

            TotalForce = NormalForce + ShearForce;
        }

        /// <summary>
        /// Updates the shear displacement increment based on relative velocity.
        /// </summary>
        public void UpdateShearDisplacement(Vector3 relativeVelocity, float deltaTime)
        {
            // Project relative velocity onto the contact plane
            Vector3 relativeVelocityTangential = relativeVelocity -
                Vector3.Dot(relativeVelocity, ContactNormal) * ContactNormal;

            ShearDisplacementIncrement = relativeVelocityTangential * deltaTime;
        }

        /// <summary>
        /// Calculates pore water pressure at the contact point based on water table elevation.
        /// Uses hydrostatic pressure: u = γ_w * h, where h is depth below water table.
        /// </summary>
        /// <param name="waterTableZ">Elevation of water table (m)</param>
        /// <param name="waterDensity">Density of water (kg/m³), default 1000</param>
        public void CalculatePorePressure(float waterTableZ, float waterDensity = 1000.0f)
        {
            WaterElevation = waterTableZ;

            // Check if contact is below water table
            if (ContactPoint.Z < waterTableZ)
            {
                IsSubmerged = true;

                // Calculate depth below water table
                float depth = waterTableZ - ContactPoint.Z;

                // Hydrostatic pressure: u = ρ_w * g * h
                float gravity = 9.81f;  // m/s²
                PorePressure = waterDensity * gravity * depth;
            }
            else
            {
                IsSubmerged = false;
                PorePressure = 0.0f;
            }
        }

        /// <summary>
        /// Sets a custom pore pressure value directly (for imported data or special conditions).
        /// </summary>
        /// <param name="pressure">Pore pressure in Pascals</param>
        public void SetPorePressure(float pressure)
        {
            PorePressure = Math.Max(0.0f, pressure);
            IsSubmerged = PorePressure > 0.0f;
        }

        /// <summary>
        /// Resets contact state (call when contact breaks).
        /// </summary>
        public void Reset()
        {
            IsActive = false;
            PenetrationDepth = 0.0f;
            NormalForce = Vector3.Zero;
            ShearForce = Vector3.Zero;
            TotalForce = Vector3.Zero;
            ShearDisplacementIncrement = Vector3.Zero;
            AccumulatedShearDisplacement = Vector3.Zero;
            HasSlipped = false;
            Type = ContactType.None;
        }

        /// <summary>
        /// Sets joint mechanical properties from a JointSet.
        /// </summary>
        public void SetJointProperties(JointSet jointSet)
        {
            IsJointContact = true;
            JointSetId = jointSet.Id;
            NormalStiffness = jointSet.NormalStiffness;
            ShearStiffness = jointSet.ShearStiffness;
            Cohesion = jointSet.Cohesion;

            float frictionRad = jointSet.FrictionAngle * MathF.PI / 180.0f;
            FrictionCoefficient = MathF.Tan(frictionRad);

            TensileStrength = jointSet.TensileStrength;
            DilationAngle = jointSet.Dilation;
        }

        /// <summary>
        /// Checks if this contact lies on a specific joint set's plane.
        /// Compares the contact normal with the joint set's normal vector.
        /// </summary>
        /// <param name="jointSet">The joint set to check against</param>
        /// <param name="toleranceDegrees">Angular tolerance in degrees (default 5°)</param>
        /// <returns>True if the contact is on the joint plane, false otherwise</returns>
        public bool IsOnJointPlane(JointSet jointSet, float toleranceDegrees = 5.0f)
        {
            if (jointSet == null)
                return false;

            Vector3 jointNormal = jointSet.GetNormal();

            // Calculate the absolute value of the dot product (handles both orientations)
            float dot = Math.Abs(Vector3.Dot(ContactNormal, jointNormal));

            // Convert tolerance to cosine value
            float toleranceRad = toleranceDegrees * MathF.PI / 180.0f;
            float minDot = MathF.Cos(toleranceRad);

            return dot >= minDot;
        }

        /// <summary>
        /// Finds which joint set this contact belongs to, if any.
        /// </summary>
        /// <param name="jointSets">List of joint sets to check</param>
        /// <param name="toleranceDegrees">Angular tolerance in degrees</param>
        /// <returns>The matching JointSet, or null if no match found</returns>
        public JointSet FindMatchingJointSet(IList<JointSet> jointSets, float toleranceDegrees = 5.0f)
        {
            if (jointSets == null || jointSets.Count == 0)
                return null;

            // Find the joint set with the best (highest) normal alignment
            JointSet bestMatch = null;
            float bestDot = 0.0f;
            float minDot = MathF.Cos(toleranceDegrees * MathF.PI / 180.0f);

            foreach (var jointSet in jointSets)
            {
                Vector3 jointNormal = jointSet.GetNormal();
                float dot = Math.Abs(Vector3.Dot(ContactNormal, jointNormal));

                if (dot >= minDot && dot > bestDot)
                {
                    bestDot = dot;
                    bestMatch = jointSet;
                }
            }

            return bestMatch;
        }
    }

    /// <summary>
    /// Type of contact between blocks.
    /// </summary>
    public enum ContactType
    {
        None,       // No contact
        Sticking,   // Contact with no sliding (elastic)
        Sliding,    // Contact with sliding (plastic)
        Separated   // Contact has opened (tension)
    }
}
