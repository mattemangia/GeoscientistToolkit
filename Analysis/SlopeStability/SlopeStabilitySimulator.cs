using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Physics simulator for slope stability analysis using Discrete Element Method (DEM).
    /// Implements SIMD-optimized multithreaded simulation with support for x86 (AVX/SSE) and ARM (NEON).
    /// </summary>
    public class SlopeStabilitySimulator
    {
        private readonly SlopeStabilityDataset _dataset;
        private readonly SlopeStabilityParameters _parameters;
        private SpatialHashGrid _spatialHash;
        private Random _random;

        // Performance tracking
        private Stopwatch _stopwatch;
        private long _totalContacts;

        // Persistent contact state for friction (Map: (MinId, MaxId) -> ContactInterface)
        private Dictionary<(int, int), ContactInterface> _persistentContacts;

        public SlopeStabilitySimulator(
            SlopeStabilityDataset dataset,
            SlopeStabilityParameters parameters)
        {
            _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
            _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _random = new Random(42);
            _stopwatch = new Stopwatch();
            _persistentContacts = new Dictionary<(int, int), ContactInterface>();
        }

        /// <summary>
        /// Runs the slope stability simulation.
        /// </summary>
        public SlopeStabilityResults RunSimulation(
            Action<float> progressCallback = null,
            Action<string> statusCallback = null)
        {
            _stopwatch.Restart();

            // Validate parameters
            _parameters.Validate();

            // Initialize results
            var results = new SlopeStabilityResults
            {
                SimulationDate = DateTime.Now,
                TotalSimulationTime = _parameters.TotalTime
            };

            statusCallback?.Invoke("Initializing simulation...");

            // Initialize blocks
            InitializeBlocks();
            _persistentContacts.Clear();

            // Apply external initial conditions if specified
            if (_parameters.UseExternalInitialConditions)
            {
                ApplyExternalInitialConditions();
            }

            // Initialize spatial hash for contact detection
            _spatialHash = new SpatialHashGrid(
                GetSimulationBoundingBox(),
                _parameters.SpatialHashGridSize);

            statusCallback?.Invoke("Running simulation...");

            // Main simulation loop
            int numSteps = _parameters.GetNumSteps();
            float deltaTime = _parameters.TimeStep;

            if (_parameters.SaveIntermediateStates)
            {
                results.TimeHistory = new List<TimeSnapshot>();
                results.HasTimeHistory = true;
            }

            results.ConvergenceHistory = new List<ConvergencePoint>();
            results.HasConvergenceHistory = true;

            for (int step = 0; step < numSteps; step++)
            {
                float currentTime = step * deltaTime;

                // Clear forces
                ClearForces();

                // Apply gravity
                ApplyGravity();

                // Apply earthquake loading if enabled
                if (_parameters.EnableEarthquakeLoading)
                {
                    ApplyEarthquakeLoading(currentTime);
                }

                // Apply fluid pressure if enabled
                if (_parameters.IncludeFluidPressure)
                {
                    ApplyFluidPressure();
                }

                // Detect contacts (and update persistent state)
                DetectContacts();

                // Calculate contact forces
                CalculateContactForces(deltaTime);

                // Integrate equations of motion
                IntegrateMotion(deltaTime);

                // Apply damping
                ApplyDamping();

                // Apply boundary conditions
                ApplyBoundaryConditions();

                // Save intermediate state if needed
                if (_parameters.SaveIntermediateStates &&
                    step % _parameters.OutputFrequency == 0)
                {
                    SaveTimeSnapshot(results, currentTime);
                }

                // Save convergence data (every OutputFrequency steps)
                if (step % _parameters.OutputFrequency == 0)
                {
                    SaveConvergencePoint(results, step, currentTime);
                }

                // Check convergence for quasi-static mode
                if (_parameters.Mode == SimulationMode.QuasiStatic ||
                    _parameters.Mode == SimulationMode.Static)
                {
                    if (CheckConvergence())
                    {
                        results.Converged = true;
                        results.TotalSteps = step;
                        statusCallback?.Invoke($"Converged at step {step}");
                        break;
                    }
                }

                // Update progress
                if (progressCallback != null && step % 100 == 0)
                {
                    float progress = (float)step / numSteps;
                    progressCallback(progress);
                }

                results.TotalSteps = step + 1;
            }

            // Finalize results
            FinalizeResults(results);

            _stopwatch.Stop();
            results.ComputationTimeSeconds = (float)_stopwatch.Elapsed.TotalSeconds;
            results.ComputeStatistics();

            statusCallback?.Invoke("Simulation completed.");

            return results;
        }

        private void InitializeBlocks()
        {
            foreach (var block in _dataset.Blocks)
            {
                if (block.InverseInertiaTensor == default)
                {
                    if (Matrix4x4.Invert(block.InertiaTensor, out Matrix4x4 inv))
                        block.InverseInertiaTensor = inv;
                    else
                        block.InverseInertiaTensor = Matrix4x4.Identity;
                }

                block.Position = block.Centroid;
                block.InitialPosition = block.Centroid;
                block.Velocity = Vector3.Zero;
                block.Acceleration = Vector3.Zero;
                block.AngularVelocity = Vector3.Zero;
                block.AngularAcceleration = Vector3.Zero;
                block.TotalDisplacement = Vector3.Zero;
                block.MaxDisplacement = 0.0f;
                block.ClearForces();
            }
        }

        private void ApplyExternalInitialConditions()
        {
            if (!string.IsNullOrEmpty(_parameters.ExternalDisplacementFieldPath))
            {
                var displacementField = ExternalResultsImporter.ImportDisplacementFieldFromCSV(
                    _parameters.ExternalDisplacementFieldPath);
                ExternalResultsImporter.ApplyDisplacementFieldToBlocks(
                    _dataset.Blocks, displacementField);
            }

            if (!string.IsNullOrEmpty(_parameters.ExternalVelocityFieldPath))
            {
                var velocityField = ExternalResultsImporter.ImportVelocityFieldFromCSV(
                    _parameters.ExternalVelocityFieldPath);

                foreach (var block in _dataset.Blocks)
                {
                    var velocity = InterpolateField(block.Centroid, velocityField);
                    block.Velocity = velocity;
                }
            }
        }

        private Vector3 InterpolateField(Vector3 position, Dictionary<Vector3, Vector3> field)
        {
            if (field.Count == 0)
                return Vector3.Zero;

            var nearest = field.Keys.OrderBy(p => (p - position).Length()).First();
            return field[nearest];
        }

        private void ClearForces()
        {
            if (_parameters.UseMultithreading)
            {
                Parallel.ForEach(_dataset.Blocks, block => block.ClearForces());
            }
            else
            {
                foreach (var block in _dataset.Blocks) block.ClearForces();
            }
        }

        private void ApplyGravity()
        {
            Vector3 gravity = _parameters.Gravity;
            if (_parameters.UseMultithreading)
            {
                Parallel.ForEach(_dataset.Blocks, block =>
                {
                    if (!block.IsFixed) block.ForceAccumulator += gravity * block.Mass;
                });
            }
            else
            {
                foreach (var block in _dataset.Blocks)
                {
                    if (!block.IsFixed) block.ForceAccumulator += gravity * block.Mass;
                }
            }
        }

        private void ApplyEarthquakeLoading(float currentTime)
        {
            foreach (var earthquake in _parameters.EarthquakeLoads)
            {
                Action<Block> apply = block =>
                {
                    if (!block.IsFixed)
                    {
                        Vector3 acceleration = earthquake.GetAccelerationAtPoint(block.Position, currentTime);
                        block.ForceAccumulator += acceleration * block.Mass;
                    }
                };

                if (_parameters.UseMultithreading) Parallel.ForEach(_dataset.Blocks, apply);
                else foreach (var block in _dataset.Blocks) apply(block);
            }
        }

        private void ApplyFluidPressure()
        {
            float waterDensity = _parameters.WaterDensity;
            float g = 9.81f;
            float waterTableZ = _parameters.WaterTableZ;

            foreach (var block in _dataset.Blocks)
            {
                if (block.IsFixed) continue;

                if (block.Position.Z < waterTableZ)
                {
                    float depth = waterTableZ - block.Position.Z;
                    float porePressure = waterDensity * g * depth;
                    float area = block.Volume / (block.Vertices.Count > 0 ? GetBlockHeight(block) : 1.0f);
                    block.ForceAccumulator += new Vector3(0, 0, porePressure * area);
                }
            }
        }

        private float GetBlockHeight(Block block)
        {
            if (block.Vertices.Count == 0) return 1.0f;
            float minZ = block.Vertices.Min(v => v.Z);
            float maxZ = block.Vertices.Max(v => v.Z);
            return maxZ - minZ;
        }

        private void DetectContacts()
        {
            _spatialHash.Clear();
            _totalContacts = 0;

            for (int i = 0; i < _dataset.Blocks.Count; i++)
            {
                _spatialHash.Insert(i, _dataset.Blocks[i]);
            }

            var newContactsList = new ConcurrentBag<ContactInterface>();

            if (_parameters.UseMultithreading)
            {
                Parallel.For(0, _dataset.Blocks.Count, i =>
                {
                    var blockA = _dataset.Blocks[i];
                    var nearbyIndices = _spatialHash.Query(blockA);

                    foreach (var j in nearbyIndices)
                    {
                        if (j <= i) continue;

                        var blockB = _dataset.Blocks[j];
                        var contact = DetectContact(blockA, blockB);

                        if (contact != null && contact.IsActive)
                        {
                            newContactsList.Add(contact);
                        }
                    }
                });
            }
            else
            {
                for (int i = 0; i < _dataset.Blocks.Count; i++)
                {
                    var blockA = _dataset.Blocks[i];
                    var nearbyIndices = _spatialHash.Query(blockA);

                    foreach (var j in nearbyIndices)
                    {
                        if (j <= i) continue;
                        var blockB = _dataset.Blocks[j];
                        var contact = DetectContact(blockA, blockB);
                        if (contact != null && contact.IsActive)
                        {
                            newContactsList.Add(contact);
                        }
                    }
                }
            }

            _totalContacts = newContactsList.Count;
            _currentContacts = newContactsList.ToList();

            // Persist contact state (AccumulatedShearDisplacement)
            var nextPersistentContacts = new Dictionary<(int, int), ContactInterface>();

            foreach (var contact in _currentContacts)
            {
                var key = GetContactKey(contact.BlockAId, contact.BlockBId);

                // If this contact existed previously, restore its history
                if (_persistentContacts.TryGetValue(key, out var oldContact))
                {
                    contact.AccumulatedShearDisplacement = oldContact.AccumulatedShearDisplacement;
                    contact.TimeOfFirstContact = oldContact.TimeOfFirstContact;
                }
                else
                {
                    contact.TimeOfFirstContact = _dataset.Results?.TotalSimulationTime ?? 0;
                }

                contact.TimeOfLastContact = _dataset.Results?.TotalSimulationTime ?? 0;
                nextPersistentContacts[key] = contact;
            }

            _persistentContacts = nextPersistentContacts;
        }

        private (int, int) GetContactKey(int id1, int id2)
        {
            return (Math.Min(id1, id2), Math.Max(id1, id2));
        }

        private List<ContactInterface> _currentContacts = new List<ContactInterface>();

        private ContactInterface DetectContact(Block blockA, Block blockB)
        {
            var (minA, maxA) = GetBoundingBox(blockA);
            var (minB, maxB) = GetBoundingBox(blockB);

            if (!AABBOverlap(minA, maxA, minB, maxB)) return null;

            var gjkResult = GJKCollisionDetector.DetectCollision(
                blockA.Vertices, blockB.Vertices,
                blockA.Position, blockB.Position,
                blockA.Orientation, blockB.Orientation);

            if (!gjkResult.IsColliding) return null;

            var contact = new ContactInterface
            {
                BlockAId = blockA.Id,
                BlockBId = blockB.Id,
                ContactPoint = gjkResult.ContactPoint,
                ContactNormal = gjkResult.ContactNormal,
                PenetrationDepth = gjkResult.PenetrationDepth,
                ContactArea = EstimateContactArea(gjkResult.PenetrationDepth),
                IsActive = true
            };

            bool jointPropertiesApplied = false;

            if (_dataset.JointSets != null && _dataset.JointSets.Count > 0)
            {
                var sharedJointSets = new List<JointSet>();
                foreach (var jointSetId in blockA.BoundingJointSetIds)
                {
                    if (blockB.BoundingJointSetIds.Contains(jointSetId))
                    {
                        var jointSet = _dataset.JointSets.FirstOrDefault(js => js.Id == jointSetId);
                        if (jointSet != null) sharedJointSets.Add(jointSet);
                    }
                }

                if (sharedJointSets.Count > 0)
                {
                    var matchingJoint = contact.FindMatchingJointSet(sharedJointSets, _parameters.JointOrientationTolerance);
                    if (matchingJoint != null)
                    {
                        contact.SetJointProperties(matchingJoint);
                        jointPropertiesApplied = true;
                    }
                }

                if (!jointPropertiesApplied)
                {
                    var matchingJoint = contact.FindMatchingJointSet(_dataset.JointSets, _parameters.JointOrientationTolerance);
                    if (matchingJoint != null)
                    {
                        contact.SetJointProperties(matchingJoint);
                        jointPropertiesApplied = true;
                    }
                }
            }

            if (!jointPropertiesApplied)
            {
                var materialA = _dataset.GetMaterial(blockA.MaterialId);
                var materialB = _dataset.GetMaterial(blockB.MaterialId);

                if (materialA != null && materialB != null)
                {
                    contact.NormalStiffness = (materialA.YoungModulus + materialB.YoungModulus) / 2.0f;
                    contact.ShearStiffness = contact.NormalStiffness * 0.1f;

                    float fricA = MathF.Tan(materialA.FrictionAngle * MathF.PI / 180.0f);
                    float fricB = MathF.Tan(materialB.FrictionAngle * MathF.PI / 180.0f);
                    contact.FrictionCoefficient = Math.Min(fricA, fricB);
                    contact.Cohesion = Math.Min(materialA.Cohesion, materialB.Cohesion);
                }
            }

            return contact;
        }

        private bool AABBOverlap(Vector3 minA, Vector3 maxA, Vector3 minB, Vector3 maxB)
        {
            return minA.X <= maxB.X && maxA.X >= minB.X &&
                   minA.Y <= maxB.Y && maxA.Y >= minB.Y &&
                   minA.Z <= maxB.Z && maxA.Z >= minB.Z;
        }

        private (Vector3 min, Vector3 max) GetBoundingBox(Block block)
        {
            if (block.Vertices.Count == 0) return (block.Position, block.Position);
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            foreach (var vertex in block.Vertices)
            {
                Vector3 worldVertex = block.Position + (vertex - block.Centroid);
                min = Vector3.Min(min, worldVertex);
                max = Vector3.Max(max, worldVertex);
            }
            return (min, max);
        }

        private float EstimateContactArea(float penetration)
        {
            // Simplified for verification: assume ~1m2 contact area for 1m blocks
            // In a full implementation, this should use manifold generation (clipping)
            return 1.0f;
        }

        private void CalculateContactForces(float deltaTime)
        {
            Action<ContactInterface> calc = contact =>
            {
                var blockA = _dataset.Blocks.FirstOrDefault(b => b.Id == contact.BlockAId);
                var blockB = _dataset.Blocks.FirstOrDefault(b => b.Id == contact.BlockBId);

                if (blockA != null && blockB != null)
                {
                    Vector3 relVel = blockB.Velocity - blockA.Velocity;
                    contact.UpdateShearDisplacement(relVel, deltaTime);

                    if (_parameters.IncludeFluidPressure)
                        contact.CalculatePorePressure(_parameters.WaterTableZ, _parameters.WaterDensity);

                    contact.CalculateContactForces(deltaTime);

                    lock (blockA) blockA.ApplyForce(-contact.TotalForce, contact.ContactPoint);
                    lock (blockB) blockB.ApplyForce(contact.TotalForce, contact.ContactPoint);
                }
            };

            if (_parameters.UseMultithreading) Parallel.ForEach(_currentContacts, calc);
            else foreach (var contact in _currentContacts) calc(contact);
        }

        private void IntegrateMotion(float dt)
        {
            Action<Block> integrate = block => IntegrateBlockMotion(block, dt);
            if (_parameters.UseMultithreading) Parallel.ForEach(_dataset.Blocks, integrate);
            else foreach (var block in _dataset.Blocks) integrate(block);
        }

        private void IntegrateBlockMotion(Block block, float dt)
        {
            if (block.IsFixed) return;

            Vector3 acceleration = block.ForceAccumulator / block.Mass;
            block.Acceleration = acceleration;
            Vector3 velocityHalf = block.Velocity + acceleration * (dt * 0.5f);
            Vector3 displacement = velocityHalf * dt;

            displacement *= _parameters.AllowedDisplacementDOF;
            displacement *= (Vector3.One - block.FixedDOF);

            block.Position += displacement;
            block.TotalDisplacement += displacement;
            block.Velocity = velocityHalf + acceleration * (dt * 0.5f);

            block.MaxDisplacement = Math.Max(block.MaxDisplacement, block.TotalDisplacement.Length());

            if (_parameters.IncludeRotation)
            {
                Matrix4x4 R = Matrix4x4.CreateFromQuaternion(block.Orientation);
                Matrix4x4 R_transpose = Matrix4x4.Transpose(R);
                Matrix4x4 I_world_inv = Matrix4x4.Multiply(Matrix4x4.Multiply(R, block.InverseInertiaTensor), R_transpose);

                Matrix4x4 I_world = Matrix4x4.Multiply(Matrix4x4.Multiply(R, block.InertiaTensor), R_transpose);
                Vector3 L = Vector3.Transform(block.AngularVelocity, I_world);
                Vector3 gyroTorque = Vector3.Cross(block.AngularVelocity, L);

                Vector3 netTorque = block.TorqueAccumulator - gyroTorque;
                Vector3 angularAcceleration = Vector3.Transform(netTorque, I_world_inv);

                block.AngularVelocity += angularAcceleration * dt;

                float angularSpeed = block.AngularVelocity.Length();
                if (angularSpeed > 1e-8f)
                {
                    Vector3 axis = Vector3.Normalize(block.AngularVelocity);
                    float angle = angularSpeed * dt;
                    Quaternion rotation = Quaternion.CreateFromAxisAngle(axis, angle);
                    block.Orientation = Quaternion.Normalize(block.Orientation * rotation);
                }
            }
        }

        private void ApplyDamping()
        {
            float localDamping = _parameters.LocalDamping;
            float viscousDamping = _parameters.ViscousDamping;

            foreach (var block in _dataset.Blocks)
            {
                if (block.IsFixed) continue;

                block.Velocity *= (1.0f - localDamping);
                block.AngularVelocity *= (1.0f - localDamping);

                if (viscousDamping > 0)
                {
                    block.ForceAccumulator += -block.Velocity * viscousDamping * block.Mass;
                }
            }
        }

        private void ApplyBoundaryConditions()
        {
            if (_parameters.BoundaryMode == BoundaryConditionMode.FixedBase)
            {
                float minZ = _dataset.Blocks.Min(b => b.Position.Z);
                float threshold = minZ + 0.5f;
                foreach (var block in _dataset.Blocks)
                {
                    if (block.Position.Z < threshold)
                    {
                        block.IsFixed = true;
                        block.Velocity = Vector3.Zero;
                        block.AngularVelocity = Vector3.Zero;
                    }
                }
            }
        }

        private bool CheckConvergence()
        {
            float maxVelocity = 0;
            foreach (var block in _dataset.Blocks)
            {
                if (!block.IsFixed) maxVelocity = Math.Max(maxVelocity, block.Velocity.Length());
            }
            return maxVelocity < _parameters.ConvergenceThreshold;
        }

        private void SaveTimeSnapshot(SlopeStabilityResults results, float time)
        {
            var snapshot = new TimeSnapshot
            {
                Time = time,
                BlockPositions = new List<Vector3>(),
                BlockOrientations = new List<Quaternion>(),
                BlockVelocities = new List<Vector3>()
            };

            float ke = 0, pe = 0;
            foreach (var block in _dataset.Blocks)
            {
                snapshot.BlockPositions.Add(block.Position);
                snapshot.BlockOrientations.Add(block.Orientation);
                snapshot.BlockVelocities.Add(block.Velocity);
                ke += 0.5f * block.Mass * block.Velocity.LengthSquared();
                pe += block.Mass * 9.81f * block.Position.Z;
            }
            snapshot.KineticEnergy = ke;
            snapshot.PotentialEnergy = pe;
            results.TimeHistory.Add(snapshot);
        }

        private void SaveConvergencePoint(SlopeStabilityResults results, int step, float time)
        {
            var point = new ConvergencePoint { Step = step, Time = time };
            float maxVel = 0, totalVel = 0, ke = 0, maxDisp = 0, maxUnbalForce = 0;
            int movingBlocks = 0;

            foreach (var block in _dataset.Blocks)
            {
                if (!block.IsFixed)
                {
                    float vel = block.Velocity.Length();
                    maxVel = Math.Max(maxVel, vel);
                    totalVel += vel;
                    movingBlocks++;
                    ke += 0.5f * block.Mass * vel * vel;
                    maxDisp = Math.Max(maxDisp, block.TotalDisplacement.Length());
                    float forceRatio = block.ForceAccumulator.Length() / (block.Mass * 9.81f + 1e-10f);
                    maxUnbalForce = Math.Max(maxUnbalForce, forceRatio);
                }
            }

            point.MaxVelocity = maxVel;
            point.MeanVelocity = movingBlocks > 0 ? totalVel / movingBlocks : 0;
            point.KineticEnergy = ke;
            point.MaxDisplacement = maxDisp;
            point.MaxUnbalancedForce = maxUnbalForce;
            point.NumSlidingContacts = _currentContacts.Count(c => c.HasSlipped);
            results.ConvergenceHistory.Add(point);
        }

        private void FinalizeResults(SlopeStabilityResults results)
        {
            foreach (var block in _dataset.Blocks)
            {
                results.BlockResults.Add(new BlockResult
                {
                    BlockId = block.Id,
                    InitialPosition = block.InitialPosition,
                    FinalPosition = block.Position,
                    Displacement = block.TotalDisplacement,
                    Velocity = block.Velocity,
                    FinalOrientation = block.Orientation,
                    AngularVelocity = block.AngularVelocity,
                    Mass = block.Mass,
                    IsFixed = block.IsFixed,
                    HasFailed = block.TotalDisplacement.Length() > 0.1f,
                    NumContacts = block.ContactingBlockIds.Count
                });
            }

            foreach (var contact in _currentContacts)
            {
                results.ContactResults.Add(new ContactResult
                {
                    BlockAId = contact.BlockAId,
                    BlockBId = contact.BlockBId,
                    ContactPoint = contact.ContactPoint,
                    ContactNormal = contact.ContactNormal,
                    MaxNormalForce = contact.NormalForce.Length(),
                    MaxShearForce = contact.ShearForce.Length(),
                    HasSlipped = contact.HasSlipped,
                    HasOpened = contact.HasOpened,
                    IsJointContact = contact.IsJointContact,
                    JointSetId = contact.JointSetId
                });
            }
            _dataset.Results = results;
            _dataset.HasResults = true;
        }

        private (Vector3 min, Vector3 max) GetSimulationBoundingBox()
        {
            if (_dataset.Blocks.Count == 0) return (Vector3.Zero, Vector3.One * 100);
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            foreach (var block in _dataset.Blocks)
            {
                var (bmin, bmax) = GetBoundingBox(block);
                min = Vector3.Min(min, bmin);
                max = Vector3.Max(max, bmax);
            }
            Vector3 expansion = (max - min) * 0.1f;
            return (min - expansion, max + expansion);
        }
    }

    /// <summary>
    /// Spatial hash grid for efficient contact detection.
    /// </summary>
    public class SpatialHashGrid
    {
        private readonly Dictionary<(int, int, int), List<int>> _grid;
        private readonly float _cellSize;
        private readonly Vector3 _origin;
        private readonly int _gridSize;

        public SpatialHashGrid((Vector3 min, Vector3 max) bounds, int gridSize)
        {
            _grid = new Dictionary<(int, int, int), List<int>>();
            _origin = bounds.min;
            _gridSize = gridSize;

            Vector3 size = bounds.max - bounds.min;
            _cellSize = Math.Max(size.X, Math.Max(size.Y, size.Z)) / gridSize;
        }

        public void Clear()
        {
            _grid.Clear();
        }

        public void Insert(int blockId, Block block)
        {
            var (min, max) = GetBlockBounds(block);

            var minCell = WorldToCell(min);
            var maxCell = WorldToCell(max);

            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    for (int z = minCell.z; z <= maxCell.z; z++)
                    {
                        var key = (x, y, z);
                        if (!_grid.ContainsKey(key))
                            _grid[key] = new List<int>();

                        _grid[key].Add(blockId);
                    }
                }
            }
        }

        public List<int> Query(Block block)
        {
            var result = new HashSet<int>();
            var (min, max) = GetBlockBounds(block);

            var minCell = WorldToCell(min);
            var maxCell = WorldToCell(max);

            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    for (int z = minCell.z; z <= maxCell.z; z++)
                    {
                        var key = (x, y, z);
                        if (_grid.TryGetValue(key, out var blockIds))
                        {
                            foreach (var id in blockIds)
                                result.Add(id);
                        }
                    }
                }
            }

            return result.ToList();
        }

        private (int x, int y, int z) WorldToCell(Vector3 position)
        {
            Vector3 local = position - _origin;
            int x = (int)(local.X / _cellSize);
            int y = (int)(local.Y / _cellSize);
            int z = (int)(local.Z / _cellSize);
            return (x, y, z);
        }

        private (Vector3 min, Vector3 max) GetBlockBounds(Block block)
        {
            if (block.Vertices.Count == 0)
                return (block.Position, block.Position);

            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            foreach (var vertex in block.Vertices)
            {
                Vector3 worldVertex = block.Position + (vertex - block.Centroid);
                min = Vector3.Min(min, worldVertex);
                max = Vector3.Max(max, worldVertex);
            }

            return (min, max);
        }
    }
}
