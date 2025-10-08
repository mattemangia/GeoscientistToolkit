// GeoscientistToolkit/Analysis/AcousticSimulation/ChunkedAcousticSimulator.cs
// FIXED VERSION - Implements a stabilized stencil to prevent checkerboarding, adds global boundary conditions, and generalizes the full-face source.

using System.Text;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

public class ChunkedAcousticSimulator : IDisposable
{
    private readonly nint[] _bufMaxVel = new nint[3];
    private readonly nint[] _bufStr = new nint[6];
    private readonly nint[] _bufVel = new nint[3];
    private readonly int _chunkDepth;

    private readonly List<WaveFieldChunk> _chunks = new();
    private readonly CL _cl = CL.GetApi();
    private readonly float _damageRatePerSec = 0.2f;
    private readonly bool _isRickerActive;

    private readonly float _lambda, _mu;
    private readonly SimulationParameters _params;

    private readonly float _rickerT0;

    private readonly bool _useGPU;
    private float _artificialDampingFactor = 0.2f;
    private nint _bufDmg, _bufYoungsModulus, _bufPoissonRatio;
    private nint _bufMat, _bufDen, _bufMatLookup;
    private float _dt;
    private float _dynamicViscosityCoeff;
    private long _lastDeviceBytes;
    private float[,,] _perVoxelPoissonRatio;
    private float[,,] _perVoxelYoungsModulus;
    private nint _platform, _device, _context, _queue, _program, _kernelStress, _kernelVelocity;
    private bool _usePerVoxelProperties;

    public ChunkedAcousticSimulator(SimulationParameters parameters)
    {
        _params = parameters;
        _useGPU = parameters.UseGPU;

        var E = MathF.Max(1e-6f, _params.YoungsModulusMPa) * 1e6f;
        var nu = _params.PoissonRatio;
        _mu = E / (2f * (1f + nu));
        _lambda = E * nu / ((1f + nu) * (1f - 2f * nu));

        if (_params.UseRickerWavelet)
        {
            _isRickerActive = true;
            var freq = Math.Max(1.0f, _params.SourceFrequencyKHz * 1000f);
            _rickerT0 = 1.2f / freq;
        }

        var targetBytes = (_params.ChunkSizeMB > 0 ? _params.ChunkSizeMB : 256) * 1024L * 1024L;
        var bytesPerZ = (long)_params.Width * _params.Height * sizeof(float) * 16;
        _chunkDepth = (int)Math.Clamp(targetBytes / Math.Max(1, bytesPerZ), 8, _params.Depth);

        InitChunks();

        if (_useGPU)
            try
            {
                InitOpenCL();
                BuildProgramAndKernels();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CL] GPU init failed, falling back to CPU: {ex.Message}");
                _useGPU = false;
                CleanupOpenCL();
            }
    }

    public long CurrentMemoryUsageMB =>
        (GC.GetTotalMemory(false) + Interlocked.Read(ref _lastDeviceBytes)) / (1024 * 1024);

    public float TimeStep => _dt;
    public float Progress { get; private set; }
    public int CurrentStep { get; private set; }

    public void Dispose()
    {
        if (_useGPU) CleanupOpenCL();

        foreach (var chunk in _chunks.Where(c => !string.IsNullOrEmpty(c.FilePath)))
            try
            {
                if (File.Exists(chunk.FilePath)) File.Delete(chunk.FilePath);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[ChunkedSimulator] Cleanup failed: {ex.Message}");
            }

        _chunks.Clear();
    }

    public event EventHandler<SimulationProgressEventArgs> ProgressUpdated;
    public event EventHandler<WaveFieldUpdateEventArgs> WaveFieldUpdated;

    public void SetPerVoxelMaterialProperties(float[,,] youngsModulus, float[,,] poissonRatio)
    {
        _perVoxelYoungsModulus = youngsModulus;
        _perVoxelPoissonRatio = poissonRatio;
        _usePerVoxelProperties = true;
    }

    private void InitChunks()
    {
        for (var z0 = 0; z0 < _params.Depth; z0 += _chunkDepth)
        {
            var z1 = Math.Min(z0 + _chunkDepth, _params.Depth);
            var d = z1 - z0;

            _chunks.Add(new WaveFieldChunk
            {
                StartZ = z0, EndZ = z1,
                Vx = new float[_params.Width, _params.Height, d],
                Vy = new float[_params.Width, _params.Height, d],
                Vz = new float[_params.Width, _params.Height, d],
                Sxx = new float[_params.Width, _params.Height, d],
                Syy = new float[_params.Width, _params.Height, d],
                Szz = new float[_params.Width, _params.Height, d],
                Sxy = new float[_params.Width, _params.Height, d],
                Sxz = new float[_params.Width, _params.Height, d],
                Syz = new float[_params.Width, _params.Height, d],
                Damage = new float[_params.Width, _params.Height, d],
                MaxAbsVx = new float[_params.Width, _params.Height, d],
                MaxAbsVy = new float[_params.Width, _params.Height, d],
                MaxAbsVz = new float[_params.Width, _params.Height, d]
            });
        }

        Logger.Log($"[ChunkedSimulator] Initialized {_chunks.Count} chunks of depth ~{_chunkDepth}");
    }

    public async Task<SimulationResults> RunAsync(byte[,,] labels, float[,,] density, CancellationToken token)
    {
        var started = DateTime.Now;
        var results = new SimulationResults
            { TimeSeriesSnapshots = _params.SaveTimeSeries ? new List<WaveFieldSnapshot>() : null };

        CalculateTimeStep(labels, density);

        float minDensity = float.MaxValue, maxDensity = float.MinValue;
        for (var i = 0; i < density.GetLength(0); i++)
        for (var j = 0; j < density.GetLength(1); j++)
        for (var k = 0; k < density.GetLength(2); k++)
            if (_params.IsMaterialSelected(labels[i, j, k]))
            {
                minDensity = Math.Min(minDensity, density[i, j, k]);
                maxDensity = Math.Max(maxDensity, density[i, j, k]);
            }

        Logger.Log($"[Simulator] Running with density range (kg/m³): {minDensity:F2} to {maxDensity:F2}");

        if (_params.EnableOffloading)
        {
            Logger.Log(
                $"[ChunkedSimulator] Offloading all chunks to disk before starting simulation. Offload dir: {_params.OffloadDirectory}");
            foreach (var chunk in _chunks) await OffloadChunkAsync(chunk);
        }

        var maxSteps = Math.Max(1, _params.TimeSteps);
        var step = 0;
        bool pHit = false, sHit = false;
        int pStep = 0, sStep = 0;

        while (step < maxSteps && !token.IsCancellationRequested)
        {
            step++;
            CurrentStep = step;

            var sourceValue = GetCurrentSourceValue(step);

            await ProcessStressUpdatePass(labels, density, sourceValue, token);
            await ProcessVelocityUpdatePass(labels, density, token);

            if (_params.SaveTimeSeries && step % _params.SnapshotInterval == 0)
                results.TimeSeriesSnapshots?.Add(CreateSnapshot(step));

            if (!pHit && CheckPWaveArrival())
            {
                pHit = true;
                pStep = step;
            }

            if (pHit && !sHit && CheckSWaveArrival())
            {
                sHit = true;
                sStep = step;
            }

            Progress = (float)step / maxSteps;
            ProgressUpdated?.Invoke(this,
                new SimulationProgressEventArgs
                    { Progress = Progress, Step = step, Message = $"Step {step}/{maxSteps}" });
        }

        foreach (var chunk in _chunks.Where(c => !c.IsInMemory))
            await LoadChunkAsync(chunk);

        var distance = CalculateTxRxDistance();
        results.PWaveVelocity = pStep > 0 ? distance / (pStep * _dt) : 0;
        results.SWaveVelocity = sStep > 0 ? distance / (sStep * _dt) : 0;
        results.VpVsRatio = results.SWaveVelocity > 0 ? results.PWaveVelocity / results.SWaveVelocity : 0;
        results.PWaveTravelTime = pStep;
        results.SWaveTravelTime = sStep;
        results.TotalTimeSteps = step;
        results.ComputationTime = DateTime.Now - started;

        results.WaveFieldVx = await ReconstructMaxFieldAsync(0);
        results.WaveFieldVy = await ReconstructMaxFieldAsync(1);
        results.WaveFieldVz = await ReconstructMaxFieldAsync(2);
        results.DamageField = GetDamageField();

        return results;
    }

    private async Task ProcessStressUpdatePass(byte[,,] labels, float[,,] density, float sourceValue,
        CancellationToken token)
    {
        // Case 1: In-Memory Processing. Fast, but requires high RAM.
        // Exchange all boundaries first, then apply global BCs, then process all chunks.
        if (!_params.EnableOffloading)
        {
            for (var i = 0; i < _chunks.Count - 1; i++)
            {
                ExchangeBoundary(_chunks[i].Vx, _chunks[i + 1].Vx);
                ExchangeBoundary(_chunks[i].Vy, _chunks[i + 1].Vy);
                ExchangeBoundary(_chunks[i].Vz, _chunks[i + 1].Vz);
            }

            ApplyGlobalBoundaryConditions(true);

            foreach (var chunk in _chunks)
                if (_useGPU)
                {
                    await ProcessChunkGPU_StressOnly(chunk, labels, density, sourceValue, token);
                }
                else
                {
                    if (sourceValue != 0) ApplySourceToChunkCPU(chunk, sourceValue, labels);
                    UpdateChunkStressCPU(chunk, labels, density);
                }

            return;
        }

        // Case 2: Efficient Offloading with a Sliding Window.
        // This minimizes disk I/O by keeping only two adjacent chunks in memory at a time.
        if (_chunks.Count <= 1)
        {
            if (_chunks.Any())
            {
                var chunk = _chunks.First();
                await LoadChunkAsync(chunk);
                ApplyGlobalBoundaryConditions(true);
                if (_useGPU)
                {
                    await ProcessChunkGPU_StressOnly(chunk, labels, density, sourceValue, token);
                }
                else
                {
                    if (sourceValue != 0) ApplySourceToChunkCPU(chunk, sourceValue, labels);
                    UpdateChunkStressCPU(chunk, labels, density);
                }

                await OffloadChunkAsync(chunk);
            }

            return;
        }

        for (var i = 0; i < _chunks.Count; i++)
        {
            var currentChunk = _chunks[i];
            await LoadChunkAsync(currentChunk);

            // Load the next chunk to exchange boundaries
            if (i + 1 < _chunks.Count)
            {
                await LoadChunkAsync(_chunks[i + 1]);
                ExchangeBoundary(currentChunk.Vx, _chunks[i + 1].Vx);
                ExchangeBoundary(currentChunk.Vy, _chunks[i + 1].Vy);
                ExchangeBoundary(currentChunk.Vz, _chunks[i + 1].Vz);
            }

            // Process the current chunk. Its boundaries are now up-to-date.
            ApplyGlobalBoundaryConditions(true);
            if (_useGPU)
            {
                await ProcessChunkGPU_StressOnly(currentChunk, labels, density, sourceValue, token);
            }
            else
            {
                if (sourceValue != 0) ApplySourceToChunkCPU(currentChunk, sourceValue, labels);
                UpdateChunkStressCPU(currentChunk, labels, density);
            }

            // Offload the previous chunk, which is no longer needed.
            if (i > 0) await OffloadChunkAsync(_chunks[i - 1]);
        }

        // Offload the final two chunks
        await OffloadChunkAsync(_chunks[_chunks.Count - 2]);
        await OffloadChunkAsync(_chunks.Last());
    }

    private async Task ProcessVelocityUpdatePass(byte[,,] labels, float[,,] density, CancellationToken token)
    {
        // Case 1: In-Memory Processing.
        if (!_params.EnableOffloading)
        {
            for (var i = 0; i < _chunks.Count - 1; i++)
            {
                ExchangeBoundary(_chunks[i].Sxx, _chunks[i + 1].Sxx);
                ExchangeBoundary(_chunks[i].Syy, _chunks[i + 1].Syy);
                ExchangeBoundary(_chunks[i].Szz, _chunks[i + 1].Szz);
                ExchangeBoundary(_chunks[i].Sxy, _chunks[i + 1].Sxy);
                ExchangeBoundary(_chunks[i].Sxz, _chunks[i + 1].Sxz);
                ExchangeBoundary(_chunks[i].Syz, _chunks[i + 1].Syz);
            }

            ApplyGlobalBoundaryConditions(false);

            foreach (var chunk in _chunks)
            {
                if (_useGPU)
                    await ProcessChunkGPU_VelocityOnly(chunk, labels, density, token);
                else
                    UpdateChunkVelocityCPU(chunk, labels, density);

                // After processing, this chunk is up-to-date for this timestep
                FireWaveFieldUpdate(chunk);
            }

            return;
        }

        // Case 2: Efficient Offloading with a Sliding Window.
        if (_chunks.Count <= 1)
        {
            if (_chunks.Any())
            {
                var chunk = _chunks.First();
                await LoadChunkAsync(chunk);
                ApplyGlobalBoundaryConditions(false);
                if (_useGPU) await ProcessChunkGPU_VelocityOnly(chunk, labels, density, token);
                else UpdateChunkVelocityCPU(chunk, labels, density);
                FireWaveFieldUpdate(chunk);
                await OffloadChunkAsync(chunk);
            }

            return;
        }

        for (var i = 0; i < _chunks.Count; i++)
        {
            var currentChunk = _chunks[i];
            await LoadChunkAsync(currentChunk);

            if (i + 1 < _chunks.Count)
            {
                await LoadChunkAsync(_chunks[i + 1]);
                ExchangeBoundary(currentChunk.Sxx, _chunks[i + 1].Sxx);
                ExchangeBoundary(currentChunk.Syy, _chunks[i + 1].Syy);
                ExchangeBoundary(currentChunk.Szz, _chunks[i + 1].Szz);
                ExchangeBoundary(currentChunk.Sxy, _chunks[i + 1].Sxy);
                ExchangeBoundary(currentChunk.Sxz, _chunks[i + 1].Sxz);
                ExchangeBoundary(currentChunk.Syz, _chunks[i + 1].Syz);
            }

            ApplyGlobalBoundaryConditions(false);
            if (_useGPU) await ProcessChunkGPU_VelocityOnly(currentChunk, labels, density, token);
            else UpdateChunkVelocityCPU(currentChunk, labels, density);

            FireWaveFieldUpdate(currentChunk);

            if (i > 0) await OffloadChunkAsync(_chunks[i - 1]);
        }

        await OffloadChunkAsync(_chunks[_chunks.Count - 2]);
        await OffloadChunkAsync(_chunks.Last());
    }

    private void FireWaveFieldUpdate(WaveFieldChunk chunk)
    {
        if (CurrentStep % 5 == 0) // Throttle updates to avoid overwhelming the UI thread
            WaveFieldUpdated?.Invoke(this, new WaveFieldUpdateEventArgs
            {
                ChunkVelocityFields = (chunk.Vx, chunk.Vy, chunk.Vz),
                ChunkStartZ = chunk.StartZ,
                ChunkDepth = chunk.EndZ - chunk.StartZ,
                TimeStep = CurrentStep,
                SimTime = CurrentStep * _dt
            });
    }

    private void ApplyGlobalBoundaryConditions(bool isStressUpdate)
    {
        foreach (var chunk in _chunks)
        {
            if (!chunk.IsInMemory) continue;

            var d = chunk.EndZ - chunk.StartZ;
            // X boundaries
            for (var z = 0; z < d; z++)
            for (var y = 0; y < _params.Height; y++)
                if (isStressUpdate)
                {
                    chunk.Vx[0, y, z] = chunk.Vx[1, y, z];
                    chunk.Vx[_params.Width - 1, y, z] = chunk.Vx[_params.Width - 2, y, z];
                }
                else
                {
                    chunk.Sxx[0, y, z] = chunk.Sxx[1, y, z];
                    chunk.Sxx[_params.Width - 1, y, z] = chunk.Sxx[_params.Width - 2, y, z];
                    chunk.Sxy[0, y, z] = chunk.Sxy[1, y, z];
                    chunk.Sxy[_params.Width - 1, y, z] = chunk.Sxy[_params.Width - 2, y, z];
                    chunk.Sxz[0, y, z] = chunk.Sxz[1, y, z];
                    chunk.Sxz[_params.Width - 1, y, z] = chunk.Sxz[_params.Width - 2, y, z];
                }

            // Y boundaries
            for (var z = 0; z < d; z++)
            for (var x = 0; x < _params.Width; x++)
                if (isStressUpdate)
                {
                    chunk.Vy[x, 0, z] = chunk.Vy[x, 1, z];
                    chunk.Vy[x, _params.Height - 1, z] = chunk.Vy[x, _params.Height - 2, z];
                }
                else
                {
                    chunk.Syy[x, 0, z] = chunk.Syy[x, 1, z];
                    chunk.Syy[x, _params.Height - 1, z] = chunk.Syy[x, _params.Height - 2, z];
                    chunk.Sxy[x, 0, z] = chunk.Sxy[x, 1, z];
                    chunk.Sxy[x, _params.Height - 1, z] = chunk.Sxy[x, _params.Height - 2, z];
                    chunk.Syz[x, 0, z] = chunk.Syz[x, 1, z];
                    chunk.Syz[x, _params.Height - 1, z] = chunk.Syz[x, _params.Height - 2, z];
                }
        }

        // Z boundaries (only for first and last chunk)
        if (_chunks.Any())
        {
            var firstChunk = _chunks.First();
            var lastChunk = _chunks.Last();

            if (!firstChunk.IsInMemory || !lastChunk.IsInMemory) return;

            var d_last = lastChunk.EndZ - lastChunk.StartZ;

            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
                if (isStressUpdate)
                {
                    firstChunk.Vz[x, y, 0] = firstChunk.Vz[x, y, 1];
                    lastChunk.Vz[x, y, d_last - 1] = lastChunk.Vz[x, y, d_last - 2];
                }
                else
                {
                    firstChunk.Szz[x, y, 0] = firstChunk.Szz[x, y, 1];
                    lastChunk.Szz[x, y, d_last - 1] = lastChunk.Szz[x, y, d_last - 2];
                    firstChunk.Sxz[x, y, 0] = firstChunk.Sxz[x, y, 1];
                    lastChunk.Sxz[x, y, d_last - 1] = lastChunk.Sxz[x, y, d_last - 2];
                    firstChunk.Syz[x, y, 0] = firstChunk.Syz[x, y, 1];
                    lastChunk.Syz[x, y, d_last - 1] = lastChunk.Syz[x, y, d_last - 2];
                }
        }
    }

    private void ExchangeBoundary(float[,,] topField, float[,,] bottomField)
    {
        var width = topField.GetLength(0);
        var height = topField.GetLength(1);
        var topDepth = topField.GetLength(2);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            topField[x, y, topDepth - 1] = bottomField[x, y, 1];
            bottomField[x, y, 0] = topField[x, y, topDepth - 2];
        }
    }

    private float GetCurrentSourceValue(int step)
    {
        var baseAmp = _params.SourceAmplitude * MathF.Sqrt(MathF.Max(1e-6f, _params.SourceEnergyJ));
        baseAmp *= 1e6f; // Scaling factor

        if (!_isRickerActive)
            return step >= 1 && step <= 3 ? baseAmp : 0f;

        var t = step * _dt;
        if (t > 2 * _rickerT0) return 0f;

        var freq = Math.Max(1.0f, _params.SourceFrequencyKHz * 1000f);
        var x = MathF.PI * freq * (t - _rickerT0);
        var xx = x * x;
        return baseAmp * (1.0f - 2.0f * xx) * MathF.Exp(-xx);
    }

    private void CalculateTimeStep(byte[,,] labels, float[,,] density)
    {
        var vpMax = 0f;

        for (var z = 0; z < _params.Depth; z++)
        for (var y = 0; y < _params.Height; y++)
        for (var x = 0; x < _params.Width; x++)
        {
            if (!_params.IsMaterialSelected(labels[x, y, z])) continue;
            if (density[x, y, z] <= 0) continue;

            var rho = MathF.Max(100f, density[x, y, z]);
            float E, nu;

            if (_usePerVoxelProperties && _perVoxelYoungsModulus != null && _perVoxelPoissonRatio != null)
            {
                E = _perVoxelYoungsModulus[x, y, z] * 1e6f;
                nu = _perVoxelPoissonRatio[x, y, z];
            }
            else
            {
                E = _params.YoungsModulusMPa * 1e6f;
                nu = _params.PoissonRatio;
            }

            if (E <= 0 || rho <= 0 || nu >= 0.5f || nu <= -1.0f) continue;

            var lambda_val = E * nu / ((1f + nu) * (1f - 2f * nu));
            var mu_val = E / (2f * (1f + nu));
            var vp = MathF.Sqrt((lambda_val + 2f * mu_val) / rho);
            if (vp > vpMax) vpMax = vp;
        }

        if (vpMax < 1e-3f)
        {
            _dt = _params.TimeStepSeconds > 0 ? _params.TimeStepSeconds : 1e-7f;
            Logger.LogWarning(
                $"[CFL] Could not determine valid Vp_max from selected materials. Using default dt={_dt * 1e6f:F4} µs.");
            return;
        }

        _dt = 0.25f * (_params.PixelSize / (MathF.Sqrt(3) * vpMax));
        Logger.Log($"[CFL] Calculated stable timestep: dt={_dt * 1e6f:F4} µs based on Vp_max={vpMax:F0} m/s");
    }

    private bool CheckPWaveArrival()
    {
        var rx = Math.Clamp((int)(_params.RxPosition.X * _params.Width), 1, _params.Width - 2);
        var ry = Math.Clamp((int)(_params.RxPosition.Y * _params.Height), 1, _params.Height - 2);
        var rz = Math.Clamp((int)(_params.RxPosition.Z * _params.Depth), 1, _params.Depth - 2);

        var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ);
        if (c == null || !c.IsInMemory) return false;

        var lz = rz - c.StartZ;
        var comp = _params.Axis switch
        {
            0 => MathF.Abs(c.Vx[rx, ry, lz]),
            1 => MathF.Abs(c.Vy[rx, ry, lz]),
            _ => MathF.Abs(c.Vz[rx, ry, lz])
        };
        return comp > 1e-12f;
    }

    private bool CheckSWaveArrival()
    {
        var rx = Math.Clamp((int)(_params.RxPosition.X * _params.Width), 1, _params.Width - 2);
        var ry = Math.Clamp((int)(_params.RxPosition.Y * _params.Height), 1, _params.Height - 2);
        var rz = Math.Clamp((int)(_params.RxPosition.Z * _params.Depth), 1, _params.Depth - 2);

        var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ);
        if (c == null || !c.IsInMemory) return false;

        var lz = rz - c.StartZ;
        var mag = _params.Axis switch
        {
            0 => MathF.Sqrt(c.Vy[rx, ry, lz] * c.Vy[rx, ry, lz] + c.Vz[rx, ry, lz] * c.Vz[rx, ry, lz]),
            1 => MathF.Sqrt(c.Vx[rx, ry, lz] * c.Vx[rx, ry, lz] + c.Vz[rx, ry, lz] * c.Vz[rx, ry, lz]),
            _ => MathF.Sqrt(c.Vx[rx, ry, lz] * c.Vx[rx, ry, lz] + c.Vy[rx, ry, lz] * c.Vy[rx, ry, lz])
        };
        return mag > 1e-12f;
    }

    private float CalculateTxRxDistance()
    {
        var dx = (_params.RxPosition.X - _params.TxPosition.X) * _params.Width * _params.PixelSize;
        var dy = (_params.RxPosition.Y - _params.TxPosition.Y) * _params.Height * _params.PixelSize;
        var dz = (_params.RxPosition.Z - _params.TxPosition.Z) * _params.Depth * _params.PixelSize;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private WaveFieldSnapshot CreateSnapshot(int step)
    {
        var ds = Math.Max(1, Math.Max(_params.Width, Math.Max(_params.Height, _params.Depth)) / 128);
        var w = Math.Max(1, _params.Width / ds);
        var h = Math.Max(1, _params.Height / ds);
        var d = Math.Max(1, _params.Depth / ds);

        var vx = new float[w, h, d];
        var vy = new float[w, h, d];
        var vz = new float[w, h, d];

        foreach (var c in _chunks)
        {
            if (!c.IsInMemory) LoadChunkAsync(c).Wait();
            var cd = c.EndZ - c.StartZ;

            for (var lz = 0; lz < cd; lz += ds)
            {
                var gz = c.StartZ + lz;
                var dz = gz / ds;
                if (dz >= d) continue;

                for (var y = 0; y < _params.Height; y += ds)
                {
                    var dy = y / ds;
                    if (dy >= h) continue;

                    for (var x = 0; x < _params.Width; x += ds)
                    {
                        var dx = x / ds;
                        if (dx >= w) continue;

                        vx[dx, dy, dz] = c.Vx[x, y, lz];
                        vy[dx, dy, dz] = c.Vy[x, y, lz];
                        vz[dx, dy, dz] = c.Vz[x, y, lz];
                    }
                }
            }
        }

        var snap = new WaveFieldSnapshot
        {
            TimeStep = step,
            SimulationTime = step * _dt,
            Width = w,
            Height = h,
            Depth = d
        };
        snap.SetVelocityFields(vx, vy, vz);
        return snap;
    }

    public async Task<float[,,]> ReconstructFieldAsync(int axis)
    {
        var field = new float[_params.Width, _params.Height, _params.Depth];

        await Task.Run(() =>
        {
            foreach (var c in _chunks)
            {
                if (!c.IsInMemory)
                    LoadChunkAsync(c).Wait();

                var d = c.EndZ - c.StartZ;

                for (var z = 0; z < d; z++)
                {
                    var globalZ = c.StartZ + z;
                    for (var y = 0; y < _params.Height; y++)
                    for (var x = 0; x < _params.Width; x++)
                    {
                        var v = axis switch
                        {
                            0 => c.Vx[x, y, z],
                            1 => c.Vy[x, y, z],
                            _ => c.Vz[x, y, z]
                        };
                        field[x, y, globalZ] = v;
                    }
                }
            }
        });
        return field;
    }

    public async Task<float[,,]> ReconstructMaxFieldAsync(int axis)
    {
        var field = new float[_params.Width, _params.Height, _params.Depth];

        await Task.Run(() =>
        {
            foreach (var c in _chunks)
            {
                if (!c.IsInMemory)
                    LoadChunkAsync(c).Wait();

                var d = c.EndZ - c.StartZ;

                for (var z = 0; z < d; z++)
                {
                    var globalZ = c.StartZ + z;
                    for (var y = 0; y < _params.Height; y++)
                    for (var x = 0; x < _params.Width; x++)
                    {
                        var v = axis switch
                        {
                            0 => c.MaxAbsVx[x, y, z],
                            1 => c.MaxAbsVy[x, y, z],
                            _ => c.MaxAbsVz[x, y, z]
                        };
                        field[x, y, globalZ] = v;
                    }
                }
            }
        });
        return field;
    }

    public float[,,] GetDamageField()
    {
        var damageField = new float[_params.Width, _params.Height, _params.Depth];
        foreach (var chunk in _chunks)
        {
            if (!chunk.IsInMemory) continue;
            var d = chunk.EndZ - chunk.StartZ;

            for (var z = 0; z < d; z++)
            for (var y = 0; y < _params.Height; y++)
            for (var x = 0; x < _params.Width; x++)
                damageField[x, y, chunk.StartZ + z] = chunk.Damage[x, y, z];
        }

        return damageField;
    }

    private Task OffloadChunkAsync(WaveFieldChunk chunk)
    {
        if (!chunk.IsInMemory || string.IsNullOrEmpty(_params.OffloadDirectory))
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            if (string.IsNullOrEmpty(chunk.FilePath))
                chunk.FilePath = Path.Combine(_params.OffloadDirectory, $"chunk_{chunk.StartZ}.tmp");

            try
            {
                using (var writer = new BinaryWriter(new FileStream(chunk.FilePath, FileMode.Create, FileAccess.Write,
                           FileShare.None, 65536)))
                {
                    WriteField(writer, chunk.Vx);
                    WriteField(writer, chunk.Vy);
                    WriteField(writer, chunk.Vz);
                    WriteField(writer, chunk.Sxx);
                    WriteField(writer, chunk.Syy);
                    WriteField(writer, chunk.Szz);
                    WriteField(writer, chunk.Sxy);
                    WriteField(writer, chunk.Sxz);
                    WriteField(writer, chunk.Syz);
                    WriteField(writer, chunk.Damage);
                    WriteField(writer, chunk.MaxAbsVx);
                    WriteField(writer, chunk.MaxAbsVy);
                    WriteField(writer, chunk.MaxAbsVz);
                }

                chunk.Vx = null;
                chunk.Vy = null;
                chunk.Vz = null;
                chunk.Sxx = null;
                chunk.Syy = null;
                chunk.Szz = null;
                chunk.Sxy = null;
                chunk.Sxz = null;
                chunk.Syz = null;
                chunk.Damage = null;
                chunk.MaxAbsVx = null;
                chunk.MaxAbsVy = null;
                chunk.MaxAbsVz = null;
                GC.Collect(); // Force GC to reclaim the large arrays immediately
                chunk.IsInMemory = false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ChunkedSimulator] Failed to offload chunk {chunk.StartZ}: {ex.Message}");
                chunk.FilePath = null;
            }
        });
    }

    private Task LoadChunkAsync(WaveFieldChunk chunk)
    {
        if (chunk.IsInMemory)
            return Task.CompletedTask;

        if (string.IsNullOrEmpty(chunk.FilePath) || !File.Exists(chunk.FilePath))
        {
            if (!string.IsNullOrEmpty(chunk.FilePath))
                Logger.LogError($"[ChunkedSimulator] Load failed: Offload file not found at {chunk.FilePath}");
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                var d = chunk.EndZ - chunk.StartZ;
                chunk.Vx = new float[_params.Width, _params.Height, d];
                chunk.Vy = new float[_params.Width, _params.Height, d];
                chunk.Vz = new float[_params.Width, _params.Height, d];
                chunk.Sxx = new float[_params.Width, _params.Height, d];
                chunk.Syy = new float[_params.Width, _params.Height, d];
                chunk.Szz = new float[_params.Width, _params.Height, d];
                chunk.Sxy = new float[_params.Width, _params.Height, d];
                chunk.Sxz = new float[_params.Width, _params.Height, d];
                chunk.Syz = new float[_params.Width, _params.Height, d];
                chunk.Damage = new float[_params.Width, _params.Height, d];
                chunk.MaxAbsVx = new float[_params.Width, _params.Height, d];
                chunk.MaxAbsVy = new float[_params.Width, _params.Height, d];
                chunk.MaxAbsVz = new float[_params.Width, _params.Height, d];

                using (var reader = new BinaryReader(File.OpenRead(chunk.FilePath)))
                {
                    ReadField(reader, chunk.Vx);
                    ReadField(reader, chunk.Vy);
                    ReadField(reader, chunk.Vz);
                    ReadField(reader, chunk.Sxx);
                    ReadField(reader, chunk.Syy);
                    ReadField(reader, chunk.Szz);
                    ReadField(reader, chunk.Sxy);
                    ReadField(reader, chunk.Sxz);
                    ReadField(reader, chunk.Syz);
                    ReadField(reader, chunk.Damage);
                    ReadField(reader, chunk.MaxAbsVx);
                    ReadField(reader, chunk.MaxAbsVy);
                    ReadField(reader, chunk.MaxAbsVz);
                }

                chunk.IsInMemory = true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ChunkedSimulator] Failed to load chunk {chunk.StartZ}: {ex.Message}");
            }
        });
    }

    private void WriteField(BinaryWriter writer, float[,,] field)
    {
        var buffer = new byte[field.Length * sizeof(float)];
        Buffer.BlockCopy(field, 0, buffer, 0, buffer.Length);
        writer.Write(buffer);
    }

    private void ReadField(BinaryReader reader, float[,,] field)
    {
        var buffer = reader.ReadBytes(field.Length * sizeof(float));
        Buffer.BlockCopy(buffer, 0, field, 0, buffer.Length);
    }

    private void CleanupOpenCL()
    {
        ReleaseChunkBuffers();
        if (_kernelVelocity != 0)
        {
            _cl.ReleaseKernel(_kernelVelocity);
            _kernelVelocity = 0;
        }

        if (_kernelStress != 0)
        {
            _cl.ReleaseKernel(_kernelStress);
            _kernelStress = 0;
        }

        if (_program != 0)
        {
            _cl.ReleaseProgram(_program);
            _program = 0;
        }

        if (_queue != 0)
        {
            _cl.ReleaseCommandQueue(_queue);
            _queue = 0;
        }

        if (_context != 0)
        {
            _cl.ReleaseContext(_context);
            _context = 0;
        }
    }

    private void ReleaseChunkBuffers()
    {
        if (_bufMat != 0)
        {
            _cl.ReleaseMemObject(_bufMat);
            _bufMat = 0;
        }

        if (_bufDen != 0)
        {
            _cl.ReleaseMemObject(_bufDen);
            _bufDen = 0;
        }

        if (_bufMatLookup != 0)
        {
            _cl.ReleaseMemObject(_bufMatLookup);
            _bufMatLookup = 0;
        }

        if (_bufDmg != 0)
        {
            _cl.ReleaseMemObject(_bufDmg);
            _bufDmg = 0;
        }

        if (_bufYoungsModulus != 0)
        {
            _cl.ReleaseMemObject(_bufYoungsModulus);
            _bufYoungsModulus = 0;
        }

        if (_bufPoissonRatio != 0)
        {
            _cl.ReleaseMemObject(_bufPoissonRatio);
            _bufPoissonRatio = 0;
        }

        for (var i = 0; i < 3; i++)
            if (_bufVel[i] != 0)
            {
                _cl.ReleaseMemObject(_bufVel[i]);
                _bufVel[i] = 0;
            }

        for (var i = 0; i < 3; i++)
            if (_bufMaxVel[i] != 0)
            {
                _cl.ReleaseMemObject(_bufMaxVel[i]);
                _bufMaxVel[i] = 0;
            }

        for (var i = 0; i < 6; i++)
            if (_bufStr[i] != 0)
            {
                _cl.ReleaseMemObject(_bufStr[i]);
                _bufStr[i] = 0;
            }

        _lastDeviceBytes = 0;
    }

    private string GetKernelSource()
    {
        return @"
    #define M_PI_F 3.14159265358979323846f

    // --- STABILIZED STENCIL HELPERS ---
    float d_dy_for_vx(__global const float* F, int idx, int width, float inv_d) {
        return 0.25f * ((F[idx] + F[idx + 1]) - (F[idx - width] + F[idx + 1 - width])) * inv_d;
    }
    float d_dz_for_vx(__global const float* F, int idx, int wh, float inv_d) {
        return 0.25f * ((F[idx] + F[idx + 1]) - (F[idx - wh] + F[idx + 1 - wh])) * inv_d;
    }
    float d_dx_for_vy(__global const float* F, int idx, int width, float inv_d) {
        return 0.25f * ((F[idx] + F[idx + width]) - (F[idx - 1] + F[idx - 1 + width])) * inv_d;
    }
    float d_dz_for_vy(__global const float* F, int idx, int width, int wh, float inv_d) {
         return 0.25f * ((F[idx] + F[idx + width]) - (F[idx - wh] + F[idx + width - wh])) * inv_d;
    }
    float d_dx_for_vz(__global const float* F, int idx, int wh, float inv_d) {
        return 0.25f * ((F[idx] + F[idx + wh]) - (F[idx - 1] + F[idx - 1 + wh])) * inv_d;
    }
    float d_dy_for_vz(__global const float* F, int idx, int width, int wh, float inv_d) {
        return 0.25f * ((F[idx] + F[idx + wh]) - (F[idx - width] + F[idx - width + wh])) * inv_d;
    }

    __kernel void updateStress(
        __global const uchar* material, __global const float* density, __global const uchar* material_lookup,
        __global const float* vx, __global const float* vy, __global const float* vz,
        __global float* sxx, __global float* syy, __global float* szz,
        __global float* sxy, __global float* sxz, __global float* syz,
        __global float* damage, __global const float* youngsModulus, __global const float* poissonRatio,
        const float dt, const float dx, const int width, const int height, const int depth,
        const int chunkStartZ,
        const float damageRatePerSec, const float confiningPressureMPa, const float cohesionMPa, const float failureAngleDeg,
        const int usePlasticModel, const int useBrittleModel, const float sourceValue,
        const int srcX, const int srcY, const int srcZ_global,
        const int isFullFace, const int sourceAxis,
        const int totalSimDepth
    )
    {
        int idx = get_global_id(0); 
        int wh = width * height;
        if (idx >= width * height * depth) return;
        
        int z_local = idx / wh;
        int rem = idx % wh;
        int y = rem / width;
        int x = rem % width;
        
        int z_global = z_local + chunkStartZ;
        
        uchar mat = material[idx];

        if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z_local <= 0 || z_local >= depth-1) return;
        
        if (sourceValue != 0.0f && material_lookup[mat] != 0) {
            if (isFullFace != 0) {
                int src_x_face = (srcX < width / 2) ? 2 : width - 3;
                int src_y_face = (srcY < height / 2) ? 2 : height - 3;
                int src_z_face_global = (srcZ_global < totalSimDepth / 2) ? 2 : totalSimDepth - 3;

                if (sourceAxis == 0 && x == src_x_face) sxx[idx] += sourceValue;
                if (sourceAxis == 1 && y == src_y_face) syy[idx] += sourceValue;
                if (sourceAxis == 2 && z_global == src_z_face_global) szz[idx] += sourceValue;
            } else {
                float dx_dist = (float)(x - srcX);
                float dy_dist = (float)(y - srcY);
                float dz_dist = (float)(z_global - srcZ_global);

                if (fabs(dx_dist) <= 1.5f && fabs(dy_dist) <= 1.5f && fabs(dz_dist) <= 1.5f) {
                    float weight = exp(-0.5f * (dx_dist*dx_dist + dy_dist*dy_dist + dz_dist*dz_dist));
                    float weightedSource = sourceValue * weight;
                    sxx[idx] += weightedSource;
                    syy[idx] += weightedSource;
                    szz[idx] += weightedSource;
                }
            }
        }
        
        if (material_lookup[mat] == 0 || density[idx] <= 0.0f) return;
        
        float E = youngsModulus[idx] * 1e6f; float nu = poissonRatio[idx];
        if (E <= 0.0f || nu <= -1.0f || nu >= 0.5f) return;
        
        float mu = E / (2.0f * (1.0f + nu)); float lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));

        int xm1=idx-1; int ym1=idx-width; int zm1=idx-wh;
        
        float dvx_dx = (vx[idx] - vx[xm1]) / dx;
        float dvy_dy = (vy[idx] - vy[ym1]) / dx;
        float dvz_dz = (vz[idx] - vz[zm1]) / dx;
        float dvx_dy = (vx[idx] - vx[ym1]) / dx;
        float dvy_dx = (vy[idx] - vy[xm1]) / dx;
        float dvx_dz = (vx[idx] - vx[zm1]) / dx;
        float dvz_dx = (vz[idx] - vz[xm1]) / dx;
        float dvy_dz = (vy[idx] - vy[zm1]) / dx;
        float dvz_dy = (vz[idx] - vz[ym1]) / dx;
        
        float volumetric_strain = dvx_dx + dvy_dy + dvz_dz;
        float damage_factor = (1.0f - damage[idx] * 0.9f);
        
        float sxx_new = sxx[idx] + dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvx_dx);
        float syy_new = syy[idx] + dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvy_dy);
        float szz_new = szz[idx] + dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvz_dz);
        float sxy_new = sxy[idx] + dt * damage_factor * mu * (dvx_dy + dvy_dx);
        float sxz_new = sxz[idx] + dt * damage_factor * mu * (dvx_dz + dvz_dx);
        float syz_new = syz[idx] + dt * damage_factor * mu * (dvy_dz + dvz_dy);

        if (usePlasticModel != 0 || useBrittleModel != 0)
        {
            float cohesion = cohesionMPa * 1e6f;
            float frictionAngle = failureAngleDeg * (M_PI_F / 180.0f);
            float sin_phi = sin(frictionAngle);
            float cos_phi = cos(frictionAngle);

            float s_mean = (sxx_new + syy_new + szz_new) / 3.0f - (confiningPressureMPa * 1e6f);
            float dev_sxx = sxx_new - s_mean;
            float dev_syy = syy_new - s_mean;
            float dev_szz = szz_new - s_mean;
            
            float j2 = 0.5f * (dev_sxx * dev_sxx + dev_syy * dev_syy + dev_szz * dev_szz) + sxy_new * sxy_new + sxz_new * sxz_new + syz_new * syz_new;
            float sqrt_j2 = sqrt(j2);
            
            float yield_val = sqrt_j2 + sin_phi / sqrt(3.0f) * s_mean - cohesion * cos_phi / sqrt(3.0f);

            if (yield_val > 0)
            {
                if (useBrittleModel != 0)
                {
                    damage[idx] += dt * damageRatePerSec * (yield_val / (cohesion + 1e-6f));
                    damage[idx] = clamp(damage[idx], 0.0f, 1.0f);
                }
                if (usePlasticModel != 0)
                {
                    float return_factor = (cohesion * cos_phi / sqrt(3.0f) - sin_phi / sqrt(3.0f) * s_mean) / (sqrt_j2 + 1e-9f);
                    if (return_factor < 1.0f)
                    {
                        sxx_new = (dev_sxx * return_factor) + s_mean;
                        syy_new = (dev_syy * return_factor) + s_mean;
                        szz_new = (dev_szz * return_factor) + s_mean;
                        sxy_new *= return_factor;
                        sxz_new *= return_factor;
                        syz_new *= return_factor;
                    }
                }
            }
        }

        sxx[idx] = sxx_new;
        syy[idx] = syy_new;
        szz[idx] = szz_new;
        sxy[idx] = sxy_new;
        sxz[idx] = sxz_new;
        syz[idx] = syz_new;
    }

    __kernel void updateVelocity(
        __global const uchar* material, __global const float* density, __global const uchar* material_lookup,
        __global float* vx, __global float* vy, __global float* vz,
        __global const float* sxx, __global const float* syy, __global const float* szz,
        __global const float* sxy, __global const float* sxz, __global const float* syz,
        __global float* max_vx, __global float* max_vy, __global float* max_vz,
        const float dt, const float dx, const int width, const int height, const int depth,
        const float artificialDampingFactor
    )
    {
        int idx = get_global_id(0);
        int wh = width * height;
        if (idx >= wh * depth) return;
        
        uchar mat = material[idx];
        if (material_lookup[mat] == 0 || density[idx] <= 0.0f) return;

        int z = idx / wh; int rem = idx % wh; int y = rem / width; int x = rem % width;
        if (x <= 1 || x >= width-2 || y <= 1 || y >= height-2 || z <= 1 || z >= depth-2) return;
        
        float rho_inv = 1.0f / fmax(100.0f, density[idx]);
        float inv_dx = 1.0f / dx;
        
        float dsxx_dx_vx = (sxx[idx] - sxx[idx - 1]) * inv_dx;
        float dsxy_dy_vx = d_dy_for_vx(sxy, idx, width, inv_dx);
        float dsxz_dz_vx = d_dz_for_vx(sxz, idx, wh, inv_dx);
        
        float dsyx_dx_vy = d_dx_for_vy(sxy, idx, width, inv_dx);
        float dsyy_dy_vy = (syy[idx] - syy[idx - width]) * inv_dx;
        float dsyz_dz_vy = d_dz_for_vy(syz, idx, width, wh, inv_dx);

        float dszx_dx_vz = d_dx_for_vz(sxz, idx, wh, inv_dx);
        float dszy_dy_vz = d_dy_for_vz(syz, idx, width, wh, inv_dx);
        float dszz_dz_vz = (szz[idx] - szz[idx - wh]) * inv_dx;
        
        const float damping = 0.999f;
        
        int xp1 = idx + 1; int xm1 = idx - 1;
        int yp1 = idx + width; int ym1 = idx - width;
        int zp1 = idx + wh; int zm1 = idx - wh;
        
        float laplacian_vx = vx[xp1] + vx[xm1] + vx[yp1] + vx[ym1] + vx[zp1] + vx[zm1] - 6.0f * vx[idx];
        float laplacian_vy = vy[xp1] + vy[xm1] + vy[yp1] + vy[ym1] + vy[zp1] + vy[zm1] - 6.0f * vy[idx];
        float laplacian_vz = vz[xp1] + vz[xm1] + vz[yp1] + vz[ym1] + vz[zp1] + vz[zm1] - 6.0f * vz[idx];
        
        float dvx_update = dt * (dsxx_dx_vx + dsxy_dy_vx + dsxz_dz_vx) * rho_inv;
        float dvy_update = dt * (dsyx_dx_vy + dsyy_dy_vy + dsyz_dz_vy) * rho_inv;
        float dvz_update = dt * (dszx_dx_vz + dszy_dy_vz + dszz_dz_vz) * rho_inv;
        
        vx[idx] = vx[idx] * damping + dvx_update + (artificialDampingFactor / 6.0f) * laplacian_vx;
        vy[idx] = vy[idx] * damping + dvy_update + (artificialDampingFactor / 6.0f) * laplacian_vy;
        vz[idx] = vz[idx] * damping + dvz_update + (artificialDampingFactor / 6.0f) * laplacian_vz;

        max_vx[idx] = fmax(max_vx[idx], fabs(vx[idx]));
        max_vy[idx] = fmax(max_vy[idx], fabs(vy[idx]));
        max_vz[idx] = fmax(max_vz[idx], fabs(vz[idx]));
    }";
    }

    private sealed class WaveFieldChunk
    {
        public float[,,] Damage;
        public string FilePath;
        public bool IsInMemory = true;
        public float[,,] MaxAbsVx, MaxAbsVy, MaxAbsVz;
        public int StartZ, EndZ;
        public float[,,] Sxx, Syy, Szz, Sxy, Sxz, Syz;
        public float[,,] Vx, Vy, Vz;
    }

    #region OpenCL and GPU Processing

    private unsafe void InitOpenCL()
    {
        uint nplat = 0;
        _cl.GetPlatformIDs(0, null, &nplat);
        if (nplat == 0) throw new InvalidOperationException("OpenCL: no platforms.");
        var plats = new nint[nplat];
        fixed (nint* pPlats = plats)
        {
            _cl.GetPlatformIDs(nplat, pPlats, null);
        }

        _platform = plats[0];

        uint ndev = 0;
        _cl.GetDeviceIDs(_platform, DeviceType.Gpu, 0, null, &ndev);
        var chosen = DeviceType.Gpu;
        if (ndev == 0)
        {
            _cl.GetDeviceIDs(_platform, DeviceType.Cpu, 0, null, &ndev);
            if (ndev == 0) throw new InvalidOperationException("OpenCL: no devices.");
            chosen = DeviceType.Cpu;
        }

        var devs = new nint[ndev];
        fixed (nint* pDevs = devs)
        {
            _cl.GetDeviceIDs(_platform, chosen, ndev, pDevs, null);
        }

        _device = devs[0];

        int err;
        nint[] one = { _device };
        fixed (nint* p = one)
        {
            _context = _cl.CreateContext(null, 1u, p, null, null, out err);
        }

        if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateContext failed: {err}");

        _queue = _cl.CreateCommandQueue(_context, _device, CommandQueueProperties.None, out err);
        if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateCommandQueue failed: {err}");
    }

    private unsafe void BuildProgramAndKernels()
    {
        string[] sources = { GetKernelSource() };
        _program = _cl.CreateProgramWithSource(_context, 1u, sources, null, out var err);
        if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateProgramWithSource failed: {err}");

        nint[] devs = { _device };
        fixed (nint* p = devs)
        {
            var buildErr = _cl.BuildProgram(_program, 1u, p, (string)null, null, null);
            if (buildErr != (int)CLEnum.Success)
            {
                nuint logSize;
                _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
                var log = new byte[logSize];
                fixed (byte* pLog = log)
                {
                    _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, logSize, pLog, null);
                    throw new InvalidOperationException(
                        $"BuildProgram failed: {buildErr}\n{Encoding.UTF8.GetString(log)}");
                }
            }
        }

        _kernelStress = _cl.CreateKernel(_program, "updateStress", out var err1);
        if (err1 != (int)CLEnum.Success)
            throw new InvalidOperationException($"CreateKernel(updateStress) failed: {err1}");
        _kernelVelocity = _cl.CreateKernel(_program, "updateVelocity", out var err2);
        if (err2 != (int)CLEnum.Success)
            throw new InvalidOperationException($"CreateKernel(updateVelocity) failed: {err2}");
    }

    private async Task ProcessChunkGPU_StressOnly(WaveFieldChunk c, byte[,,] labels, float[,,] density,
        float sourceValue, CancellationToken token)
    {
        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            int w = _params.Width, h = _params.Height, d = c.EndZ - c.StartZ;
            var size = w * h * d;

            var mat = new byte[size];
            var den = new float[size];
            var vx = new float[size];
            var vy = new float[size];
            var vz = new float[size];
            var sxx = new float[size];
            var syy = new float[size];
            var szz = new float[size];
            var sxy = new float[size];
            var sxz = new float[size];
            var syz = new float[size];
            var dmg = new float[size];
            var ym = new float[size];
            var pr = new float[size];

            var matLookup = _params.CreateMaterialLookupTable();

            var k = 0;
            for (var lz = 0; lz < d; lz++)
            {
                var gz = c.StartZ + lz;
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++, k++)
                {
                    mat[k] = labels[x, y, gz];
                    den[k] = MathF.Max(100f, density[x, y, gz]);
                    vx[k] = c.Vx[x, y, lz];
                    vy[k] = c.Vy[x, y, lz];
                    vz[k] = c.Vz[x, y, lz];
                    sxx[k] = c.Sxx[x, y, lz];
                    syy[k] = c.Syy[x, y, lz];
                    szz[k] = c.Szz[x, y, lz];
                    sxy[k] = c.Sxy[x, y, lz];
                    sxz[k] = c.Sxz[x, y, lz];
                    syz[k] = c.Syz[x, y, lz];
                    dmg[k] = c.Damage[x, y, lz];
                    if (_usePerVoxelProperties)
                    {
                        ym[k] = _perVoxelYoungsModulus[x, y, gz];
                        pr[k] = _perVoxelPoissonRatio[x, y, gz];
                    }
                    else
                    {
                        ym[k] = _params.YoungsModulusMPa;
                        pr[k] = _params.PoissonRatio;
                    }
                }
            }

            unsafe
            {
                fixed (byte* pMat = mat)
                fixed (float* pDen = den)
                fixed (float* pVx = vx)
                fixed (float* pVy = vy)
                fixed (float* pVz = vz)
                fixed (float* pSxx = sxx)
                fixed (float* pSyy = syy)
                fixed (float* pSzz = szz)
                fixed (float* pSxy = sxy)
                fixed (float* pSxz = sxz)
                fixed (float* pSyz = syz)
                fixed (float* pDmg = dmg)
                fixed (float* pYm = ym)
                fixed (float* pPr = pr)
                fixed (byte* pMatLookup = matLookup)
                {
                    int err;
                    _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(byte)), pMat, out err);
                    _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pDen, out err);
                    _bufMatLookup = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, 256,
                        pMatLookup, out err);
                    _bufVel[0] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pVx, out err);
                    _bufVel[1] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pVy, out err);
                    _bufVel[2] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pVz, out err);
                    _bufStr[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSxx, out err);
                    _bufStr[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSyy, out err);
                    _bufStr[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSzz, out err);
                    _bufStr[3] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSxy, out err);
                    _bufStr[4] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSxz, out err);
                    _bufStr[5] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSyz, out err);
                    _bufDmg = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pDmg, out err);
                    _bufYoungsModulus = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pYm, out err);
                    _bufPoissonRatio = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pPr, out err);

                    _lastDeviceBytes = (long)size * (sizeof(byte) + sizeof(float) * 15 + 1) + 256;

                    SetKernelArgs(_kernelStress, w, h, d, c.StartZ, sourceValue);
                    var gws = (nuint)size;
                    _cl.EnqueueNdrangeKernel(_queue, _kernelStress, 1, null, &gws, null, 0, null, null);

                    _cl.Finish(_queue);

                    _cl.EnqueueReadBuffer(_queue, _bufStr[0], true, 0, (nuint)(size * sizeof(float)), pSxx, 0, null,
                        null);
                    _cl.EnqueueReadBuffer(_queue, _bufStr[1], true, 0, (nuint)(size * sizeof(float)), pSyy, 0, null,
                        null);
                    _cl.EnqueueReadBuffer(_queue, _bufStr[2], true, 0, (nuint)(size * sizeof(float)), pSzz, 0, null,
                        null);
                    _cl.EnqueueReadBuffer(_queue, _bufStr[3], true, 0, (nuint)(size * sizeof(float)), pSxy, 0, null,
                        null);
                    _cl.EnqueueReadBuffer(_queue, _bufStr[4], true, 0, (nuint)(size * sizeof(float)), pSxz, 0, null,
                        null);
                    _cl.EnqueueReadBuffer(_queue, _bufStr[5], true, 0, (nuint)(size * sizeof(float)), pSyz, 0, null,
                        null);
                    _cl.EnqueueReadBuffer(_queue, _bufDmg, true, 0, (nuint)(size * sizeof(float)), pDmg, 0, null, null);
                }
            }

            k = 0;
            for (var lz = 0; lz < d; lz++)
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++, k++)
            {
                c.Sxx[x, y, lz] = sxx[k];
                c.Syy[x, y, lz] = syy[k];
                c.Szz[x, y, lz] = szz[k];
                c.Sxy[x, y, lz] = sxy[k];
                c.Sxz[x, y, lz] = sxz[k];
                c.Syz[x, y, lz] = syz[k];
                c.Damage[x, y, lz] = dmg[k];
            }

            ReleaseChunkBuffers();
        });
    }

    private async Task ProcessChunkGPU_VelocityOnly(WaveFieldChunk c, byte[,,] labels, float[,,] density,
        CancellationToken token)
    {
        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            int w = _params.Width, h = _params.Height, d = c.EndZ - c.StartZ;
            var size = w * h * d;

            var mat = new byte[size];
            var den = new float[size];
            var vx = new float[size];
            var vy = new float[size];
            var vz = new float[size];
            var sxx = new float[size];
            var syy = new float[size];
            var szz = new float[size];
            var sxy = new float[size];
            var sxz = new float[size];
            var syz = new float[size];
            var max_vx = new float[size];
            var max_vy = new float[size];
            var max_vz = new float[size];

            var matLookup = _params.CreateMaterialLookupTable();

            var k = 0;
            for (var lz = 0; lz < d; lz++)
            {
                var gz = c.StartZ + lz;
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++, k++)
                {
                    mat[k] = labels[x, y, gz];
                    den[k] = MathF.Max(100f, density[x, y, gz]);
                    vx[k] = c.Vx[x, y, lz];
                    vy[k] = c.Vy[x, y, lz];
                    vz[k] = c.Vz[x, y, lz];
                    sxx[k] = c.Sxx[x, y, lz];
                    syy[k] = c.Syy[x, y, lz];
                    szz[k] = c.Szz[x, y, lz];
                    sxy[k] = c.Sxy[x, y, lz];
                    sxz[k] = c.Sxz[x, y, lz];
                    syz[k] = c.Syz[x, y, lz];
                    max_vx[k] = c.MaxAbsVx[x, y, lz];
                    max_vy[k] = c.MaxAbsVy[x, y, lz];
                    max_vz[k] = c.MaxAbsVz[x, y, lz];
                }
            }

            unsafe
            {
                fixed (byte* pMat = mat)
                fixed (float* pDen = den)
                fixed (float* pVx = vx)
                fixed (float* pVy = vy)
                fixed (float* pVz = vz)
                fixed (float* pSxx = sxx)
                fixed (float* pSyy = syy)
                fixed (float* pSzz = szz)
                fixed (float* pSxy = sxy)
                fixed (float* pSxz = sxz)
                fixed (float* pSyz = syz)
                fixed (float* pMaxVx = max_vx)
                fixed (float* pMaxVy = max_vy)
                fixed (float* pMaxVz = max_vz)
                fixed (byte* pMatLookup = matLookup)
                {
                    int err;
                    _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(byte)), pMat, out err);
                    _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pDen, out err);
                    _bufMatLookup = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, 256,
                        pMatLookup, out err);
                    _bufVel[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pVx, out err);
                    _bufVel[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pVy, out err);
                    _bufVel[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pVz, out err);
                    _bufStr[0] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSxx, out err);
                    _bufStr[1] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSyy, out err);
                    _bufStr[2] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSzz, out err);
                    _bufStr[3] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSxy, out err);
                    _bufStr[4] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSxz, out err);
                    _bufStr[5] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pSyz, out err);
                    _bufMaxVel[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pMaxVx, out err);
                    _bufMaxVel[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pMaxVy, out err);
                    _bufMaxVel[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                        (nuint)(size * sizeof(float)), pMaxVz, out err);

                    _lastDeviceBytes = (long)size * (sizeof(byte) + sizeof(float) * 15) + 256;

                    SetKernelArgs(_kernelVelocity, w, h, d, c.StartZ, 0);
                    var gws = (nuint)size;
                    _cl.EnqueueNdrangeKernel(_queue, _kernelVelocity, 1, null, &gws, null, 0, null, null);

                    _cl.Finish(_queue);

                    _cl.EnqueueReadBuffer(_queue, _bufVel[0], true, 0, (nuint)(size * sizeof(float)), pVx, 0, null,
                        null);
                    _cl.EnqueueReadBuffer(_queue, _bufVel[1], true, 0, (nuint)(size * sizeof(float)), pVy, 0, null,
                        null);
                    _cl.EnqueueReadBuffer(_queue, _bufVel[2], true, 0, (nuint)(size * sizeof(float)), pVz, 0, null,
                        null);
                    _cl.EnqueueReadBuffer(_queue, _bufMaxVel[0], true, 0, (nuint)(size * sizeof(float)), pMaxVx, 0,
                        null, null);
                    _cl.EnqueueReadBuffer(_queue, _bufMaxVel[1], true, 0, (nuint)(size * sizeof(float)), pMaxVy, 0,
                        null, null);
                    _cl.EnqueueReadBuffer(_queue, _bufMaxVel[2], true, 0, (nuint)(size * sizeof(float)), pMaxVz, 0,
                        null, null);
                }
            }

            k = 0;
            for (var lz = 0; lz < d; lz++)
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++, k++)
            {
                c.Vx[x, y, lz] = vx[k];
                c.Vy[x, y, lz] = vy[k];
                c.Vz[x, y, lz] = vz[k];
                c.MaxAbsVx[x, y, lz] = max_vx[k];
                c.MaxAbsVy[x, y, lz] = max_vy[k];
                c.MaxAbsVz[x, y, lz] = max_vz[k];
            }

            ReleaseChunkBuffers();
        });
    }

    private unsafe void SetKernelArgs(nint kernel, int w, int h, int d, int chunkStartZ, float sourceValue)
    {
        uint a = 0;
        var bufMatArg = _bufMat;
        var bufDenArg = _bufDen;
        var bufMatLookupArg = _bufMatLookup;
        var bufVel0Arg = _bufVel[0];
        var bufVel1Arg = _bufVel[1];
        var bufVel2Arg = _bufVel[2];
        var bufStr0Arg = _bufStr[0];
        var bufStr1Arg = _bufStr[1];
        var bufStr2Arg = _bufStr[2];
        var bufStr3Arg = _bufStr[3];
        var bufStr4Arg = _bufStr[4];
        var bufStr5Arg = _bufStr[5];
        var bufDmgArg = _bufDmg;
        var bufYmArg = _bufYoungsModulus;
        var bufPrArg = _bufPoissonRatio;
        var bufMaxVel0Arg = _bufMaxVel[0];
        var bufMaxVel1Arg = _bufMaxVel[1];
        var bufMaxVel2Arg = _bufMaxVel[2];

        var paramsPixelSize = _params.PixelSize;

        if (kernel == _kernelStress)
        {
            var paramsConfiningPressureMPa = _params.ConfiningPressureMPa;
            var paramsCohesionMPa = _params.CohesionMPa;
            var paramsFailureAngleDeg = _params.FailureAngleDeg;

            var tx = (int)(_params.TxPosition.X * _params.Width);
            var ty = (int)(_params.TxPosition.Y * _params.Height);
            var tz = (int)(_params.TxPosition.Z * _params.Depth); // Use GLOBAL tz
            var isFullFace = _params.UseFullFaceTransducers ? 1 : 0;
            var sourceAxis = _params.Axis;
            var usePlastic = _params.UsePlasticModel ? 1 : 0;
            var useBrittle = _params.UseBrittleModel ? 1 : 0;

            // --- FIX: Pass total simulation depth to the kernel ---
            var totalSimDepth = _params.Depth;

            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMatArg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufDenArg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMatLookupArg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufVel0Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufVel1Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufVel2Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr0Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr1Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr2Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr3Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr4Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr5Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufDmgArg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufYmArg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufPrArg);
            _cl.SetKernelArg(kernel, a++, sizeof(float), in _dt);
            _cl.SetKernelArg(kernel, a++, sizeof(float), in paramsPixelSize);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in w);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in h);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in d);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in chunkStartZ); // Pass chunk's Z offset
            _cl.SetKernelArg(kernel, a++, sizeof(float), in _damageRatePerSec);
            _cl.SetKernelArg(kernel, a++, sizeof(float), in paramsConfiningPressureMPa);
            _cl.SetKernelArg(kernel, a++, sizeof(float), in paramsCohesionMPa);
            _cl.SetKernelArg(kernel, a++, sizeof(float), in paramsFailureAngleDeg);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in usePlastic);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in useBrittle);
            _cl.SetKernelArg(kernel, a++, sizeof(float), in sourceValue);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in tx);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in ty);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in tz); // Pass GLOBAL tz
            _cl.SetKernelArg(kernel, a++, sizeof(int), in isFullFace);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in sourceAxis);

            // --- FIX: Add the new argument to the call ---
            _cl.SetKernelArg(kernel, a++, sizeof(int), in totalSimDepth);
        }
        else // _kernelVelocity
        {
            var artificialDampingFactor = _params.ArtificialDampingFactor;

            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMatArg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufDenArg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMatLookupArg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufVel0Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufVel1Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufVel2Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr0Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr1Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr2Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr3Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr4Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr5Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMaxVel0Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMaxVel1Arg);
            _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMaxVel2Arg);
            _cl.SetKernelArg(kernel, a++, sizeof(float), in _dt);
            _cl.SetKernelArg(kernel, a++, sizeof(float), in paramsPixelSize);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in w);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in h);
            _cl.SetKernelArg(kernel, a++, sizeof(int), in d);
            _cl.SetKernelArg(kernel, a++, sizeof(float), in artificialDampingFactor);
        }
    }

    #endregion

    #region CPU Processing

    private void UpdateChunkStressCPU(WaveFieldChunk c, byte[,,] labels, float[,,] density)
    {
        var d = c.EndZ - c.StartZ;
        var inv_dx = 1.0f / _params.PixelSize;

        Parallel.For(1, d - 1, lz =>
        {
            var gz = c.StartZ + lz;
            for (var y = 1; y < _params.Height - 1; y++)
            for (var x = 1; x < _params.Width - 1; x++)
            {
                var materialId = labels[x, y, gz];

                if (!_params.IsMaterialSelected(materialId) || density[x, y, gz] <= 0f)
                    continue;

                float localE, localNu;
                if (_usePerVoxelProperties && _perVoxelYoungsModulus != null && _perVoxelPoissonRatio != null)
                {
                    localE = _perVoxelYoungsModulus[x, y, gz] * 1e6f;
                    localNu = _perVoxelPoissonRatio[x, y, gz];
                    if (localE <= 0f || localNu <= -1.0f || localNu >= 0.5f) continue;
                }
                else
                {
                    localE = _params.YoungsModulusMPa * 1e6f;
                    localNu = _params.PoissonRatio;
                }

                var localMu = localE / (2f * (1f + localNu));
                var localLambda = localE * localNu / ((1f + localNu) * (1f - 2f * localNu));

                var dvx_dx = (c.Vx[x, y, lz] - c.Vx[x - 1, y, lz]) * inv_dx;
                var dvy_dy = (c.Vy[x, y, lz] - c.Vy[x, y - 1, lz]) * inv_dx;
                var dvz_dz = (c.Vz[x, y, lz] - c.Vz[x, y, lz - 1]) * inv_dx;

                var dvx_dy = (c.Vx[x, y, lz] - c.Vx[x, y - 1, lz]) * inv_dx;
                var dvy_dx = (c.Vy[x, y, lz] - c.Vy[x - 1, y, lz]) * inv_dx;
                var dvx_dz = (c.Vx[x, y, lz] - c.Vx[x, y, lz - 1]) * inv_dx;
                var dvz_dx = (c.Vz[x, y, lz] - c.Vz[x - 1, y, lz]) * inv_dx;
                var dvy_dz = (c.Vy[x, y, lz] - c.Vy[x, y, lz - 1]) * inv_dx;
                var dvz_dy = (c.Vz[x, y, lz] - c.Vz[x, y - 1, lz]) * inv_dx;

                var volumetric = dvx_dx + dvy_dy + dvz_dz;
                var damp = 1f - c.Damage[x, y, lz] * 0.9f; // Damage reduces stiffness

                var sxx_new = c.Sxx[x, y, lz] + _dt * damp * (localLambda * volumetric + 2f * localMu * dvx_dx);
                var syy_new = c.Syy[x, y, lz] + _dt * damp * (localLambda * volumetric + 2f * localMu * dvy_dy);
                var szz_new = c.Szz[x, y, lz] + _dt * damp * (localLambda * volumetric + 2f * localMu * dvz_dz);
                var sxy_new = c.Sxy[x, y, lz] + _dt * damp * localMu * (dvx_dy + dvy_dx);
                var sxz_new = c.Sxz[x, y, lz] + _dt * damp * localMu * (dvx_dz + dvz_dx);
                var syz_new = c.Syz[x, y, lz] + _dt * damp * localMu * (dvy_dz + dvz_dy);

                if (_params.UsePlasticModel || _params.UseBrittleModel)
                {
                    // Apply Mohr-Coulomb Failure Criterion
                    var cohesion = _params.CohesionMPa * 1e6f; // Pa
                    var frictionAngle = _params.FailureAngleDeg * (MathF.PI / 180.0f); // rad
                    var sin_phi = MathF.Sin(frictionAngle);
                    var cos_phi = MathF.Cos(frictionAngle);

                    var s_mean = (sxx_new + syy_new + szz_new) / 3.0f - _params.ConfiningPressureMPa * 1e6f;
                    var dev_sxx = sxx_new - s_mean;
                    var dev_syy = syy_new - s_mean;
                    var dev_szz = szz_new - s_mean;

                    var j2 = 0.5f * (dev_sxx * dev_sxx + dev_syy * dev_syy + dev_szz * dev_szz) + sxy_new * sxy_new +
                             sxz_new * sxz_new + syz_new * syz_new;
                    var sqrt_j2 = MathF.Sqrt(j2);

                    var yield_val = sqrt_j2 + sin_phi / MathF.Sqrt(3.0f) * s_mean -
                                    cohesion * cos_phi / MathF.Sqrt(3.0f);

                    if (yield_val > 0)
                    {
                        if (_params.UseBrittleModel)
                        {
                            c.Damage[x, y, lz] += _dt * _damageRatePerSec * (yield_val / (cohesion + 1e-6f));
                            c.Damage[x, y, lz] = Math.Clamp(c.Damage[x, y, lz], 0f, 1f);
                        }

                        if (_params.UsePlasticModel)
                        {
                            var return_factor =
                                (cohesion * cos_phi / MathF.Sqrt(3.0f) - sin_phi / MathF.Sqrt(3.0f) * s_mean) /
                                (sqrt_j2 + 1e-9f);
                            if (return_factor < 1.0f)
                            {
                                sxx_new = dev_sxx * return_factor + s_mean;
                                syy_new = dev_syy * return_factor + s_mean;
                                szz_new = dev_szz * return_factor + s_mean;
                                sxy_new *= return_factor;
                                sxz_new *= return_factor;
                                syz_new *= return_factor;
                            }
                        }
                    }
                }

                c.Sxx[x, y, lz] = sxx_new;
                c.Syy[x, y, lz] = syy_new;
                c.Szz[x, y, lz] = szz_new;
                c.Sxy[x, y, lz] = sxy_new;
                c.Sxz[x, y, lz] = sxz_new;
                c.Syz[x, y, lz] = syz_new;
            }
        });
    }

    #region CPU Stencil Helpers

    private static float d_dy_for_vx(float[,,] F, int x, int y, int lz, float inv_d)
    {
        return 0.25f * (F[x, y, lz] + F[x + 1, y, lz] - (F[x, y - 1, lz] + F[x + 1, y - 1, lz])) * inv_d;
    }

    private static float d_dz_for_vx(float[,,] F, int x, int y, int lz, float inv_d)
    {
        return 0.25f * (F[x, y, lz] + F[x + 1, y, lz] - (F[x, y, lz - 1] + F[x + 1, y, lz - 1])) * inv_d;
    }

    private static float d_dx_for_vy(float[,,] F, int x, int y, int lz, float inv_d)
    {
        return 0.25f * (F[x, y, lz] + F[x, y + 1, lz] - (F[x - 1, y, lz] + F[x - 1, y + 1, lz])) * inv_d;
    }

    private static float d_dz_for_vy(float[,,] F, int x, int y, int lz, float inv_d)
    {
        return 0.25f * (F[x, y, lz] + F[x, y + 1, lz] - (F[x, y, lz - 1] + F[x, y + 1, lz - 1])) * inv_d;
    }

    private static float d_dx_for_vz(float[,,] F, int x, int y, int lz, float inv_d)
    {
        return 0.25f * (F[x, y, lz] + F[x, y, lz + 1] - (F[x - 1, y, lz] + F[x - 1, y, lz + 1])) * inv_d;
    }

    private static float d_dy_for_vz(float[,,] F, int x, int y, int lz, float inv_d)
    {
        return 0.25f * (F[x, y, lz] + F[x, y, lz + 1] - (F[x, y - 1, lz] + F[x, y - 1, lz + 1])) * inv_d;
    }

    #endregion

    private void UpdateChunkVelocityCPU(WaveFieldChunk c, byte[,,] labels, float[,,] density)
    {
        var d = c.EndZ - c.StartZ;
        var inv_dx = 1.0f / _params.PixelSize;

        Parallel.For(2, d - 2, lz =>
        {
            var gz = c.StartZ + lz;
            for (var y = 2; y < _params.Height - 2; y++)
            for (var x = 2; x < _params.Width - 2; x++)
            {
                var materialId = labels[x, y, gz];

                if (!_params.IsMaterialSelected(materialId) || density[x, y, gz] <= 0f)
                    continue;

                var rho_inv = 1.0f / MathF.Max(100f, density[x, y, gz]);

                var dsxx_dx = (c.Sxx[x, y, lz] - c.Sxx[x - 1, y, lz]) * inv_dx;
                var dsxy_dy = d_dy_for_vx(c.Sxy, x, y, lz, inv_dx);
                var dsxz_dz = d_dz_for_vx(c.Sxz, x, y, lz, inv_dx);

                var dsyx_dx = d_dx_for_vy(c.Sxy, x, y, lz, inv_dx);
                var dsyy_dy = (c.Syy[x, y, lz] - c.Syy[x, y - 1, lz]) * inv_dx;
                var dsyz_dz = d_dz_for_vy(c.Syz, x, y, lz, inv_dx);

                var dszx_dx = d_dx_for_vz(c.Sxz, x, y, lz, inv_dx);
                var dszy_dy = d_dy_for_vz(c.Syz, x, y, lz, inv_dx);
                var dszz_dz = (c.Szz[x, y, lz] - c.Szz[x, y, lz - 1]) * inv_dx;

                const float damping = 0.999f;

                var laplacian_vx = c.Vx[x + 1, y, lz] + c.Vx[x - 1, y, lz] +
                                   c.Vx[x, y + 1, lz] + c.Vx[x, y - 1, lz] +
                                   c.Vx[x, y, lz + 1] + c.Vx[x, y, lz - 1] -
                                   6.0f * c.Vx[x, y, lz];

                var laplacian_vy = c.Vy[x + 1, y, lz] + c.Vy[x - 1, y, lz] +
                                   c.Vy[x, y + 1, lz] + c.Vy[x, y - 1, lz] +
                                   c.Vy[x, y, lz + 1] + c.Vy[x, y, lz - 1] -
                                   6.0f * c.Vy[x, y, lz];

                var laplacian_vz = c.Vz[x + 1, y, lz] + c.Vz[x - 1, y, lz] +
                                   c.Vz[x, y + 1, lz] + c.Vz[x, y - 1, lz] +
                                   c.Vz[x, y, lz + 1] + c.Vz[x, y, lz - 1] -
                                   6.0f * c.Vz[x, y, lz];

                var dvx_dt = (dsxx_dx + dsxy_dy + dsxz_dz) * rho_inv;
                var dvy_dt = (dsyx_dx + dsyy_dy + dsyz_dz) * rho_inv;
                var dvz_dt = (dszx_dx + dszy_dy + dszz_dz) * rho_inv;

                c.Vx[x, y, lz] = c.Vx[x, y, lz] * damping + _dt * dvx_dt +
                                 _params.ArtificialDampingFactor / 6.0f * laplacian_vx;
                c.Vy[x, y, lz] = c.Vy[x, y, lz] * damping + _dt * dvy_dt +
                                 _params.ArtificialDampingFactor / 6.0f * laplacian_vy;
                c.Vz[x, y, lz] = c.Vz[x, y, lz] * damping + _dt * dvz_dt +
                                 _params.ArtificialDampingFactor / 6.0f * laplacian_vz;

                const float maxVel = 10000f;
                c.Vx[x, y, lz] = Math.Clamp(c.Vx[x, y, lz], -maxVel, maxVel);
                c.Vy[x, y, lz] = Math.Clamp(c.Vy[x, y, lz], -maxVel, maxVel);
                c.Vz[x, y, lz] = Math.Clamp(c.Vz[x, y, lz], -maxVel, maxVel);

                c.MaxAbsVx[x, y, lz] = MathF.Max(c.MaxAbsVx[x, y, lz], MathF.Abs(c.Vx[x, y, lz]));
                c.MaxAbsVy[x, y, lz] = MathF.Max(c.MaxAbsVy[x, y, lz], MathF.Abs(c.Vy[x, y, lz]));
                c.MaxAbsVz[x, y, lz] = MathF.Max(c.MaxAbsVz[x, y, lz], MathF.Abs(c.Vz[x, y, lz]));
            }
        });
    }

    private void ApplySourceToChunkCPU(WaveFieldChunk chunk, float sourceValue, byte[,,] labels)
    {
        var tx = (int)(_params.TxPosition.X * _params.Width);
        var ty = (int)(_params.TxPosition.Y * _params.Height);
        var tz = (int)(_params.TxPosition.Z * _params.Depth);

        if (_params.UseFullFaceTransducers)
        {
            // Determine which face to apply the source to based on the transducer's position.
            var src_x = tx < _params.Width / 2 ? 2 : _params.Width - 3;
            var src_y = ty < _params.Height / 2 ? 2 : _params.Height - 3;
            var src_z_global = tz < _params.Depth / 2 ? 2 : _params.Depth - 3;

            switch (_params.Axis)
            {
                case 0: // X-Axis
                    // --- FIX: Apply source to the entire geometric face, regardless of material at that exact voxel. ---
                    // The wave will only propagate through the material in the next step anyway.
                    for (var z = chunk.StartZ; z < chunk.EndZ; z++)
                    for (var y = 0; y < _params.Height; y++)
                        chunk.Sxx[src_x, y, z - chunk.StartZ] += sourceValue;
                    break;
                case 1: // Y-Axis
                    // --- FIX: Apply source to the entire geometric face. ---
                    for (var z = chunk.StartZ; z < chunk.EndZ; z++)
                    for (var x = 0; x < _params.Width; x++)
                        chunk.Syy[x, src_y, z - chunk.StartZ] += sourceValue;
                    break;
                case 2: // Z-Axis
                    // --- FIX: Apply source to the entire geometric face. ---
                    // Check if the target source plane is within the current chunk.
                    if (src_z_global >= chunk.StartZ && src_z_global < chunk.EndZ)
                    {
                        var local_z = src_z_global - chunk.StartZ;
                        for (var y = 0; y < _params.Height; y++)
                        for (var x = 0; x < _params.Width; x++)
                            chunk.Szz[x, y, local_z] += sourceValue;
                    }

                    break;
            }
        }
        else // Point source logic remains the same, as it correctly checks the material.
        {
            if (tz < chunk.StartZ || tz >= chunk.EndZ) return;

            float[] weights = { 0.073f, 0.12f, 0.073f };

            for (var dz = -1; dz <= 1; dz++)
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var curX = tx + dx;
                var curY = ty + dy;
                var curZ = tz + dz;
                var localZ = curZ - chunk.StartZ;

                if (curX < 1 || curX >= _params.Width - 1 ||
                    curY < 1 || curY >= _params.Height - 1 ||
                    localZ < 1 || localZ >= chunk.EndZ - chunk.StartZ - 1)
                    continue;

                if (!_params.IsMaterialSelected(labels[curX, curY, curZ])) continue;

                var weight = 1.0f;
                if (dx != 0) weight *= weights[dx + 1];
                if (dy != 0) weight *= weights[dy + 1];
                if (dz != 0) weight *= weights[dz + 1];

                var weightedSource = sourceValue * weight;
                chunk.Sxx[curX, curY, localZ] += weightedSource;
                chunk.Syy[curX, curY, localZ] += weightedSource;
                chunk.Szz[curX, curY, localZ] += weightedSource;
            }
        }
    }

    #endregion
}