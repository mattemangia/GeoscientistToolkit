// GeoscientistToolkit/Analysis/AcousticSimulation/ChunkedAcousticSimulator.cs
// FIXED VERSION - Complete implementation with corrected boundary handling

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Util;
using Silk.NET.Core.Native;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.AcousticSimulation
{
    public class ChunkedAcousticSimulator : IDisposable
    {
        private readonly SimulationParameters _params;
        private long _lastDeviceBytes;

        public long CurrentMemoryUsageMB => (GC.GetTotalMemory(false) + Interlocked.Read(ref _lastDeviceBytes)) / (1024 * 1024);

        private readonly float _lambda, _mu;
        private float _dt;
        private readonly float _damageRatePerSec = 0.2f;
        
        private float _rickerT0;
        private readonly bool _isRickerActive;

        public float TimeStep => _dt;
        public float Progress { get; private set; }
        public int CurrentStep { get; private set; }

        public event EventHandler<SimulationProgressEventArgs> ProgressUpdated;
        public event EventHandler<WaveFieldUpdateEventArgs> WaveFieldUpdated;

        private sealed class WaveFieldChunk
        {
            public int StartZ, EndZ;
            public float[,,] Vx, Vy, Vz;
            public float[,,] Sxx, Syy, Szz, Sxy, Sxz, Syz;
            public float[,,] Damage;
            public float[,,] MaxAbsVx, MaxAbsVy, MaxAbsVz;
            public bool IsInMemory = true;
            public string FilePath;
        }

        private readonly List<WaveFieldChunk> _chunks = new();
        private readonly int _chunkDepth;

        private readonly bool _useGPU;
        private readonly CL _cl = CL.GetApi();
        private nint _platform, _device, _context, _queue, _program, _kernelStress, _kernelVelocity;
        private nint _bufMat, _bufDen, _bufMatLookup; // NEW: Material lookup buffer
        private readonly nint[] _bufVel = new nint[3];
        private readonly nint[] _bufMaxVel = new nint[3];
        private readonly nint[] _bufStr = new nint[6];
        private nint _bufDmg, _bufYoungsModulus, _bufPoissonRatio;

        private float[,,] _perVoxelYoungsModulus;
        private float[,,] _perVoxelPoissonRatio;
        private bool _usePerVoxelProperties = false;

        public ChunkedAcousticSimulator(SimulationParameters parameters)
        {
            _params = parameters;
            _useGPU = parameters.UseGPU;

            float E = MathF.Max(1e-6f, _params.YoungsModulusMPa) * 1e6f;
            float nu = _params.PoissonRatio;
            _mu = E / (2f * (1f + nu));
            _lambda = E * nu / ((1f + nu) * (1f - 2f * nu));
            
            if (_params.UseRickerWavelet)
            {
                _isRickerActive = true;
                float freq = Math.Max(1.0f, _params.SourceFrequencyKHz * 1000f);
                _rickerT0 = 1.2f / freq;
            }

            long targetBytes = (_params.ChunkSizeMB > 0 ? _params.ChunkSizeMB : 256) * 1024L * 1024L;
            long bytesPerZ = (long)_params.Width * _params.Height * sizeof(float) * 13;
            _chunkDepth = (int)Math.Clamp(targetBytes / Math.Max(1, bytesPerZ), 8, _params.Depth);

            InitChunks();

            if (_useGPU)
            {
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
        }

        public void SetPerVoxelMaterialProperties(float[,,] youngsModulus, float[,,] poissonRatio)
        {
            _perVoxelYoungsModulus = youngsModulus;
            _perVoxelPoissonRatio = poissonRatio;
            _usePerVoxelProperties = true;
        }

        private void InitChunks()
        {
            for (int z0 = 0; z0 < _params.Depth; z0 += _chunkDepth)
            {
                int z1 = Math.Min(z0 + _chunkDepth, _params.Depth);
                int d = z1 - z0;

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
            var results = new SimulationResults { TimeSeriesSnapshots = _params.SaveTimeSeries ? new List<WaveFieldSnapshot>() : null };

            CalculateTimeStep(density);
            
            // FIX: Ensure all chunks start in memory
            foreach (var chunk in _chunks)
            {
                chunk.IsInMemory = true;
            }

            int maxSteps = Math.Max(1, _params.TimeSteps);
            int step = 0;
            bool pHit = false, sHit = false;
            int pStep = 0, sStep = 0;

            while (step < maxSteps && !token.IsCancellationRequested)
            {
                step++;
                CurrentStep = step;
                
                float sourceValue = GetCurrentSourceValue(step);

                // FIX: Process with proper boundary exchange
                await ProcessStressUpdatePass(labels, density, sourceValue, token);
                await ProcessVelocityUpdatePass(labels, density, token);

                if (_params.SaveTimeSeries && step % _params.SnapshotInterval == 0)
                    results.TimeSeriesSnapshots?.Add(CreateSnapshot(step));

                if (_params.EnableRealTimeVisualization && step % 10 == 0)
                {
                    WaveFieldUpdated?.Invoke(this, new WaveFieldUpdateEventArgs
                    {
                        WaveField = GetCombinedMagnitude(),
                        TimeStep = step,
                        SimTime = step * _dt,
                        Dataset = labels
                    });
                }

                if (!pHit && CheckPWaveArrival()) { pHit = true; pStep = step; }
                if (pHit && !sHit && CheckSWaveArrival()) { sHit = true; sStep = step; }

                Progress = (float)step / maxSteps;
                ProgressUpdated?.Invoke(this, new SimulationProgressEventArgs { Progress = Progress, Step = step, Message = $"Step {step}/{maxSteps}" });
            }

            // Ensure all data in memory for reconstruction
            foreach (var chunk in _chunks.Where(c => !c.IsInMemory))
                await LoadChunkAsync(chunk);

            float distance = CalculateTxRxDistance();
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

        // FIX: Stress update with correct boundary exchange
        private async Task ProcessStressUpdatePass(byte[,,] labels, float[,,] density, float sourceValue, CancellationToken token)
        {
            // CRITICAL: For correct FD, we need all ghost cells updated with data from previous timestep
            // This requires all chunks in memory. If offloading is enabled, load everything first.
            if (_params.EnableOffloading)
            {
                foreach (var chunk in _chunks.Where(c => !c.IsInMemory))
                    await LoadChunkAsync(chunk);
            }

            // Exchange ALL boundaries BEFORE processing any chunk
            // This ensures every stencil operation reads consistent data
            for (int i = 0; i < _chunks.Count - 1; i++)
            {
                ExchangeStressBetweenChunks(_chunks[i], _chunks[i + 1]);
            }

            // Apply global boundary conditions at volume edges
            ApplyGlobalBoundaryConditions();

            // Now process all chunks - they all have valid ghost cells
            for (int i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];

                if (_useGPU)
                    await ProcessChunkGPU_StressOnly(chunk, labels, density, sourceValue, token);
                else
                {
                    if (sourceValue != 0)
                        ApplySourceToChunkCPU(chunk, sourceValue);
                    UpdateChunkStressCPU(chunk, labels, density);
                }
            }

            // Optionally offload chunks after the entire pass completes
            // Note: They'll be reloaded at the start of next pass
            if (_params.EnableOffloading)
            {
                for (int i = 0; i < _chunks.Count - 1; i++)
                    await OffloadChunkAsync(_chunks[i]);
            }
        }

        // FIX: Velocity update with correct boundary exchange
        private async Task ProcessVelocityUpdatePass(byte[,,] labels, float[,,] density, CancellationToken token)
        {
            // Load all chunks if offloading is enabled
            if (_params.EnableOffloading)
            {
                foreach (var chunk in _chunks.Where(c => !c.IsInMemory))
                    await LoadChunkAsync(chunk);
            }

            // Exchange ALL boundaries BEFORE processing
            for (int i = 0; i < _chunks.Count - 1; i++)
            {
                ExchangeVelocityBetweenChunks(_chunks[i], _chunks[i + 1]);
            }

            // Apply global boundary conditions
            ApplyGlobalBoundaryConditions();

            // Process all chunks
            for (int i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];

                if (_useGPU)
                    await ProcessChunkGPU_VelocityOnly(chunk, labels, density, token);
                else
                    UpdateChunkVelocityCPU(chunk, labels, density);
            }

            // Optionally offload after pass
            if (_params.EnableOffloading)
            {
                for (int i = 0; i < _chunks.Count - 1; i++)
                    await OffloadChunkAsync(_chunks[i]);
            }
        }

        // Apply absorbing boundary conditions at global volume boundaries
        private void ApplyGlobalBoundaryConditions()
        {
            if (_chunks.Count == 0) return;

            // First chunk: top boundary (z=0) - use simple absorbing condition
            var firstChunk = _chunks[0];
            int d0 = firstChunk.EndZ - firstChunk.StartZ;
            for (int y = 0; y < _params.Height; y++)
            {
                for (int x = 0; x < _params.Width; x++)
                {
                    // Copy first interior layer to ghost layer (free boundary approximation)
                    firstChunk.Vx[x, y, 0] = firstChunk.Vx[x, y, 1];
                    firstChunk.Vy[x, y, 0] = firstChunk.Vy[x, y, 1];
                    firstChunk.Vz[x, y, 0] = firstChunk.Vz[x, y, 1];
                    firstChunk.Sxx[x, y, 0] = firstChunk.Sxx[x, y, 1];
                    firstChunk.Syy[x, y, 0] = firstChunk.Syy[x, y, 1];
                    firstChunk.Szz[x, y, 0] = firstChunk.Szz[x, y, 1];
                    firstChunk.Sxy[x, y, 0] = firstChunk.Sxy[x, y, 1];
                    firstChunk.Sxz[x, y, 0] = firstChunk.Sxz[x, y, 1];
                    firstChunk.Syz[x, y, 0] = firstChunk.Syz[x, y, 1];
                }
            }

            // Last chunk: bottom boundary (z=depth-1)
            var lastChunk = _chunks[_chunks.Count - 1];
            int d_last = lastChunk.EndZ - lastChunk.StartZ;
            for (int y = 0; y < _params.Height; y++)
            {
                for (int x = 0; x < _params.Width; x++)
                {
                    lastChunk.Vx[x, y, d_last - 1] = lastChunk.Vx[x, y, d_last - 2];
                    lastChunk.Vy[x, y, d_last - 1] = lastChunk.Vy[x, y, d_last - 2];
                    lastChunk.Vz[x, y, d_last - 1] = lastChunk.Vz[x, y, d_last - 2];
                    lastChunk.Sxx[x, y, d_last - 1] = lastChunk.Sxx[x, y, d_last - 2];
                    lastChunk.Syy[x, y, d_last - 1] = lastChunk.Syy[x, y, d_last - 2];
                    lastChunk.Szz[x, y, d_last - 1] = lastChunk.Szz[x, y, d_last - 2];
                    lastChunk.Sxy[x, y, d_last - 1] = lastChunk.Sxy[x, y, d_last - 2];
                    lastChunk.Sxz[x, y, d_last - 1] = lastChunk.Sxz[x, y, d_last - 2];
                    lastChunk.Syz[x, y, d_last - 1] = lastChunk.Syz[x, y, d_last - 2];
                }
            }
        }

        // FIX: Ensure 3-chunk window is in memory
        private async Task EnsureChunksInMemory(int chunkIndex)
        {
            if (!_chunks[chunkIndex].IsInMemory)
                await LoadChunkAsync(_chunks[chunkIndex]);

            if (chunkIndex > 0 && !_chunks[chunkIndex - 1].IsInMemory)
                await LoadChunkAsync(_chunks[chunkIndex - 1]);

            if (chunkIndex < _chunks.Count - 1 && !_chunks[chunkIndex + 1].IsInMemory)
                await LoadChunkAsync(_chunks[chunkIndex + 1]);
        }

        // FIX: Correct boundary exchange between adjacent chunks
        private void ExchangeStressBetweenChunks(WaveFieldChunk topChunk, WaveFieldChunk bottomChunk)
        {
            if (!topChunk.IsInMemory || !bottomChunk.IsInMemory) return;

            int topDepth = topChunk.EndZ - topChunk.StartZ;

            for (int y = 0; y < _params.Height; y++)
            {
                for (int x = 0; x < _params.Width; x++)
                {
                    // Top chunk's last layer gets bottom chunk's second layer
                    topChunk.Sxx[x, y, topDepth - 1] = bottomChunk.Sxx[x, y, 1];
                    topChunk.Syy[x, y, topDepth - 1] = bottomChunk.Syy[x, y, 1];
                    topChunk.Szz[x, y, topDepth - 1] = bottomChunk.Szz[x, y, 1];
                    topChunk.Sxy[x, y, topDepth - 1] = bottomChunk.Sxy[x, y, 1];
                    topChunk.Sxz[x, y, topDepth - 1] = bottomChunk.Sxz[x, y, 1];
                    topChunk.Syz[x, y, topDepth - 1] = bottomChunk.Syz[x, y, 1];

                    // Bottom chunk's first layer gets top chunk's second-to-last layer
                    bottomChunk.Sxx[x, y, 0] = topChunk.Sxx[x, y, topDepth - 2];
                    bottomChunk.Syy[x, y, 0] = topChunk.Syy[x, y, topDepth - 2];
                    bottomChunk.Szz[x, y, 0] = topChunk.Szz[x, y, topDepth - 2];
                    bottomChunk.Sxy[x, y, 0] = topChunk.Sxy[x, y, topDepth - 2];
                    bottomChunk.Sxz[x, y, 0] = topChunk.Sxz[x, y, topDepth - 2];
                    bottomChunk.Syz[x, y, 0] = topChunk.Syz[x, y, topDepth - 2];
                }
            }
        }

        private void ExchangeVelocityBetweenChunks(WaveFieldChunk topChunk, WaveFieldChunk bottomChunk)
        {
            if (!topChunk.IsInMemory || !bottomChunk.IsInMemory) return;

            int topDepth = topChunk.EndZ - topChunk.StartZ;

            for (int y = 0; y < _params.Height; y++)
            {
                for (int x = 0; x < _params.Width; x++)
                {
                    topChunk.Vx[x, y, topDepth - 1] = bottomChunk.Vx[x, y, 1];
                    topChunk.Vy[x, y, topDepth - 1] = bottomChunk.Vy[x, y, 1];
                    topChunk.Vz[x, y, topDepth - 1] = bottomChunk.Vz[x, y, 1];

                    bottomChunk.Vx[x, y, 0] = topChunk.Vx[x, y, topDepth - 2];
                    bottomChunk.Vy[x, y, 0] = topChunk.Vy[x, y, topDepth - 2];
                    bottomChunk.Vz[x, y, 0] = topChunk.Vz[x, y, topDepth - 2];
                }
            }
        }

        #region OpenCL and GPU Processing
        private unsafe void InitOpenCL()
        {
            uint nplat = 0;
            _cl.GetPlatformIDs(0, null, &nplat);
            if (nplat == 0) throw new InvalidOperationException("OpenCL: no platforms.");
            var plats = new nint[nplat];
            fixed (nint* pPlats = plats) _cl.GetPlatformIDs(nplat, pPlats, null);
            _platform = plats[0];

            uint ndev = 0;
            _cl.GetDeviceIDs(_platform, DeviceType.Gpu, 0, null, &ndev);
            DeviceType chosen = DeviceType.Gpu;
            if (ndev == 0)
            {
                _cl.GetDeviceIDs(_platform, DeviceType.Cpu, 0, null, &ndev);
                if (ndev == 0) throw new InvalidOperationException("OpenCL: no devices.");
                chosen = DeviceType.Cpu;
            }
            var devs = new nint[ndev];
            fixed (nint* pDevs = devs) _cl.GetDeviceIDs(_platform, chosen, ndev, pDevs, null);
            _device = devs[0];
            
            int err;
            nint[] one = { _device };
            fixed (nint* p = one)
                _context = _cl.CreateContext(null, 1u, p, null, null, out err);
            if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateContext failed: {err}");

            _queue = _cl.CreateCommandQueue(_context, _device, CommandQueueProperties.None, out err);
            if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateCommandQueue failed: {err}");
        }

        private unsafe void BuildProgramAndKernels()
        {
            string[] sources = { GetKernelSource() };
            _program = _cl.CreateProgramWithSource(_context, 1u, sources, (UIntPtr*)null, out int err);
            if (err != (int)CLEnum.Success) throw new InvalidOperationException($"CreateProgramWithSource failed: {err}");

            nint[] devs = { _device };
            fixed (nint* p = devs)
            {
                int buildErr = _cl.BuildProgram(_program, 1u, p, (string)null, null, null);
                if (buildErr != (int)CLEnum.Success)
                {
                    nuint logSize;
                    _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);
                    var log = new byte[logSize];
                    fixed (byte* pLog = log)
                    {
                        _cl.GetProgramBuildInfo(_program, _device, ProgramBuildInfo.BuildLog, logSize, pLog, null);
                        throw new InvalidOperationException($"BuildProgram failed: {buildErr}\n{System.Text.Encoding.UTF8.GetString(log)}");
                    }
                }
            }

            _kernelStress = _cl.CreateKernel(_program, "updateStress", out int err1);
            if (err1 != (int)CLEnum.Success) throw new InvalidOperationException($"CreateKernel(updateStress) failed: {err1}");
            _kernelVelocity = _cl.CreateKernel(_program, "updateVelocity", out int err2);
            if (err2 != (int)CLEnum.Success) throw new InvalidOperationException($"CreateKernel(updateVelocity) failed: {err2}");
        }

        private async Task ProcessChunkGPU_StressOnly(WaveFieldChunk c, byte[,,] labels, float[,,] density, float sourceValue, CancellationToken token)
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                int w = _params.Width, h = _params.Height, d = c.EndZ - c.StartZ;
                int size = w * h * d;

                var mat = new byte[size]; var den = new float[size]; var vx = new float[size]; var vy = new float[size]; var vz = new float[size];
                var sxx = new float[size]; var syy = new float[size]; var szz = new float[size]; var sxy = new float[size]; var sxz = new float[size]; var syz = new float[size];
                var dmg = new float[size]; var ym = new float[size]; var pr = new float[size];
                
                // NEW: Get material lookup table
                var matLookup = _params.CreateMaterialLookupTable();

                int k = 0;
                for (int lz = 0; lz < d; lz++)
                {
                    int gz = c.StartZ + lz;
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++, k++)
                    {
                        mat[k] = labels[x, y, gz]; den[k] = MathF.Max(100f, density[x, y, gz]);
                        vx[k] = c.Vx[x, y, lz]; vy[k] = c.Vy[x, y, lz]; vz[k] = c.Vz[x, y, lz];
                        sxx[k] = c.Sxx[x, y, lz]; syy[k] = c.Syy[x, y, lz]; szz[k] = c.Szz[x, y, lz];
                        sxy[k] = c.Sxy[x, y, lz]; sxz[k] = c.Sxz[x, y, lz]; syz[k] = c.Syz[x, y, lz];
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
                    fixed (byte* pMat = mat) fixed (float* pDen = den) fixed (float* pVx = vx) fixed (float* pVy = vy) fixed (float* pVz = vz)
                    fixed (float* pSxx = sxx) fixed (float* pSyy = syy) fixed (float* pSzz = szz) fixed (float* pSxy = sxy) fixed (float* pSxz = sxz) fixed (float* pSyz = syz)
                    fixed (float* pDmg = dmg) fixed (float* pYm = ym) fixed (float* pPr = pr)
                    fixed (byte* pMatLookup = matLookup) // NEW: Pin material lookup
                    {
                        _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(byte)), pMat, out int err);
                        _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDen, out err);
                        _bufMatLookup = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, 256, pMatLookup, out err); // NEW
                        _bufVel[0] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVx, out err);
                        _bufVel[1] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVy, out err);
                        _bufVel[2] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVz, out err);
                        _bufStr[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxx, out err);
                        _bufStr[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSyy, out err);
                        _bufStr[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSzz, out err);
                        _bufStr[3] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxy, out err);
                        _bufStr[4] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxz, out err);
                        _bufStr[5] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSyz, out err);
                        _bufDmg = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDmg, out err);
                        _bufYoungsModulus = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pYm, out err);
                        _bufPoissonRatio = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pPr, out err);

                        _lastDeviceBytes = (long)size * (sizeof(byte) + sizeof(float) * 13) + 256;
                        
                        SetKernelArgs(_kernelStress, w, h, d, c.StartZ, sourceValue);
                        nuint gws = (nuint)size;
                        _cl.EnqueueNdrangeKernel(_queue, _kernelStress, 1, null, &gws, null, 0, null, null);
                        
                        _cl.Finish(_queue);

                        _cl.EnqueueReadBuffer(_queue, _bufStr[0], true, 0, (nuint)(size * sizeof(float)), pSxx, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufStr[1], true, 0, (nuint)(size * sizeof(float)), pSyy, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufStr[2], true, 0, (nuint)(size * sizeof(float)), pSzz, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufStr[3], true, 0, (nuint)(size * sizeof(float)), pSxy, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufStr[4], true, 0, (nuint)(size * sizeof(float)), pSxz, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufStr[5], true, 0, (nuint)(size * sizeof(float)), pSyz, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufDmg, true, 0, (nuint)(size * sizeof(float)), pDmg, 0, null, null);
                    }
                }

                k = 0;
                for (int lz = 0; lz < d; lz++)
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++, k++)
                {
                    c.Sxx[x, y, lz] = sxx[k]; c.Syy[x, y, lz] = syy[k]; c.Szz[x, y, lz] = szz[k];
                    c.Sxy[x, y, lz] = sxy[k]; c.Sxz[x, y, lz] = sxz[k]; c.Syz[x, y, lz] = syz[k];
                    c.Damage[x, y, lz] = dmg[k];
                }

                ReleaseChunkBuffers();
            });
        }
        
        private async Task ProcessChunkGPU_VelocityOnly(WaveFieldChunk c, byte[,,] labels, float[,,] density, CancellationToken token)
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                int w = _params.Width, h = _params.Height, d = c.EndZ - c.StartZ;
                int size = w * h * d;

                var mat = new byte[size]; var den = new float[size]; var vx = new float[size]; var vy = new float[size]; var vz = new float[size];
                var sxx = new float[size]; var syy = new float[size]; var szz = new float[size]; var sxy = new float[size]; var sxz = new float[size]; var syz = new float[size];
                var max_vx = new float[size]; var max_vy = new float[size]; var max_vz = new float[size];
                
                // NEW: Get material lookup table
                var matLookup = _params.CreateMaterialLookupTable();

                int k = 0;
                for (int lz = 0; lz < d; lz++)
                {
                    int gz = c.StartZ + lz;
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++, k++)
                    {
                        mat[k] = labels[x, y, gz]; den[k] = MathF.Max(100f, density[x, y, gz]);
                        vx[k] = c.Vx[x, y, lz]; vy[k] = c.Vy[x, y, lz]; vz[k] = c.Vz[x, y, lz];
                        sxx[k] = c.Sxx[x, y, lz]; syy[k] = c.Syy[x, y, lz]; szz[k] = c.Szz[x, y, lz];
                        sxy[k] = c.Sxy[x, y, lz]; sxz[k] = c.Sxz[x, y, lz]; syz[k] = c.Syz[x, y, lz];
                        max_vx[k] = c.MaxAbsVx[x, y, lz];
                        max_vy[k] = c.MaxAbsVy[x, y, lz];
                        max_vz[k] = c.MaxAbsVz[x, y, lz];
                    }
                }

                unsafe
                {
                    fixed (byte* pMat = mat) fixed (float* pDen = den) fixed (float* pVx = vx) fixed (float* pVy = vy) fixed (float* pVz = vz)
                    fixed (float* pSxx = sxx) fixed (float* pSyy = syy) fixed (float* pSzz = szz) fixed (float* pSxy = sxy) fixed (float* pSxz = sxz) fixed (float* pSyz = syz)
                    fixed (float* pMaxVx = max_vx) fixed (float* pMaxVy = max_vy) fixed (float* pMaxVz = max_vz)
                    fixed (byte* pMatLookup = matLookup) // NEW: Pin material lookup
                    {
                        _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(byte)), pMat, out int err);
                        _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDen, out err);
                        _bufMatLookup = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, 256, pMatLookup, out err); // NEW
                        _bufVel[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVx, out err);
                        _bufVel[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVy, out err);
                        _bufVel[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVz, out err);
                        _bufStr[0] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxx, out err);
                        _bufStr[1] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSyy, out err);
                        _bufStr[2] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSzz, out err);
                        _bufStr[3] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxy, out err);
                        _bufStr[4] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxz, out err);
                        _bufStr[5] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSyz, out err);
                        _bufMaxVel[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pMaxVx, out err);
                        _bufMaxVel[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pMaxVy, out err);
                        _bufMaxVel[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pMaxVz, out err);

                        _lastDeviceBytes = (long)size * (sizeof(byte) + sizeof(float) * 15) + 256;
                        
                        SetKernelArgs(_kernelVelocity, w, h, d, c.StartZ, 0);
                        nuint gws = (nuint)size;
                        _cl.EnqueueNdrangeKernel(_queue, _kernelVelocity, 1, null, &gws, null, 0, null, null);
                        
                        _cl.Finish(_queue);

                        _cl.EnqueueReadBuffer(_queue, _bufVel[0], true, 0, (nuint)(size * sizeof(float)), pVx, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufVel[1], true, 0, (nuint)(size * sizeof(float)), pVy, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufVel[2], true, 0, (nuint)(size * sizeof(float)), pVz, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufMaxVel[0], true, 0, (nuint)(size * sizeof(float)), pMaxVx, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufMaxVel[1], true, 0, (nuint)(size * sizeof(float)), pMaxVy, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufMaxVel[2], true, 0, (nuint)(size * sizeof(float)), pMaxVz, 0, null, null);
                    }
                }

                k = 0;
                for (int lz = 0; lz < d; lz++)
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++, k++)
                {
                    c.Vx[x, y, lz] = vx[k]; c.Vy[x, y, lz] = vy[k]; c.Vz[x, y, lz] = vz[k];
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
            nint bufMatArg = _bufMat;
            nint bufDenArg = _bufDen;
            nint bufMatLookupArg = _bufMatLookup; // NEW
            nint bufVel0Arg = _bufVel[0];
            nint bufVel1Arg = _bufVel[1];
            nint bufVel2Arg = _bufVel[2];
            nint bufStr0Arg = _bufStr[0];
            nint bufStr1Arg = _bufStr[1];
            nint bufStr2Arg = _bufStr[2];
            nint bufStr3Arg = _bufStr[3];
            nint bufStr4Arg = _bufStr[4];
            nint bufStr5Arg = _bufStr[5];
            nint bufDmgArg = _bufDmg;
            nint bufYmArg = _bufYoungsModulus;
            nint bufPrArg = _bufPoissonRatio;
            nint bufMaxVel0Arg = _bufMaxVel[0];
            nint bufMaxVel1Arg = _bufMaxVel[1];
            nint bufMaxVel2Arg = _bufMaxVel[2];

            var paramsPixelSize = _params.PixelSize;
            var paramsSelectedMaterialId = _params.SelectedMaterialID;
            
            if (kernel == _kernelStress)
            {
                var paramsConfiningPressureMPa = _params.ConfiningPressureMPa;
                var paramsCohesionMPa = _params.CohesionMPa;
                var paramsFailureAngleDeg = _params.FailureAngleDeg;
                var paramsUsePlasticModel = _params.UsePlasticModel;
                var paramsUseBrittleModel = _params.UseBrittleModel;

                // Source parameters
                int tx = (int)(_params.TxPosition.X * _params.Width);
                int ty = (int)(_params.TxPosition.Y * _params.Height);
                int tz = (int)(_params.TxPosition.Z * _params.Depth);
                
                // FIX: Only apply full-face source in the chunk that contains the source position
                int applySource = 0;
                if (sourceValue != 0)
                {
                    if (_params.UseFullFaceTransducers)
                    {
                        // Check if this chunk contains the source face
                        bool chunkContainsSource = false;
                        switch (_params.Axis)
                        {
                            case 0: // X-axis source at x=0
                                chunkContainsSource = (tx <= 2);
                                break;
                            case 1: // Y-axis source at y=0
                                chunkContainsSource = (ty <= 2);
                                break;
                            case 2: // Z-axis source at z=0
                                chunkContainsSource = (chunkStartZ == 0 && tz <= 2);
                                break;
                        }
                        applySource = chunkContainsSource ? 1 : 0;
                    }
                    else
                    {
                        // Point source - check if in this chunk
                        if (tz >= chunkStartZ && tz < chunkStartZ + d)
                            applySource = 1;
                    }
                }

                int localTz = tz - chunkStartZ;
                int isFullFace = _params.UseFullFaceTransducers ? 1 : 0;
                int sourceAxis = _params.Axis;

                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMatArg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufDenArg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMatLookupArg); // NEW
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
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _dt);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsPixelSize);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in w);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in h);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in d);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(byte), in paramsSelectedMaterialId);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _damageRatePerSec);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsConfiningPressureMPa);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsCohesionMPa);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsFailureAngleDeg);
                int usePlastic = paramsUsePlasticModel ? 1 : 0;
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in usePlastic);
                int useBrittle = paramsUseBrittleModel ? 1 : 0;
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in useBrittle);
                
                // Source application args
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in sourceValue);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in applySource);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in tx);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in ty);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in localTz);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in isFullFace);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in sourceAxis);
            }
            else // _kernelVelocity
            {
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMatArg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufDenArg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMatLookupArg); // NEW
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
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _dt);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsPixelSize);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in w);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in h);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in d);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(byte), in paramsSelectedMaterialId);
            }
        }
        
        #endregion

        #region CPU Processing
        private void UpdateChunkStressCPU(WaveFieldChunk c, byte[,,] labels, float[,,] density)
        {
            int d = c.EndZ - c.StartZ;
            Parallel.For(1, d - 1, lz =>
            {
                int gz = c.StartZ + lz;
                for (int y = 1; y < _params.Height - 1; y++)
                for (int x = 1; x < _params.Width - 1; x++)
                {
                    byte materialId = labels[x, y, gz];
                    
                    // MULTI-MATERIAL: Check if this material is selected
                    if (!_params.IsMaterialSelected(materialId) || density[x,y,gz] <= 0f) 
                        continue;

                    float localE, localNu, localLambda, localMu;
                    if (_usePerVoxelProperties && _perVoxelYoungsModulus != null && _perVoxelPoissonRatio != null)
                    {
                        localE = _perVoxelYoungsModulus[x, y, gz] * 1e6f;
                        localNu = _perVoxelPoissonRatio[x, y, gz];
                        
                        // Skip if material properties are invalid
                        if (localE <= 0f || localNu <= -1.0f || localNu >= 0.5f) continue;
                    }
                    else
                    {
                        localE = _params.YoungsModulusMPa * 1e6f;
                        localNu = _params.PoissonRatio;
                    }
                    
                    localMu = localE / (2f * (1f + localNu));
                    localLambda = localE * localNu / ((1f + localNu) * (1f - 2f * localNu));
                    
                    float dvx_dx = (c.Vx[x + 1, y, lz] - c.Vx[x - 1, y, lz]) / (2 * _params.PixelSize);
                    float dvy_dy = (c.Vy[x, y + 1, lz] - c.Vy[x, y - 1, lz]) / (2 * _params.PixelSize);
                    float dvz_dz = (c.Vz[x, y, lz + 1] - c.Vz[x, y, lz - 1]) / (2 * _params.PixelSize);
                    float dvy_dx = (c.Vy[x + 1, y, lz] - c.Vy[x - 1, y, lz]) / (2 * _params.PixelSize);
                    float dvx_dy = (c.Vx[x, y + 1, lz] - c.Vx[x, y - 1, lz]) / (2 * _params.PixelSize);
                    float dvz_dx = (c.Vz[x + 1, y, lz] - c.Vz[x - 1, y, lz]) / (2 * _params.PixelSize);
                    float dvx_dz = (c.Vx[x, y, lz + 1] - c.Vx[x, y, lz - 1]) / (2 * _params.PixelSize);
                    float dvz_dy = (c.Vz[x, y + 1, lz] - c.Vz[x, y - 1, lz]) / (2 * _params.PixelSize);
                    float dvy_dz = (c.Vy[x, y, lz + 1] - c.Vy[x, y, lz - 1]) / (2 * _params.PixelSize);
                    
                    float volumetric = dvx_dx + dvy_dy + dvz_dz;
                    float damp = (1f - c.Damage[x, y, lz] * 0.5f);

                    float sxx_new = c.Sxx[x, y, lz] + _dt * damp * (localLambda * volumetric + 2f * localMu * dvx_dx);
                    float syy_new = c.Syy[x, y, lz] + _dt * damp * (localLambda * volumetric + 2f * localMu * dvy_dy);
                    float szz_new = c.Szz[x, y, lz] + _dt * damp * (localLambda * volumetric + 2f * localMu * dvz_dz);
                    float sxy_new = c.Sxy[x, y, lz] + _dt * damp * localMu * (dvy_dx + dvx_dy);
                    float sxz_new = c.Sxz[x, y, lz] + _dt * damp * localMu * (dvz_dx + dvx_dz);
                    float syz_new = c.Syz[x, y, lz] + _dt * damp * localMu * (dvz_dy + dvy_dz);

                    if (_params.UsePlasticModel)
                    {
                        float mean = (sxx_new + syy_new + szz_new) / 3.0f;
                        float dev_xx = sxx_new - mean;
                        float dev_yy = syy_new - mean;
                        float dev_zz = szz_new - mean;
                        float J2 = 0.5f * (dev_xx*dev_xx + dev_yy*dev_yy + dev_zz*dev_zz) + sxy_new*sxy_new + sxz_new*sxz_new + syz_new*syz_new;
                        float tau = MathF.Sqrt(J2);
                        float failureAngleRad = _params.FailureAngleDeg * MathF.PI / 180.0f;
                        float sinPhi = MathF.Sin(failureAngleRad);
                        float cosPhi = MathF.Cos(failureAngleRad);
                        float cohesionPa = _params.CohesionMPa * 1e6f;
                        float p = -mean + _params.ConfiningPressureMPa * 1e6f;
                        float yield = tau + p * sinPhi - cohesionPa * cosPhi;

                        if (yield > 0 && tau > 1e-10f)
                        {
                            float scale = (tau - (cohesionPa * cosPhi - p * sinPhi)) / tau;
                            scale = Math.Min(scale, 0.95f);
                            sxx_new = (dev_xx * (1 - scale)) + mean;
                            syy_new = (dev_yy * (1 - scale)) + mean;
                            szz_new = (dev_zz * (1 - scale)) + mean;
                            sxy_new *= (1 - scale);
                            sxz_new *= (1 - scale);
                            syz_new *= (1 - scale);
                        }
                    }
                    
                    c.Sxx[x, y, lz] = sxx_new;
                    c.Syy[x, y, lz] = syy_new;
                    c.Szz[x, y, lz] = szz_new;
                    c.Sxy[x, y, lz] = sxy_new;
                    c.Sxz[x, y, lz] = sxz_new;
                    c.Syz[x, y, lz] = syz_new;

                    if (_params.UseBrittleModel)
                    {
                        float cohesionPa = _params.CohesionMPa * 1e6f;
                        float failureAngleRad = _params.FailureAngleDeg * MathF.PI / 180.0f;
                        float sinPhi = MathF.Sin(failureAngleRad);
                        float cosPhi = MathF.Cos(failureAngleRad);
                        float ucsPa = (2.0f * cohesionPa * cosPhi) / (1.0f - sinPhi);
                        float tensileLimitPa = ucsPa / 10.0f;

                        float meanStress = (sxx_new + syy_new + szz_new) / 3.0f;
                        float p = -meanStress + _params.ConfiningPressureMPa * 1e6f;
                        float dev_xx = sxx_new - meanStress;
                        float dev_yy = syy_new - meanStress;
                        float dev_zz = szz_new - meanStress;
                        float J2 = 0.5f * (dev_xx*dev_xx + dev_yy*dev_yy + dev_zz*dev_zz) + sxy_new*sxy_new + sxz_new*sxz_new + syz_new*syz_new;
                        float tau = MathF.Sqrt(J2);
                        float yieldShear = tau + p * sinPhi - cohesionPa * cosPhi;
                        
                        float maxTensile = MathF.Max(sxx_new, MathF.Max(syy_new, szz_new));
                        
                        float dInc = 0f;
                        if (yieldShear > 0 && cohesionPa > 0)
                        {
                            dInc += _damageRatePerSec * _dt * (yieldShear / (cohesionPa * cosPhi));
                        }
                        if (maxTensile > tensileLimitPa && tensileLimitPa > 0)
                        {
                            dInc += _damageRatePerSec * _dt * (maxTensile / tensileLimitPa - 1.0f);
                        }
                        
                        if (dInc > 0)
                        {
                            c.Damage[x, y, lz] = Math.Min(0.9f, c.Damage[x, y, lz] + dInc);
                        }
                    }
                }
            });
        }

        private void UpdateChunkVelocityCPU(WaveFieldChunk c, byte[,,] labels, float[,,] density)
        {
            int d = c.EndZ - c.StartZ;
            Parallel.For(1, d - 1, lz =>
            {
                int gz = c.StartZ + lz;
                for (int y = 1; y < _params.Height - 1; y++)
                for (int x = 1; x < _params.Width - 1; x++)
                {
                    byte materialId = labels[x, y, gz];
                    
                    // MULTI-MATERIAL: Check if this material is selected
                    if (!_params.IsMaterialSelected(materialId) || density[x,y,gz] <= 0f)
                        continue;
                    
                    float rho = MathF.Max(100f, density[x, y, gz]);
                    
                    float dsxx_dx = (c.Sxx[Math.Min(x + 1, _params.Width - 1), y, lz] - 
                                    c.Sxx[Math.Max(x - 1, 0), y, lz]) / (2 * _params.PixelSize);
                    float dsyy_dy = (c.Syy[x, Math.Min(y + 1, _params.Height - 1), lz] - 
                                    c.Syy[x, Math.Max(y - 1, 0), lz]) / (2 * _params.PixelSize);
                    float dszz_dz = 0;
                    if (lz > 0 && lz < d - 1)
                    {
                        dszz_dz = (c.Szz[x, y, lz + 1] - c.Szz[x, y, lz - 1]) / (2 * _params.PixelSize);
                    }
                    
                    float dsxy_dy = (c.Sxy[x, Math.Min(y + 1, _params.Height - 1), lz] - 
                                    c.Sxy[x, Math.Max(y - 1, 0), lz]) / (2 * _params.PixelSize);
                    float dsxy_dx = (c.Sxy[Math.Min(x + 1, _params.Width - 1), y, lz] - 
                                    c.Sxy[Math.Max(x - 1, 0), y, lz]) / (2 * _params.PixelSize);
                    float dsxz_dz = 0, dsxz_dx = 0, dsyz_dz = 0, dsyz_dy = 0;
                    if (lz > 0 && lz < d - 1)
                    {
                        dsxz_dz = (c.Sxz[x, y, lz + 1] - c.Sxz[x, y, lz - 1]) / (2 * _params.PixelSize);
                        dsyz_dz = (c.Syz[x, y, lz + 1] - c.Syz[x, y, lz - 1]) / (2 * _params.PixelSize);
                    }
                    dsxz_dx = (c.Sxz[Math.Min(x + 1, _params.Width - 1), y, lz] - 
                              c.Sxz[Math.Max(x - 1, 0), y, lz]) / (2 * _params.PixelSize);
                    dsyz_dy = (c.Syz[x, Math.Min(y + 1, _params.Height - 1), lz] - 
                              c.Syz[x, Math.Max(y - 1, 0), lz]) / (2 * _params.PixelSize);
                    
                    const float damping = 0.999f;
                    
                    c.Vx[x, y, lz] = c.Vx[x, y, lz] * damping + _dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
                    c.Vy[x, y, lz] = c.Vy[x, y, lz] * damping + _dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
                    c.Vz[x, y, lz] = c.Vz[x, y, lz] * damping + _dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
                    
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

        private void ApplySourceToChunkCPU(WaveFieldChunk chunk, float sourceValue)
        {
            int tx = (int)(_params.TxPosition.X * _params.Width);
            int ty = (int)(_params.TxPosition.Y * _params.Height);
            int tz = (int)(_params.TxPosition.Z * _params.Depth);

            if (_params.UseFullFaceTransducers)
            {
                switch (_params.Axis)
                {
                    case 0: // X-axis, apply to YZ face at x=0
                        if (tx > 2) return;
                        for (int z = chunk.StartZ; z < chunk.EndZ; z++)
                        for (int y = 0; y < _params.Height; y++)
                            chunk.Sxx[0, y, z - chunk.StartZ] += sourceValue;
                        break;
                    case 1: // Y-axis, apply to XZ face at y=0
                        if (ty > 2) return;
                        for (int z = chunk.StartZ; z < chunk.EndZ; z++)
                        for (int x = 0; x < _params.Width; x++)
                            chunk.Syy[x, 0, z - chunk.StartZ] += sourceValue;
                        break;
                    case 2: // Z-axis, apply to XY face at z=0
                        if (chunk.StartZ > 0 || tz > 2) return;
                        for (int y = 0; y < _params.Height; y++)
                        for (int x = 0; x < _params.Width; x++)
                            chunk.Szz[x, y, 0] += sourceValue;
                        break;
                }
            }
            else // Point source
            {
                if (tz < chunk.StartZ || tz >= chunk.EndZ) return;
                int localTz = tz - chunk.StartZ;
                int radius = 3;

                for (int dz = -radius; dz <= radius; dz++)
                {
                    int lz = localTz + dz;
                    if (lz < 0 || lz >= (chunk.EndZ - chunk.StartZ)) continue;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int y = ty + dy;
                        if (y < 0 || y >= _params.Height) continue;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int x = tx + dx;
                            if (x < 0 || x >= _params.Width) continue;
                            
                            float distSq = dx * dx + dy * dy + dz * dz;
                            if (distSq > radius * radius) continue;
                            
                            float falloff = MathF.Exp(-distSq / (radius * radius * 0.5f));
                            float localSource = sourceValue * falloff;
                            
                            chunk.Sxx[x, y, lz] += localSource;
                            chunk.Syy[x, y, lz] += localSource;
                            chunk.Szz[x, y, lz] += localSource;
                        }
                    }
                }
            }
        }
        #endregion

        private float GetCurrentSourceValue(int step)
        {
            float baseAmp = _params.SourceAmplitude * MathF.Sqrt(MathF.Max(1e-6f, _params.SourceEnergyJ));
            baseAmp *= 1e4f;

            if (!_isRickerActive)
                return (step == 1) ? baseAmp : 0f;

            float t = step * _dt;
            if (t > 2 * _rickerT0) return 0f;

            float freq = Math.Max(1.0f, _params.SourceFrequencyKHz * 1000f);
            float x = MathF.PI * freq * (t - _rickerT0);
            float xx = x * x;
            return baseAmp * (1.0f - 2.0f * xx) * MathF.Exp(-xx);
        }
        private void CalculateTimeStep(float[,,] density)
        {
            float vpMax = 0f;

            if (_usePerVoxelProperties && _perVoxelYoungsModulus != null && _perVoxelPoissonRatio != null)
            {
                for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                    if (density[x, y, z] <= 0) continue;

                    float rho = MathF.Max(100f, density[x, y, z]);
                    float E = _perVoxelYoungsModulus[x, y, z] * 1e6f;
                    float nu = _perVoxelPoissonRatio[x, y, z];

                    if (E <= 0 || rho <= 0 || nu >= 0.5f || nu <= -1.0f) continue;

                    float lambda = E * nu / ((1f + nu) * (1f - 2f * nu));
                    float mu = E / (2f * (1f + nu));
                    float vp = MathF.Sqrt((lambda + 2f * mu) / rho);
                    if (vp > vpMax) vpMax = vp;
                }
            }
            else
            {
                float rhoMin = float.MaxValue;
                for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                     if (density[x,y,z] > 0)
                        rhoMin = MathF.Min(rhoMin, MathF.Max(100f, density[x, y, z]));
                }
                
                if(rhoMin == float.MaxValue) rhoMin = 2700;
                
                vpMax = MathF.Sqrt((_lambda + 2f * _mu) / rhoMin);
            }

            if (vpMax < 1e-3f)
            {
                _dt = _params.TimeStepSeconds;
                Logger.LogWarning($"[CFL] Could not determine valid Vp_max. Using default dt={_dt * 1e6f:F4} µs.");
                return;
            }

            _dt = 0.5f * (_params.PixelSize / (MathF.Sqrt(3) * vpMax));
            Logger.Log($"[CFL] Calculated stable timestep: dt={_dt*1e6f:F4} µs based on Vp_max={vpMax:F0} m/s");
        }

        private bool CheckPWaveArrival()
        {
            int rx = Math.Clamp((int)(_params.RxPosition.X * _params.Width), 0, _params.Width - 1);
            int ry = Math.Clamp((int)(_params.RxPosition.Y * _params.Height), 0, _params.Height - 1);
            int rz = Math.Clamp((int)(_params.RxPosition.Z * _params.Depth), 0, _params.Depth - 1);

            var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ); 
            if (c == null || !c.IsInMemory) return false;

            int lz = rz - c.StartZ;
            float comp = _params.Axis switch 
            { 
                0 => MathF.Abs(c.Vx[rx, ry, lz]), 
                1 => MathF.Abs(c.Vy[rx, ry, lz]), 
                _ => MathF.Abs(c.Vz[rx, ry, lz]) 
            };
            return comp > 1e-9f;
        }

        private bool CheckSWaveArrival()
        {
            int rx = (int)(_params.RxPosition.X * _params.Width);
            int ry = (int)(_params.RxPosition.Y * _params.Height);
            int rz = (int)(_params.RxPosition.Z * _params.Depth);
            
            var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ);
            if (c == null || !c.IsInMemory) return false;
            
            int lz = rz - c.StartZ;
            float mag = _params.Axis switch
            {
                0 => MathF.Sqrt(c.Vy[rx, ry, lz] * c.Vy[rx, ry, lz] + c.Vz[rx, ry, lz] * c.Vz[rx, ry, lz]),
                1 => MathF.Sqrt(c.Vx[rx, ry, lz] * c.Vx[rx, ry, lz] + c.Vz[rx, ry, lz] * c.Vz[rx, ry, lz]),
                _ => MathF.Sqrt(c.Vx[rx, ry, lz] * c.Vx[rx, ry, lz] + c.Vy[rx, ry, lz] * c.Vy[rx, ry, lz]),
            };
            return mag > 1e-9f;
        }
        
        private float CalculateTxRxDistance()
        {
            int tx = (int)(_params.TxPosition.X * _params.Width);
            int ty = (int)(_params.TxPosition.Y * _params.Height);
            int tz = (int)(_params.TxPosition.Z * _params.Depth);
            int rx = (int)(_params.RxPosition.X * _params.Width);
            int ry = (int)(_params.RxPosition.Y * _params.Height);
            int rz = (int)(_params.RxPosition.Z * _params.Depth);
            
            return MathF.Sqrt((tx - rx) * (tx - rx) + (ty - ry) * (ty - ry) + (tz - rz) * (tz - rz)) * _params.PixelSize;
        }

        private WaveFieldSnapshot CreateSnapshot(int step)
        {
            int ds = Math.Max(1, Math.Max(_params.Width, Math.Max(_params.Height, _params.Depth)) / 128);
            int w = Math.Max(1, _params.Width / ds);
            int h = Math.Max(1, _params.Height / ds);
            int d = Math.Max(1, _params.Depth / ds);
            
            var vx = new float[w, h, d];
            var vy = new float[w, h, d];
            var vz = new float[w, h, d];
            
            foreach (var c in _chunks)
            {
                if (!c.IsInMemory) LoadChunkAsync(c).Wait();
                int cd = c.EndZ - c.StartZ;
                
                for (int lz = 0; lz < cd; lz += ds)
                {
                    int gz = c.StartZ + lz;
                    int dz = gz / ds;
                    if (dz >= d) continue;
                    
                    for (int y = 0; y < _params.Height; y += ds)
                    {
                        int dy = y / ds;
                        if (dy >= h) continue;
                        
                        for (int x = 0; x < _params.Width; x += ds)
                        {
                            int dx = x / ds;
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
        
        public async Task<float[,,]> ReconstructFieldAsync(int comp)
        {
            var field = new float[_params.Width, _params.Height, _params.Depth];
            await Task.Run(() =>
            {
                foreach (var c in _chunks)
                {
                    if (!c.IsInMemory) return;
                    int d = c.EndZ - c.StartZ;
                    
                    for (int z = 0; z < d; z++)
                    for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                    {
                        float v = comp switch { 0 => c.Vx[x, y, z], 1 => c.Vy[x, y, z], _ => c.Vz[x, y, z] };
                        field[x, y, c.StartZ + z] = v;
                    }
                }
            });
            return field;
        }
        
        public async Task<float[,,]> ReconstructMaxFieldAsync(int comp)
        {
            var field = new float[_params.Width, _params.Height, _params.Depth];
            await Task.Run(() =>
            {
                foreach (var c in _chunks)
                {
                    if (!c.IsInMemory) return;
                    int d = c.EndZ - c.StartZ;
                    
                    for (int z = 0; z < d; z++)
                    for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                    {
                        float v = comp switch { 0 => c.MaxAbsVx[x, y, z], 1 => c.MaxAbsVy[x, y, z], _ => c.MaxAbsVz[x, y, z] };
                        field[x, y, c.StartZ + z] = v;
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
                int d = chunk.EndZ - chunk.StartZ;
                
                for (int z = 0; z < d; z++)
                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                    damageField[x, y, chunk.StartZ + z] = chunk.Damage[x, y, z];
                }
            }
            return damageField;
        }

        private float[,,] GetCombinedMagnitude()
        {
            var out3d = new float[_params.Width, _params.Height, _params.Depth];
            foreach (var c in _chunks)
            {
                if (!c.IsInMemory) LoadChunkAsync(c).Wait();
                int d = c.EndZ - c.StartZ;
                
                for (int z = 0; z < d; z++)
                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                    float vx = c.Vx[x, y, z];
                    float vy = c.Vy[x, y, z];
                    float vz = c.Vz[x, y, z];
                    out3d[x, y, c.StartZ + z] = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
                }
            }
            return out3d;
        }

        private Task OffloadChunkAsync(WaveFieldChunk chunk)
        {
            if (!chunk.IsInMemory || string.IsNullOrEmpty(_params.OffloadDirectory))
                return Task.CompletedTask;

            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(chunk.FilePath))
                    chunk.FilePath = Path.Combine(_params.OffloadDirectory, $"chunk_{chunk.StartZ}_{Guid.NewGuid()}.tmp");

                try
                {
                    using (var writer = new BinaryWriter(File.Create(chunk.FilePath)))
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

                    chunk.Vx = null; chunk.Vy = null; chunk.Vz = null;
                    chunk.Sxx = null; chunk.Syy = null; chunk.Szz = null;
                    chunk.Sxy = null; chunk.Sxz = null; chunk.Syz = null;
                    chunk.Damage = null;
                    chunk.MaxAbsVx = null; chunk.MaxAbsVy = null; chunk.MaxAbsVz = null;
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
            if (chunk.IsInMemory || string.IsNullOrEmpty(chunk.FilePath) || !File.Exists(chunk.FilePath))
                return Task.CompletedTask;

            return Task.Run(() =>
            {
                try
                {
                    int d = chunk.EndZ - chunk.StartZ;
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
                    File.Delete(chunk.FilePath);
                    chunk.FilePath = null;
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

        public void Dispose()
        {
            if (_useGPU) CleanupOpenCL();
            
            foreach (var chunk in _chunks.Where(c => !string.IsNullOrEmpty(c.FilePath)))
            {
                try { if (File.Exists(chunk.FilePath)) File.Delete(chunk.FilePath); }
                catch (Exception ex) { Logger.LogWarning($"[ChunkedSimulator] Cleanup failed: {ex.Message}"); }
            }
            _chunks.Clear();
        }

        private void CleanupOpenCL()
        {
            ReleaseChunkBuffers();
            if (_kernelVelocity != 0) { _cl.ReleaseKernel(_kernelVelocity); _kernelVelocity = 0; }
            if (_kernelStress != 0) { _cl.ReleaseKernel(_kernelStress); _kernelStress = 0; }
            if (_program != 0) { _cl.ReleaseProgram(_program); _program = 0; }
            if (_queue != 0) { _cl.ReleaseCommandQueue(_queue); _queue = 0; }
            if (_context != 0) { _cl.ReleaseContext(_context); _context = 0; }
        }

        private void ReleaseChunkBuffers()
        {
            if (_bufMat != 0) { _cl.ReleaseMemObject(_bufMat); _bufMat = 0; }
            if (_bufDen != 0) { _cl.ReleaseMemObject(_bufDen); _bufDen = 0; }
            if (_bufDmg != 0) { _cl.ReleaseMemObject(_bufDmg); _bufDmg = 0; }
            if (_bufYoungsModulus != 0) { _cl.ReleaseMemObject(_bufYoungsModulus); _bufYoungsModulus = 0; }
            if (_bufPoissonRatio != 0) { _cl.ReleaseMemObject(_bufPoissonRatio); _bufPoissonRatio = 0; }
            for (int i = 0; i < 3; i++) if (_bufVel[i] != 0) { _cl.ReleaseMemObject(_bufVel[i]); _bufVel[i] = 0; }
            for (int i = 0; i < 3; i++) if (_bufMaxVel[i] != 0) { _cl.ReleaseMemObject(_bufMaxVel[i]); _bufMaxVel[i] = 0; }
            for (int i = 0; i < 6; i++) if (_bufStr[i] != 0) { _cl.ReleaseMemObject(_bufStr[i]); _bufStr[i] = 0; }
            _lastDeviceBytes = 0;
        }

        private string GetKernelSource()
        {
            return @"
    #define M_PI_F 3.14159265358979323846f

    __kernel void updateStress(
        __global const uchar* material, 
        __global const float* density,
        __global float* vx, __global float* vy, __global float* vz,
        __global float* sxx, __global float* syy, __global float* szz,
        __global float* sxy, __global float* sxz, __global float* syz,
        __global float* damage, 
        __global const float* youngsModulus,
        __global const float* poissonRatio,
        const float dt, const float dx,
        const int width, const int height, const int depth, 
        const uchar selectedMaterial,
        const float damageRatePerSec,
        const float confiningPressureMPa,
        const float cohesionMPa,
        const float failureAngleDeg,
        const int usePlasticModel,
        const int useBrittleModel,
        const float sourceValue,
        const int applySource,
        const int srcX, const int srcY, const int srcZ_local,
        const int isFullFace,
        const int sourceAxis
    )
    {
        int idx = get_global_id(0); 
        if (idx >= width * height * depth) return;
        
        uchar mat = material[idx];
        
        // CRITICAL: Only process voxels of selected material(s)
        // Waves only propagate through chosen materials
        if (mat != selectedMaterial || density[idx] <= 0.0f) return;
        
        int z = idx / (width * height);
        int rem = idx % (width * height);
        int y = rem / width;
        int x = rem % width;
        
        if (applySource != 0) {
            if (isFullFace != 0) {
                if (sourceAxis == 0 && x < 2) sxx[idx] += sourceValue;
                if (sourceAxis == 1 && y < 2) syy[idx] += sourceValue;
                if (sourceAxis == 2 && z < 2) szz[idx] += sourceValue;
            } else {
                float dist_sq = (float)((x-srcX)*(x-srcX) + (y-srcY)*(y-srcY) + (z-srcZ_local)*(z-srcZ_local));
                if (dist_sq < 9.1f) {
                    float falloff = exp(-dist_sq / 4.5f);
                    float localSource = sourceValue * falloff;
                    sxx[idx] += localSource;
                    syy[idx] += localSource;
                    szz[idx] += localSource;
                }
            }
        }
        
        if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;
        
        float E = youngsModulus[idx] * 1e6f;
        float nu = poissonRatio[idx];
        
        // Skip if material properties are invalid
        if (E <= 0.0f || nu <= -1.0f || nu >= 0.5f) return;
        
        float mu = E / (2.0f * (1.0f + nu));
        float lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));

        int xp1 = idx + 1; int xm1 = idx - 1;
        int yp1 = idx + width; int ym1 = idx - width;
        int zp1 = idx + width * height; int zm1 = idx - width * height;
        
        if (zp1 >= width * height * depth || zm1 < 0) return;
        
        float dvx_dx = (vx[xp1] - vx[xm1]) / (2.0f * dx);
        float dvy_dy = (vy[yp1] - vy[ym1]) / (2.0f * dx);
        float dvz_dz = (vz[zp1] - vz[zm1]) / (2.0f * dx);
        float dvy_dx = (vy[xp1] - vy[xm1]) / (2.0f * dx);
        float dvx_dy = (vx[yp1] - vx[ym1]) / (2.0f * dx);
        float dvz_dx = (vz[xp1] - vz[xm1]) / (2.0f * dx);
        float dvx_dz = (vx[zp1] - vx[zm1]) / (2.0f * dx);
        float dvz_dy = (vz[yp1] - vz[ym1]) / (2.0f * dx);
        float dvy_dz = (vy[zp1] - vy[zm1]) / (2.0f * dx);
        
        float volumetric_strain = dvx_dx + dvy_dy + dvz_dz;
        float damage_factor = (1.0f - damage[idx] * 0.5f);
        
        float sxx_new = sxx[idx] + dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvx_dx);
        float syy_new = syy[idx] + dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvy_dy);
        float szz_new = szz[idx] + dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvz_dz);
        float sxy_new = sxy[idx] + dt * damage_factor * mu * (dvy_dx + dvx_dy);
        float sxz_new = sxz[idx] + dt * damage_factor * mu * (dvz_dx + dvx_dz);
        float syz_new = syz[idx] + dt * damage_factor * mu * (dvz_dy + dvy_dz);

        if (usePlasticModel != 0) {
            float mean = (sxx_new + syy_new + szz_new) / 3.0f;
            float dev_xx = sxx_new - mean;
            float dev_yy = syy_new - mean;
            float dev_zz = szz_new - mean;

            float J2 = 0.5f * (dev_xx*dev_xx + dev_yy*dev_yy + dev_zz*dev_zz) + sxy_new*sxy_new + sxz_new*sxz_new + syz_new*syz_new;
            float tau = sqrt(J2);

            float sinPhi = sin(failureAngleDeg * M_PI_F / 180.0f);
            float cosPhi = cos(failureAngleDeg * M_PI_F / 180.0f);
            float cohesionPa = cohesionMPa * 1e6f;
            float p = -mean + confiningPressureMPa * 1e6f;

            float yield = tau + p * sinPhi - cohesionPa * cosPhi;

            if (yield > 0 && tau > 1e-10f) {
                float scale = (tau - (cohesionPa * cosPhi - p * sinPhi)) / tau;
                scale = fmin(scale, 0.95f);
                
                sxx_new = (dev_xx * (1 - scale)) + mean;
                syy_new = (dev_yy * (1 - scale)) + mean;
                szz_new = (dev_zz * (1 - scale)) + mean;
                sxy_new *= (1 - scale);
                sxz_new *= (1 - scale);
                syz_new *= (1 - scale);
            }
        }

        sxx[idx] = sxx_new; syy[idx] = syy_new; szz[idx] = szz_new;
        sxy[idx] = sxy_new; sxz[idx] = sxz_new; syz[idx] = syz_new;

        float stress_limit = 1e9f;
        sxx[idx] = clamp(sxx[idx], -stress_limit, stress_limit);
        syy[idx] = clamp(syy[idx], -stress_limit, stress_limit);
        szz[idx] = clamp(szz[idx], -stress_limit, stress_limit);
        sxy[idx] = clamp(sxy[idx], -stress_limit, stress_limit);
        sxz[idx] = clamp(sxz[idx], -stress_limit, stress_limit);
        syz[idx] = clamp(syz[idx], -stress_limit, stress_limit);
        
        if (useBrittleModel != 0) {
            float cohesionPa_d = cohesionMPa * 1e6f;
            float failureAngleRad_d = failureAngleDeg * M_PI_F / 180.0f;
            float sinPhi_d = sin(failureAngleRad_d);
            float cosPhi_d = cos(failureAngleRad_d);
            float ucsPa_d = (2.0f * cohesionPa_d * cosPhi_d) / (1.0f - sinPhi_d);
            float tensileLimitPa_d = ucsPa_d / 10.0f;

            float mean_final = (sxx[idx] + syy[idx] + szz[idx]) / 3.0f;
            float p_final = -mean_final + confiningPressureMPa * 1e6f;
            float dev_xx_final = sxx[idx] - mean_final;
            float dev_yy_final = syy[idx] - mean_final;
            float dev_zz_final = szz[idx] - mean_final;
            float J2_final = 0.5f * (dev_xx_final*dev_xx_final + dev_yy_final*dev_yy_final + dev_zz_final*dev_zz_final) + sxy[idx]*sxy[idx] + sxz[idx]*sxz[idx] + syz[idx]*syz[idx];
            float tau_final = sqrt(J2_final);
            float yieldShear_final = tau_final + p_final * sinPhi_d - cohesionPa_d * cosPhi_d;

            float tensile_max = fmax(sxx[idx], fmax(syy[idx], szz[idx]));
            
            float damage_increment = 0.0f;
            if (yieldShear_final > 0 && cohesionPa_d > 0) {
                damage_increment += damageRatePerSec * dt * (yieldShear_final / (cohesionPa_d * cosPhi_d));
            }
            if (tensile_max > tensileLimitPa_d && tensileLimitPa_d > 0) {
                damage_increment += damageRatePerSec * dt * (tensile_max / tensileLimitPa_d - 1.0f);
            }

            if(damage_increment > 0) {
                damage[idx] = clamp(damage[idx] + damage_increment, 0.0f, 0.9f);
            }
        }
    }
    
    __kernel void updateVelocity(
        __global const uchar* material, 
        __global const float* density,
        __global float* vx, __global float* vy, __global float* vz,
        __global const float* sxx, __global const float* syy, __global const float* szz,
        __global const float* sxy, __global const float* sxz, __global const float* syz,
        __global float* max_vx, __global float* max_vy, __global float* max_vz,
        const float dt, const float dx, 
        const int width, const int height, const int depth, 
        const uchar selectedMaterial)
    {
        int idx = get_global_id(0);
        if (idx >= width * height * depth) return;
        
        uchar mat = material[idx];
        
        // CRITICAL: Only process voxels of selected material(s)
        if (mat != selectedMaterial || density[idx] <= 0.0f) return;

        int z = idx / (width * height);
        int rem = idx % (width * height);
        int y = rem / width;
        int x = rem % width;
        
        if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;
        
        int xp1 = idx + 1; int xm1 = idx - 1;
        int yp1 = idx + width; int ym1 = idx - width;
        int zp1 = idx + width * height; int zm1 = idx - width * height;
        
        if (zp1 >= width * height * depth || zm1 < 0) return;
        
        float rho = fmax(100.0f, density[idx]);
        
        float dsxx_dx = (sxx[xp1] - sxx[xm1]) / (2.0f * dx);
        float dsyy_dy = (syy[yp1] - syy[ym1]) / (2.0f * dx);
        float dszz_dz = (szz[zp1] - szz[zm1]) / (2.0f * dx);
        float dsxy_dy = (sxy[yp1] - sxy[ym1]) / (2.0f * dx);
        float dsxy_dx = (sxy[xp1] - sxy[xm1]) / (2.0f * dx);
        float dsxz_dz = (sxz[zp1] - sxz[zm1]) / (2.0f * dx);
        float dsxz_dx = (sxz[xp1] - sxz[xm1]) / (2.0f * dx);
        float dsyz_dz = (syz[zp1] - syz[zm1]) / (2.0f * dx);
        float dsyz_dy = (syz[yp1] - syz[ym1]) / (2.0f * dx);
        
        const float damping = 0.999f;
        
        vx[idx] = vx[idx] * damping + dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
        vy[idx] = vy[idx] * damping + dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
        vz[idx] = vz[idx] * damping + dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
        
        float max_velocity = 10000.0f;
        vx[idx] = clamp(vx[idx], -max_velocity, max_velocity);
        vy[idx] = clamp(vy[idx], -max_velocity, max_velocity);
        vz[idx] = clamp(vz[idx], -max_velocity, max_velocity);

        max_vx[idx] = fmax(max_vx[idx], fabs(vx[idx]));
        max_vy[idx] = fmax(max_vy[idx], fabs(vy[idx]));
        max_vz[idx] = fmax(max_vz[idx], fabs(vz[idx]));
    }";
        }
    }
}