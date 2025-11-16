// GeoscientistToolkit/Data/Media/AudioDatasetViewer.cs

using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using NAudio.Wave;
using NAudio.Dsp;

namespace GeoscientistToolkit.Data.Media;

/// <summary>
/// Viewer for audio datasets with waveform display and playback controls
/// Thread-safe implementation with null checking
/// </summary>
public class AudioDatasetViewer : IDatasetViewer
{
    private readonly AudioDataset _dataset;
    private readonly object _playbackLock = new object();

    // Playback state
    private WaveOutEvent _waveOut;
    private AudioFileReader _audioFileReader;
    private bool _isPlaying = false;
    private double _currentTime = 0.0;
    private DateTime _lastUpdateTime = DateTime.Now;

    // UI state
    private float _playbackSpeed = 1.0f;
    private bool _loop = false;
    private float _waveformZoom = 1.0f;
    private float _waveformScroll = 0.0f;
    private Vector4 _waveformColor = new Vector4(0.2f, 0.8f, 1.0f, 1.0f);

    // Display modes
    private enum DisplayMode
    {
        Waveform,
        Spectrogram,
        FrequencySpectrum,
        Combined
    }
    private DisplayMode _displayMode = DisplayMode.Waveform;

    // Spectrum analysis
    private float[] _spectrumData;
    private int _fftSize = 2048;
    private float _spectrumScale = 1.0f;
    private bool _logScale = true;
    private Complex[] _fftBuffer;
    private float[] _sampleBuffer;
    private int _sampleBufferPos = 0;

    // Spectrogram data
    private List<float[]> _spectrogramData;
    private int _spectrogramMaxLines = 512;
    private object _spectrogramLock = new object();

    // Volume control
    private float _volume = 0.7f;

    public AudioDatasetViewer(AudioDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));

        // Initialize FFT buffers
        _fftBuffer = new Complex[_fftSize];
        _sampleBuffer = new float[_fftSize];
        _spectrumData = new float[_fftSize / 2];
        _spectrogramData = new List<float[]>();
    }

    public void DrawToolbarControls()
    {
        if (_dataset == null) return;

        // Playback controls
        var playIcon = _isPlaying ? "⏸" : "▶";
        if (ImGui.Button($"{playIcon} {(_isPlaying ? "Pause" : "Play")}"))
        {
            TogglePlayback();
        }

        ImGui.SameLine();
        if (ImGui.Button("⏹ Stop"))
        {
            StopPlayback();
        }

        ImGui.SameLine();
        if (ImGui.Button("⏮ Start"))
        {
            SeekToStart();
        }

        ImGui.SameLine();
        if (ImGui.Button("⏭ End"))
        {
            SeekToEnd();
        }

        ImGui.SameLine();
        ImGui.Checkbox("Loop", ref _loop);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.SliderFloat("Volume", ref _volume, 0.0f, 1.0f, "%.2f"))
        {
            UpdateVolume();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.SliderFloat("Speed", ref _playbackSpeed, 0.5f, 2.0f, "%.2fx"))
        {
            _playbackSpeed = Math.Clamp(_playbackSpeed, 0.5f, 2.0f);
            // Note: Speed change requires recreating the audio stream
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        var displayModeNames = Enum.GetNames(typeof(DisplayMode));
        var currentMode = (int)_displayMode;
        if (ImGui.Combo("View", ref currentMode, displayModeNames, displayModeNames.Length))
        {
            _displayMode = (DisplayMode)currentMode;
        }

        if (_displayMode == DisplayMode.FrequencySpectrum || _displayMode == DisplayMode.Spectrogram)
        {
            ImGui.SameLine();
            ImGui.Checkbox("Log Scale", ref _logScale);
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"{FormatTime(_currentTime)}/{FormatTime(_dataset.DurationSeconds)}");

        if (_isPlaying)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "●");
        }
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        if (_dataset == null) return;

        try
        {
            _dataset.Load();

            if (_dataset.IsMissing)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Audio file not found: {_dataset.FilePath}");
                return;
            }

            // Update playback position
            UpdatePlaybackPosition();

            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            var dl = ImGui.GetWindowDrawList();
            var io = ImGui.GetIO();

            // Draw background
            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF1A1A1A);

            // Draw based on display mode
            switch (_displayMode)
            {
                case DisplayMode.Waveform:
                    if (_dataset.WaveformData != null && _dataset.WaveformData.Length > 0)
                        DrawWaveform(canvasPos, canvasSize, dl, io);
                    else
                        DrawAudioInfo(canvasPos, canvasSize);
                    break;

                case DisplayMode.Spectrogram:
                    DrawSpectrogram(canvasPos, canvasSize, dl, io);
                    break;

                case DisplayMode.FrequencySpectrum:
                    DrawFrequencySpectrum(canvasPos, canvasSize, dl, io);
                    break;

                case DisplayMode.Combined:
                    DrawCombinedView(canvasPos, canvasSize, dl, io);
                    break;
            }

            // Draw playback position indicator
            DrawPlaybackIndicator(canvasPos, canvasSize, dl);

            // Draw timeline
            DrawTimeline(canvasPos, canvasSize, dl, io);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AudioViewer] Error in DrawContent: {ex.Message}");
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error: {ex.Message}");
        }
    }

    private void DrawWaveform(Vector2 canvasPos, Vector2 canvasSize, ImDrawListPtr dl, ImGuiIOPtr io)
    {
        if (_dataset.WaveformData == null || _dataset.WaveformData.Length == 0) return;

        // Reserve space for timeline at bottom
        var waveformHeight = canvasSize.Y - 60;
        var waveformRect = new Vector2(canvasSize.X, waveformHeight);

        dl.PushClipRect(canvasPos, canvasPos + waveformRect, true);

        var centerY = canvasPos.Y + waveformHeight * 0.5f;
        var maxAmplitude = waveformHeight * 0.45f;

        // Calculate visible range based on zoom and scroll
        var totalSamples = _dataset.WaveformData.Length;
        var visibleSamples = (int)(totalSamples / _waveformZoom);
        var startSample = (int)(_waveformScroll * (totalSamples - visibleSamples));
        startSample = Math.Clamp(startSample, 0, totalSamples - visibleSamples);
        var endSample = Math.Min(startSample + visibleSamples, totalSamples);

        // Draw waveform
        var samplesPerPixel = Math.Max(1, visibleSamples / (int)canvasSize.X);
        var color = ImGui.ColorConvertFloat4ToU32(_waveformColor);

        for (int x = 0; x < (int)canvasSize.X; x++)
        {
            var sampleIndex = startSample + (int)((float)x / canvasSize.X * visibleSamples);
            if (sampleIndex >= endSample) break;

            // Average samples for this pixel
            float maxVal = 0;
            for (int s = 0; s < samplesPerPixel && sampleIndex + s < endSample; s++)
            {
                var val = Math.Abs(_dataset.WaveformData[sampleIndex + s]);
                maxVal = Math.Max(maxVal, val);
            }

            var amplitude = maxVal * maxAmplitude;
            var x1 = canvasPos.X + x;
            var y1 = centerY - amplitude;
            var y2 = centerY + amplitude;

            dl.AddLine(new Vector2(x1, y1), new Vector2(x1, y2), color, 1.0f);
        }

        // Draw center line
        dl.AddLine(new Vector2(canvasPos.X, centerY), new Vector2(canvasPos.X + canvasSize.X, centerY), 0xFF404040);

        dl.PopClipRect();

        // Handle zoom and scroll
        var waveformArea = canvasPos + waveformRect;
        var isMouseOver = io.MousePos.X >= canvasPos.X && io.MousePos.X < waveformArea.X &&
                          io.MousePos.Y >= canvasPos.Y && io.MousePos.Y < waveformArea.Y;

        if (isMouseOver && Math.Abs(io.MouseWheel) > 0.0f)
        {
            _waveformZoom = Math.Clamp(_waveformZoom + io.MouseWheel * 0.2f, 1.0f, 20.0f);
        }

        if (isMouseOver && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
        {
            var dragDelta = io.MouseDelta.X / canvasSize.X;
            _waveformScroll = Math.Clamp(_waveformScroll - dragDelta, 0.0f, 1.0f);
        }
    }

    private void DrawSpectrogram(Vector2 canvasPos, Vector2 canvasSize, ImDrawListPtr dl, ImGuiIOPtr io)
    {
        var waveformHeight = canvasSize.Y - 60;
        var spectrogramRect = new Vector2(canvasSize.X, waveformHeight);

        dl.PushClipRect(canvasPos, canvasPos + spectrogramRect, true);

        // Update spectrogram if playing
        if (_isPlaying && _audioFileReader != null)
        {
            UpdateSpectrogram();
        }

        // Draw spectrogram data
        lock (_spectrogramLock)
        {
            if (_spectrogramData != null && _spectrogramData.Count > 0)
            {
                var lineWidth = canvasSize.X / _spectrogramData.Count;
                var freqBins = _spectrogramData[0].Length;
                var binHeight = waveformHeight / freqBins;

                for (int x = 0; x < _spectrogramData.Count; x++)
                {
                    var spectrum = _spectrogramData[x];
                    for (int y = 0; y < freqBins; y++)
                    {
                        var magnitude = spectrum[y];

                        if (_logScale)
                        {
                            magnitude = (float)Math.Log10(magnitude * 100 + 1) / 2.0f;
                        }

                        magnitude = Math.Clamp(magnitude, 0f, 1f);

                        // Color mapping: blue (low) -> green -> yellow -> red (high)
                        var color = GetSpectrogramColor(magnitude);

                        var rectMin = new Vector2(
                            canvasPos.X + x * lineWidth,
                            canvasPos.Y + waveformHeight - (y + 1) * binHeight
                        );
                        var rectMax = new Vector2(
                            canvasPos.X + (x + 1) * lineWidth,
                            canvasPos.Y + waveformHeight - y * binHeight
                        );

                        dl.AddRectFilled(rectMin, rectMax, color);
                    }
                }
            }
            else
            {
                var textPos = canvasPos + spectrogramRect * 0.5f - new Vector2(80, 10);
                dl.AddText(textPos, 0xFFFFFFFF, "Spectrogram View");
                dl.AddText(textPos + new Vector2(0, 20), 0xFFAAAAAA, "(Play to see spectrogram)");
            }
        }

        // Draw frequency labels
        var maxFreq = _dataset.SampleRate / 2; // Nyquist frequency
        var freqSteps = 5;
        for (int i = 0; i <= freqSteps; i++)
        {
            var freq = (maxFreq / freqSteps) * i;
            var y = canvasPos.Y + waveformHeight - (waveformHeight / freqSteps) * i;
            var freqText = freq >= 1000 ? $"{freq / 1000:F1}kHz" : $"{freq:F0}Hz";
            dl.AddText(new Vector2(canvasPos.X + 5, y - 10), 0xFF808080, freqText);
        }

        dl.PopClipRect();
    }

    private uint GetSpectrogramColor(float magnitude)
    {
        // Color gradient: black -> blue -> cyan -> green -> yellow -> red
        byte r, g, b;

        if (magnitude < 0.2f)
        {
            // Black to blue
            var t = magnitude / 0.2f;
            r = 0;
            g = 0;
            b = (byte)(t * 255);
        }
        else if (magnitude < 0.4f)
        {
            // Blue to cyan
            var t = (magnitude - 0.2f) / 0.2f;
            r = 0;
            g = (byte)(t * 255);
            b = 255;
        }
        else if (magnitude < 0.6f)
        {
            // Cyan to green
            var t = (magnitude - 0.4f) / 0.2f;
            r = 0;
            g = 255;
            b = (byte)((1 - t) * 255);
        }
        else if (magnitude < 0.8f)
        {
            // Green to yellow
            var t = (magnitude - 0.6f) / 0.2f;
            r = (byte)(t * 255);
            g = 255;
            b = 0;
        }
        else
        {
            // Yellow to red
            var t = (magnitude - 0.8f) / 0.2f;
            r = 255;
            g = (byte)((1 - t) * 255);
            b = 0;
        }

        return 0xFF000000 | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    private void DrawFrequencySpectrum(Vector2 canvasPos, Vector2 canvasSize, ImDrawListPtr dl, ImGuiIOPtr io)
    {
        var waveformHeight = canvasSize.Y - 60;
        var spectrumRect = new Vector2(canvasSize.X, waveformHeight);

        dl.PushClipRect(canvasPos, canvasPos + spectrumRect, true);

        // Update spectrum if playing
        if (_isPlaying && _audioFileReader != null)
        {
            UpdateSpectrum();
        }

        // Draw spectrum
        if (_spectrumData != null && _spectrumData.Length > 0)
        {
            var centerY = canvasPos.Y + waveformHeight;
            var color = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 1.0f, 0.4f, 1.0f));

            for (int i = 0; i < _spectrumData.Length && i < (int)canvasSize.X; i++)
            {
                var magnitude = _spectrumData[i];
                if (_logScale)
                {
                    magnitude = (float)Math.Log10(magnitude * 100 + 1) / 2.0f; // Log scale
                }

                var height = magnitude * waveformHeight * _spectrumScale;
                height = Math.Clamp(height, 0, waveformHeight);

                var x = canvasPos.X + (canvasSize.X / _spectrumData.Length) * i;
                var barWidth = Math.Max(1, canvasSize.X / _spectrumData.Length);

                dl.AddRectFilled(
                    new Vector2(x, centerY - height),
                    new Vector2(x + barWidth, centerY),
                    color
                );
            }

            // Draw frequency labels
            var maxFreq = _dataset.SampleRate / 2;
            var freqMarkers = new[] { 100, 500, 1000, 2000, 5000, 10000, 20000 };
            foreach (var freq in freqMarkers)
            {
                if (freq > maxFreq) break;
                var x = canvasPos.X + (canvasSize.X * freq / maxFreq);
                var label = freq >= 1000 ? $"{freq / 1000}k" : $"{freq}";
                dl.AddLine(new Vector2(x, canvasPos.Y), new Vector2(x, canvasPos.Y + waveformHeight), 0x40FFFFFF);
                dl.AddText(new Vector2(x - 15, canvasPos.Y + 5), 0xFF808080, label);
            }
        }
        else
        {
            var textPos = canvasPos + spectrumRect * 0.5f - new Vector2(100, 10);
            dl.AddText(textPos, 0xFFFFFFFF, "Frequency Spectrum");
            dl.AddText(textPos + new Vector2(0, 20), 0xFFAAAAAA, "(Play to see spectrum)");
        }

        dl.PopClipRect();
    }

    private void DrawCombinedView(Vector2 canvasPos, Vector2 canvasSize, ImDrawListPtr dl, ImGuiIOPtr io)
    {
        var halfHeight = (canvasSize.Y - 60) * 0.5f;

        // Draw waveform on top half
        if (_dataset.WaveformData != null && _dataset.WaveformData.Length > 0)
        {
            var waveformSize = new Vector2(canvasSize.X, halfHeight);
            DrawWaveform(canvasPos, waveformSize, dl, io);
        }

        // Draw spectrum on bottom half
        var spectrumPos = canvasPos + new Vector2(0, halfHeight);
        var spectrumSize = new Vector2(canvasSize.X, halfHeight);

        dl.AddLine(
            new Vector2(canvasPos.X, spectrumPos.Y),
            new Vector2(canvasPos.X + canvasSize.X, spectrumPos.Y),
            0xFF404040,
            2.0f
        );

        DrawFrequencySpectrum(spectrumPos, spectrumSize, dl, io);
    }

    private void UpdateSpectrum()
    {
        if (_audioFileReader == null) return;

        try
        {
            // Read samples from current position
            var buffer = new float[_fftSize];
            var currentPos = _audioFileReader.Position;
            var samplesRead = _audioFileReader.Read(buffer, 0, _fftSize);
            _audioFileReader.Position = currentPos; // Reset position

            if (samplesRead > 0)
            {
                // Perform FFT
                PerformFFT(buffer, samplesRead);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AudioViewer] FFT update failed: {ex.Message}");
        }
    }

    private void UpdateSpectrogram()
    {
        if (_audioFileReader == null) return;

        try
        {
            // Read samples from current position
            var buffer = new float[_fftSize];
            var currentPos = _audioFileReader.Position;
            var samplesRead = _audioFileReader.Read(buffer, 0, _fftSize);
            _audioFileReader.Position = currentPos; // Reset position

            if (samplesRead > 0)
            {
                // Perform FFT and add to spectrogram
                var spectrum = PerformFFTForSpectrogram(buffer, samplesRead);

                lock (_spectrogramLock)
                {
                    _spectrogramData.Add(spectrum);

                    // Limit spectrogram history
                    if (_spectrogramData.Count > _spectrogramMaxLines)
                    {
                        _spectrogramData.RemoveAt(0);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AudioViewer] Spectrogram update failed: {ex.Message}");
        }
    }

    private void PerformFFT(float[] samples, int count)
    {
        // Prepare FFT buffer with Hanning window
        for (int i = 0; i < _fftSize; i++)
        {
            var windowValue = i < count ? samples[i] : 0f;

            // Apply Hanning window to reduce spectral leakage
            var hannWindow = 0.5f * (1 - (float)Math.Cos(2 * Math.PI * i / _fftSize));
            _fftBuffer[i] = new Complex(windowValue * hannWindow, 0);
        }

        // Perform FFT using NAudio's FastFourierTransform
        FastFourierTransform.FFT(true, (int)Math.Log(_fftSize, 2), _fftBuffer);

        // Calculate magnitude spectrum (only first half due to symmetry)
        for (int i = 0; i < _fftSize / 2; i++)
        {
            var real = _fftBuffer[i].X;
            var imag = _fftBuffer[i].Y;
            _spectrumData[i] = (float)Math.Sqrt(real * real + imag * imag) / _fftSize;
        }
    }

    private float[] PerformFFTForSpectrogram(float[] samples, int count)
    {
        var spectrum = new float[_fftSize / 2];
        var fftBuffer = new Complex[_fftSize];

        // Prepare FFT buffer with Hanning window
        for (int i = 0; i < _fftSize; i++)
        {
            var windowValue = i < count ? samples[i] : 0f;
            var hannWindow = 0.5f * (1 - (float)Math.Cos(2 * Math.PI * i / _fftSize));
            fftBuffer[i] = new Complex(windowValue * hannWindow, 0);
        }

        // Perform FFT
        FastFourierTransform.FFT(true, (int)Math.Log(_fftSize, 2), fftBuffer);

        // Calculate magnitude spectrum
        for (int i = 0; i < _fftSize / 2; i++)
        {
            var real = fftBuffer[i].X;
            var imag = fftBuffer[i].Y;
            spectrum[i] = (float)Math.Sqrt(real * real + imag * imag) / _fftSize;
        }

        return spectrum;
    }

    private void DrawAudioInfo(Vector2 canvasPos, Vector2 canvasSize)
    {
        var center = canvasPos + canvasSize * 0.5f;
        var textPos = center - new Vector2(100, 60);

        ImGui.SetCursorScreenPos(textPos);
        ImGui.BeginGroup();

        ImGui.Text($"Sample Rate: {_dataset.SampleRate} Hz");
        ImGui.Text($"Channels: {_dataset.Channels}");
        ImGui.Text($"Bit Depth: {_dataset.BitsPerSample} bits");
        ImGui.Text($"Duration: {FormatTime(_dataset.DurationSeconds)}");
        ImGui.Text($"Encoding: {_dataset.Encoding}");
        ImGui.Text($"Size: {_dataset.GetSizeInBytes() / 1024.0 / 1024.0:F2} MB");

        ImGui.EndGroup();
    }

    private void DrawPlaybackIndicator(Vector2 canvasPos, Vector2 canvasSize, ImDrawListPtr dl)
    {
        if (_dataset.DurationSeconds <= 0) return;

        var progress = (float)(_currentTime / _dataset.DurationSeconds);
        progress = Math.Clamp(progress, 0f, 1f);

        var indicatorX = canvasPos.X + canvasSize.X * progress;
        dl.AddLine(
            new Vector2(indicatorX, canvasPos.Y),
            new Vector2(indicatorX, canvasPos.Y + canvasSize.Y - 40),
            0xFFFF0000,
            2.0f
        );
    }

    private void DrawTimeline(Vector2 canvasPos, Vector2 canvasSize, ImDrawListPtr dl, ImGuiIOPtr io)
    {
        var timelineY = canvasPos.Y + canvasSize.Y - 35;
        var timelineHeight = 30f;
        var timelinePos = new Vector2(canvasPos.X, timelineY);

        // Timeline background
        dl.AddRectFilled(timelinePos, timelinePos + new Vector2(canvasSize.X, timelineHeight), 0xFF252525);

        // Timeline progress
        var progress = _dataset.DurationSeconds > 0 ? (float)(_currentTime / _dataset.DurationSeconds) : 0f;
        progress = Math.Clamp(progress, 0f, 1f);

        dl.AddRectFilled(timelinePos, timelinePos + new Vector2(canvasSize.X * progress, timelineHeight), 0xFF3060A0);

        // Time markers
        var markerCount = 10;
        for (int i = 0; i <= markerCount; i++)
        {
            var markerX = timelinePos.X + (canvasSize.X / markerCount) * i;
            var markerTime = (_dataset.DurationSeconds / markerCount) * i;
            dl.AddLine(new Vector2(markerX, timelinePos.Y + timelineHeight - 5), new Vector2(markerX, timelinePos.Y + timelineHeight), 0xFF606060);

            if (i % 2 == 0)
            {
                var timeStr = FormatTime(markerTime);
                dl.AddText(new Vector2(markerX - 20, timelinePos.Y + 2), 0xFFAAAAAA, timeStr);
            }
        }

        // Handle timeline scrubbing
        ImGui.SetCursorScreenPos(timelinePos);
        ImGui.InvisibleButton("##timeline", new Vector2(canvasSize.X, timelineHeight));

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var mouseX = io.MousePos.X;
            var relativeX = (mouseX - timelinePos.X) / canvasSize.X;
            SeekTo(relativeX * _dataset.DurationSeconds);
        }
    }

    private void UpdatePlaybackPosition()
    {
        lock (_playbackLock)
        {
            if (_waveOut != null && _audioFileReader != null && _isPlaying)
            {
                _currentTime = _audioFileReader.CurrentTime.TotalSeconds;

                // Check if playback ended
                if (_waveOut.PlaybackState == PlaybackState.Stopped && _currentTime >= _dataset.DurationSeconds)
                {
                    if (_loop)
                    {
                        SeekToStart();
                        StartPlayback();
                    }
                    else
                    {
                        _isPlaying = false;
                        DisposePlayback();
                    }
                }
            }
        }
    }

    private void TogglePlayback()
    {
        if (_isPlaying)
        {
            PausePlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void StartPlayback()
    {
        if (_dataset == null || _dataset.IsMissing) return;

        try
        {
            lock (_playbackLock)
            {
                DisposePlayback();

                _audioFileReader = new AudioFileReader(_dataset.FilePath);
                _audioFileReader.CurrentTime = TimeSpan.FromSeconds(_currentTime);

                _waveOut = new WaveOutEvent();
                _waveOut.Volume = _volume;
                _waveOut.Init(_audioFileReader);
                _waveOut.Play();

                _isPlaying = true;
                _lastUpdateTime = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AudioViewer] Failed to start playback: {ex.Message}");
            _isPlaying = false;
            DisposePlayback();
        }
    }

    private void PausePlayback()
    {
        lock (_playbackLock)
        {
            if (_waveOut != null)
            {
                _waveOut.Pause();
                _isPlaying = false;
            }
        }
    }

    private void StopPlayback()
    {
        lock (_playbackLock)
        {
            _isPlaying = false;
            DisposePlayback();
            SeekToStart();

            // Clear spectrogram data on stop
            lock (_spectrogramLock)
            {
                _spectrogramData.Clear();
            }
        }
    }

    private void SeekToStart()
    {
        SeekTo(0.0);
    }

    private void SeekToEnd()
    {
        if (_dataset != null)
        {
            SeekTo(_dataset.DurationSeconds);
        }
    }

    private void SeekTo(double timeSeconds)
    {
        if (_dataset == null) return;

        lock (_playbackLock)
        {
            _currentTime = Math.Clamp(timeSeconds, 0.0, _dataset.DurationSeconds);

            if (_audioFileReader != null)
            {
                try
                {
                    _audioFileReader.CurrentTime = TimeSpan.FromSeconds(_currentTime);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[AudioViewer] Failed to seek: {ex.Message}");
                }
            }
        }
    }

    private void UpdateVolume()
    {
        lock (_playbackLock)
        {
            if (_waveOut != null)
            {
                _waveOut.Volume = _volume;
            }
        }
    }

    private void DisposePlayback()
    {
        lock (_playbackLock)
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;

            _audioFileReader?.Dispose();
            _audioFileReader = null;
        }
    }

    private string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}";
    }

    public void Dispose()
    {
        StopPlayback();
        DisposePlayback();
    }
}
