// GeoscientistToolkit/Analysis/SlopeStability/SlopeStability2DSimulator.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// 2D slope stability simulator adapted from 3D SlopeStabilitySimulator.
    /// Performs rigid body dynamics for 2D polygon blocks with contact mechanics.
    /// </summary>
    public class SlopeStability2DSimulator
    {
        #region Fields

        private SlopeStability2DDataset _dataset;
        private List<Block2D> _blocks;
        private SlopeStabilityParameters _params;
        private float _sectionThickness;

        // Spatial partitioning for collision detection
        private SpatialHash2D _spatialHash;

        // Contacts
        private List<Contact2D> _activeContacts;

        // Simulation state
        private float _currentTime;
        private int _iteration;
        private bool _isRunning;

        // Statistics
        private float _totalKineticEnergy;
        private float _maxDisplacement;

        #endregion

        #region Constructor

        public SlopeStability2DSimulator(SlopeStability2DDataset dataset)
        {
            _dataset = dataset;
            _blocks = dataset.Blocks;
            _params = dataset.Parameters;
            _sectionThickness = dataset.SectionThickness;

            _activeContacts = new List<Contact2D>();
            _currentTime = 0;
            _iteration = 0;
            _isRunning = false;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Run the simulation.
        /// </summary>
        public SlopeStability2DResults Run(Action<float> progressCallback = null)
        {
            Logger.Log("[SlopeStability2DSimulator] Starting 2D simulation");
            Logger.Log($"  Blocks: {_blocks.Count}");
            Logger.Log($"  Time step: {_params.TimeStep}s");
            Logger.Log($"  Total time: {_params.TotalTime}s");

            Initialize();

            _isRunning = true;
            int maxIterations = _params.MaxIterations > 0
                ? _params.MaxIterations
                : (int)(_params.TotalTime / _params.TimeStep);

            var timeHistory = new List<TimeStep2D>();
            int historyInterval = Math.Max(1, maxIterations / 100); // Store ~100 snapshots

            while (_isRunning && _iteration < maxIterations && _currentTime < _params.TotalTime)
            {
                Step();

                // Record history
                if (_params.RecordTimeHistory && (_iteration % historyInterval == 0))
                {
                    timeHistory.Add(RecordTimeStep());
                }

                // Progress callback
                if (_iteration % 100 == 0 && progressCallback != null)
                {
                    float progress = _currentTime / _params.TotalTime;
                    progressCallback(progress);
                }

                // Check convergence for quasi-static analysis
                if (_params.SimulationMode == SimulationMode.QuasiStatic)
                {
                    if (_totalKineticEnergy < 1e-6f && _iteration > 100)
                    {
                        Logger.Log($"  Converged at iteration {_iteration}, KE={_totalKineticEnergy:E2}");
                        break;
                    }
                }

                _iteration++;
            }

            _isRunning = false;

            Logger.Log($"[SlopeStability2DSimulator] Simulation complete");
            Logger.Log($"  Iterations: {_iteration}");
            Logger.Log($"  Final time: {_currentTime:F3}s");
            Logger.Log($"  Final KE: {_totalKineticEnergy:E2} J");
            Logger.Log($"  Max displacement: {_maxDisplacement:F3}m");

            return GenerateResults(timeHistory);
        }

        /// <summary>
        /// Stop the simulation.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
        }

        #endregion

        #region Simulation Steps

        /// <summary>
        /// Initialize simulation.
        /// </summary>
        private void Initialize()
        {
            _currentTime = 0;
            _iteration = 0;
            _totalKineticEnergy = 0;
            _maxDisplacement = 0;

            // Reset block states
            foreach (var block in _blocks)
            {
                block.Position = block.Centroid;
                block.Velocity = Vector2.Zero;
                block.Acceleration = Vector2.Zero;
                block.Rotation = 0;
                block.AngularVelocity = 0;
                block.AngularAcceleration = 0;
                block.TotalDisplacement = Vector2.Zero;
                block.MaxDisplacement = 0;
                block.DisplacementHistory.Clear();
                block.ContactingBlockIds.Clear();
                block.HasFailed = false;
            }

            // Initialize spatial hash
            float cellSize = 2.0f; // 2m cells
            _spatialHash = new SpatialHash2D(cellSize);
            UpdateSpatialHash();

            Logger.Log("[SlopeStability2DSimulator] Initialized");
        }

        /// <summary>
        /// Perform one simulation step.
        /// </summary>
        private void Step()
        {
            // 1. Reset forces
            foreach (var block in _blocks)
            {
                if (!block.IsActive || block.IsFixed) continue;
                block.ResetForces();
            }

            // 2. Apply gravity
            ApplyGravity();

            // 3. Apply earthquake loading (if enabled)
            if (_params.UseEarthquakeLoading)
            {
                ApplyEarthquakeForces();
            }

            // 4. Detect contacts
            DetectContacts();

            // 5. Apply contact forces
            ApplyContactForces();

            // 6. Integrate motion (Velocity Verlet)
            IntegrateMotion();

            // 7. Apply damping
            ApplyDamping();

            // 8. Update tracking
            UpdateTracking();

            // 9. Update spatial hash
            if (_iteration % 10 == 0)
            {
                UpdateSpatialHash();
            }

            _currentTime += _params.TimeStep;
        }

        /// <summary>
        /// Apply gravitational forces to all blocks using configured gravity from parameters.
        /// </summary>
        private void ApplyGravity()
        {
            // Use gravity from parameters (convert 3D to 2D: use X and Z components, Z becomes Y in 2D)
            // Default is (0, 0, -9.81) so we get (0, -9.81) in 2D
            var gravity3D = _params.Gravity;
            var gravity = new Vector2(gravity3D.X, gravity3D.Z);

            foreach (var block in _blocks)
            {
                if (!block.IsActive || block.IsFixed) continue;

                var material = _dataset.GetMaterial(block.MaterialId);
                if (material == null) continue;

                float mass = block.GetMass(material.Density) * _sectionThickness;
                Vector2 gravityForce = mass * gravity;

                block.Force += gravityForce;
            }
        }

        /// <summary>
        /// Apply earthquake pseudo-static forces using configured gravity magnitude.
        /// </summary>
        private void ApplyEarthquakeForces()
        {
            // Simplified earthquake loading: horizontal acceleration as fraction of gravity
            float kh = _params.EarthquakeIntensity; // Horizontal seismic coefficient
            float gravityMagnitude = _params.Gravity.Length(); // Use configured gravity magnitude
            var earthquakeAccel = new Vector2(kh * gravityMagnitude, 0);

            foreach (var block in _blocks)
            {
                if (!block.IsActive || block.IsFixed) continue;

                var material = _dataset.GetMaterial(block.MaterialId);
                if (material == null) continue;

                float mass = block.GetMass(material.Density) * _sectionThickness;
                Vector2 earthquakeForce = mass * earthquakeAccel;

                block.Force += earthquakeForce;
            }
        }

        /// <summary>
        /// Detect contacts between blocks.
        /// </summary>
        private void DetectContacts()
        {
            _activeContacts.Clear();

            // Use spatial hash for broad phase
            var potentialPairs = _spatialHash.GetPotentialCollisions();

            foreach (var (id1, id2) in potentialPairs)
            {
                var block1 = _blocks[id1];
                var block2 = _blocks[id2];

                if (!block1.IsActive || !block2.IsActive) continue;

                // Narrow phase: polygon-polygon collision detection
                if (DetectPolygonContact(block1, block2, out var contact))
                {
                    _activeContacts.Add(contact);
                    block1.ContactingBlockIds.Add(block2.Id);
                    block2.ContactingBlockIds.Add(block1.Id);
                }
            }
        }

        /// <summary>
        /// Detect contact between two polygonal blocks using SAT.
        /// </summary>
        private bool DetectPolygonContact(Block2D block1, Block2D block2, out Contact2D contact)
        {
            contact = null;

            var vertices1 = block1.GetTransformedVertices();
            var vertices2 = block2.GetTransformedVertices();

            // Separating Axis Theorem (SAT) for 2D polygons
            // Check both polygon edge normals as potential separating axes

            float minPenetration = float.MaxValue;
            Vector2 contactNormal = Vector2.Zero;
            Vector2 contactPoint = Vector2.Zero;

            // Check axes from block1
            if (!CheckSeparation(vertices1, vertices2, ref minPenetration, ref contactNormal))
                return false;

            // Check axes from block2
            if (!CheckSeparation(vertices2, vertices1, ref minPenetration, ref contactNormal))
                return false;

            // Contact detected - create contact object
            // Find contact point (use centroid as approximation for now)
            contactPoint = (block1.Position + block2.Position) * 0.5f;

            contact = new Contact2D
            {
                Block1Id = block1.Id,
                Block2Id = block2.Id,
                ContactPoint = contactPoint,
                ContactNormal = Vector2.Normalize(contactNormal),
                Penetration = minPenetration
            };

            return true;
        }

        /// <summary>
        /// Check for separation along polygon edge normals.
        /// </summary>
        private bool CheckSeparation(List<Vector2> poly1, List<Vector2> poly2,
            ref float minPenetration, ref Vector2 contactNormal)
        {
            for (int i = 0; i < poly1.Count; i++)
            {
                var edge = poly1[(i + 1) % poly1.Count] - poly1[i];
                var axis = new Vector2(-edge.Y, edge.X); // Perpendicular
                axis = Vector2.Normalize(axis);

                // Project both polygons onto axis
                float min1 = float.MaxValue, max1 = float.MinValue;
                float min2 = float.MaxValue, max2 = float.MinValue;

                foreach (var v in poly1)
                {
                    float proj = Vector2.Dot(v, axis);
                    min1 = Math.Min(min1, proj);
                    max1 = Math.Max(max1, proj);
                }

                foreach (var v in poly2)
                {
                    float proj = Vector2.Dot(v, axis);
                    min2 = Math.Min(min2, proj);
                    max2 = Math.Max(max2, proj);
                }

                // Check for separation
                if (max1 < min2 || max2 < min1)
                    return false; // Separated

                // Calculate penetration
                float penetration = Math.Min(max1 - min2, max2 - min1);
                if (penetration < minPenetration)
                {
                    minPenetration = penetration;
                    contactNormal = axis;
                }
            }

            return true; // No separation found
        }

        /// <summary>
        /// Apply contact forces (normal and friction).
        /// </summary>
        private void ApplyContactForces()
        {
            foreach (var contact in _activeContacts)
            {
                var block1 = _blocks.Find(b => b.Id == contact.Block1Id);
                var block2 = _blocks.Find(b => b.Id == contact.Block2Id);

                if (block1 == null || block2 == null) continue;

                // Get material properties
                var mat1 = _dataset.GetMaterial(block1.MaterialId);
                var mat2 = _dataset.GetMaterial(block2.MaterialId);
                if (mat1 == null || mat2 == null) continue;

                // Use minimum friction and cohesion
                float friction = Math.Min(mat1.FrictionAngle, mat2.FrictionAngle) * MathF.PI / 180f;
                float cohesion = Math.Min(mat1.Cohesion, mat2.Cohesion);

                // Normal force (penalty method)
                float stiffness = 1e6f; // N/m (per unit thickness)
                float normalForce = stiffness * contact.Penetration * _sectionThickness;

                Vector2 normalForceVec = normalForce * contact.ContactNormal;

                // Apply normal forces
                if (!block1.IsFixed)
                    block1.AddForceAtPoint(normalForceVec, contact.ContactPoint);
                if (!block2.IsFixed)
                    block2.AddForceAtPoint(-normalForceVec, contact.ContactPoint);

                // Relative velocity at contact
                Vector2 relVel = block1.Velocity - block2.Velocity;
                Vector2 tangent = new Vector2(-contact.ContactNormal.Y, contact.ContactNormal.X);
                float tangentialVel = Vector2.Dot(relVel, tangent);

                // Friction force (Coulomb model)
                float maxFriction = normalForce * MathF.Tan(friction) + cohesion * _sectionThickness;
                float frictionForce = Math.Min(Math.Abs(tangentialVel) * stiffness * 0.1f, maxFriction);

                if (tangentialVel != 0)
                {
                    frictionForce *= -Math.Sign(tangentialVel);
                }

                Vector2 frictionForceVec = frictionForce * tangent;

                // Apply friction forces
                if (!block1.IsFixed)
                    block1.AddForceAtPoint(frictionForceVec, contact.ContactPoint);
                if (!block2.IsFixed)
                    block2.AddForceAtPoint(-frictionForceVec, contact.ContactPoint);

                // Store contact forces for results
                contact.NormalForce = normalForce;
                contact.ShearForce = Math.Abs(frictionForce);
                contact.IsSliding = Math.Abs(frictionForce) >= maxFriction * 0.99f;
            }
        }

        /// <summary>
        /// Integrate equations of motion using Velocity Verlet.
        /// </summary>
        private void IntegrateMotion()
        {
            float dt = _params.TimeStep;

            foreach (var block in _blocks)
            {
                if (!block.IsActive || block.IsFixed) continue;

                var material = _dataset.GetMaterial(block.MaterialId);
                if (material == null) continue;

                float mass = block.GetMass(material.Density) * _sectionThickness;
                float momentOfInertia = block.GetMassedMomentOfInertia(material.Density) * _sectionThickness;

                if (mass < 1e-6f) continue;

                // Velocity Verlet integration
                // v(t+dt/2) = v(t) + a(t) * dt/2
                Vector2 halfStepVel = block.Velocity + block.Acceleration * dt * 0.5f;
                float halfStepAngVel = block.AngularVelocity + block.AngularAcceleration * dt * 0.5f;

                // x(t+dt) = x(t) + v(t+dt/2) * dt
                block.Position += halfStepVel * dt;
                block.Rotation += halfStepAngVel * dt;

                // a(t+dt) = F(t+dt) / m
                block.Acceleration = block.Force / mass;
                block.AngularAcceleration = momentOfInertia > 1e-6f ? block.Torque / momentOfInertia : 0;

                // v(t+dt) = v(t+dt/2) + a(t+dt) * dt/2
                block.Velocity = halfStepVel + block.Acceleration * dt * 0.5f;
                block.AngularVelocity = halfStepAngVel + block.AngularAcceleration * dt * 0.5f;
            }
        }

        /// <summary>
        /// Apply damping to velocities.
        /// </summary>
        private void ApplyDamping()
        {
            float dampingFactor = 1.0f - _params.LocalDamping;

            // For quasi-static, use stronger damping
            if (_params.SimulationMode == SimulationMode.QuasiStatic)
            {
                dampingFactor = 0.9f;
            }

            foreach (var block in _blocks)
            {
                if (!block.IsActive || block.IsFixed) continue;

                block.Velocity *= dampingFactor;
                block.AngularVelocity *= dampingFactor;
            }
        }

        /// <summary>
        /// Update tracking statistics.
        /// </summary>
        private void UpdateTracking()
        {
            _totalKineticEnergy = 0;
            _maxDisplacement = 0;

            foreach (var block in _blocks)
            {
                if (!block.IsActive) continue;

                block.UpdateDisplacement();

                var material = _dataset.GetMaterial(block.MaterialId);
                if (material == null) continue;

                float mass = block.GetMass(material.Density) * _sectionThickness;
                float momentOfInertia = block.GetMassedMomentOfInertia(material.Density) * _sectionThickness;

                float transKE = 0.5f * mass * block.Velocity.LengthSquared();
                float rotKE = 0.5f * momentOfInertia * block.AngularVelocity * block.AngularVelocity;

                _totalKineticEnergy += transKE + rotKE;
                _maxDisplacement = Math.Max(_maxDisplacement, block.MaxDisplacement);
            }
        }

        /// <summary>
        /// Update spatial hash for broad-phase collision detection.
        /// </summary>
        private void UpdateSpatialHash()
        {
            _spatialHash.Clear();

            for (int i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                if (!block.IsActive) continue;

                var (min, max) = block.GetBounds();
                _spatialHash.Insert(i, min, max);
            }
        }

        /// <summary>
        /// Record current time step state.
        /// </summary>
        private TimeStep2D RecordTimeStep()
        {
            var timeStep = new TimeStep2D
            {
                Time = _currentTime,
                TotalKineticEnergy = _totalKineticEnergy
            };

            foreach (var block in _blocks)
            {
                if (!block.IsActive) continue;
                timeStep.BlockStates.Add((block.Id, block.Position, block.Rotation));
            }

            return timeStep;
        }

        #endregion

        #region Results Generation

        /// <summary>
        /// Generate final results.
        /// </summary>
        private SlopeStability2DResults GenerateResults(List<TimeStep2D> timeHistory)
        {
            var results = new SlopeStability2DResults
            {
                TotalSimulationTime = _currentTime,
                TotalIterations = _iteration,
                Converged = _totalKineticEnergy < 1e-5f,
                FinalKineticEnergy = _totalKineticEnergy,
                MaxDisplacement = _maxDisplacement,
                TimeHistory = timeHistory,
                HasTimeHistory = timeHistory.Count > 0
            };

            // Block results
            foreach (var block in _blocks)
            {
                var blockResult = new Block2DResult
                {
                    BlockId = block.Id,
                    FinalPosition = block.Position,
                    TotalDisplacement = block.TotalDisplacement,
                    MaxDisplacement = block.MaxDisplacement,
                    FinalRotation = block.Rotation,
                    FinalVelocity = block.Velocity,
                    HasFailed = block.HasFailed,
                    SafetyFactor = CalculateSafetyFactor(block),
                    MaxStress = CalculateMaxStress(block)
                };

                results.BlockResults.Add(blockResult);
            }

            // Contact results
            foreach (var contact in _activeContacts)
            {
                var contactResult = new Contact2DResult
                {
                    Block1Id = contact.Block1Id,
                    Block2Id = contact.Block2Id,
                    ContactPoint = contact.ContactPoint,
                    ContactNormal = contact.ContactNormal,
                    NormalForce = contact.NormalForce,
                    ShearForce = contact.ShearForce,
                    IsSliding = contact.IsSliding
                };

                results.ContactResults.Add(contactResult);
            }

            return results;
        }

        /// <summary>
        /// Calculate safety factor for a block (simplified).
        /// </summary>
        private float CalculateSafetyFactor(Block2D block)
        {
            // Simplified SF calculation based on displacement
            if (block.MaxDisplacement < 0.01f)
                return float.PositiveInfinity;

            return 1.0f / (1.0f + block.MaxDisplacement);
        }

        /// <summary>
        /// Calculate an approximate maximum stress for a block (plane stress).
        /// </summary>
        private float CalculateMaxStress(Block2D block)
        {
            var material = _dataset.GetMaterial(block.MaterialId);
            if (material == null)
                return 0.0f;

            float contactNormal = 0.0f;
            float contactShear = 0.0f;

            foreach (var contact in _activeContacts)
            {
                if (contact.Block1Id != block.Id && contact.Block2Id != block.Id)
                    continue;

                contactNormal += contact.NormalForce;
                contactShear += contact.ShearForce;
            }

            float area = Math.Max(block.Area * _sectionThickness, 1e-6f);
            float sigmaX = contactNormal / area;
            float gravityMagnitude = _params.Gravity.Length(); // Use configured gravity magnitude
            float sigmaY = block.GetMass(material.Density) * _sectionThickness * gravityMagnitude / area;
            float tauXY = contactShear / area;

            float vonMises = MathF.Sqrt(
                sigmaX * sigmaX - sigmaX * sigmaY + sigmaY * sigmaY + 3.0f * tauXY * tauXY);

            return vonMises;
        }

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// Contact between two 2D blocks.
    /// </summary>
    internal class Contact2D
    {
        public int Block1Id { get; set; }
        public int Block2Id { get; set; }
        public Vector2 ContactPoint { get; set; }
        public Vector2 ContactNormal { get; set; }
        public float Penetration { get; set; }
        public float NormalForce { get; set; }
        public float ShearForce { get; set; }
        public bool IsSliding { get; set; }
    }

    /// <summary>
    /// Spatial hash grid for broad-phase collision detection in 2D.
    /// </summary>
    internal class SpatialHash2D
    {
        private Dictionary<(int, int), List<int>> _grid;
        private float _cellSize;

        public SpatialHash2D(float cellSize)
        {
            _cellSize = cellSize;
            _grid = new Dictionary<(int, int), List<int>>();
        }

        public void Clear()
        {
            _grid.Clear();
        }

        public void Insert(int id, Vector2 min, Vector2 max)
        {
            int minX = (int)MathF.Floor(min.X / _cellSize);
            int minY = (int)MathF.Floor(min.Y / _cellSize);
            int maxX = (int)MathF.Floor(max.X / _cellSize);
            int maxY = (int)MathF.Floor(max.Y / _cellSize);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    var key = (x, y);
                    if (!_grid.ContainsKey(key))
                        _grid[key] = new List<int>();

                    _grid[key].Add(id);
                }
            }
        }

        public HashSet<(int, int)> GetPotentialCollisions()
        {
            var pairs = new HashSet<(int, int)>();

            foreach (var cell in _grid.Values)
            {
                for (int i = 0; i < cell.Count; i++)
                {
                    for (int j = i + 1; j < cell.Count; j++)
                    {
                        int id1 = Math.Min(cell[i], cell[j]);
                        int id2 = Math.Max(cell[i], cell[j]);
                        pairs.Add((id1, id2));
                    }
                }
            }

            return pairs;
        }
    }

    #endregion
}
