// GeoscientistToolkit/UI/AcousticVolume/AcousticAnalysisTool.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
// Using the standard Complex type

namespace GeoscientistToolkit.UI.AcousticVolume;

/// <summary>
///     Provides analysis tools for AcousticVolumeDatasets, including statistics, histograms, and frequency analysis.
/// </summary>
public class AcousticAnalysisTool : IDatasetTools
{
    private bool _analyzeFullVolume;
    private float[] _fftMagnitudes;
    private float[] _fftMagnitudesDb; // For logarithmic scale
    private string _fftTitle = "FFT Spectrum";
    private float[] _histogramData;
    private string _histogramTitle = "Histogram";
    private bool _isCalculating;

    // Data for FFT
    private List<byte> _lineData;

    private float _nyquistFrequency; // The maximum frequency that can be resolved

    // Analysis settings
    private int _selectedVolume = 2; // Default to Combined
    private int _sliceIndex;

    // Results
    private string _statsResult = "No analysis run yet.";
    private bool _useLogarithmicScale = true; // Default to log scale as it's more revealing

    /// <summary>
    ///     Main entry point for drawing the tool's UI.
    /// </summary>
    /// <param name="dataset">The active dataset, which must be an AcousticVolumeDataset.</param>
    public void Draw(Dataset dataset)
    {
        if (dataset is not AcousticVolumeDataset acousticDataset)
        {
            ImGui.TextDisabled("This tool requires an Acoustic Volume Dataset.");
            return;
        }

        // Check for new line data from the viewer on every frame
        if (AcousticInteractionManager.HasNewLine)
        {
            AcousticInteractionManager.HasNewLine = false;
            ExtractLineData(acousticDataset);
        }

        if (ImGui.BeginTabBar("AnalysisTabs"))
        {
            if (ImGui.BeginTabItem("Statistics"))
            {
                DrawStatisticsTab(acousticDataset);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Histogram"))
            {
                DrawHistogramTab(acousticDataset);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Frequency Spectrum"))
            {
                DrawFftTab(acousticDataset);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    /// <summary>
    ///     Draws the UI for the Statistics tab.
    /// </summary>
    private void DrawStatisticsTab(AcousticVolumeDataset dataset)
    {
        DrawCommonControls(dataset);

        if (ImGui.Button("Calculate Statistics", new Vector2(-1, 0)))
            if (!_isCalculating)
            {
                _isCalculating = true;
                // Run calculation in a background thread to keep UI responsive
                Task.Run(() => CalculateStatistics(dataset));
            }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Results:");
        ImGui.TextWrapped(_isCalculating ? "Calculating..." : _statsResult);
    }

    /// <summary>
    ///     Draws the UI for the Histogram tab.
    /// </summary>
    private void DrawHistogramTab(AcousticVolumeDataset dataset)
    {
        DrawCommonControls(dataset);

        if (ImGui.Button("Generate Histogram", new Vector2(-1, 0)))
            if (!_isCalculating)
            {
                _isCalculating = true;
                Task.Run(() => GenerateHistogram(dataset));
            }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Histogram:");

        if (_isCalculating)
            ImGui.Text("Generating...");
        else if (_histogramData != null && _histogramData.Length > 0)
            ImGui.PlotHistogram(_histogramTitle, ref _histogramData[0], _histogramData.Length, 0,
                null, 0, _histogramData.Max(), new Vector2(0, 150));
        else
            ImGui.TextDisabled("No histogram generated.");
    }

    /// <summary>
    ///     Draws the UI for the Frequency Spectrum (FFT) tab.
    /// </summary>
    private void DrawFftTab(AcousticVolumeDataset dataset)
    {
        ImGui.Text("Analyze the frequency content along a user-defined line.");
        ImGui.Separator();
        DrawVolumeSelector(dataset);

        if (AcousticInteractionManager.InteractionMode == ViewerInteractionMode.DrawingLine)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Drawing mode active in viewer window...");
            if (ImGui.Button("Cancel Drawing")) AcousticInteractionManager.CancelLineDrawing();
        }
        else
        {
            if (ImGui.Button("Select Line in Viewer...")) AcousticInteractionManager.StartLineDrawing();
        }

        if (_lineData == null || _lineData.Count == 0)
        {
            ImGui.TextDisabled("\nNo line data extracted. Use the button above to select a line in the viewer.");
        }
        else
        {
            ImGui.Text($"Selected {_lineData.Count} data points.");
            if (ImGui.Button("Compute FFT on Selected Line", new Vector2(-1, 0)))
                if (!_isCalculating)
                {
                    _isCalculating = true;
                    Task.Run(() => ComputeFFT(dataset.VoxelSize));
                }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Spatial Frequency Spectrum:");

        ImGui.Checkbox("Logarithmic Scale (dB)", ref _useLogarithmicScale);

        if (_isCalculating)
            ImGui.Text("Calculating...");
        else if (_useLogarithmicScale && _fftMagnitudesDb != null && _fftMagnitudesDb.Length > 0)
            PlotWithTooltip(_fftTitle + " (dB)", _fftMagnitudesDb, _nyquistFrequency, "cycles/meter");
        else if (!_useLogarithmicScale && _fftMagnitudes != null && _fftMagnitudes.Length > 0)
            PlotWithTooltip(_fftTitle, _fftMagnitudes, _nyquistFrequency, "cycles/meter");
        else
            ImGui.TextDisabled("No FFT computed.");
    }

    /// <summary>
    ///     Draws a plot and shows a tooltip with the X and Y values under the cursor.
    /// </summary>
    private void PlotWithTooltip(string title, float[] data, float xMax, string xUnit)
    {
        ImGui.PlotLines(title, ref data[0], data.Length, 0,
            $"Nyquist: {xMax:F2} {xUnit}", data.Min(), data.Max(), new Vector2(0, 150));

        if (ImGui.IsItemHovered())
        {
            var io = ImGui.GetIO();
            var plotSize = ImGui.GetItemRectSize();
            var mousePos = (io.MousePos - ImGui.GetItemRectMin()) / plotSize;

            if (mousePos.X >= 0 && mousePos.X <= 1)
            {
                var index = (int)(mousePos.X * data.Length);
                if (index >= 0 && index < data.Length)
                {
                    var freq = (float)index / data.Length * xMax;
                    ImGui.SetTooltip($"Frequency: {freq:F3} {xUnit}\nMagnitude: {data[index]:F3}");
                }
            }
        }
    }

    /// <summary>
    ///     Draws UI controls common to multiple tabs.
    /// </summary>
    private void DrawCommonControls(AcousticVolumeDataset dataset)
    {
        DrawVolumeSelector(dataset);
        ImGui.Checkbox("Analyze Full Volume", ref _analyzeFullVolume);

        if (!_analyzeFullVolume)
        {
            var volume = GetSelectedVolume(dataset);
            if (volume != null)
                ImGui.SliderInt("Slice Index (Z)", ref _sliceIndex, 0, volume.Depth - 1);
            else
                ImGui.TextDisabled("Select a valid volume to see slice options.");
        }
    }

    /// <summary>
    ///     Draws the radio buttons for selecting the source wave field volume.
    /// </summary>
    private void DrawVolumeSelector(AcousticVolumeDataset dataset)
    {
        ImGui.Text("Source Volume:");
        if (dataset.PWaveField != null)
        {
            if (ImGui.RadioButton("P-Wave", ref _selectedVolume, 0)) ClearResults();
            ImGui.SameLine();
        }

        if (dataset.SWaveField != null)
        {
            if (ImGui.RadioButton("S-Wave", ref _selectedVolume, 1)) ClearResults();
            ImGui.SameLine();
        }

        if (dataset.CombinedWaveField != null)
            if (ImGui.RadioButton("Combined", ref _selectedVolume, 2))
                ClearResults();
        if (dataset.DamageField != null)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton("Damage", ref _selectedVolume, 3)) ClearResults();
        }

        ImGui.NewLine();
    }

    private void ClearResults()
    {
        _lineData = null;
        _fftMagnitudes = null;
        _fftMagnitudesDb = null;
        _histogramData = null;
        _statsResult = "No analysis run yet.";
    }

    /// <summary>
    ///     Returns the ChunkedVolume object corresponding to the user's selection.
    /// </summary>
    private ChunkedVolume GetSelectedVolume(AcousticVolumeDataset dataset)
    {
        return _selectedVolume switch
        {
            0 => dataset.PWaveField,
            1 => dataset.SWaveField,
            2 => dataset.CombinedWaveField,
            3 => dataset.DamageField,
            _ => dataset.CombinedWaveField // Default fallback
        };
    }

    /// <summary>
    ///     Asynchronously calculates statistics for the selected volume/slice.
    /// </summary>
    private void CalculateStatistics(AcousticVolumeDataset dataset)
    {
        var volume = GetSelectedVolume(dataset);
        if (volume == null)
        {
            _statsResult = "Selected volume is not available.";
            _isCalculating = false;
            return;
        }

        long count = 0;
        double sum = 0;
        double sumOfSquares = 0;
        byte min = 255;
        byte max = 0;

        if (_analyzeFullVolume)
        {
            for (var z = 0; z < volume.Depth; z++)
            {
                var slice = new byte[volume.Width * volume.Height];
                volume.ReadSliceZ(z, slice);
                ProcessSlice(slice);
            }
        }
        else
        {
            var slice = new byte[volume.Width * volume.Height];
            volume.ReadSliceZ(_sliceIndex, slice);
            ProcessSlice(slice);
        }

        void ProcessSlice(byte[] slice)
        {
            for (var i = 0; i < slice.Length; i++)
            {
                var val = slice[i];
                sum += val;
                sumOfSquares += val * val;
                if (val < min) min = val;
                if (val > max) max = val;
            }

            count += slice.Length;
        }

        if (count == 0)
        {
            _statsResult = "No data to analyze.";
            _isCalculating = false;
            return;
        }

        var mean = sum / count;
        var variance = sumOfSquares / count - mean * mean;
        var stdDev = Math.Sqrt(variance);

        _statsResult = $"Min Value: {min}\n" +
                       $"Max Value: {max}\n" +
                       $"Mean: {mean:F2}\n" +
                       $"Standard Deviation: {stdDev:F2}\n" +
                       $"Voxel Count: {count:N0}";
        _isCalculating = false;
    }

    /// <summary>
    ///     Asynchronously generates a histogram for the selected volume/slice.
    /// </summary>
    private void GenerateHistogram(AcousticVolumeDataset dataset)
    {
        var volume = GetSelectedVolume(dataset);
        if (volume == null)
        {
            _histogramData = null;
            _isCalculating = false;
            return;
        }

        var bins = new int[256];

        if (_analyzeFullVolume)
        {
            for (var z = 0; z < volume.Depth; z++)
            {
                var slice = new byte[volume.Width * volume.Height];
                volume.ReadSliceZ(z, slice);
                foreach (var val in slice) bins[val]++;
            }
        }
        else
        {
            var slice = new byte[volume.Width * volume.Height];
            volume.ReadSliceZ(_sliceIndex, slice);
            foreach (var val in slice) bins[val]++;
        }

        _histogramData = bins.Select(b => (float)b).ToArray();
        _histogramTitle = $"Histogram ({(_analyzeFullVolume ? "Full Volume" : $"Slice {_sliceIndex}")})";
        _isCalculating = false;
    }

    /// <summary>
    ///     Extracts 1D data along a line defined in the viewer using Bresenham's line algorithm.
    /// </summary>
    private void ExtractLineData(AcousticVolumeDataset dataset)
    {
        var volume = GetSelectedVolume(dataset);
        if (volume == null) return;

        // Get coordinates from the interaction manager
        var x1 = (int)AcousticInteractionManager.LineStartPoint.X;
        var y1 = (int)AcousticInteractionManager.LineStartPoint.Y;
        var x2 = (int)AcousticInteractionManager.LineEndPoint.X;
        var y2 = (int)AcousticInteractionManager.LineEndPoint.Y;
        var slice_coord = AcousticInteractionManager.LineSliceIndex;

        _lineData = new List<byte>();

        // Bresenham's line algorithm
        int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
        int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
        int err = dx + dy, e2;

        while (true)
        {
            // Sample the volume based on which view the line was drawn in
            switch (AcousticInteractionManager.LineViewIndex)
            {
                case 0: // XY View (volume coords: x=x1, y=y1, z=slice_coord)
                    if (x1 >= 0 && x1 < volume.Width && y1 >= 0 && y1 < volume.Height)
                        _lineData.Add(volume[x1, y1, slice_coord]);
                    break;
                case 1: // XZ View (volume coords: x=x1, y=slice_coord, z=y1)
                    if (x1 >= 0 && x1 < volume.Width && y1 >= 0 && y1 < volume.Depth)
                        _lineData.Add(volume[x1, slice_coord, y1]);
                    break;
                case 2: // YZ View (volume coords: x=slice_coord, y=x1, z=y1)
                    if (x1 >= 0 && x1 < volume.Height && y1 >= 0 && y1 < volume.Depth)
                        _lineData.Add(volume[slice_coord, x1, y1]);
                    break;
            }

            if (x1 == x2 && y1 == y2) break;
            e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x1 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y1 += sy;
            }
        }

        Logger.Log($"[AcousticAnalysisTool] Extracted {_lineData.Count} data points for FFT.");
    }

    /// <summary>
    ///     Asynchronously computes the Fast Fourier Transform of the extracted line data.
    /// </summary>
    private void ComputeFFT(double voxelSize)
    {
        if (_lineData == null || _lineData.Count < 2)
        {
            _fftMagnitudes = null;
            _fftMagnitudesDb = null;
            _isCalculating = false;
            return;
        }

        var originalN = _lineData.Count;

        // FFT is most efficient for lengths that are a power of 2. Pad with zeros.
        var n = 1;
        while (n < originalN) n <<= 1;

        // Convert byte data to complex numbers for the transform
        var complexData = new Complex[n];
        for (var i = 0; i < n; i++)
            if (i < originalN)
                complexData[i] = new Complex(_lineData[i], 0);
            else
                complexData[i] = new Complex(0, 0); // Zero padding

        // Perform the FFT
        Fft(complexData, false); // Use the production-ready iterative FFT

        // We only need the first half of the spectrum (due to symmetry)
        var halfN = n / 2;
        _fftMagnitudes = new float[halfN];
        for (var i = 0; i < halfN; i++)
            // The result is the magnitude of the complex number, normalized by N
            _fftMagnitudes[i] = (float)(complexData[i].Magnitude / n);

        // Also compute a dB scale version for better visualization
        _fftMagnitudesDb = new float[halfN];
        var epsilon = 1e-12f; // To avoid log(0)
        for (var i = 0; i < halfN; i++)
            // Convert magnitude to dB
            _fftMagnitudesDb[i] = 20.0f * (float)Math.Log10(Math.Max(_fftMagnitudes[i], epsilon));

        // Calculate the physical frequency range
        var samplingRate = 1.0 / voxelSize; // samples per meter
        _nyquistFrequency = (float)(samplingRate / 2.0);

        _fftTitle = $"FFT Spectrum ({originalN} points, padded to {n})";
        _isCalculating = false;
    }

    /// <summary>
    ///     A robust, iterative, in-place Cooley-Tukey FFT algorithm.
    ///     This is a production-quality replacement for the simpler recursive version.
    /// </summary>
    /// <param name="x">Input complex data. Length MUST be a power of 2.</param>
    /// <param name="inverse">If true, computes the inverse FFT.</param>
    public static void Fft(Complex[] x, bool inverse)
    {
        var n = x.Length;
        if (n <= 1) return;

        // Bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j) (x[i], x[j]) = (x[j], x[i]); // Swap
        }

        // Cooley-Tukey iterations
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = (inverse ? 2 : -2) * Math.PI / len;
            var wlen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < n; i += len)
            {
                var w = new Complex(1, 0);
                for (var j = 0; j < len / 2; j++)
                {
                    var u = x[i + j];
                    var v = x[i + j + len / 2] * w;
                    x[i + j] = u + v;
                    x[i + j + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }

        // If inverse, scale by 1/N
        if (inverse)
            for (var i = 0; i < n; i++)
                x[i] /= n;
    }
}