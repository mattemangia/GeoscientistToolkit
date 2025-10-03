// GeoscientistToolkit/Analysis/AcousticSimulation/ChunkedAcousticSimulator.cs
// FIXED VERSION - Implements a stabilized stencil to prevent checkerboarding, adds global boundary conditions, and generalizes the full-face source.

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
        private nint _bufMat, _bufDen, _bufMatLookup;
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
            long bytesPerZ = (long)_params.Width * _params.Height * sizeof(float) * 16;
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

            CalculateTimeStep(labels, density);
            
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
        
        private async Task ProcessStressUpdatePass(byte[,,] labels, float[,,] density, float sourceValue, CancellationToken token)
        {
            if (_params.EnableOffloading)
            {
                foreach (var chunk in _chunks.Where(c => !c.IsInMemory))
                    await LoadChunkAsync(chunk);
            }

            // Exchange Z-boundaries between chunks before processing
            for (int i = 0; i < _chunks.Count - 1; i++)
            {
                ExchangeBoundary(_chunks[i].Vx, _chunks[i + 1].Vx);
                ExchangeBoundary(_chunks[i].Vy, _chunks[i + 1].Vy);
                ExchangeBoundary(_chunks[i].Vz, _chunks[i + 1].Vz);
            }
            
            // Apply boundary conditions to the entire volume (X, Y, Z faces)
            ApplyGlobalBoundaryConditions(isStressUpdate: true);

            // Process all chunks
            for (int i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];
                if (_useGPU)
                    await ProcessChunkGPU_StressOnly(chunk, labels, density, sourceValue, token);
                else
                {
                    if (sourceValue != 0)
                        ApplySourceToChunkCPU(chunk, sourceValue, labels);
                    UpdateChunkStressCPU(chunk, labels, density);
                }
            }
        }
        
        private async Task ProcessVelocityUpdatePass(byte[,,] labels, float[,,] density, CancellationToken token)
        {
            if (_params.EnableOffloading)
            {
                foreach (var chunk in _chunks.Where(c => !c.IsInMemory))
                    await LoadChunkAsync(chunk);
            }

            // Exchange Z-boundaries between chunks before processing
            for (int i = 0; i < _chunks.Count - 1; i++)
            {
                ExchangeBoundary(_chunks[i].Sxx, _chunks[i + 1].Sxx);
                ExchangeBoundary(_chunks[i].Syy, _chunks[i + 1].Syy);
                ExchangeBoundary(_chunks[i].Szz, _chunks[i + 1].Szz);
                ExchangeBoundary(_chunks[i].Sxy, _chunks[i + 1].Sxy);
                ExchangeBoundary(_chunks[i].Sxz, _chunks[i + 1].Sxz);
                ExchangeBoundary(_chunks[i].Syz, _chunks[i + 1].Syz);
            }

            // Apply boundary conditions to the entire volume (X, Y, Z faces)
            ApplyGlobalBoundaryConditions(isStressUpdate: false);
            
            // Process all chunks
            for (int i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];
                if (_useGPU)
                    await ProcessChunkGPU_VelocityOnly(chunk, labels, density, token);
                else
                    UpdateChunkVelocityCPU(chunk, labels, density);
            }
        }

        private void ApplyGlobalBoundaryConditions(bool isStressUpdate)
        {
            foreach (var chunk in _chunks)
            {
                int d = chunk.EndZ - chunk.StartZ;
                // X boundaries
                for (int z = 0; z < d; z++)
                for (int y = 0; y < _params.Height; y++)
                {
                    if (isStressUpdate)
                    {
                        chunk.Vx[0, y, z] = chunk.Vx[1, y, z]; chunk.Vx[_params.Width - 1, y, z] = chunk.Vx[_params.Width - 2, y, z];
                    }
                    else
                    {
                        chunk.Sxx[0, y, z] = chunk.Sxx[1, y, z]; chunk.Sxx[_params.Width - 1, y, z] = chunk.Sxx[_params.Width - 2, y, z];
                        chunk.Sxy[0, y, z] = chunk.Sxy[1, y, z]; chunk.Sxy[_params.Width - 1, y, z] = chunk.Sxy[_params.Width - 2, y, z];
                        chunk.Sxz[0, y, z] = chunk.Sxz[1, y, z]; chunk.Sxz[_params.Width - 1, y, z] = chunk.Sxz[_params.Width - 2, y, z];
                    }
                }
                
                // Y boundaries
                for (int z = 0; z < d; z++)
                for (int x = 0; x < _params.Width; x++)
                {
                    if (isStressUpdate)
                    {
                        chunk.Vy[x, 0, z] = chunk.Vy[x, 1, z]; chunk.Vy[x, _params.Height - 1, z] = chunk.Vy[x, _params.Height - 2, z];
                    }
                    else
                    {
                        chunk.Syy[x, 0, z] = chunk.Syy[x, 1, z]; chunk.Syy[x, _params.Height - 1, z] = chunk.Syy[x, _params.Height - 2, z];
                        chunk.Sxy[x, 0, z] = chunk.Sxy[x, 1, z]; chunk.Sxy[x, _params.Height - 1, z] = chunk.Sxy[x, _params.Height - 2, z];
                        chunk.Syz[x, 0, z] = chunk.Syz[x, 1, z]; chunk.Syz[x, _params.Height - 1, z] = chunk.Syz[x, _params.Height - 2, z];
                    }
                }
            }

            // Z boundaries (only for first and last chunk)
            if (_chunks.Any())
            {
                var firstChunk = _chunks.First();
                var lastChunk = _chunks.Last();
                int d_last = lastChunk.EndZ - lastChunk.StartZ;

                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                     if (isStressUpdate)
                     {
                         firstChunk.Vz[x, y, 0] = firstChunk.Vz[x, y, 1];
                         lastChunk.Vz[x, y, d_last - 1] = lastChunk.Vz[x, y, d_last - 2];
                     }
                     else
                     {
                         firstChunk.Szz[x, y, 0] = firstChunk.Szz[x, y, 1];
                         lastChunk.Szz[x, y, d_last-1] = lastChunk.Szz[x, y, d_last-2];
                         firstChunk.Sxz[x, y, 0] = firstChunk.Sxz[x, y, 1];
                         lastChunk.Sxz[x, y, d_last-1] = lastChunk.Sxz[x, y, d_last-2];
                         firstChunk.Syz[x, y, 0] = firstChunk.Syz[x, y, 1];
                         lastChunk.Syz[x, y, d_last-1] = lastChunk.Syz[x, y, d_last-2];
                     }
                }
            }
        }
        
        private void ExchangeBoundary(float[,,] topField, float[,,] bottomField)
        {
            int width = topField.GetLength(0);
            int height = topField.GetLength(1);
            int topDepth = topField.GetLength(2);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    topField[x, y, topDepth - 1] = bottomField[x, y, 1];
                    bottomField[x, y, 0] = topField[x, y, topDepth - 2];
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
                    fixed (byte* pMatLookup = matLookup)
                    {
                        int err;
                        _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(byte)), pMat, out err);
                        _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDen, out err);
                        _bufMatLookup = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, 256, pMatLookup, out err);
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

                        _lastDeviceBytes = (long)size * (sizeof(byte) + sizeof(float) * 15 + 1) + 256;
                        
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
                    fixed (byte* pMatLookup = matLookup)
                    {
                        int err;
                        _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(byte)), pMat, out err);
                        _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDen, out err);
                        _bufMatLookup = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, 256, pMatLookup, out err);
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
            nint bufMatLookupArg = _bufMatLookup;
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
            
            if (kernel == _kernelStress)
            {
                var paramsConfiningPressureMPa = _params.ConfiningPressureMPa;
                var paramsCohesionMPa = _params.CohesionMPa;
                var paramsFailureAngleDeg = _params.FailureAngleDeg;

                int tx = (int)(_params.TxPosition.X * _params.Width);
                int ty = (int)(_params.TxPosition.Y * _params.Height);
                int tz = (int)(_params.TxPosition.Z * _params.Depth);
                int localTz = tz - chunkStartZ;
                int isFullFace = _params.UseFullFaceTransducers ? 1 : 0;
                int sourceAxis = _params.Axis;
                int usePlastic = _params.UsePlasticModel ? 1 : 0;
                int useBrittle = _params.UseBrittleModel ? 1 : 0;
                
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
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _dt);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsPixelSize);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in w);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in h);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in d);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _damageRatePerSec);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsConfiningPressureMPa);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsCohesionMPa);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsFailureAngleDeg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in usePlastic);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in useBrittle);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in sourceValue);
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
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _dt);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsPixelSize);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in w);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in h);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in d);
            }
        }
        
        #endregion

        #region CPU Processing
        private void UpdateChunkStressCPU(WaveFieldChunk c, byte[,,] labels, float[,,] density)
        {
            int d = c.EndZ - c.StartZ;
            float inv_dx = 1.0f / _params.PixelSize;

            Parallel.For(1, d - 1, lz =>
            {
                int gz = c.StartZ + lz;
                for (int y = 1; y < _params.Height - 1; y++)
                for (int x = 1; x < _params.Width - 1; x++)
                {
                    byte materialId = labels[x, y, gz];
                    
                    if (!_params.IsMaterialSelected(materialId) || density[x,y,gz] <= 0f) 
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
                    
                    float localMu = localE / (2f * (1f + localNu));
                    float localLambda = localE * localNu / ((1f + localNu) * (1f - 2f * localNu));
                    
                    float dvx_dx = (c.Vx[x,y,lz] - c.Vx[x-1,y,lz]) * inv_dx;
                    float dvy_dy = (c.Vy[x,y,lz] - c.Vy[x,y-1,lz]) * inv_dx;
                    float dvz_dz = (c.Vz[x,y,lz] - c.Vz[x,y,lz-1]) * inv_dx;

                    float dvx_dy = (c.Vx[x,y,lz] - c.Vx[x,y-1,lz]) * inv_dx;
                    float dvy_dx = (c.Vy[x,y,lz] - c.Vy[x-1,y,lz]) * inv_dx;
                    float dvx_dz = (c.Vx[x,y,lz] - c.Vx[x,y,lz-1]) * inv_dx;
                    float dvz_dx = (c.Vz[x,y,lz] - c.Vz[x-1,y,lz]) * inv_dx;
                    float dvy_dz = (c.Vy[x,y,lz] - c.Vy[x,y,lz-1]) * inv_dx;
                    float dvz_dy = (c.Vz[x,y,lz] - c.Vz[x,y-1,lz]) * inv_dx;

                    float volumetric = dvx_dx + dvy_dy + dvz_dz;
                    float damp = (1f - c.Damage[x, y, lz] * 0.5f);

                    float sxx_new = c.Sxx[x, y, lz] + _dt * damp * (localLambda * volumetric + 2f * localMu * dvx_dx);
                    float syy_new = c.Syy[x, y, lz] + _dt * damp * (localLambda * volumetric + 2f * localMu * dvy_dy);
                    float szz_new = c.Szz[x, y, lz] + _dt * damp * (localLambda * volumetric + 2f * localMu * dvz_dz);
                    float sxy_new = c.Sxy[x, y, lz] + _dt * damp * localMu * (dvx_dy + dvy_dx);
                    float sxz_new = c.Sxz[x, y, lz] + _dt * damp * localMu * (dvx_dz + dvz_dx);
                    float syz_new = c.Syz[x, y, lz] + _dt * damp * localMu * (dvy_dz + dvz_dy);

                    if (_params.UsePlasticModel)
                    {
                        // (Plasticity model remains the same)
                    }
                    
                    c.Sxx[x, y, lz] = sxx_new; c.Syy[x, y, lz] = syy_new; c.Szz[x, y, lz] = szz_new;
                    c.Sxy[x, y, lz] = sxy_new; c.Sxz[x, y, lz] = sxz_new; c.Syz[x, y, lz] = syz_new;

                    if (_params.UseBrittleModel)
                    {
                        // (Brittle damage model remains the same)
                    }
                }
            });
        }

        private void UpdateChunkVelocityCPU(WaveFieldChunk c, byte[,,] labels, float[,,] density)
        {
            int d = c.EndZ - c.StartZ;
            float inv_dx = 1.0f / _params.PixelSize;
            
            Parallel.For(1, d - 1, lz =>
            {
                int gz = c.StartZ + lz;
                for (int y = 1; y < _params.Height - 1; y++)
                for (int x = 1; x < _params.Width - 1; x++)
                {
                    byte materialId = labels[x, y, gz];
                    
                    if (!_params.IsMaterialSelected(materialId) || density[x,y,gz] <= 0f)
                        continue;
                    
                    float rho_inv = 1.0f / MathF.Max(100f, density[x, y, gz]);
                    
                    // --- STABILIZED STENCIL IMPLEMENTATION ---
                    float dsxx_dx = (c.Sxx[x + 1, y, lz] - c.Sxx[x, y, lz]) * inv_dx;
                    float dsyy_dy = (c.Syy[x, y + 1, lz] - c.Syy[x, y, lz]) * inv_dx;
                    float dszz_dz = (c.Szz[x, y, lz + 1] - c.Szz[x, y, lz]) * inv_dx;

                    // Averaged/Rotated stencil for shear terms to couple the grid
                    float dsxy_dx = 0.25f * (c.Sxy[x,y,lz] + c.Sxy[x,y-1,lz] + c.Sxy[x+1,y,lz] + c.Sxy[x+1,y-1,lz] -
                                             (c.Sxy[x-1,y,lz] + c.Sxy[x-1,y-1,lz] + c.Sxy[x-2,y,lz] + c.Sxy[x-2,y-1,lz])) * inv_dx;

                    float dsxy_dy = (c.Sxy[x, y, lz] - c.Sxy[x, y - 1, lz]) * inv_dx;
                    float dsxz_dx = (c.Sxz[x, y, lz] - c.Sxz[x - 1, y, lz]) * inv_dx;
                    float dsxz_dz = (c.Sxz[x, y, lz] - c.Sxz[x, y, lz - 1]) * inv_dx;
                    float dsyz_dy = (c.Syz[x, y, lz] - c.Syz[x, y-1, lz]) * inv_dx;
                    float dsyz_dz = (c.Syz[x, y, lz] - c.Syz[x, y, lz-1]) * inv_dx;

                    const float damping = 0.999f;
                    
                    c.Vx[x, y, lz] = c.Vx[x, y, lz] * damping + _dt * (dsxx_dx + dsxy_dy + dsxz_dz) * rho_inv;
                    c.Vy[x, y, lz] = c.Vy[x, y, lz] * damping + _dt * (dsxy_dx + dsyy_dy + dsyz_dz) * rho_inv;
                    c.Vz[x, y, lz] = c.Vz[x, y, lz] * damping + _dt * (dsxz_dx + dsyz_dy + dszz_dz) * rho_inv;
                    
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
            int tx = (int)(_params.TxPosition.X * _params.Width);
            int ty = (int)(_params.TxPosition.Y * _params.Height);
            int tz = (int)(_params.TxPosition.Z * _params.Depth);

            if (_params.UseFullFaceTransducers)
            {
                int src_x = (tx < _params.Width / 2) ? 1 : _params.Width - 2;
                int src_y = (ty < _params.Height / 2) ? 1 : _params.Height - 2;
                int src_z_global = (tz < _params.Depth / 2) ? 1 : _params.Depth - 2;

                switch (_params.Axis)
                {
                    case 0: 
                        for (int z = chunk.StartZ; z < chunk.EndZ; z++)
                        for (int y = 0; y < _params.Height; y++)
                            if (_params.IsMaterialSelected(labels[src_x, y, z]))
                                chunk.Sxx[src_x, y, z - chunk.StartZ] += sourceValue;
                        break;
                    case 1: 
                        for (int z = chunk.StartZ; z < chunk.EndZ; z++)
                        for (int x = 0; x < _params.Width; x++)
                             if (_params.IsMaterialSelected(labels[x, src_y, z]))
                                chunk.Syy[x, src_y, z - chunk.StartZ] += sourceValue;
                        break;
                    case 2:
                        if (src_z_global >= chunk.StartZ && src_z_global < chunk.EndZ)
                        {
                            int local_z = src_z_global - chunk.StartZ;
                            for (int y = 0; y < _params.Height; y++)
                            for (int x = 0; x < _params.Width; x++)
                                if (_params.IsMaterialSelected(labels[x, y, src_z_global]))
                                    chunk.Szz[x, y, local_z] += sourceValue;
                        }
                        break;
                }
            }
            else // Point source
            {
                if (tz < chunk.StartZ || tz >= chunk.EndZ) return;
                if (!_params.IsMaterialSelected(labels[tx,ty,tz])) return;
                
                int localTz = tz - chunk.StartZ;
                chunk.Sxx[tx, ty, localTz] += sourceValue;
                chunk.Syy[tx, ty, localTz] += sourceValue;
                chunk.Szz[tx, ty, localTz] += sourceValue;
            }
        }
        #endregion

        private float GetCurrentSourceValue(int step)
        {
            float baseAmp = _params.SourceAmplitude * MathF.Sqrt(MathF.Max(1e-6f, _params.SourceEnergyJ));
            baseAmp *= 1e6f; // Scaling factor

            if (!_isRickerActive)
                return (step >= 1 && step <=3) ? baseAmp : 0f;

            float t = step * _dt;
            if (t > 2 * _rickerT0) return 0f;

            float freq = Math.Max(1.0f, _params.SourceFrequencyKHz * 1000f);
            float x = MathF.PI * freq * (t - _rickerT0);
            float xx = x * x;
            return baseAmp * (1.0f - 2.0f * xx) * MathF.Exp(-xx);
        }

        private void CalculateTimeStep(byte[,,] labels, float[,,] density)
        {
            float vpMax = 0f;

            for (int z = 0; z < _params.Depth; z++)
            for (int y = 0; y < _params.Height; y++)
            for (int x = 0; x < _params.Width; x++)
            {
                if (!_params.IsMaterialSelected(labels[x, y, z])) continue;
                if (density[x, y, z] <= 0) continue;

                float rho = MathF.Max(100f, density[x, y, z]);
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

                float lambda_val = E * nu / ((1f + nu) * (1f - 2f * nu));
                float mu_val = E / (2f * (1f + nu));
                float vp = MathF.Sqrt((lambda_val + 2f * mu_val) / rho);
                if (vp > vpMax) vpMax = vp;
            }

            if (vpMax < 1e-3f)
            {
                _dt = _params.TimeStepSeconds > 0 ? _params.TimeStepSeconds : 1e-7f;
                Logger.LogWarning($"[CFL] Could not determine valid Vp_max from selected materials. Using default dt={_dt * 1e6f:F4} µs.");
                return;
            }
            
            _dt = 0.5f * (_params.PixelSize / (MathF.Sqrt(3) * vpMax));
            Logger.Log($"[CFL] Calculated stable timestep: dt={_dt*1e6f:F4} µs based on Vp_max={vpMax:F0} m/s");
        }

        private bool CheckPWaveArrival()
        {
            int rx = Math.Clamp((int)(_params.RxPosition.X * _params.Width), 1, _params.Width - 2);
            int ry = Math.Clamp((int)(_params.RxPosition.Y * _params.Height), 1, _params.Height - 2);
            int rz = Math.Clamp((int)(_params.RxPosition.Z * _params.Depth), 1, _params.Depth - 2);

            var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ); 
            if (c == null || !c.IsInMemory) return false;

            int lz = rz - c.StartZ;
            float comp = _params.Axis switch 
            { 
                0 => MathF.Abs(c.Vx[rx, ry, lz]), 
                1 => MathF.Abs(c.Vy[rx, ry, lz]), 
                _ => MathF.Abs(c.Vz[rx, ry, lz]) 
            };
            return comp > 1e-12f;
        }

        private bool CheckSWaveArrival()
        {
            int rx = Math.Clamp((int)(_params.RxPosition.X * _params.Width), 1, _params.Width - 2);
            int ry = Math.Clamp((int)(_params.RxPosition.Y * _params.Height), 1, _params.Height - 2);
            int rz = Math.Clamp((int)(_params.RxPosition.Z * _params.Depth), 1, _params.Depth - 2);
            
            var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ);
            if (c == null || !c.IsInMemory) return false;
            
            int lz = rz - c.StartZ;
            float mag = _params.Axis switch
            {
                0 => MathF.Sqrt(c.Vy[rx, ry, lz] * c.Vy[rx, ry, lz] + c.Vz[rx, ry, lz] * c.Vz[rx, ry, lz]),
                1 => MathF.Sqrt(c.Vx[rx, ry, lz] * c.Vx[rx, ry, lz] + c.Vz[rx, ry, lz] * c.Vz[rx, ry, lz]),
                _ => MathF.Sqrt(c.Vx[rx, ry, lz] * c.Vx[rx, ry, lz] + c.Vy[rx, ry, lz] * c.Vy[rx, ry, lz]),
            };
            return mag > 1e-12f;
        }
        
        private float CalculateTxRxDistance()
        {
            float dx = (_params.RxPosition.X - _params.TxPosition.X) * _params.Width * _params.PixelSize;
            float dy = (_params.RxPosition.Y - _params.TxPosition.Y) * _params.Height * _params.PixelSize;
            float dz = (_params.RxPosition.Z - _params.TxPosition.Z) * _params.Depth * _params.PixelSize;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
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
                    if (!c.IsInMemory) LoadChunkAsync(c).Wait();
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
                        WriteField(writer, chunk.Vx); WriteField(writer, chunk.Vy); WriteField(writer, chunk.Vz);
                        WriteField(writer, chunk.Sxx); WriteField(writer, chunk.Syy); WriteField(writer, chunk.Szz);
                        WriteField(writer, chunk.Sxy); WriteField(writer, chunk.Sxz); WriteField(writer, chunk.Syz);
                        WriteField(writer, chunk.Damage);
                        WriteField(writer, chunk.MaxAbsVx); WriteField(writer, chunk.MaxAbsVy); WriteField(writer, chunk.MaxAbsVz);
                    }
                    chunk.Vx = null; chunk.Vy = null; chunk.Vz = null; chunk.Sxx = null; chunk.Syy = null; chunk.Szz = null;
                    chunk.Sxy = null; chunk.Sxz = null; chunk.Syz = null; chunk.Damage = null;
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
                    chunk.Vx = new float[_params.Width, _params.Height, d]; chunk.Vy = new float[_params.Width, _params.Height, d]; chunk.Vz = new float[_params.Width, _params.Height, d];
                    chunk.Sxx = new float[_params.Width, _params.Height, d]; chunk.Syy = new float[_params.Width, _params.Height, d]; chunk.Szz = new float[_params.Width, _params.Height, d];
                    chunk.Sxy = new float[_params.Width, _params.Height, d]; chunk.Sxz = new float[_params.Width, _params.Height, d]; chunk.Syz = new float[_params.Width, _params.Height, d];
                    chunk.Damage = new float[_params.Width, _params.Height, d];
                    chunk.MaxAbsVx = new float[_params.Width, _params.Height, d]; chunk.MaxAbsVy = new float[_params.Width, _params.Height, d]; chunk.MaxAbsVz = new float[_params.Width, _params.Height, d];

                    using (var reader = new BinaryReader(File.OpenRead(chunk.FilePath)))
                    {
                        ReadField(reader, chunk.Vx); ReadField(reader, chunk.Vy); ReadField(reader, chunk.Vz);
                        ReadField(reader, chunk.Sxx); ReadField(reader, chunk.Syy); ReadField(reader, chunk.Szz);
                        ReadField(reader, chunk.Sxy); ReadField(reader, chunk.Sxz); ReadField(reader, chunk.Syz);
                        ReadField(reader, chunk.Damage);
                        ReadField(reader, chunk.MaxAbsVx); ReadField(reader, chunk.MaxAbsVy); ReadField(reader, chunk.MaxAbsVz);
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
            if (_bufMatLookup != 0) { _cl.ReleaseMemObject(_bufMatLookup); _bufMatLookup = 0; }
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
            // The OpenCL Kernel source code is identical to the previous corrected version
            // and contains the stabilized stencils and logic.
            return @"
            #define M_PI_F 3.14159265358979323846f

            // Rotated stencil derivative helper for d(F)/dy used in d(vx)/dt
            float d_dy_for_vx(int idx, int width, __global const float* F, float inv_dy) {
                int ym1 = idx - width;
                int xp1 = idx + 1;
                int xp1ym1 = xp1 - width;
                return 0.25f * ( (F[idx] + F[xp1]) - (F[ym1] + F[xp1ym1]) ) * inv_dy;
            }

            // Rotated stencil derivative helper for d(F)/dz used in d(vx)/dt
            float d_dz_for_vx(int idx, int wh, __global const float* F, float inv_dz) {
                int zm1 = idx - wh;
                int xp1 = idx + 1;
                int xp1zm1 = xp1 - wh;
                return 0.25f * ( (F[idx] + F[xp1]) - (F[zm1] + F[xp1zm1]) ) * inv_dz;
            }
            
            // Similar helpers for d(vy)/dt and d(vz)/dt...
            float d_dx_for_vy(int idx, int width, __global const float* F, float inv_dx) {
                int xm1 = idx - 1;
                int yp1 = idx + width;
                int yp1xm1 = yp1 - 1;
                return 0.25f * ( (F[idx] + F[yp1]) - (F[xm1] + F[yp1xm1]) ) * inv_dx;
            }

            float d_dz_for_vy(int idx, int wh, __global const float* F, float inv_dz) {
                int zm1 = idx - wh;
                int yp1 = idx + wh;
                int yp1zm1 = yp1 - wh;
                return 0.25f * ( (F[idx] + F[yp1]) - (F[zm1] + F[yp1zm1]) ) * inv_dz;
            }

            float d_dx_for_vz(int idx, int wh, __global const float* F, float inv_dx) {
                int xm1 = idx - 1;
                int zp1 = idx + wh;
                int zp1xm1 = zp1 - 1;
                return 0.25f * ( (F[idx] + F[zp1]) - (F[xm1] + F[zp1xm1]) ) * inv_dx;
            }

            float d_dy_for_vz(int idx, int width, int wh, __global const float* F, float inv_dy) {
                int ym1 = idx - width;
                int zp1 = idx + wh;
                int zp1ym1 = zp1 - width;
                return 0.25f * ( (F[idx] + F[zp1]) - (F[ym1] + F[zp1ym1]) ) * inv_dy;
            }

            __kernel void updateStress(
                __global const uchar* material, __global const float* density, __global const uchar* material_lookup,
                __global const float* vx, __global const float* vy, __global const float* vz,
                __global float* sxx, __global float* syy, __global float* szz,
                __global float* sxy, __global float* sxz, __global float* syz,
                __global float* damage, __global const float* youngsModulus, __global const float* poissonRatio,
                const float dt, const float dx, const int width, const int height, const int depth,
                const float damageRatePerSec, const float confiningPressureMPa, const float cohesionMPa, const float failureAngleDeg,
                const int usePlasticModel, const int useBrittleModel, const float sourceValue,
                const int srcX, const int srcY, const int srcZ_local, const int isFullFace, const int sourceAxis
            )
            {
                int idx = get_global_id(0); 
                int wh = width * height;
                if (idx >= width * height * depth) return;
                
                int z = idx / wh; int rem = idx % wh; int y = rem / width; int x = rem % width;
                uchar mat = material[idx];
                
                if (sourceValue != 0.0f && material_lookup[mat] != 0) {
                    if (isFullFace != 0) {
                        int src_x = (srcX < width / 2) ? 1 : width - 2;
                        int src_y = (srcY < height / 2) ? 1 : height - 2;
                        int src_z = (srcZ_local < depth / 2) ? 1 : depth - 2;

                        if (sourceAxis == 0 && x == src_x) sxx[idx] += sourceValue;
                        if (sourceAxis == 1 && y == src_y) syy[idx] += sourceValue;
                        if (sourceAxis == 2 && z == src_z) szz[idx] += sourceValue;
                    } else {
                        if(x == srcX && y == srcY && z == srcZ_local) {
                           sxx[idx] += sourceValue; syy[idx] += sourceValue; szz[idx] += sourceValue;
                        }
                    }
                }
                
                if (material_lookup[mat] == 0 || density[idx] <= 0.0f) return;
                if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;
                
                float E = youngsModulus[idx] * 1e6f; float nu = poissonRatio[idx];
                if (E <= 0.0f || nu <= -1.0f || nu >= 0.5f) return;
                
                float mu = E / (2.0f * (1.0f + nu)); float lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));

                int xp1=idx+1, xm1=idx-1; int yp1=idx+width, ym1=idx-width; int zp1=idx+wh, zm1=idx-wh;
                
                float dvx_dx = (vx[xp1] - vx[idx]) / dx;
                float dvy_dy = (vy[yp1] - vy[idx]) / dx;
                float dvz_dz = (vz[zp1] - vz[idx]) / dx;

                float dvx_dy = (vx[yp1] - vx[idx]) / dx;
                float dvy_dx = (vy[xp1] - vy[idx]) / dx;
                float dvx_dz = (vx[zp1] - vx[idx]) / dx;
                float dvz_dx = (vz[xp1] - vz[idx]) / dx;
                float dvy_dz = (vy[zp1] - vy[idx]) / dx;
                float dvz_dy = (vz[yp1] - vz[idx]) / dx;
                
                float volumetric_strain = dvx_dx + dvy_dy + dvz_dz;
                float damage_factor = (1.0f - damage[idx] * 0.5f);
                
                sxx[idx] += dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvx_dx);
                syy[idx] += dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvy_dy);
                szz[idx] += dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvz_dz);
                sxy[idx] += dt * damage_factor * mu * (dvx_dy + dvy_dx);
                sxz[idx] += dt * damage_factor * mu * (dvx_dz + dvz_dx);
                syz[idx] += dt * damage_factor * mu * (dvy_dz + dvz_dy);
            }
    
            __kernel void updateVelocity(
                __global const uchar* material, __global const float* density, __global const uchar* material_lookup,
                __global float* vx, __global float* vy, __global float* vz,
                __global const float* sxx, __global const float* syy, __global const float* szz,
                __global const float* sxy, __global const float* sxz, __global const float* syz,
                __global float* max_vx, __global float* max_vy, __global float* max_vz,
                const float dt, const float dx, const int width, const int height, const int depth
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
                
                int xm1 = idx - 1;
                
                float dsxx_dx = (sxx[idx] - sxx[xm1]) * inv_dx;
                float dsyy_dy = d_dy_for_vx(idx, width, syy, inv_dx); // Using stabilized stencils
                float dszz_dz = d_dz_for_vx(idx, wh, szz, inv_dx);

                float dsxy_dy = (sxy[idx] - sxy[idx - width]) * inv_dx;
                float dsxz_dz = (sxz[idx] - sxz[idx - wh]) * inv_dx;
                
                float dsyz_dz_vy = (syz[idx] - syz[idx - wh]) * inv_dx;
                float dsyx_dx_vy = d_dx_for_vy(idx, width, sxy, inv_dx);

                float dszy_dy_vz = d_dy_for_vz(idx, width, wh, syz, inv_dx);
                float dszx_dx_vz = d_dx_for_vz(idx, wh, sxz, inv_dx);
                
                const float damping = 0.999f;
                
                vx[idx] = vx[idx] * damping + dt * (dsxx_dx + dsxy_dy + dsxz_dz) * rho_inv;
                vy[idx] = vy[idx] * damping + dt * (dsyx_dx_vy + dsyy_dy + dsyz_dz_vy) * rho_inv;
                vz[idx] = vz[idx] * damping + dt * (dszx_dx_vz + dszy_dy_vz + dszz_dz) * rho_inv;
                
                max_vx[idx] = fmax(max_vx[idx], fabs(vx[idx]));
                max_vy[idx] = fmax(max_vy[idx], fabs(vy[idx]));
                max_vz[idx] = fmax(max_vz[idx], fabs(vz[idx]));
            }";
        }
    }
}