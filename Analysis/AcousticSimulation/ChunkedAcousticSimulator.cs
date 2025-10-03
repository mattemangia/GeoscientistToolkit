// GeoscientistToolkit/Analysis/AcousticSimulation/ChunkedAcousticSimulator.cs
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
    /// <summary>
    /// High-performance, chunk-based acoustic simulator with optional GPU acceleration.
    /// This design is memory-efficient for very large datasets.
    /// </summary>
    public class ChunkedAcousticSimulator : IDisposable
    {
        private readonly SimulationParameters _params;
        private long _lastDeviceBytes;

        // Public property for UI to read current memory usage
        public long CurrentMemoryUsageMB
        {
            get
            {
                long managedBytes = GC.GetTotalMemory(false);
                long deviceBytes = Interlocked.Read(ref _lastDeviceBytes);
                return (managedBytes + deviceBytes) / (1024 * 1024);
            }
        }

        private readonly float _lambda, _mu;
        private float _dt;
        private readonly float _tensileLimitPa;
        private readonly float _shearLimitPa;
        private readonly float _damageRatePerSec = 0.2f;
        
        // Ricker wavelet parameters
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
            public bool IsInMemory = true;
            public string FilePath;
        }

        private readonly List<WaveFieldChunk> _chunks = new();
        private readonly int _chunkDepth;

        private readonly bool _useGPU;
        private readonly CL _cl = CL.GetApi();
        private nint _platform;
        private nint _device;
        private nint _context;
        private nint _queue;
        private nint _program;
        private nint _kernelStress;
        private nint _kernelVelocity;

        private nint _bufMat;
        private nint _bufDen;
        private readonly nint[] _bufVel = new nint[3];
        private readonly nint[] _bufStr = new nint[6];
        private nint _bufDmg;
        // --- MODIFICATION: Buffers for per-voxel material properties ---
        private nint _bufYoungsModulus;
        private nint _bufPoissonRatio;


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

            // These are now calculated on-the-fly from Mohr-Coulomb parameters
            _tensileLimitPa = 0; // 0.05f * E;
            _shearLimitPa = 0; // 0.03f * E;

            long targetBytes = (_params.ChunkSizeMB > 0 ? _params.ChunkSizeMB : 256) * 1024L * 1024L;
            long bytesPerZ = (long)_params.Width * _params.Height * sizeof(float) * 10;
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

        private float[,,] _perVoxelYoungsModulus;
        private float[,,] _perVoxelPoissonRatio;
        private bool _usePerVoxelProperties = false;


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
                    Vx = new float[_params.Width, _params.Height, d], Vy = new float[_params.Width, _params.Height, d], Vz = new float[_params.Width, _params.Height, d],
                    Sxx = new float[_params.Width, _params.Height, d], Syy = new float[_params.Width, _params.Height, d], Szz = new float[_params.Width, _params.Height, d],
                    Sxy = new float[_params.Width, _params.Height, d], Sxz = new float[_params.Width, _params.Height, d], Syz = new float[_params.Width, _params.Height, d],
                    Damage = new float[_params.Width, _params.Height, d]
                });
            }
            Logger.Log($"[ChunkedSimulator] Initialized with {_chunks.Count} chunks of depth {_chunkDepth}");
        }

        public async Task<SimulationResults> RunAsync(byte[,,] labels, float[,,] density, CancellationToken token)
        {
            var started = DateTime.Now;
            var results = new SimulationResults { TimeSeriesSnapshots = _params.SaveTimeSeries ? new List<WaveFieldSnapshot>() : null };

            CalculateTimeStep(density);
            
            if (_params.EnableOffloading)
            {
                // Initially, offload all chunks except the first two needed for the first step.
                for (int i = 2; i < _chunks.Count; i++)
                {
                    await OffloadChunkAsync(_chunks[i]);
                }
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

                // --- STRESS UPDATE PASS ---
                for (int i = 0; i < _chunks.Count; i++)
                {
                    if (_params.EnableOffloading)
                    {
                        if (i + 1 < _chunks.Count) await LoadChunkAsync(_chunks[i + 1]);
                        if (i - 2 >= 0) await OffloadChunkAsync(_chunks[i - 2]);
                    }
                    var c = _chunks[i];
                    if (!c.IsInMemory) await LoadChunkAsync(c);

                    if (_useGPU)
                        await ProcessChunkGPU_StressOnly(c, labels, density, sourceValue, token);
                    else
                    {
                        if (sourceValue != 0)
                            ApplySourceToChunkCPU(c, sourceValue);
                        UpdateChunkStressCPU(c, labels, density);
                    }
                }
                ExchangeStressBoundaries();

                // --- VELOCITY UPDATE PASS ---
                for (int i = 0; i < _chunks.Count; i++)
                {
                    if (_params.EnableOffloading)
                    {
                        if (i + 1 < _chunks.Count) await LoadChunkAsync(_chunks[i + 1]);
                        if (i - 2 >= 0) await OffloadChunkAsync(_chunks[i - 2]);
                    }
                    var c = _chunks[i];
                    if (!c.IsInMemory) await LoadChunkAsync(c);

                    if (_useGPU)
                        await ProcessChunkGPU_VelocityOnly(c, labels, density, token);
                    else
                        UpdateChunkVelocityCPU(c, labels, density);
                }
                ExchangeVelocityBoundaries();

                if (_params.SaveTimeSeries && step % _params.SnapshotInterval == 0)
                    results.TimeSeriesSnapshots?.Add(CreateSnapshot(step));

                if (_params.EnableRealTimeVisualization && step % 10 == 0)
                {
                    WaveFieldUpdated?.Invoke(this, new WaveFieldUpdateEventArgs
                    {
                        WaveField = GetCombinedMagnitude(), TimeStep = step, SimTime = step * _dt, Dataset = labels
                    });
                }

                if (!pHit && CheckPWaveArrival()) { pHit = true; pStep = step; }
                if (pHit && !sHit && CheckSWaveArrival()) { sHit = true; sStep = step; }

                Progress = (float)step / maxSteps;
                ProgressUpdated?.Invoke(this, new SimulationProgressEventArgs { Progress = Progress, Step = step, Message = $"Step {step}" });
            }

            if (_params.EnableOffloading)
            {
                // Reload all chunks to reconstruct the final fields
                foreach(var chunk in _chunks)
                {
                    await LoadChunkAsync(chunk);
                }
            }

            float distance = CalculateTxRxDistance();
            results.PWaveVelocity = pStep > 0 ? distance / (pStep * _dt) : 0;
            results.SWaveVelocity = sStep > 0 ? distance / (sStep * _dt) : 0;
            results.VpVsRatio = results.SWaveVelocity > 0 ? results.PWaveVelocity / results.SWaveVelocity : 0;
            results.PWaveTravelTime = pStep;
            results.SWaveTravelTime = sStep;
            results.TotalTimeSteps = step;
            results.ComputationTime = DateTime.Now - started;

            results.WaveFieldVx = await ReconstructFieldAsync(0);
            results.WaveFieldVy = await ReconstructFieldAsync(1);
            results.WaveFieldVz = await ReconstructFieldAsync(2);
            results.DamageField = GetDamageField();

            return results;
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
            
            int err; // Declare err here to be in scope for the whole method
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
                var dmg = new float[size];
                var ym = new float[size]; 
                var pr = new float[size];


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
                    fixed (float* pDmg = dmg)
                    fixed (float* pYm = ym) fixed (float* pPr = pr)
                    {
                        _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(byte)), pMat, out int err);
                        _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDen, out err);
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

                        
                        _lastDeviceBytes = (long)size * (sizeof(byte) + sizeof(float) * 13);
                        
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
                    }
                }

                unsafe
                {
                    fixed (byte* pMat = mat) fixed (float* pDen = den) fixed (float* pVx = vx) fixed (float* pVy = vy) fixed (float* pVz = vz)
                    fixed (float* pSxx = sxx) fixed (float* pSyy = syy) fixed (float* pSzz = szz) fixed (float* pSxy = sxy) fixed (float* pSxz = sxz) fixed (float* pSyz = syz)
                    {
                        _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(byte)), pMat, out int err);
                        _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDen, out err);
                        _bufVel[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVx, out err);
                        _bufVel[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVy, out err);
                        _bufVel[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVz, out err);
                        _bufStr[0] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxx, out err);
                        _bufStr[1] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSyy, out err);
                        _bufStr[2] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSzz, out err);
                        _bufStr[3] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxy, out err);
                        _bufStr[4] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxz, out err);
                        _bufStr[5] = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSyz, out err);
                        
                        _lastDeviceBytes = (long)size * (sizeof(byte) + sizeof(float) * 12);
                        
                        SetKernelArgs(_kernelVelocity, w, h, d, c.StartZ, 0);
                        nuint gws = (nuint)size;
                        _cl.EnqueueNdrangeKernel(_queue, _kernelVelocity, 1, null, &gws, null, 0, null, null);
                        
                        _cl.Finish(_queue);

                        _cl.EnqueueReadBuffer(_queue, _bufVel[0], true, 0, (nuint)(size * sizeof(float)), pVx, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufVel[1], true, 0, (nuint)(size * sizeof(float)), pVy, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufVel[2], true, 0, (nuint)(size * sizeof(float)), pVz, 0, null, null);
                    }
                }

                k = 0;
                for (int lz = 0; lz < d; lz++)
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++, k++)
                {
                    c.Vx[x, y, lz] = vx[k]; c.Vy[x, y, lz] = vy[k]; c.Vz[x, y, lz] = vz[k];
                }

                ReleaseChunkBuffers();
            });
        }
        
        private unsafe void SetKernelArgs(nint kernel, int w, int h, int d, int chunkStartZ, float sourceValue)
        {
            uint a = 0;
            nint bufMatArg = _bufMat;
            nint bufDenArg = _bufDen;
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
                int applySource = 0;
                if (sourceValue != 0)
                {
                    if (_params.UseFullFaceTransducers) applySource = 1;
                    else if (tz >= chunkStartZ && tz < chunkStartZ + d) applySource = 1;
                }

                int localTz = tz - chunkStartZ;
                int isFullFace = _params.UseFullFaceTransducers ? 1 : 0;
                int sourceAxis = _params.Axis;

                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufMatArg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufDenArg);
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
                
                // New source args
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
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufVel0Arg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufVel1Arg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufVel2Arg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr0Arg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr1Arg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr2Arg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr3Arg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr4Arg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufStr5Arg);
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
            if (materialId != _params.SelectedMaterialID || density[x,y,gz] <= 0f) continue;

            float localE, localNu, localLambda, localMu;
            if (_usePerVoxelProperties && _perVoxelYoungsModulus != null && _perVoxelPoissonRatio != null)
            {
                localE = _perVoxelYoungsModulus[x, y, gz] * 1e6f;
                localNu = _perVoxelPoissonRatio[x, y, gz];
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
                // Source: Based on typical rock mechanics principles, e.g., in "Engineering Rock Mechanics" by Hudson & Harrison.
                // Uniaxial Compressive Strength (UCS) is derived from Mohr-Coulomb parameters.
                // Tensile strength is estimated as a fraction (typically 1/10) of UCS, a common empirical relationship for rocks.
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
            if (materialId != _params.SelectedMaterialID || density[x,y,gz] <= 0f) continue;
            
            float rho = MathF.Max(100f, density[x, y, gz]);
            
            // Calculate stress gradients with boundary checks
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
            
            // Reduced damping for better propagation
            const float damping = 0.999f; // Very light damping
            
            // Update velocities
            c.Vx[x, y, lz] = c.Vx[x, y, lz] * damping + _dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
            c.Vy[x, y, lz] = c.Vy[x, y, lz] * damping + _dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
            c.Vz[x, y, lz] = c.Vz[x, y, lz] * damping + _dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
            
            // Apply velocity limits to prevent instability
            const float maxVel = 10000f; // 10 km/s
            c.Vx[x, y, lz] = Math.Clamp(c.Vx[x, y, lz], -maxVel, maxVel);
            c.Vy[x, y, lz] = Math.Clamp(c.Vy[x, y, lz], -maxVel, maxVel);
            c.Vz[x, y, lz] = Math.Clamp(c.Vz[x, y, lz], -maxVel, maxVel);
        }
    });
}

        #endregion

        #region Utilities
        private Task OffloadChunkAsync(WaveFieldChunk chunk)
        {
            if (!chunk.IsInMemory || string.IsNullOrEmpty(_params.OffloadDirectory))
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(chunk.FilePath))
                {
                    chunk.FilePath = Path.Combine(_params.OffloadDirectory, $"chunk_{chunk.StartZ}_{Guid.NewGuid()}.tmp");
                }

                try
                {
                    using (var writer = new BinaryWriter(File.Create(chunk.FilePath)))
                    {
                        WriteField(writer, chunk.Vx); WriteField(writer, chunk.Vy); WriteField(writer, chunk.Vz);
                        WriteField(writer, chunk.Sxx); WriteField(writer, chunk.Syy); WriteField(writer, chunk.Szz);
                        WriteField(writer, chunk.Sxy); WriteField(writer, chunk.Sxz); WriteField(writer, chunk.Syz);
                        WriteField(writer, chunk.Damage);
                    }

                    chunk.Vx = null; chunk.Vy = null; chunk.Vz = null;
                    chunk.Sxx = null; chunk.Syy = null; chunk.Szz = null;
                    chunk.Sxy = null; chunk.Sxz = null; chunk.Syz = null;
                    chunk.Damage = null;
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
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                try
                {
                    int d = chunk.EndZ - chunk.StartZ;
                    chunk.Vx = new float[_params.Width, _params.Height, d]; chunk.Vy = new float[_params.Width, _params.Height, d]; chunk.Vz = new float[_params.Width, _params.Height, d];
                    chunk.Sxx = new float[_params.Width, _params.Height, d]; chunk.Syy = new float[_params.Width, _params.Height, d]; chunk.Szz = new float[_params.Width, _params.Height, d];
                    chunk.Sxy = new float[_params.Width, _params.Height, d]; chunk.Sxz = new float[_params.Width, _params.Height, d]; chunk.Syz = new float[_params.Width, _params.Height, d];
                    chunk.Damage = new float[_params.Width, _params.Height, d];

                    using (var reader = new BinaryReader(File.OpenRead(chunk.FilePath)))
                    {
                        ReadField(reader, chunk.Vx); ReadField(reader, chunk.Vy); ReadField(reader, chunk.Vz);
                        ReadField(reader, chunk.Sxx); ReadField(reader, chunk.Syy); ReadField(reader, chunk.Szz);
                        ReadField(reader, chunk.Sxy); ReadField(reader, chunk.Sxz); ReadField(reader, chunk.Syz);
                        ReadField(reader, chunk.Damage);
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
        private void CalculateTimeStep(float[,,] density)
        {
            float vpMax = 0f;

            if (_usePerVoxelProperties && _perVoxelYoungsModulus != null && _perVoxelPoissonRatio != null)
            {
                // Calculate local max velocity for heterogeneous media
                for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                    if (density[x, y, z] <= 0) continue;

                    float rho = MathF.Max(100f, density[x, y, z]);
                    float E = _perVoxelYoungsModulus[x, y, z] * 1e6f; // MPa to Pa
                    float nu = _perVoxelPoissonRatio[x, y, z];

                    if (E <= 0 || rho <= 0 || nu >= 0.5f || nu <= -1.0f) continue;

                    float lambda = E * nu / ((1f + nu) * (1f - 2f * nu));
                    float mu = E / (2f * (1f + nu));
                    float vp = MathF.Sqrt((lambda + 2f * mu) / rho);
                    if (vp > vpMax)
                    {
                        vpMax = vp;
                    }
                }
            }
            else
            {
                // Fallback to global properties
                float rhoMin = float.MaxValue;
                for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                     if (density[x,y,z] > 0)
                        rhoMin = MathF.Min(rhoMin, MathF.Max(100f, density[x, y, z]));
                }
                
                if(rhoMin == float.MaxValue) rhoMin = 2700; // fallback rock density
                
                vpMax = MathF.Sqrt((_lambda + 2f * _mu) / rhoMin);
            }

            if (vpMax < 1e-3f) {
                _dt = _params.TimeStepSeconds; // Fallback to a user-defined (likely unstable) value
                Logger.LogWarning($"[CFL] Could not determine a valid Vp_max. Falling back to default dt={_dt * 1e6f:F4} µs.");
                return;
            }

            // CFL condition from Courant, Friedrichs, Lewy (1928) for 3D finite differences.
            // dt <= dx / (sqrt(3) * Vp_max)
            // A safety factor (e.g., 0.5) is added for stability with complex models.
            _dt = 0.5f * (_params.PixelSize / (MathF.Sqrt(3) * vpMax));
            Logger.Log($"[CFL] Calculated stable timestep: dt={_dt*1e6f:F4} µs based on Vp_max={vpMax:F0} m/s");
        }
        
        private float GetCurrentSourceValue(int step)
        {
            float baseAmp = _params.SourceAmplitude * MathF.Sqrt(MathF.Max(1e-6f, _params.SourceEnergyJ));
            baseAmp *= 1e4f; // Scaling factor to make it impactful

            if (!_isRickerActive)
            {
                // Simple impulse source at the first step
                return (step == 1) ? baseAmp : 0f;
            }
            else
            {
                // Ricker wavelet source
                float t = step * _dt;
                if (t > 2 * _rickerT0) return 0f;

                float freq = Math.Max(1.0f, _params.SourceFrequencyKHz * 1000f);
                float x = MathF.PI * freq * (t - _rickerT0);
                float xx = x * x;
                return baseAmp * (1.0f - 2.0f * xx) * MathF.Exp(-xx);
            }
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

        private void ExchangeVelocityBoundaries()
        {
            for (int i = 0; i < _chunks.Count - 1; i++)
            {
                var topChunk = _chunks[i];
                var bottomChunk = _chunks[i + 1];

                if (!topChunk.IsInMemory || !bottomChunk.IsInMemory) continue;

                int topChunkDepth = topChunk.EndZ - topChunk.StartZ;
                int lastInteriorSlice_top = topChunkDepth - 2;
                int bottomGhostSlice_top = topChunkDepth - 1;
                int topGhostSlice_bottom = 0;
                int firstInteriorSlice_bottom = 1;

                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                    topChunk.Vx[x, y, bottomGhostSlice_top] = bottomChunk.Vx[x, y, firstInteriorSlice_bottom];
                    topChunk.Vy[x, y, bottomGhostSlice_top] = bottomChunk.Vy[x, y, firstInteriorSlice_bottom];
                    topChunk.Vz[x, y, bottomGhostSlice_top] = bottomChunk.Vz[x, y, firstInteriorSlice_bottom];

                    bottomChunk.Vx[x, y, topGhostSlice_bottom] = topChunk.Vx[x, y, lastInteriorSlice_top];
                    bottomChunk.Vy[x, y, topGhostSlice_bottom] = topChunk.Vy[x, y, lastInteriorSlice_top];
                    bottomChunk.Vz[x, y, topGhostSlice_bottom] = topChunk.Vz[x, y, lastInteriorSlice_top];
                }
            }
        }

        private void ExchangeStressBoundaries()
        {
            for (int i = 0; i < _chunks.Count - 1; i++)
            {
                var topChunk = _chunks[i];
                var bottomChunk = _chunks[i + 1];

                if (!topChunk.IsInMemory || !bottomChunk.IsInMemory) continue;

                int topChunkDepth = topChunk.EndZ - topChunk.StartZ;
                int lastInteriorSlice_top = topChunkDepth - 2;
                int bottomGhostSlice_top = topChunkDepth - 1;
                int topGhostSlice_bottom = 0;
                int firstInteriorSlice_bottom = 1;

                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                    topChunk.Sxx[x, y, bottomGhostSlice_top] = bottomChunk.Sxx[x, y, firstInteriorSlice_bottom];
                    topChunk.Syy[x, y, bottomGhostSlice_top] = bottomChunk.Syy[x, y, firstInteriorSlice_bottom];
                    topChunk.Szz[x, y, bottomGhostSlice_top] = bottomChunk.Szz[x, y, firstInteriorSlice_bottom];
                    topChunk.Sxy[x, y, bottomGhostSlice_top] = bottomChunk.Sxy[x, y, firstInteriorSlice_bottom];
                    topChunk.Sxz[x, y, bottomGhostSlice_top] = bottomChunk.Sxz[x, y, firstInteriorSlice_bottom];
                    topChunk.Syz[x, y, bottomGhostSlice_top] = bottomChunk.Syz[x, y, firstInteriorSlice_bottom];

                    bottomChunk.Sxx[x, y, topGhostSlice_bottom] = topChunk.Sxx[x, y, lastInteriorSlice_top];
                    bottomChunk.Syy[x, y, topGhostSlice_bottom] = topChunk.Syy[x, y, lastInteriorSlice_top];
                    bottomChunk.Szz[x, y, topGhostSlice_bottom] = topChunk.Szz[x, y, lastInteriorSlice_top];
                    bottomChunk.Sxy[x, y, topGhostSlice_bottom] = topChunk.Sxy[x, y, lastInteriorSlice_top];
                    bottomChunk.Sxz[x, y, topGhostSlice_bottom] = topChunk.Sxz[x, y, lastInteriorSlice_top];
                    bottomChunk.Syz[x, y, topGhostSlice_bottom] = topChunk.Syz[x, y, lastInteriorSlice_top];
                }
            }
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
            int rx = (int)(_params.RxPosition.X * _params.Width); int ry = (int)(_params.RxPosition.Y * _params.Height); int rz = (int)(_params.RxPosition.Z * _params.Depth);
            var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ); if (c == null || !c.IsInMemory) return false;
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
            int tx = (int)(_params.TxPosition.X * _params.Width); int ty = (int)(_params.TxPosition.Y * _params.Height); int tz = (int)(_params.TxPosition.Z * _params.Depth);
            int rx = (int)(_params.RxPosition.X * _params.Width); int ry = (int)(_params.RxPosition.Y * _params.Height); int rz = (int)(_params.RxPosition.Z * _params.Depth);
            return MathF.Sqrt((tx - rx) * (tx - rx) + (ty - ry) * (ty - ry) + (tz - rz) * (tz - rz)) * _params.PixelSize;
        }

        private WaveFieldSnapshot CreateSnapshot(int step)
        {
            int ds = Math.Max(1, Math.Max(_params.Width, Math.Max(_params.Height, _params.Depth)) / 128);
            int w = Math.Max(1, _params.Width / ds); int h = Math.Max(1, _params.Height / ds); int d = Math.Max(1, _params.Depth / ds);
            var vx = new float[w, h, d]; var vy = new float[w, h, d]; var vz = new float[w, h, d];
            foreach (var c in _chunks)
            {
                if (!c.IsInMemory) LoadChunkAsync(c).Wait();
                int cd = c.EndZ - c.StartZ;
                for (int lz = 0; lz < cd; lz += ds)
                {
                    int gz = c.StartZ + lz; int dz = gz / ds; if (dz >= d) continue;
                    for (int y = 0; y < _params.Height; y += ds)
                    {
                        int dy = y / ds; if (dy >= h) continue;
                        for (int x = 0; x < _params.Width; x += ds)
                        {
                            int dx = x / ds; if (dx >= w) continue;
                            vx[dx, dy, dz] = c.Vx[x, y, lz]; vy[dx, dy, dz] = c.Vy[x, y, lz]; vz[dx, dy, dz] = c.Vz[x, y, lz];
                        }
                    }
                }
            }
            var snap = new WaveFieldSnapshot { TimeStep = step, SimulationTime = step * _dt, Width = w, Height = h, Depth = d };
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
                    if (!c.IsInMemory) return; // Should have been reloaded by RunAsync
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
                    float vx = c.Vx[x, y, z], vy = c.Vy[x, y, z], vz = c.Vz[x, y, z];
                    out3d[x, y, c.StartZ + z] = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
                }
            }
            return out3d;
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_useGPU) CleanupOpenCL();
            
            // Clean up any remaining offloaded files
            foreach (var chunk in _chunks.Where(c => !string.IsNullOrEmpty(c.FilePath)))
            {
                try
                {
                    if (File.Exists(chunk.FilePath)) File.Delete(chunk.FilePath);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[ChunkedSimulator] Could not clean up offload file {chunk.FilePath}: {ex.Message}");
                }
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
            // --- MODIFICATION: Release new buffers ---
            if (_bufYoungsModulus != 0) { _cl.ReleaseMemObject(_bufYoungsModulus); _bufYoungsModulus = 0; }
            if (_bufPoissonRatio != 0) { _cl.ReleaseMemObject(_bufPoissonRatio); _bufPoissonRatio = 0; }
            for (int i = 0; i < 3; i++) if (_bufVel[i] != 0) { _cl.ReleaseMemObject(_bufVel[i]); _bufVel[i] = 0; }
            for (int i = 0; i < 6; i++) if (_bufStr[i] != 0) { _cl.ReleaseMemObject(_bufStr[i]); _bufStr[i] = 0; }
            _lastDeviceBytes = 0;
        }

        private string GetKernelSource()
{
    // --- MODIFICATION: Kernel updated for multi-material properties and realistic damage ---
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
            // Source: Based on typical rock mechanics principles, e.g., in ""Engineering Rock Mechanics"" by Hudson & Harrison.
            // Uniaxial Compressive Strength (UCS) is derived from Mohr-Coulomb parameters.
            // Tensile strength is estimated as a fraction (typically 1/10) of UCS, a common empirical relationship for rocks.
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
        const float dt, const float dx, 
        const int width, const int height, const int depth, 
        const uchar selectedMaterial)
    {
        int idx = get_global_id(0);
        if (idx >= width * height * depth) return;
        
        if (material[idx] != selectedMaterial || density[idx] <= 0.0f) return;

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
    }";
}
        
        #endregion
    }
}