// GeoscientistToolkit/UI/AcousticVolume/AcousticAnalysisTool.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace GeoscientistToolkit.UI.AcousticVolume
{
    /// <summary>
    /// Provides analysis tools for AcousticVolumeDatasets, including statistics, histograms, and frequency analysis.
    /// </summary>
    public class AcousticAnalysisTool : IDatasetTools
    {
        // Analysis settings
        private int _selectedVolume = 2; // Default to Combined
        private bool _analyzeFullVolume = false;
        private int _sliceIndex = 0;
        
        // Results
        private string _statsResult = "No analysis run yet.";
        private float[] _histogramData;
        private string _histogramTitle = "Histogram";
        private float[] _fftData;
        private float[] _fftDataDb; // For logarithmic scale
        private string _fftTitle = "FFT Spectrum";
        private bool _isCalculating = false;
        private bool _useLogarithmicScale = true; // Default to log scale as it's more revealing

        // Data for FFT
        private List<byte> _lineData;

        /// <summary>
        /// Main entry point for drawing the tool's UI.
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
        /// Draws the UI for the Statistics tab.
        /// </summary>
        private void DrawStatisticsTab(AcousticVolumeDataset dataset)
        {
            DrawCommonControls(dataset);

            if (ImGui.Button("Calculate Statistics", new Vector2(-1, 0)))
            {
                if (!_isCalculating)
                {
                    _isCalculating = true;
                    // Run calculation in a background thread to keep UI responsive
                    Task.Run(() => CalculateStatistics(dataset));
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Results:");
            ImGui.TextWrapped(_isCalculating ? "Calculating..." : _statsResult);
        }

        /// <summary>
        /// Draws the UI for the Histogram tab.
        /// </summary>
        private void DrawHistogramTab(AcousticVolumeDataset dataset)
        {
            DrawCommonControls(dataset);

            if (ImGui.Button("Generate Histogram", new Vector2(-1, 0)))
            {
                if (!_isCalculating)
                {
                    _isCalculating = true;
                    Task.Run(() => GenerateHistogram(dataset));
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Histogram:");

            if (_isCalculating)
            {
                ImGui.Text("Generating...");
            }
            else if (_histogramData != null && _histogramData.Length > 0)
            {
                ImGui.PlotHistogram(_histogramTitle, ref _histogramData[0], _histogramData.Length, 0,
                    null, 0, _histogramData.Max(), new Vector2(0, 150));
            }
            else
            {
                ImGui.TextDisabled("No histogram generated.");
            }
        }

        /// <summary>
        /// Draws the UI for the Frequency Spectrum (FFT) tab.
        /// </summary>
        private void DrawFftTab(AcousticVolumeDataset dataset)
        {
            ImGui.Text("Analyze the frequency content along a user-defined line.");
            ImGui.Separator();
            DrawVolumeSelector(dataset);
            
            if (AcousticInteractionManager.InteractionMode == ViewerInteractionMode.DrawingLine)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Drawing mode active in viewer window...");
                if (ImGui.Button("Cancel Drawing"))
                {
                    AcousticInteractionManager.CancelLineDrawing();
                }
            }
            else
            {
                if (ImGui.Button("Select Line in Viewer..."))
                {
                    AcousticInteractionManager.StartLineDrawing();
                }
            }
            
            if (_lineData == null || _lineData.Count == 0)
            {
                ImGui.TextDisabled("\nNo line data extracted. Use the button above to select a line in the viewer.");
            }
            else
            {
                ImGui.Text($"Selected {_lineData.Count} data points.");
                if (ImGui.Button("Compute FFT on Selected Line", new Vector2(-1, 0)))
                {
                    if (!_isCalculating)
                    {
                        _isCalculating = true;
                        Task.Run(ComputeFFT);
                    }
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Frequency Spectrum:");

            ImGui.Checkbox("Logarithmic Scale (dB)", ref _useLogarithmicScale);

            if (_isCalculating)
            {
                ImGui.Text("Calculating...");
            }
            else if (_useLogarithmicScale && _fftDataDb != null && _fftDataDb.Length > 0)
            {
                ImGui.PlotLines(_fftTitle + " (dB)", ref _fftDataDb[0], _fftDataDb.Length, 0, 
                    "Frequency ->", _fftDataDb.Min(), _fftDataDb.Max(), new Vector2(0, 150));
            }
            else if (!_useLogarithmicScale && _fftData != null && _fftData.Length > 0)
            {
                 ImGui.PlotLines(_fftTitle, ref _fftData[0], _fftData.Length, 0,
                    "Frequency ->", 0, _fftData.Max(), new Vector2(0, 150));
            }
            else
            {
                ImGui.TextDisabled("No FFT computed.");
            }
        }
        
        /// <summary>
        /// Draws UI controls common to multiple tabs.
        /// </summary>
        private void DrawCommonControls(AcousticVolumeDataset dataset)
        {
            DrawVolumeSelector(dataset);
            ImGui.Checkbox("Analyze Full Volume", ref _analyzeFullVolume);

            if (!_analyzeFullVolume)
            {
                var volume = GetSelectedVolume(dataset);
                if (volume != null)
                {
                    ImGui.SliderInt("Slice Index (Z)", ref _sliceIndex, 0, volume.Depth - 1);
                }
                else
                {
                     ImGui.TextDisabled("Select a valid volume to see slice options.");
                }
            }
        }
        
        /// <summary>
        /// Draws the radio buttons for selecting the source wave field volume.
        /// </summary>
        private void DrawVolumeSelector(AcousticVolumeDataset dataset)
        {
             ImGui.Text("Source Volume:");
            if (dataset.PWaveField != null) { if(ImGui.RadioButton("P-Wave", ref _selectedVolume, 0)) ClearResults(); ImGui.SameLine(); }
            if (dataset.SWaveField != null) { if(ImGui.RadioButton("S-Wave", ref _selectedVolume, 1)) ClearResults(); ImGui.SameLine(); }
            if (dataset.CombinedWaveField != null) { if(ImGui.RadioButton("Combined", ref _selectedVolume, 2)) ClearResults(); }
            if (dataset.DamageField != null) { ImGui.SameLine(); if(ImGui.RadioButton("Damage", ref _selectedVolume, 3)) ClearResults(); }
            ImGui.NewLine();
        }

        private void ClearResults()
        {
             _lineData = null;
             _fftData = null;
             _fftDataDb = null;
             _histogramData = null;
             _statsResult = "No analysis run yet.";
        }

        /// <summary>
        /// Returns the ChunkedVolume object corresponding to the user's selection.
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
        /// Asynchronously calculates statistics for the selected volume/slice.
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
                for (int z = 0; z < volume.Depth; z++)
                {
                    byte[] slice = new byte[volume.Width * volume.Height];
                    volume.ReadSliceZ(z, slice);
                    ProcessSlice(slice);
                }
            }
            else
            {
                byte[] slice = new byte[volume.Width * volume.Height];
                volume.ReadSliceZ(_sliceIndex, slice);
                ProcessSlice(slice);
            }

            void ProcessSlice(byte[] slice)
            {
                for (int i = 0; i < slice.Length; i++)
                {
                    byte val = slice[i];
                    sum += val;
                    sumOfSquares += val * val;
                    if (val < min) min = val;
                    if (val > max) max = val;
                }
                count += slice.Length;
            }

            double mean = sum / count;
            double variance = (sumOfSquares / count) - (mean * mean);
            double stdDev = Math.Sqrt(variance);

            _statsResult = $"Min Value: {min}\n" +
                           $"Max Value: {max}\n" +
                           $"Mean: {mean:F2}\n" +
                           $"Standard Deviation: {stdDev:F2}\n" +
                           $"Voxel Count: {count:N0}";
            _isCalculating = false;
        }

        /// <summary>
        /// Asynchronously generates a histogram for the selected volume/slice.
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

            int[] bins = new int[256];
            
            if (_analyzeFullVolume)
            {
                for (int z = 0; z < volume.Depth; z++)
                {
                    byte[] slice = new byte[volume.Width * volume.Height];
                    volume.ReadSliceZ(z, slice);
                    foreach(byte val in slice) bins[val]++;
                }
            }
            else
            {
                byte[] slice = new byte[volume.Width * volume.Height];
                volume.ReadSliceZ(_sliceIndex, slice);
                foreach(byte val in slice) bins[val]++;
            }
            
            _histogramData = bins.Select(b => (float)b).ToArray();
            _histogramTitle = $"Histogram ({(_analyzeFullVolume ? "Full Volume" : $"Slice {_sliceIndex}")})";
            _isCalculating = false;
        }
        
        /// <summary>
        /// Extracts 1D data along a line defined in the viewer using Bresenham's line algorithm.
        /// </summary>
        private void ExtractLineData(AcousticVolumeDataset dataset)
        {
            var volume = GetSelectedVolume(dataset);
            if (volume == null) return;

            // Get coordinates from the interaction manager
            int x1 = (int)AcousticInteractionManager.LineStartPoint.X;
            int y1 = (int)AcousticInteractionManager.LineStartPoint.Y;
            int x2 = (int)AcousticInteractionManager.LineEndPoint.X;
            int y2 = (int)AcousticInteractionManager.LineEndPoint.Y;
            int slice_coord = AcousticInteractionManager.LineSliceIndex;

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
                if (e2 >= dy) { err += dy; x1 += sx; }
                if (e2 <= dx) { err += dx; y1 += sy; }
            }
            Logger.Log($"[AcousticAnalysisTool] Extracted {_lineData.Count} data points for FFT.");
        }

        /// <summary>
        /// Asynchronously computes the Discrete Fourier Transform of the extracted line data.
        /// </summary>
        private void ComputeFFT()
        {
            if (_lineData == null || _lineData.Count < 2)
            {
                _fftData = null;
                _isCalculating = false;
                return;
            }

            int n = _lineData.Count;
            // Convert byte data to complex numbers for the transform
            var complexData = _lineData.Select(val => new Complex(val, 0)).ToArray();
            var spectrum = new Complex[n];

            // Perform the DFT
            for (int k = 0; k < n; k++)
            {
                spectrum[k] = new Complex(0, 0);
                for (int j = 0; j < n; j++)
                {
                    double angle = 2 * Math.PI * k * j / n;
                    spectrum[k] += complexData[j] * Complex.Exp(new Complex(0, -angle));
                }
            }
            
            // We only need the first half of the spectrum (due to symmetry)
            int halfN = n / 2;
            _fftData = new float[halfN];
            for (int i = 0; i < halfN; i++)
            {
                // The result is the magnitude of the complex number
                _fftData[i] = (float)spectrum[i].Magnitude;
            }

            // Also compute a dB scale version for better visualization
            _fftDataDb = new float[halfN];
            float epsilon = 1e-12f; // To avoid log(0)
            for (int i = 0; i < halfN; i++)
            {
                // Convert magnitude to dB
                _fftDataDb[i] = 20.0f * (float)Math.Log10(Math.Max(_fftData[i], epsilon));
            }

            _fftTitle = $"FFT Spectrum ({n} points)";
            _isCalculating = false;
        }
    }

    /// <summary>
    /// Helper struct for representing complex numbers used in DFT calculations.
    /// </summary>
    public struct Complex
    {
        public double Real, Imaginary;
        public Complex(double real, double imaginary) { Real = real; Imaginary = imaginary; }
        public double Magnitude => Math.Sqrt(Real * Real + Imaginary * Imaginary);
        public static Complex operator +(Complex a, Complex b) => new Complex(a.Real + b.Real, a.Imaginary + b.Imaginary);
        public static Complex operator *(Complex a, Complex b) => new Complex(a.Real * b.Real - a.Imaginary * b.Imaginary, a.Real * b.Imaginary + a.Imaginary * b.Real);
        public static Complex Exp(Complex z)
        {
            double expReal = Math.Exp(z.Real);
            return new Complex(expReal * Math.Cos(z.Imaginary), expReal * Math.Sin(z.Imaginary));
        }
    }
}