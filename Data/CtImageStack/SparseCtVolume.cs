// GAIA/Data/CtImageStack/SparseCtVolume.cs

using System.Numerics;
using GAIA.Util;
using OpenTK.Graphics.OpenGL;

namespace GAIA.Data.CtImageStack;

/// <summary>
///     Out-of-core sparse volume for the CT 3D renderer. Instead of uploading a whole LOD as one
///     dense 3D texture — which caps the viewable dataset at GPU memory and OOMs on large scans —
///     the volume is kept bricked (64³) on disk and only a bounded number of bricks are resident in
///     a fixed-size GPU <em>atlas</em>. A stacked <em>page table</em> maps (level, brick) to an atlas
///     slot; per-frame refinement picks each visible brick's level from its projected screen size,
///     streams the missing ones in the background, and evicts least-recently-used slots. Empty
///     bricks (max density below the display threshold) are never streamed and are skipped by the
///     ray-marcher. Resident memory is therefore constant regardless of dataset size — the same
///     principle production CT/microscopy renderers use to open tens of GB on modest cards.
///
///     All GL calls must run on the render thread; background work only reads bytes from disk.
/// </summary>
public sealed class SparseCtVolume : IDisposable
{
    public const int BrickCore = 64;         // voxels along a brick edge
    public const int Apron = 1;              // one-voxel border for correct trilinear across bricks
    public const int BrickTile = BrickCore + 2 * Apron; // 66
    private const int MaxLevels = 16;        // shader uniform-array bound
    private const int StreamBatch = 32;      // bricks read per background task

    private readonly StreamingCtVolumeDataset _dataset;
    private readonly int _levelCount;         // total LODs in the file
    private readonly int _maxAtlasLevel;      // coarsest level served by the atlas (= levelCount-2)
    private readonly int[] _lodW, _lodH, _lodD;
    private readonly int[] _bgX, _bgY, _bgZ;  // brick-grid dims per level
    private readonly int[] _pageZOffset;      // z origin of each level inside the stacked page table
    private readonly byte[][] _brickMax;      // per level, per brick: max density (conservative)
    private readonly byte[][] _pageData;      // per level, 4 bytes/brick: status, slotX, slotY, slotZ

    private int _baseTex, _atlasTex, _pageTex;
    private int _pageW, _pageH, _pageD;
    private int _atlasSlotsX, _atlasSlotsY, _atlasSlotsZ, _slotCount;
    private int _atlasVoxX, _atlasVoxY, _atlasVoxZ;

    // Residency bookkeeping. A slot holds one (level, brickIndex); -1 owner means free.
    private readonly (int level, int brick)[] _slotOwner;
    private readonly long[] _slotUsed;
    private readonly Dictionary<(int level, int brick), int> _resident = new();
    private long _frame;

    private readonly HashSet<(int level, int brick)> _requested = new();
    private Task<List<(int level, int brick, byte[] data, byte max)>> _streamTask;

    private float _voxel0World;               // world size of a finest-LOD voxel
    private Vector3 _volumeScale = Vector3.One;

    public bool Ready { get; private set; }

    public SparseCtVolume(StreamingCtVolumeDataset dataset, long atlasByteBudget, int maxTexture3DSize)
    {
        _dataset = dataset;
        dataset.LoadMetadata();
        _levelCount = dataset.LodCount;
        _maxAtlasLevel = _levelCount - 2; // coarsest level lives in the always-resident base texture

        _lodW = new int[_levelCount]; _lodH = new int[_levelCount]; _lodD = new int[_levelCount];
        _bgX = new int[_levelCount]; _bgY = new int[_levelCount]; _bgZ = new int[_levelCount];
        _pageZOffset = new int[_levelCount];
        _brickMax = new byte[_levelCount][];
        _pageData = new byte[_levelCount][];
        for (var l = 0; l < _levelCount; l++)
        {
            var info = dataset.LodInfos[l];
            _lodW[l] = info.Width; _lodH[l] = info.Height; _lodD[l] = info.Depth;
            _bgX[l] = (info.Width + BrickCore - 1) / BrickCore;
            _bgY[l] = (info.Height + BrickCore - 1) / BrickCore;
            _bgZ[l] = (info.Depth + BrickCore - 1) / BrickCore;
            _brickMax[l] = new byte[_bgX[l] * _bgY[l] * _bgZ[l]];
            _pageData[l] = new byte[_bgX[l] * _bgY[l] * _bgZ[l] * 4];
        }

        _slotOwner = Array.Empty<(int, int)>();
        _slotUsed = Array.Empty<long>();

        try
        {
            BuildBaseAndBrickMax();
            AllocateAtlasAndPageTable(atlasByteBudget, maxTexture3DSize);
            _slotOwner = new (int, int)[_slotCount];
            _slotUsed = new long[_slotCount];
            for (var i = 0; i < _slotCount; i++) _slotOwner[i] = (-1, -1);
            Ready = _slotCount > 0 && _maxAtlasLevel >= 0;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SparseCtVolume] Initialization failed: {ex.Message}");
            Ready = false;
        }
    }

    /// <summary>Otsu split of the coarsest LOD, in 0..1, so the viewer can seed its threshold.</summary>
    public float SuggestedThreshold01 { get; private set; }

    private byte[] _baseData;
    private int _baseW, _baseH, _baseD;

    private void BuildBaseAndBrickMax()
    {
        var coarse = _levelCount - 1;
        var bricks = _dataset.ReadLodBricks(coarse);
        _baseW = _lodW[coarse]; _baseH = _lodH[coarse]; _baseD = _lodD[coarse];
        _baseData = Reconstruct(coarse, bricks);
        SuggestedThreshold01 = CtVolume3DViewer.CalculateOtsuThreshold(_baseData) / 255f;

        // Conservative per-brick maxima for every level, derived from the small coarse volume:
        // over-estimating a brick's maximum never wrongly marks a non-empty brick as air, so
        // empty-space skipping is correct from the first frame and only tightened as real bricks
        // stream in with their exact maxima.
        for (var l = 0; l < _levelCount; l++)
        {
            var mx = _brickMax[l];
            var scaleX = _baseW / (double)_lodW[l];
            var scaleY = _baseH / (double)_lodH[l];
            var scaleZ = _baseD / (double)_lodD[l];
            // Walk the coarse volume once, scattering each voxel into the brick that covers it.
            for (var z = 0; z < _baseD; z++)
            {
                var lz = (int)(z / scaleZ) / BrickCore;
                if (lz >= _bgZ[l]) lz = _bgZ[l] - 1;
                for (var y = 0; y < _baseH; y++)
                {
                    var ly = (int)(y / scaleY) / BrickCore;
                    if (ly >= _bgY[l]) ly = _bgY[l] - 1;
                    var row = (z * _baseH + y) * _baseW;
                    var rowBrick = (lz * _bgY[l] + ly) * _bgX[l];
                    for (var x = 0; x < _baseW; x++)
                    {
                        var lx = (int)(x / scaleX) / BrickCore;
                        if (lx >= _bgX[l]) lx = _bgX[l] - 1;
                        var v = _baseData[row + x];
                        var bi = rowBrick + lx;
                        if (v > mx[bi]) mx[bi] = v;
                    }
                }
            }
        }
    }

    private byte[] Reconstruct(int level, byte[] bricks)
    {
        int w = _lodW[level], h = _lodH[level], d = _lodD[level], bs = BrickCore;
        var r = new byte[(long)w * h * d];
        var bx = _bgX[level]; var by = _bgY[level];
        long brickVoxels = (long)bs * bs * bs;
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var bi = ((long)(z / bs) * by * bx + (long)(y / bs) * bx + x / bs) * brickVoxels
                     + (z % bs) * bs * bs + (y % bs) * bs + x % bs;
            if (bi < bricks.LongLength) r[((long)z * h + y) * w + x] = bricks[bi];
        }
        return r;
    }

    private void AllocateAtlasAndPageTable(long budget, int maxTex)
    {
        // Stacked page table: levels concatenated along Z. Small (a few bytes per brick).
        _pageW = 1; _pageH = 1; var zoff = 0;
        for (var l = 0; l <= _maxAtlasLevel; l++)
        {
            _pageW = Math.Max(_pageW, _bgX[l]);
            _pageH = Math.Max(_pageH, _bgY[l]);
            _pageZOffset[l] = zoff;
            zoff += _bgZ[l];
        }
        _pageD = Math.Max(1, zoff);

        // Atlas: as many 66³ slots as the budget and the driver's 3D-texture limit allow.
        long slotBytes = (long)BrickTile * BrickTile * BrickTile;
        var maxSlots = (int)Math.Clamp(budget / slotBytes, 8, 32768);
        var perAxis = Math.Max(2, (int)Math.Floor(Math.Cbrt(maxSlots)));
        var slotsPerTexAxis = Math.Max(1, maxTex / BrickTile);
        perAxis = Math.Min(perAxis, slotsPerTexAxis);
        _atlasSlotsX = _atlasSlotsY = _atlasSlotsZ = perAxis;
        _slotCount = _atlasSlotsX * _atlasSlotsY * _atlasSlotsZ;
        _atlasVoxX = _atlasSlotsX * BrickTile;
        _atlasVoxY = _atlasSlotsY * BrickTile;
        _atlasVoxZ = _atlasSlotsZ * BrickTile;

        if (_maxAtlasLevel < 0) { _baseTex = CreateDense(_baseW, _baseH, _baseD, _baseData, true); return; }

        _baseTex = CreateDense(_baseW, _baseH, _baseD, _baseData, true);

        _atlasTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture3D, _atlasTex);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage3D(TextureTarget.Texture3D, 0, PixelInternalFormat.R8, _atlasVoxX, _atlasVoxY, _atlasVoxZ, 0,
            PixelFormat.Red, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

        _pageTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture3D, _pageTex);
        GL.TexImage3D(TextureTarget.Texture3D, 0, PixelInternalFormat.Rgba8ui, _pageW, _pageH, _pageD, 0,
            PixelFormat.RgbaInteger, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

        Logger.Log($"[SparseCtVolume] Atlas {_atlasSlotsX}×{_atlasSlotsY}×{_atlasSlotsZ} = {_slotCount} bricks " +
                   $"({(long)_slotCount * slotBytes / 1048576.0:F0} MiB), {_levelCount} LODs, " +
                   $"base {_baseW}×{_baseH}×{_baseD}.");
    }

    private static int CreateDense(int w, int h, int d, byte[] data, bool linear)
    {
        var t = GL.GenTexture();
        var f = linear ? (int)TextureMinFilter.Linear : (int)TextureMinFilter.Nearest;
        GL.BindTexture(TextureTarget.Texture3D, t);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage3D(TextureTarget.Texture3D, 0, PixelInternalFormat.R8, w, h, d, 0,
            PixelFormat.Red, PixelType.UnsignedByte, data);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, f);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, f);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        return t;
    }

    /// <summary>
    ///     Per-frame residency: harvest streamed bricks into the atlas, refine which bricks are
    ///     needed for the current view, mark empties, upload the page table, and request the misses.
    /// </summary>
    public void Update(Matrix4x4 viewProj, Vector3 camPos, Vector3 volumeScale, float threshold,
        float viewportHeight, float fovY)
    {
        if (!Ready) return;
        _frame++;
        _volumeScale = volumeScale;
        _voxel0World = Math.Min(volumeScale.X / _lodW[0],
            Math.Min(volumeScale.Y / _lodH[0], volumeScale.Z / _lodD[0]));

        IntakeStreamedBricks();

        var thByte = (byte)Math.Clamp((int)MathF.Round(threshold * 255f), 0, 255);
        var c = 2f * MathF.Tan(fovY * 0.5f) / MathF.Max(1f, viewportHeight);
        var lodBias = c / MathF.Max(1e-6f, _voxel0World);

        var desired = new List<(int level, int brick)>();
        Refine(viewProj, camPos, lodBias, thByte, desired);

        foreach (var key in desired)
            if (_resident.TryGetValue(key, out var slot)) _slotUsed[slot] = _frame;
            // Keep the coarser ancestor the shader falls back to alive while the fine brick streams,
            // otherwise it can be evicted and the region drops all the way to the base LOD.
            else TouchResidentAncestor(key.level, key.brick);

        RebuildPageData(thByte);
        UploadPageTable();

        RequestMisses(desired);
    }

    private void Refine(Matrix4x4 viewProj, Vector3 camPos, float lodBias, byte thByte,
        List<(int, int)> outSet)
    {
        // Depth-first refinement from the coarsest atlas level. A brick is accepted when its own
        // level is already as coarse as the view wants (or is the finest); otherwise its eight
        // finer children are visited. Empty and off-screen bricks are pruned outright.
        var stack = new Stack<(int level, int bx, int by, int bz)>();
        var top = _maxAtlasLevel;
        for (var bz = 0; bz < _bgZ[top]; bz++)
        for (var by = 0; by < _bgY[top]; by++)
        for (var bx = 0; bx < _bgX[top]; bx++)
            stack.Push((top, bx, by, bz));

        while (stack.Count > 0 && outSet.Count < _slotCount)
        {
            var (l, bx, by, bz) = stack.Pop();
            var bi = (bz * _bgY[l] + by) * _bgX[l] + bx;
            if (_brickMax[l][bi] < thByte) continue; // air

            var lo = new Vector3(bx * BrickCore / (float)_lodW[l], by * BrickCore / (float)_lodH[l],
                bz * BrickCore / (float)_lodD[l]) * _volumeScale;
            var hi = new Vector3(Math.Min((bx + 1) * BrickCore, _lodW[l]) / (float)_lodW[l],
                Math.Min((by + 1) * BrickCore, _lodH[l]) / (float)_lodH[l],
                Math.Min((bz + 1) * BrickCore, _lodD[l]) / (float)_lodD[l]) * _volumeScale;
            if (!InFrustum(viewProj, lo, hi)) continue;

            var center = (lo + hi) * 0.5f;
            var dist = MathF.Max(1e-4f, (center - camPos).Length());
            var wanted = (int)MathF.Floor(MathF.Log2(MathF.Max(1f, dist * lodBias)));
            wanted = Math.Clamp(wanted, 0, _maxAtlasLevel);

            if (l <= wanted || l == 0)
            {
                outSet.Add((l, bi));
            }
            else
            {
                var cl = l - 1;
                for (var dz = 0; dz < 2; dz++)
                for (var dy = 0; dy < 2; dy++)
                for (var dx = 0; dx < 2; dx++)
                {
                    var cx = bx * 2 + dx; var cy = by * 2 + dy; var cz = bz * 2 + dz;
                    if (cx < _bgX[cl] && cy < _bgY[cl] && cz < _bgZ[cl]) stack.Push((cl, cx, cy, cz));
                }
            }
        }
    }

    private static bool InFrustum(Matrix4x4 vp, Vector3 lo, Vector3 hi)
    {
        // Reject only when all eight corners fall outside the same clip plane — a cheap,
        // never-false-negative test that keeps bricks straddling the frustum edge.
        int outL = 0, outR = 0, outB = 0, outT = 0, outN = 0, outF = 0;
        for (var i = 0; i < 8; i++)
        {
            var p = new Vector3((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y,
                (i & 4) == 0 ? lo.Z : hi.Z);
            var c = Vector4.Transform(new Vector4(p, 1f), vp);
            if (c.X < -c.W) outL++; if (c.X > c.W) outR++;
            if (c.Y < -c.W) outB++; if (c.Y > c.W) outT++;
            if (c.Z < -c.W) outN++; if (c.Z > c.W) outF++;
        }
        return !(outL == 8 || outR == 8 || outB == 8 || outT == 8 || outN == 8 || outF == 8);
    }

    private void RebuildPageData(byte thByte)
    {
        for (var l = 0; l <= _maxAtlasLevel; l++)
        {
            var page = _pageData[l]; var mx = _brickMax[l];
            for (var i = 0; i < mx.Length; i++)
            {
                var o = i * 4;
                page[o] = mx[i] < thByte ? (byte)2 : (byte)0; // empty : not-resident
                page[o + 1] = page[o + 2] = page[o + 3] = 0;
            }
        }
        foreach (var kv in _resident)
        {
            var (l, bi) = kv.Key;
            var slot = kv.Value;
            var page = _pageData[l];
            var o = bi * 4;
            page[o] = 1;
            page[o + 1] = (byte)(slot % _atlasSlotsX);
            page[o + 2] = (byte)(slot / _atlasSlotsX % _atlasSlotsY);
            page[o + 3] = (byte)(slot / (_atlasSlotsX * _atlasSlotsY));
        }
    }

    private void UploadPageTable()
    {
        if (_pageTex == 0) return;
        GL.BindTexture(TextureTarget.Texture3D, _pageTex);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        for (var l = 0; l <= _maxAtlasLevel; l++)
            GL.TexSubImage3D(TextureTarget.Texture3D, 0, 0, 0, _pageZOffset[l], _bgX[l], _bgY[l], _bgZ[l],
                PixelFormat.RgbaInteger, PixelType.UnsignedByte, _pageData[l]);
    }

    private void IntakeStreamedBricks()
    {
        if (_streamTask is not { IsCompleted: true }) return;
        var task = _streamTask;
        _streamTask = null;
        if (!task.IsCompletedSuccessfully)
        {
            Logger.LogWarning($"[SparseCtVolume] Brick stream failed: {task.Exception?.GetBaseException().Message}");
            return;
        }

        // The whole batch is no longer in flight. Clearing it wholesale — rather than only the
        // bricks we manage to commit — is what lets a brick that could not get a slot this frame be
        // requested again next frame, instead of staying wedged in the requested set and leaving
        // that region permanently coarse (the symptom seen while orbiting a large volume).
        _requested.Clear();

        GL.BindTexture(TextureTarget.Texture3D, _atlasTex);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        foreach (var (level, brick, data, max) in task.Result)
        {
            if (_resident.ContainsKey((level, brick))) continue;
            var slot = AcquireSlot();
            if (slot < 0) continue; // atlas momentarily full; re-requested next frame if still wanted
            var sx = slot % _atlasSlotsX;
            var sy = slot / _atlasSlotsX % _atlasSlotsY;
            var sz = slot / (_atlasSlotsX * _atlasSlotsY);
            GL.TexSubImage3D(TextureTarget.Texture3D, 0, sx * BrickTile, sy * BrickTile, sz * BrickTile,
                BrickTile, BrickTile, BrickTile, PixelFormat.Red, PixelType.UnsignedByte, data);
            _slotOwner[slot] = (level, brick);
            _slotUsed[slot] = _frame;
            _resident[(level, brick)] = slot;
            _brickMax[level][brick] = max; // tighten with the exact value
        }
    }

    /// <summary>Touch the finest resident ancestor of a not-yet-resident brick so the fallback the
    /// shader will actually sample stays in the atlas instead of being evicted to the base LOD.</summary>
    private void TouchResidentAncestor(int level, int brick)
    {
        var bx = brick % _bgX[level];
        var by = brick / _bgX[level] % _bgY[level];
        var bz = brick / (_bgX[level] * _bgY[level]);
        for (var l = level + 1; l <= _maxAtlasLevel; l++)
        {
            bx >>= 1; by >>= 1; bz >>= 1;
            var bi = (bz * _bgY[l] + by) * _bgX[l] + bx;
            if (_resident.TryGetValue((l, bi), out var slot)) { _slotUsed[slot] = _frame; return; }
        }
    }

    /// <summary>A free slot, or the least-recently-used one not touched this frame.</summary>
    private int AcquireSlot()
    {
        for (var i = 0; i < _slotCount; i++)
            if (_slotOwner[i].level < 0) return i;

        var victim = -1; var oldest = long.MaxValue;
        for (var i = 0; i < _slotCount; i++)
            if (_slotUsed[i] < _frame && _slotUsed[i] < oldest) { oldest = _slotUsed[i]; victim = i; }
        if (victim < 0) return -1;

        _resident.Remove(_slotOwner[victim]);
        _slotOwner[victim] = (-1, -1);
        return victim;
    }

    private void RequestMisses(List<(int level, int brick)> desired)
    {
        if (_streamTask != null) return;
        var batch = new List<(int level, int brick)>();
        foreach (var key in desired)
        {
            if (_resident.ContainsKey(key) || _requested.Contains(key)) continue;
            batch.Add(key);
            _requested.Add(key);
            if (batch.Count >= StreamBatch) break;
        }
        if (batch.Count == 0) return;

        _streamTask = Task.Run(() =>
        {
            var result = new List<(int, int, byte[], byte)>(batch.Count);
            foreach (var (level, brick) in batch)
            {
                var bgx = _bgX[level]; var bgy = _bgY[level];
                var bx = brick % bgx; var by = brick / bgx % bgy; var bz = brick / (bgx * bgy);
                var data = new byte[BrickTile * BrickTile * BrickTile];
                _dataset.ReadLodRegion(level, bx * BrickCore - Apron, by * BrickCore - Apron,
                    bz * BrickCore - Apron, BrickTile, BrickTile, BrickTile, data);
                byte max = 0;
                for (var i = 0; i < data.Length; i++) if (data[i] > max) max = data[i];
                result.Add((level, brick, data, max));
            }
            return result;
        });
    }

    /// <summary>Binds the sparse textures and sets every uniform the sampling GLSL needs.</summary>
    public void SetUniformsAndBind(int program, int baseUnit, int atlasUnit, int pageUnit)
    {
        void S1(string n, int v) => GL.Uniform1(GL.GetUniformLocation(program, n), v);
        void S1f(string n, float v) => GL.Uniform1(GL.GetUniformLocation(program, n), v);
        void S3(string n, float x, float y, float z) => GL.Uniform3(GL.GetUniformLocation(program, n), x, y, z);

        S1("uSparseOn", Ready ? 1 : 0);
        GL.ActiveTexture(TextureUnit.Texture0 + baseUnit); GL.BindTexture(TextureTarget.Texture3D, _baseTex);
        S1("uBase", baseUnit);
        if (!Ready || _maxAtlasLevel < 0) { S1("uMaxAtlasLevel", -1); return; }

        GL.ActiveTexture(TextureUnit.Texture0 + atlasUnit); GL.BindTexture(TextureTarget.Texture3D, _atlasTex);
        S1("uAtlas", atlasUnit);
        GL.ActiveTexture(TextureUnit.Texture0 + pageUnit); GL.BindTexture(TextureTarget.Texture3D, _pageTex);
        S1("uPage", pageUnit);

        S1("uLevelCount", _levelCount);
        S1("uMaxAtlasLevel", _maxAtlasLevel);
        S1f("uBrickCore", BrickCore);
        S1f("uBrickTile", BrickTile);
        S3("uAtlasDim", _atlasVoxX, _atlasVoxY, _atlasVoxZ);
        S1f("uLodBias", _voxel0World > 0 ? 1f / _voxel0World : 1f); // multiplied by the frame's c below
        for (var l = 0; l <= _maxAtlasLevel && l < MaxLevels; l++)
        {
            S3($"uLodDim[{l}]", _lodW[l], _lodH[l], _lodD[l]);
            S1($"uPageZOff[{l}]", _pageZOffset[l]);
        }
    }

    /// <summary>The frame-dependent LOD scale (screen-projection constant × 1/voxel0). Set alongside
    /// the camera so the shader picks the same level the CPU refinement did.</summary>
    public float LodBias(float viewportHeight, float fovY)
    {
        var c = 2f * MathF.Tan(fovY * 0.5f) / MathF.Max(1f, viewportHeight);
        return c / MathF.Max(1e-6f, _voxel0World);
    }

    public void Dispose()
    {
        foreach (var t in new[] { _baseTex, _atlasTex, _pageTex }) if (t != 0) GL.DeleteTexture(t);
        _baseTex = _atlasTex = _pageTex = 0;
        Ready = false;
    }
}
