// GeoscientistToolkit/Data/Nerf/NerfModelData.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// Stores trained NeRF model data including hash grid encoding and MLP weights.
/// Implements Instant-NGP style multi-resolution hash encoding.
/// </summary>
public class NerfModelData
{
    // Hash grid encoding parameters
    public int NumLevels { get; set; } = 16;
    public int FeaturesPerLevel { get; set; } = 2;
    public int Log2HashTableSize { get; set; } = 19;
    public float BaseResolution { get; set; } = 16.0f;
    public float MaxResolution { get; set; } = 2048.0f;

    // Hash table data (multi-level hash encoding)
    // Each level has HashTableSize entries, each entry has FeaturesPerLevel floats
    public float[][] HashTables { get; set; }

    // MLP weights for density prediction
    public List<MLPLayer> DensityMLP { get; set; } = new();

    // MLP weights for color prediction (with view-dependent effects)
    public List<MLPLayer> ColorMLP { get; set; } = new();

    // Scene normalization parameters
    public Vector3 SceneCenter { get; set; } = Vector3.Zero;
    public float SceneScale { get; set; } = 1.0f;

    // Model version for compatibility
    public int ModelVersion { get; set; } = 1;

    /// <summary>
    /// Initialize model with given configuration
    /// </summary>
    public void Initialize(NerfTrainingConfig config)
    {
        NumLevels = config.HashGridLevels;
        FeaturesPerLevel = config.HashGridFeatures;
        Log2HashTableSize = (int)Math.Log2(config.HashTableSize);
        BaseResolution = config.HashGridMinResolution;
        MaxResolution = config.HashGridMaxResolution;

        int hashTableSize = 1 << Log2HashTableSize;

        // Initialize hash tables with small random values
        var rng = new Random(42);
        HashTables = new float[NumLevels][];
        for (int level = 0; level < NumLevels; level++)
        {
            HashTables[level] = new float[hashTableSize * FeaturesPerLevel];
            for (int i = 0; i < HashTables[level].Length; i++)
            {
                HashTables[level][i] = (float)(rng.NextDouble() * 2 - 1) * 1e-4f;
            }
        }

        // Initialize density MLP
        DensityMLP.Clear();
        int inputSize = NumLevels * FeaturesPerLevel; // Hash grid features
        for (int i = 0; i < config.MlpHiddenLayers; i++)
        {
            int layerInputSize = i == 0 ? inputSize : config.MlpHiddenWidth;
            DensityMLP.Add(new MLPLayer(layerInputSize, config.MlpHiddenWidth, rng));
        }
        // Final layer outputs density (1) + geometric features for color MLP
        DensityMLP.Add(new MLPLayer(config.MlpHiddenWidth, 16 + 1, rng)); // 16 features + 1 density

        // Initialize color MLP (takes geometric features + view direction encoding)
        ColorMLP.Clear();
        int colorInputSize = 16 + 16; // Geometric features + spherical harmonics for view direction
        for (int i = 0; i < config.MlpColorLayers; i++)
        {
            int layerInputSize = i == 0 ? colorInputSize : config.MlpColorWidth;
            ColorMLP.Add(new MLPLayer(layerInputSize, config.MlpColorWidth, rng));
        }
        // Final layer outputs RGB
        ColorMLP.Add(new MLPLayer(config.MlpColorWidth, 3, rng));
    }

    /// <summary>
    /// Get total size of model in bytes
    /// </summary>
    public long GetSizeBytes()
    {
        long size = 0;

        // Hash tables
        if (HashTables != null)
        {
            foreach (var table in HashTables)
            {
                if (table != null)
                    size += table.Length * sizeof(float);
            }
        }

        // MLP weights
        size += DensityMLP.Sum(l => l.GetSizeBytes());
        size += ColorMLP.Sum(l => l.GetSizeBytes());

        return size;
    }

    /// <summary>
    /// Save model to file
    /// </summary>
    public void SaveToFile(string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        // Write header
        writer.Write("NERF"); // Magic number
        writer.Write(ModelVersion);

        // Write configuration
        writer.Write(NumLevels);
        writer.Write(FeaturesPerLevel);
        writer.Write(Log2HashTableSize);
        writer.Write(BaseResolution);
        writer.Write(MaxResolution);

        // Write scene parameters
        writer.Write(SceneCenter.X);
        writer.Write(SceneCenter.Y);
        writer.Write(SceneCenter.Z);
        writer.Write(SceneScale);

        // Write hash tables
        for (int level = 0; level < NumLevels; level++)
        {
            writer.Write(HashTables[level].Length);
            foreach (var value in HashTables[level])
            {
                writer.Write(value);
            }
        }

        // Write density MLP
        writer.Write(DensityMLP.Count);
        foreach (var layer in DensityMLP)
        {
            layer.Save(writer);
        }

        // Write color MLP
        writer.Write(ColorMLP.Count);
        foreach (var layer in ColorMLP)
        {
            layer.Save(writer);
        }

        Logger.Log($"Saved NeRF model: {GetSizeBytes() / 1024:F1} KB");
    }

    /// <summary>
    /// Load model from file
    /// </summary>
    public static NerfModelData LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        // Read header
        var magic = new string(reader.ReadChars(4));
        if (magic != "NERF")
        {
            throw new InvalidDataException("Invalid NeRF model file format");
        }

        var model = new NerfModelData();
        model.ModelVersion = reader.ReadInt32();

        // Read configuration
        model.NumLevels = reader.ReadInt32();
        model.FeaturesPerLevel = reader.ReadInt32();
        model.Log2HashTableSize = reader.ReadInt32();
        model.BaseResolution = reader.ReadSingle();
        model.MaxResolution = reader.ReadSingle();

        // Read scene parameters
        model.SceneCenter = new Vector3(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle()
        );
        model.SceneScale = reader.ReadSingle();

        // Read hash tables
        model.HashTables = new float[model.NumLevels][];
        for (int level = 0; level < model.NumLevels; level++)
        {
            int length = reader.ReadInt32();
            model.HashTables[level] = new float[length];
            for (int i = 0; i < length; i++)
            {
                model.HashTables[level][i] = reader.ReadSingle();
            }
        }

        // Read density MLP
        int densityLayerCount = reader.ReadInt32();
        model.DensityMLP = new List<MLPLayer>();
        for (int i = 0; i < densityLayerCount; i++)
        {
            model.DensityMLP.Add(MLPLayer.Load(reader));
        }

        // Read color MLP
        int colorLayerCount = reader.ReadInt32();
        model.ColorMLP = new List<MLPLayer>();
        for (int i = 0; i < colorLayerCount; i++)
        {
            model.ColorMLP.Add(MLPLayer.Load(reader));
        }

        Logger.Log($"Loaded NeRF model: {model.GetSizeBytes() / 1024:F1} KB");
        return model;
    }

    /// <summary>
    /// Query the NeRF model for density and color at a 3D position
    /// </summary>
    public (float density, Vector3 color) Query(Vector3 position, Vector3 viewDirection)
    {
        // Normalize position to scene space
        var normalizedPos = (position - SceneCenter) * SceneScale;

        // Get hash grid features
        var features = GetHashGridFeatures(normalizedPos);

        // Run density MLP
        var densityOutput = RunMLP(features, DensityMLP);
        float density = MathF.Exp(densityOutput[0]); // Exponential activation for density
        var geometricFeatures = densityOutput.Skip(1).ToArray();

        // Encode view direction using spherical harmonics
        var viewEncoding = EncodeViewDirection(viewDirection);

        // Combine geometric features and view encoding
        var colorInput = geometricFeatures.Concat(viewEncoding).ToArray();

        // Run color MLP
        var colorOutput = RunMLP(colorInput, ColorMLP);
        var color = new Vector3(
            Sigmoid(colorOutput[0]),
            Sigmoid(colorOutput[1]),
            Sigmoid(colorOutput[2])
        );

        return (density, color);
    }

    private float[] GetHashGridFeatures(Vector3 position)
    {
        var features = new float[NumLevels * FeaturesPerLevel];
        int hashTableSize = 1 << Log2HashTableSize;

        for (int level = 0; level < NumLevels; level++)
        {
            // Compute resolution for this level
            float scale = MathF.Pow(MaxResolution / BaseResolution, (float)level / (NumLevels - 1));
            float resolution = BaseResolution * scale;

            // Get grid position
            var scaledPos = position * resolution;
            var gridPos = new Vector3(
                MathF.Floor(scaledPos.X),
                MathF.Floor(scaledPos.Y),
                MathF.Floor(scaledPos.Z)
            );

            // Trilinear interpolation weights
            var localPos = scaledPos - gridPos;

            // Get features from 8 corners of the grid cell
            var featureSum = new float[FeaturesPerLevel];

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    gridPos.X + (i & 1),
                    gridPos.Y + ((i >> 1) & 1),
                    gridPos.Z + ((i >> 2) & 1)
                );

                // Hash the corner position
                int hash = SpatialHash(corner, hashTableSize);

                // Compute trilinear weight
                float wx = (i & 1) == 0 ? (1 - localPos.X) : localPos.X;
                float wy = ((i >> 1) & 1) == 0 ? (1 - localPos.Y) : localPos.Y;
                float wz = ((i >> 2) & 1) == 0 ? (1 - localPos.Z) : localPos.Z;
                float weight = wx * wy * wz;

                // Accumulate features
                for (int f = 0; f < FeaturesPerLevel; f++)
                {
                    featureSum[f] += weight * HashTables[level][hash * FeaturesPerLevel + f];
                }
            }

            // Copy to output
            for (int f = 0; f < FeaturesPerLevel; f++)
            {
                features[level * FeaturesPerLevel + f] = featureSum[f];
            }
        }

        return features;
    }

    private static int SpatialHash(Vector3 pos, int tableSize)
    {
        // Standard spatial hashing using prime numbers
        const int p1 = 1;
        const uint p2 = 2654435761;
        const int p3 = 805459861;

        int x = (int)pos.X;
        int y = (int)pos.Y;
        int z = (int)pos.Z;

        return (int)(((x * p1) ^ (y * p2) ^ (z * p3)) & (tableSize - 1));
    }

    private static float[] RunMLP(float[] input, List<MLPLayer> layers)
    {
        var current = input;
        for (int i = 0; i < layers.Count; i++)
        {
            current = layers[i].Forward(current, activation: i < layers.Count - 1);
        }
        return current;
    }

    private static float[] EncodeViewDirection(Vector3 direction)
    {
        // Spherical harmonics encoding (degree 4)
        var normalized = Vector3.Normalize(direction);
        var x = normalized.X;
        var y = normalized.Y;
        var z = normalized.Z;

        return new float[]
        {
            // Degree 0
            1.0f,
            // Degree 1
            y, z, x,
            // Degree 2
            x * y, y * z, z * z - 0.333333f, x * z, x * x - y * y,
            // Degree 3
            y * (3 * x * x - y * y), x * y * z, y * (4 * z * z - x * x - y * y),
            z * (2 * z * z - 3 * x * x - 3 * y * y), x * (4 * z * z - x * x - y * y),
            z * (x * x - y * y), x * (x * x - 3 * y * y)
        };
    }

    private static float Sigmoid(float x)
    {
        return 1.0f / (1.0f + MathF.Exp(-x));
    }
}

/// <summary>
/// Single layer of the MLP (fully connected layer)
/// </summary>
public class MLPLayer
{
    public int InputSize { get; set; }
    public int OutputSize { get; set; }
    public float[] Weights { get; set; }
    public float[] Biases { get; set; }

    public MLPLayer() { }

    public MLPLayer(int inputSize, int outputSize, Random rng)
    {
        InputSize = inputSize;
        OutputSize = outputSize;

        // Xavier initialization
        float scale = MathF.Sqrt(2.0f / (inputSize + outputSize));

        Weights = new float[inputSize * outputSize];
        for (int i = 0; i < Weights.Length; i++)
        {
            Weights[i] = (float)(rng.NextDouble() * 2 - 1) * scale;
        }

        Biases = new float[outputSize];
        // Initialize biases to zero
    }

    public float[] Forward(float[] input, bool activation = true)
    {
        var output = new float[OutputSize];

        for (int o = 0; o < OutputSize; o++)
        {
            float sum = Biases[o];
            for (int i = 0; i < InputSize; i++)
            {
                sum += input[i] * Weights[i * OutputSize + o];
            }

            // ReLU activation
            output[o] = activation ? MathF.Max(0, sum) : sum;
        }

        return output;
    }

    public long GetSizeBytes()
    {
        return (Weights?.Length ?? 0) * sizeof(float) + (Biases?.Length ?? 0) * sizeof(float);
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(InputSize);
        writer.Write(OutputSize);

        writer.Write(Weights.Length);
        foreach (var w in Weights)
        {
            writer.Write(w);
        }

        writer.Write(Biases.Length);
        foreach (var b in Biases)
        {
            writer.Write(b);
        }
    }

    public static MLPLayer Load(BinaryReader reader)
    {
        var layer = new MLPLayer
        {
            InputSize = reader.ReadInt32(),
            OutputSize = reader.ReadInt32()
        };

        int weightsCount = reader.ReadInt32();
        layer.Weights = new float[weightsCount];
        for (int i = 0; i < weightsCount; i++)
        {
            layer.Weights[i] = reader.ReadSingle();
        }

        int biasesCount = reader.ReadInt32();
        layer.Biases = new float[biasesCount];
        for (int i = 0; i < biasesCount; i++)
        {
            layer.Biases[i] = reader.ReadSingle();
        }

        return layer;
    }
}
