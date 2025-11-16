// GeoscientistToolkit/Data/Media/AudioDataset.cs

using NAudio.Wave;
using NAudio.Vorbis;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Media;

/// <summary>
/// Dataset for handling audio files (WAV, MP3, OGG, etc.)
/// Uses NAudio for open-source audio processing (MIT license)
/// </summary>
public class AudioDataset : Dataset, IDisposable, ISerializableDataset
{
    public AudioDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.Audio;
    }

    // Audio properties
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public double DurationSeconds { get; set; }
    public long TotalSamples { get; set; }
    public string Format { get; set; }
    public string Encoding { get; set; }
    public long BitRate { get; set; }

    // Metadata from audio file
    public Dictionary<string, string> AudioMetadata { get; set; } = new();

    // Waveform data for visualization (downsampled)
    public float[] WaveformData { get; set; }
    public int WaveformSampleRate { get; set; } = 100; // Samples per second for visualization

    // Memory management
    private AudioFileReader _audioReader;

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }

    public object ToSerializableObject()
    {
        return new AudioDatasetDTO
        {
            TypeName = nameof(AudioDataset),
            Name = Name,
            FilePath = FilePath,
            Metadata = this.DatasetMetadata != null ? new DatasetMetadataDTO
            {
                SampleName = this.DatasetMetadata.SampleName,
                LocationName = this.DatasetMetadata.LocationName,
                Latitude = this.DatasetMetadata.Latitude,
                Longitude = this.DatasetMetadata.Longitude,
                CoordinatesX = this.DatasetMetadata.Coordinates?.X,
                CoordinatesY = this.DatasetMetadata.Coordinates?.Y,
                Depth = this.DatasetMetadata.Depth,
                SizeX = this.DatasetMetadata.Size?.X,
                SizeY = this.DatasetMetadata.Size?.Y,
                SizeZ = this.DatasetMetadata.Size?.Z,
                SizeUnit = this.DatasetMetadata.SizeUnit,
                CollectionDate = this.DatasetMetadata.CollectionDate,
                Collector = this.DatasetMetadata.Collector,
                Notes = this.DatasetMetadata.Notes,
                CustomFields = this.DatasetMetadata.CustomFields
            } : new DatasetMetadataDTO(),
            SampleRate = SampleRate,
            Channels = Channels,
            BitsPerSample = BitsPerSample,
            DurationSeconds = DurationSeconds,
            TotalSamples = TotalSamples,
            Format = Format,
            Encoding = Encoding,
            BitRate = BitRate,
            AudioMetadata = AudioMetadata
        };
    }

    public override long GetSizeInBytes()
    {
        if (File.Exists(FilePath))
            return new FileInfo(FilePath).Length;
        return 0;
    }

    public override void Load()
    {
        if (_audioReader != null) return;

        try
        {
            if (!File.Exists(FilePath))
            {
                IsMissing = true;
                Logger.LogWarning($"Audio file not found: {FilePath}");
                return;
            }

            // Determine format and create appropriate reader
            var extension = Path.GetExtension(FilePath).ToLowerInvariant();
            Format = extension.TrimStart('.');

            WaveStream reader = null;

            try
            {
                // Try to open with appropriate reader based on extension
                if (extension == ".mp3")
                {
                    reader = new Mp3FileReader(FilePath);
                    Encoding = "MP3";
                }
                else if (extension == ".wav")
                {
                    reader = new WaveFileReader(FilePath);
                    Encoding = "WAV/PCM";
                }
                else if (extension == ".ogg")
                {
                    reader = new VorbisWaveReader(FilePath);
                    Encoding = "Vorbis";
                }
                else
                {
                    // Try generic audio file reader
                    reader = new AudioFileReader(FilePath);
                    Encoding = "Unknown";
                }

                if (reader != null)
                {
                    SampleRate = reader.WaveFormat.SampleRate;
                    Channels = reader.WaveFormat.Channels;
                    BitsPerSample = reader.WaveFormat.BitsPerSample;
                    DurationSeconds = reader.TotalTime.TotalSeconds;
                    TotalSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8) / reader.WaveFormat.Channels;

                    // Calculate approximate bitrate
                    var fileSize = new FileInfo(FilePath).Length;
                    BitRate = DurationSeconds > 0 ? (long)(fileSize * 8 / DurationSeconds) : 0;

                    // Generate waveform for visualization
                    GenerateWaveform(reader);

                    Logger.Log($"Loaded audio: {SampleRate} Hz, {Channels} ch, {DurationSeconds:F2}s, {Encoding}");

                    reader.Dispose();
                }
            }
            catch (Exception ex)
            {
                reader?.Dispose();
                Logger.LogError($"Failed to load audio {FilePath}: {ex.Message}");
                IsMissing = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load audio {FilePath}: {ex.Message}");
            IsMissing = true;
        }
    }

    public override void Unload()
    {
        if (SettingsManager.Instance.Settings.Performance.EnableLazyLoading)
        {
            _audioReader?.Dispose();
            _audioReader = null;
            WaveformData = null;
            GC.Collect();
        }
    }

    /// <summary>
    /// Generate downsampled waveform data for visualization
    /// </summary>
    private void GenerateWaveform(WaveStream reader)
    {
        try
        {
            reader.Position = 0;

            var samplesNeeded = (int)(DurationSeconds * WaveformSampleRate);
            WaveformData = new float[samplesNeeded];

            var samplesPerPoint = (int)(TotalSamples / samplesNeeded);
            if (samplesPerPoint < 1) samplesPerPoint = 1;

            var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
            var sampleProvider = reader.ToSampleProvider();

            int waveformIndex = 0;
            int samplesRead;

            while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0 && waveformIndex < samplesNeeded)
            {
                // Calculate RMS or peak for this chunk
                float sum = 0;
                int count = 0;

                for (int i = 0; i < samplesRead; i += Channels)
                {
                    // Average all channels for mono waveform
                    float sample = 0;
                    for (int ch = 0; ch < Channels && i + ch < samplesRead; ch++)
                    {
                        sample += Math.Abs(buffer[i + ch]);
                    }
                    sample /= Channels;
                    sum += sample * sample;
                    count++;
                }

                if (count > 0)
                {
                    var rms = (float)Math.Sqrt(sum / count);
                    if (waveformIndex < samplesNeeded)
                        WaveformData[waveformIndex++] = rms;
                }
            }

            reader.Position = 0;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to generate waveform: {ex.Message}");
            WaveformData = null;
        }
    }

    /// <summary>
    /// Read audio samples for playback or analysis
    /// </summary>
    public float[] ReadSamples(int startSample, int count)
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            using var reader = new AudioFileReader(FilePath);
            var sampleProvider = reader.ToSampleProvider();

            // Seek to start position
            var bytesPerSample = reader.WaveFormat.BitsPerSample / 8;
            var bytesPerFrame = bytesPerSample * reader.WaveFormat.Channels;
            reader.Position = startSample * bytesPerFrame;

            var samples = new float[count];
            var samplesRead = sampleProvider.Read(samples, 0, count);

            if (samplesRead < count)
            {
                Array.Resize(ref samples, samplesRead);
            }

            return samples;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to read audio samples: {ex.Message}");
            return null;
        }
    }
}
