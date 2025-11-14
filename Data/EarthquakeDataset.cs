using System;
using System.IO;
using System.Text.Json;
using GeoscientistToolkit.Analysis.Seismology;

namespace GeoscientistToolkit.Data
{
    /// <summary>
    /// Dataset for earthquake simulation results
    /// Integrates with GIS system for spatial visualization
    /// </summary>
    public class EarthquakeDataset : Dataset
    {
        private EarthquakeSimulationParameters? _parameters;
        private EarthquakeSimulationResults? _results;
        private bool _isLoaded;

        public EarthquakeDataset(string name, string filePath)
            : base(name, filePath)
        {
            Type = DatasetType.Earthquake;
        }

        /// <summary>
        /// Simulation parameters
        /// </summary>
        public EarthquakeSimulationParameters? Parameters => _parameters;

        /// <summary>
        /// Simulation results
        /// </summary>
        public EarthquakeSimulationResults? Results => _results;

        /// <summary>
        /// Is the simulation already run?
        /// </summary>
        public bool HasResults => _results != null;

        /// <summary>
        /// Set simulation parameters
        /// </summary>
        public void SetParameters(EarthquakeSimulationParameters parameters)
        {
            _parameters = parameters;
        }

        /// <summary>
        /// Set simulation results
        /// </summary>
        public void SetResults(EarthquakeSimulationResults results)
        {
            _results = results;
            _isLoaded = true;

            // Update metadata
            DatasetMetadata.SampleName = $"Earthquake M{results.MomentMagnitude:F1}";
            Metadata["Magnitude"] = results.MomentMagnitude;
            Metadata["Epicenter"] = $"{results.EpicenterLatitude:F3}°, {results.EpicenterLongitude:F3}°";
            Metadata["Depth"] = $"{results.HypocenterDepthKm:F1} km";
            Metadata["MaxPGA"] = $"{results.MaxPGA:F3} g";
            Metadata["MaxPGV"] = $"{results.MaxPGV:F1} cm/s";
            Metadata["AffectedArea"] = $"{results.AffectedAreaKm2:F1} km²";
        }

        /// <summary>
        /// Load dataset from file
        /// </summary>
        public override void Load()
        {
            if (_isLoaded) return;

            if (!File.Exists(FilePath))
            {
                IsMissing = true;
                return;
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<EarthquakeDatasetFile>(json);

                if (data != null)
                {
                    _parameters = data.Parameters;
                    _results = data.Results;
                    _isLoaded = true;
                    IsMissing = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading earthquake dataset: {ex.Message}");
                IsMissing = true;
            }
        }

        /// <summary>
        /// Unload dataset from memory
        /// </summary>
        public override void Unload()
        {
            _results = null;
            _isLoaded = false;
        }

        /// <summary>
        /// Save dataset to file
        /// </summary>
        public void Save()
        {
            if (_parameters == null)
            {
                throw new InvalidOperationException("Cannot save earthquake dataset without parameters");
            }

            var data = new EarthquakeDatasetFile
            {
                Parameters = _parameters,
                Results = _results
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(FilePath, json);

            DateModified = DateTime.Now;
        }

        /// <summary>
        /// Export results to GIS-compatible formats
        /// </summary>
        public void ExportToGIS(string outputDirectory)
        {
            if (_results == null)
            {
                throw new InvalidOperationException("No results to export");
            }

            Directory.CreateDirectory(outputDirectory);

            string basePath = Path.Combine(outputDirectory, Name);
            _results.ExportToCSV(basePath);
        }

        /// <summary>
        /// Get size in bytes
        /// </summary>
        public override long GetSizeInBytes()
        {
            if (File.Exists(FilePath))
            {
                return new FileInfo(FilePath).Length;
            }

            // Estimate size if not saved
            if (_results != null)
            {
                // Rough estimate: grid size * number of result fields * 8 bytes per double
                int numFields = 10; // PGA, PGV, PGD, arrivals, amplitudes, etc.
                return _results.GridNX * _results.GridNY * numFields * 8L;
            }

            return 0;
        }

        /// <summary>
        /// Get summary string
        /// </summary>
        public override string ToString()
        {
            if (_results != null)
            {
                return $"Earthquake M{_results.MomentMagnitude:F1} at {_results.EpicenterLatitude:F3}°, {_results.EpicenterLongitude:F3}°";
            }
            else if (_parameters != null)
            {
                return $"Earthquake Simulation (M{_parameters.MomentMagnitude:F1}, not yet run)";
            }
            else
            {
                return "Earthquake Dataset (no parameters)";
            }
        }

        /// <summary>
        /// Create a new earthquake dataset with default parameters
        /// </summary>
        public static EarthquakeDataset CreateDefault(string name, string savePath)
        {
            var dataset = new EarthquakeDataset(name, savePath);
            dataset.SetParameters(EarthquakeSimulationParameters.CreateDefault());
            return dataset;
        }
    }

    /// <summary>
    /// File format for saving/loading earthquake datasets
    /// </summary>
    internal class EarthquakeDatasetFile
    {
        public EarthquakeSimulationParameters? Parameters { get; set; }
        public EarthquakeSimulationResults? Results { get; set; }
    }
}
