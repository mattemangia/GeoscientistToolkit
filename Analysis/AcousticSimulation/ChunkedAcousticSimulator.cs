// GeoscientistToolkit/Analysis/AcousticSimulation/ChunkedAcousticSimulator.cs
using System;
using System.Collections.Generic;
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

        public ChunkedAcousticSimulator(SimulationParameters parameters)
        {
            _params = parameters;
            _useGPU = parameters.UseGPU;

            float E = MathF.Max(1e-6f, _params.YoungsModulusMPa) * 1e6f;
            float nu = _params.PoissonRatio;
            _mu = E / (2f * (1f + nu));
            _lambda = E * nu / ((1f + nu) * (1f - 2f * nu));

            _tensileLimitPa = 0.05f * E;
            _shearLimitPa = 0.03f * E;

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
            ApplyInitialSource(labels, density);

            int maxSteps = Math.Max(1, _params.TimeSteps) * 2;
            int step = 0;
            bool pHit = false, sHit = false;
            int pStep = 0, sStep = 0;

            while (step < maxSteps && !token.IsCancellationRequested)
            {
                for (int i = 0; i < _chunks.Count; i++)
                {
                    var c = _chunks[i];
                    if (_useGPU)
                        await ProcessChunkGPUAsync(i, c, labels, density, token);
                    else
                    {
                        UpdateChunkStressCPU(c, labels, density);
                        UpdateChunkVelocityCPU(c, labels, density);
                    }
                }

                ExchangeBoundaries();
                step++;
                CurrentStep = step;

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

        private async Task ProcessChunkGPUAsync(int ci, WaveFieldChunk c, byte[,,] labels, float[,,] density, CancellationToken token)
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                int w = _params.Width, h = _params.Height, d = c.EndZ - c.StartZ;
                int size = w * h * d;

                var mat = new byte[size]; var den = new float[size]; var vx = new float[size]; var vy = new float[size]; var vz = new float[size];
                var sxx = new float[size]; var syy = new float[size]; var szz = new float[size]; var sxy = new float[size]; var sxz = new float[size]; var syz = new float[size];
                var dmg = new float[size];

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
                    }
                }
                
                unsafe
                {
                    fixed (byte* pMat = mat) fixed (float* pDen = den) fixed (float* pVx = vx) fixed (float* pVy = vy) fixed (float* pVz = vz)
                    fixed (float* pSxx = sxx) fixed (float* pSyy = syy) fixed (float* pSzz = szz) fixed (float* pSxy = sxy) fixed (float* pSxz = sxz) fixed (float* pSyz = syz)
                    fixed (float* pDmg = dmg)
                    {
                        _bufMat = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(byte)), pMat, out int err);
                        _bufDen = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDen, out err);
                        _bufVel[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVx, out err);
                        _bufVel[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVy, out err);
                        _bufVel[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pVz, out err);
                        _bufStr[0] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxx, out err);
                        _bufStr[1] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSyy, out err);
                        _bufStr[2] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSzz, out err);
                        _bufStr[3] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxy, out err);
                        _bufStr[4] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSxz, out err);
                        _bufStr[5] = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pSyz, out err);
                        _bufDmg = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr, (nuint)(size * sizeof(float)), pDmg, out err);
                        
                        _lastDeviceBytes = (long)size * (sizeof(byte) + sizeof(float) * 11);
                        
                        SetKernelArgs(_kernelStress, w, h, d);
                        nuint gws = (nuint)size;
                        _cl.EnqueueNdrangeKernel(_queue, _kernelStress, 1, null, &gws, null, 0, null, null);
                        
                        SetKernelArgs(_kernelVelocity, w, h, d);
                        _cl.EnqueueNdrangeKernel(_queue, _kernelVelocity, 1, null, &gws, null, 0, null, null);
                        _cl.Finish(_queue);

                        _cl.EnqueueReadBuffer(_queue, _bufVel[0], true, 0, (nuint)(size * sizeof(float)), pVx, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufVel[1], true, 0, (nuint)(size * sizeof(float)), pVy, 0, null, null);
                        _cl.EnqueueReadBuffer(_queue, _bufVel[2], true, 0, (nuint)(size * sizeof(float)), pVz, 0, null, null);
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
                    c.Vx[x, y, lz] = vx[k]; c.Vy[x, y, lz] = vy[k]; c.Vz[x, y, lz] = vz[k];
                    c.Sxx[x, y, lz] = sxx[k]; c.Syy[x, y, lz] = syy[k]; c.Szz[x, y, lz] = szz[k];
                    c.Sxy[x, y, lz] = sxy[k]; c.Sxz[x, y, lz] = sxz[k]; c.Syz[x, y, lz] = syz[k];
                    c.Damage[x, y, lz] = dmg[k];
                }

                ReleaseChunkBuffers();
            });
        }
        
        private unsafe void SetKernelArgs(nint kernel, int w, int h, int d)
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

            var paramsPixelSize = _params.PixelSize;
            var paramsSelectedMaterialId = _params.SelectedMaterialID;
            if (kernel == _kernelStress)
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
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(nint), in bufDmgArg);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _lambda);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _mu);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _dt);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in paramsPixelSize);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in w);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in h);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(int), in d);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(byte), in paramsSelectedMaterialId);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _tensileLimitPa);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _shearLimitPa);
                _cl.SetKernelArg(kernel, a++, (nuint)sizeof(float), in _damageRatePerSec);
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
            if (labels[x, y, gz] != _params.SelectedMaterialID) continue;
            
            // Get per-voxel material properties if available
            float localE, localNu, localLambda, localMu;
            if (_usePerVoxelProperties && _perVoxelYoungsModulus != null && _perVoxelPoissonRatio != null)
            {
                localE = _perVoxelYoungsModulus[x, y, gz] * 1e6f; // Convert MPa to Pa
                localNu = _perVoxelPoissonRatio[x, y, gz];
            }
            else
            {
                // Use global parameters
                localE = _params.YoungsModulusMPa * 1e6f; // Convert MPa to Pa
                localNu = _params.PoissonRatio;
            }
            
            // Calculate Lamé constants from local E and nu
            localMu = localE / (2f * (1f + localNu));
            localLambda = localE * localNu / ((1f + localNu) * (1f - 2f * localNu));
            
            // Calculate velocity gradients
            float dvx_dx = (c.Vx[x + 1, y, lz] - c.Vx[x - 1, y, lz]) / (2 * _params.PixelSize);
            float dvy_dy = (c.Vy[x, y + 1, lz] - c.Vy[x, y - 1, lz]) / (2 * _params.PixelSize);
            float dvz_dz = lz > 0 && lz < d - 1 ? 
                (c.Vz[x, y, lz + 1] - c.Vz[x, y, lz - 1]) / (2 * _params.PixelSize) : 0;
            float dvy_dx = (c.Vy[x + 1, y, lz] - c.Vy[x - 1, y, lz]) / (2 * _params.PixelSize);
            float dvx_dy = (c.Vx[x, y + 1, lz] - c.Vx[x, y - 1, lz]) / (2 * _params.PixelSize);
            float dvz_dx = (c.Vz[x + 1, y, lz] - c.Vz[x - 1, y, lz]) / (2 * _params.PixelSize);
            float dvx_dz = lz > 0 && lz < d - 1 ? 
                (c.Vx[x, y, lz + 1] - c.Vx[x, y, lz - 1]) / (2 * _params.PixelSize) : 0;
            float dvz_dy = (c.Vz[x, y + 1, lz] - c.Vz[x, y - 1, lz]) / (2 * _params.PixelSize);
            float dvy_dz = lz > 0 && lz < d - 1 ? 
                (c.Vy[x, y, lz + 1] - c.Vy[x, y, lz - 1]) / (2 * _params.PixelSize) : 0;
            
            float volumetric = dvx_dx + dvy_dy + dvz_dz;
            float damp = 1f - c.Damage[x, y, lz] * 0.9f;
            
            // Update stress components using local material properties
            c.Sxx[x, y, lz] += _dt * damp * (localLambda * volumetric + 2f * localMu * dvx_dx);
            c.Syy[x, y, lz] += _dt * damp * (localLambda * volumetric + 2f * localMu * dvy_dy);
            c.Szz[x, y, lz] += _dt * damp * (localLambda * volumetric + 2f * localMu * dvz_dz);
            c.Sxy[x, y, lz] += _dt * damp * localMu * (dvy_dx + dvx_dy);
            c.Sxz[x, y, lz] += _dt * damp * localMu * (dvz_dx + dvx_dz);
            c.Syz[x, y, lz] += _dt * damp * localMu * (dvz_dy + dvy_dz);
            
            // Apply damage model with per-voxel limits based on local E
            float localTensileLimit = 0.05f * localE;
            float localShearLimit = 0.03f * localE;
            
            float tensileMax = MathF.Max(c.Sxx[x, y, lz], MathF.Max(c.Syy[x, y, lz], c.Szz[x, y, lz]));
            float shearMag = MathF.Sqrt(c.Sxy[x, y, lz] * c.Sxy[x, y, lz] + 
                                        c.Sxz[x, y, lz] * c.Sxz[x, y, lz] + 
                                        c.Syz[x, y, lz] * c.Syz[x, y, lz]);
            
            float dInc = 0f;
            if (tensileMax > localTensileLimit) 
                dInc += _damageRatePerSec * _dt * (tensileMax / localTensileLimit - 1f);
            if (shearMag > localShearLimit) 
                dInc += _damageRatePerSec * _dt * (shearMag / localShearLimit - 1f);
            
            c.Damage[x, y, lz] = Math.Min(1f, Math.Max(0f, c.Damage[x, y, lz] + dInc));
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
                    if (labels[x, y, gz] != _params.SelectedMaterialID) continue;
                    float rho = MathF.Max(100f, density[x, y, gz]);
                    float dsxx_dx = (c.Sxx[x + 1, y, lz] - c.Sxx[x - 1, y, lz]) / (2 * _params.PixelSize);
                    float dsyy_dy = (c.Syy[x, y + 1, lz] - c.Syy[x, y - 1, lz]) / (2 * _params.PixelSize);
                    float dszz_dz = (c.Szz[x, y, lz + 1] - c.Szz[x, y, lz - 1]) / (2 * _params.PixelSize);
                    float dsxy_dy = (c.Sxy[x, y + 1, lz] - c.Sxy[x, y - 1, lz]) / (2 * _params.PixelSize);
                    float dsxy_dx = (c.Sxy[x + 1, y, lz] - c.Sxy[x - 1, y, lz]) / (2 * _params.PixelSize);
                    float dsxz_dz = (c.Sxz[x, y, lz + 1] - c.Sxz[x, y, lz - 1]) / (2 * _params.PixelSize);
                    float dsxz_dx = (c.Sxz[x + 1, y, lz] - c.Sxz[x - 1, y, lz]) / (2 * _params.PixelSize);
                    float dsyz_dz = (c.Syz[x, y, lz + 1] - c.Syz[x, y, lz - 1]) / (2 * _params.PixelSize);
                    float dsyz_dy = (c.Syz[x, y + 1, lz] - c.Syz[x, y - 1, lz]) / (2 * _params.PixelSize);
                    const float damping = 0.995f;
                    c.Vx[x, y, lz] = c.Vx[x, y, lz] * damping + _dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
                    c.Vy[x, y, lz] = c.Vy[x, y, lz] * damping + _dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
                    c.Vz[x, y, lz] = c.Vz[x, y, lz] * damping + _dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
                }
            });
        }

        #endregion

        #region Utilities

        private void CalculateTimeStep(float[,,] density)
        {
            float rhoMin = float.MaxValue;
            for (int z = 0; z < _params.Depth; z++)
                for (int y = 0; y < _params.Height; y++)
                    for (int x = 0; x < _params.Width; x++)
                        rhoMin = MathF.Min(rhoMin, MathF.Max(100f, density[x, y, z]));
            float vsMax = MathF.Sqrt(_mu / MathF.Max(100f, rhoMin));
            _dt = Math.Min(_params.TimeStepSeconds, _params.PixelSize / (1.7320508f * MathF.Max(1e-3f, vsMax)));
        }

        private void ApplyInitialSource(byte[,,] labels, float[,,] density)
        {
            int tx = (int)(_params.TxPosition.X * _params.Width);
            int ty = (int)(_params.TxPosition.Y * _params.Height);
            int tz = (int)(_params.TxPosition.Z * _params.Depth);
            var c = _chunks.First(k => tz >= k.StartZ && tz < k.EndZ);
            int lz = tz - c.StartZ;
            float amp = MathF.Max(1e-4f, _params.SourceAmplitude);
            switch (_params.Axis)
            {
                case 0: c.Vx[tx, ty, lz] += amp; break;
                case 1: c.Vy[tx, ty, lz] += amp; break;
                default: c.Vz[tx, ty, lz] += amp; break;
            }
        }

        private void ExchangeBoundaries()
        {
            for (int i = 0; i < _chunks.Count - 1; i++)
            {
                var a = _chunks[i]; var b = _chunks[i + 1];
                int za = a.EndZ - a.StartZ - 2; int zb = 1;
                for (int y = 0; y < _params.Height; y++)
                for (int x = 0; x < _params.Width; x++)
                {
                    b.Vx[x, y, zb - 1] = a.Vx[x, y, za + 1]; b.Vy[x, y, zb - 1] = a.Vy[x, y, za + 1]; b.Vz[x, y, zb - 1] = a.Vz[x, y, za + 1];
                    a.Vx[x, y, za] = b.Vx[x, y, zb]; a.Vy[x, y, za] = b.Vy[x, y, zb]; a.Vz[x, y, za] = b.Vz[x, y, zb];
                }
            }
        }

        private bool CheckPWaveArrival()
        {
            int rx = (int)(_params.RxPosition.X * _params.Width); int ry = (int)(_params.RxPosition.Y * _params.Height); int rz = (int)(_params.RxPosition.Z * _params.Depth);
            var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ); if (c == null) return false;
            int lz = rz - c.StartZ;
            float comp = _params.Axis switch { 0 => MathF.Abs(c.Vx[rx, ry, lz]), 1 => MathF.Abs(c.Vy[rx, ry, lz]), _ => MathF.Abs(c.Vz[rx, ry, lz]) };
            return comp > 1e-9f;
        }

        private bool CheckSWaveArrival()
        {
            int rx = (int)(_params.RxPosition.X * _params.Width); int ry = (int)(_params.RxPosition.Y * _params.Height); int rz = (int)(_params.RxPosition.Z * _params.Depth);
            var c = _chunks.FirstOrDefault(k => rz >= k.StartZ && rz < k.EndZ); if (c == null) return false;
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
        
        private async Task<float[,,]> ReconstructFieldAsync(int comp)
        {
            var field = new float[_params.Width, _params.Height, _params.Depth];
            await Task.Run(() =>
            {
                foreach (var c in _chunks)
                {
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
            for (int i = 0; i < 3; i++) if (_bufVel[i] != 0) { _cl.ReleaseMemObject(_bufVel[i]); _bufVel[i] = 0; }
            for (int i = 0; i < 6; i++) if (_bufStr[i] != 0) { _cl.ReleaseMemObject(_bufStr[i]); _bufStr[i] = 0; }
            _lastDeviceBytes = 0;
        }

        private string GetKernelSource()
{
    return @"
    // Kernel for stress update with optional per-voxel material properties
    __kernel void updateStress(
        __global const uchar* material, 
        __global const float* density,
        __global float* vx, __global float* vy, __global float* vz,
        __global float* sxx, __global float* syy, __global float* szz,
        __global float* sxy, __global float* sxz, __global float* syz,
        __global float* damage, 
        const float lambda, const float mu,  // Global defaults
        const float dt, const float dx,
        const int width, const int height, const int depth, 
        const uchar selectedMaterial,
        const float tensileLimitPa, const float shearLimitPa, 
        const float damageRatePerSec)
    {
        int idx = get_global_id(0); 
        if (idx >= width * height * depth) return;
        
        // Calculate 3D coordinates
        int z = idx / (width * height);
        int rem = idx % (width * height);
        int y = rem / width;
        int x = rem % width;
        
        // Check material and boundaries
        if (material[idx] != selectedMaterial) return;
        if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;
        
        // Calculate neighbor indices
        int xp1 = idx + 1;
        int xm1 = idx - 1;
        int yp1 = idx + width;
        int ym1 = idx - width;
        int zp1 = idx + width * height;
        int zm1 = idx - width * height;
        
        // Validate neighbor indices to prevent out-of-bounds access
        if (zp1 >= width * height * depth || zm1 < 0) return;
        
        // Calculate velocity gradients with central differences
        float dvx_dx = (vx[xp1] - vx[xm1]) / (2.0f * dx);
        float dvy_dy = (vy[yp1] - vy[ym1]) / (2.0f * dx);
        float dvz_dz = (vz[zp1] - vz[zm1]) / (2.0f * dx);
        
        float dvy_dx = (vy[xp1] - vy[xm1]) / (2.0f * dx);
        float dvx_dy = (vx[yp1] - vx[ym1]) / (2.0f * dx);
        
        float dvz_dx = (vz[xp1] - vz[xm1]) / (2.0f * dx);
        float dvx_dz = (vx[zp1] - vx[zm1]) / (2.0f * dx);
        
        float dvz_dy = (vz[yp1] - vz[ym1]) / (2.0f * dx);
        float dvy_dz = (vy[zp1] - vy[zm1]) / (2.0f * dx);
        
        // Calculate volumetric strain
        float volumetric_strain = dvx_dx + dvy_dy + dvz_dz;
        
        // Apply damage reduction
        float damage_factor = 1.0f - damage[idx] * 0.9f;
        
        // Update stress components using Hooke's law
        // σxx = λ(εxx + εyy + εzz) + 2μεxx
        sxx[idx] += dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvx_dx);
        syy[idx] += dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvy_dy);
        szz[idx] += dt * damage_factor * (lambda * volumetric_strain + 2.0f * mu * dvz_dz);
        
        // Shear stress components
        sxy[idx] += dt * damage_factor * mu * (dvy_dx + dvx_dy);
        sxz[idx] += dt * damage_factor * mu * (dvz_dx + dvx_dz);
        syz[idx] += dt * damage_factor * mu * (dvz_dy + dvy_dz);
        
        // Apply stress limiters to prevent numerical instability
        float stress_limit = 1e9f; // 1 GPa max stress
        sxx[idx] = clamp(sxx[idx], -stress_limit, stress_limit);
        syy[idx] = clamp(syy[idx], -stress_limit, stress_limit);
        szz[idx] = clamp(szz[idx], -stress_limit, stress_limit);
        sxy[idx] = clamp(sxy[idx], -stress_limit, stress_limit);
        sxz[idx] = clamp(sxz[idx], -stress_limit, stress_limit);
        syz[idx] = clamp(syz[idx], -stress_limit, stress_limit);
        
        // Calculate damage based on failure criteria
        float tensile_max = fmax(sxx[idx], fmax(syy[idx], szz[idx]));
        float shear_magnitude = sqrt(sxy[idx]*sxy[idx] + sxz[idx]*sxz[idx] + syz[idx]*syz[idx]);
        
        float damage_increment = 0.0f;
        
        // Tensile failure criterion
        if (tensile_max > tensileLimitPa) {
            damage_increment += damageRatePerSec * dt * (tensile_max / tensileLimitPa - 1.0f);
        }
        
        // Shear failure criterion
        if (shear_magnitude > shearLimitPa) {
            damage_increment += damageRatePerSec * dt * (shear_magnitude / shearLimitPa - 1.0f);
        }
        
        // Update damage with saturation at 1.0
        damage[idx] = clamp(damage[idx] + damage_increment, 0.0f, 1.0f);
    }
    
    // Kernel for velocity update
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
        
        // Calculate 3D coordinates
        int z = idx / (width * height);
        int rem = idx % (width * height);
        int y = rem / width;
        int x = rem % width;
        
        // Check material and boundaries
        if (material[idx] != selectedMaterial) return;
        if (x <= 0 || x >= width-1 || y <= 0 || y >= height-1 || z <= 0 || z >= depth-1) return;
        
        // Calculate neighbor indices
        int xp1 = idx + 1;
        int xm1 = idx - 1;
        int yp1 = idx + width;
        int ym1 = idx - width;
        int zp1 = idx + width * height;
        int zm1 = idx - width * height;
        
        // Validate neighbor indices
        if (zp1 >= width * height * depth || zm1 < 0) return;
        
        // Get density with minimum threshold for numerical stability
        float rho = fmax(100.0f, density[idx]);
        
        // Calculate stress gradients
        float dsxx_dx = (sxx[xp1] - sxx[xm1]) / (2.0f * dx);
        float dsyy_dy = (syy[yp1] - syy[ym1]) / (2.0f * dx);
        float dszz_dz = (szz[zp1] - szz[zm1]) / (2.0f * dx);
        
        float dsxy_dy = (sxy[yp1] - sxy[ym1]) / (2.0f * dx);
        float dsxy_dx = (sxy[xp1] - sxy[xm1]) / (2.0f * dx);
        
        float dsxz_dz = (sxz[zp1] - sxz[zm1]) / (2.0f * dx);
        float dsxz_dx = (sxz[xp1] - sxz[xm1]) / (2.0f * dx);
        
        float dsyz_dz = (syz[zp1] - syz[zm1]) / (2.0f * dx);
        float dsyz_dy = (syz[yp1] - syz[ym1]) / (2.0f * dx);
        
        // Apply momentum equation with damping
        const float damping = 0.995f;
        
        // Update velocities: a = F/m = (∇·σ)/ρ
        vx[idx] = vx[idx] * damping + dt * (dsxx_dx + dsxy_dy + dsxz_dz) / rho;
        vy[idx] = vy[idx] * damping + dt * (dsxy_dx + dsyy_dy + dsyz_dz) / rho;
        vz[idx] = vz[idx] * damping + dt * (dsxz_dx + dsyz_dy + dszz_dz) / rho;
        
        // Apply velocity limiters to prevent numerical instability
        float max_velocity = 10000.0f; // 10 km/s max velocity
        vx[idx] = clamp(vx[idx], -max_velocity, max_velocity);
        vy[idx] = clamp(vy[idx], -max_velocity, max_velocity);
        vz[idx] = clamp(vz[idx], -max_velocity, max_velocity);
    }";
}
        
        #endregion
    }
}