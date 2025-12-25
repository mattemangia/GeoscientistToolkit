using System;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Represents a contact interface between two blocks or a block and a boundary.
    /// Handles contact detection, force calculation, and friction/cohesion.
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

            // Shear force calculation (incremental)
            // Accumulate shear displacement
            AccumulatedShearDisplacement += ShearDisplacementIncrement;

            // Calculate trial shear force
            Vector3 trialShearForce = -ShearStiffness * ContactArea * AccumulatedShearDisplacement;

            // Mohr-Coulomb criterion: τ_max = c + σ_n * tan(φ)
            float maxShearForce = Cohesion * ContactArea + normalForceMagnitude * FrictionCoefficient;

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
