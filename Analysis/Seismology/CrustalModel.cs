using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeoscientistToolkit.Analysis.Seismology
{
    /// <summary>
    /// Represents a layer in the crustal model
    /// </summary>
    public class CrustalLayer
    {
        [JsonPropertyName("thickness_km")]
        public double ThicknessKm { get; set; }

        [JsonPropertyName("vp_km_s")]
        public double VpKmPerS { get; set; }

        [JsonPropertyName("vs_km_s")]
        public double VsKmPerS { get; set; }

        [JsonPropertyName("density_g_cm3")]
        public double DensityGPerCm3 { get; set; }

        [JsonPropertyName("q_mu")]
        public double QMu { get; set; }

        [JsonPropertyName("q_kappa")]
        public double QKappa { get; set; }

        /// <summary>
        /// Calculate impedance (density * velocity)
        /// </summary>
        public double GetPImpedance() => DensityGPerCm3 * VpKmPerS;

        /// <summary>
        /// Calculate S-wave impedance
        /// </summary>
        public double GetSImpedance() => DensityGPerCm3 * VsKmPerS;

        /// <summary>
        /// Calculate Lame's first parameter (lambda) in GPa
        /// </summary>
        public double GetLambda()
        {
            double rho = DensityGPerCm3 * 1000.0; // kg/m^3
            double vp = VpKmPerS * 1000.0; // m/s
            double vs = VsKmPerS * 1000.0; // m/s
            return rho * (vp * vp - 2.0 * vs * vs) / 1e9; // GPa
        }

        /// <summary>
        /// Calculate shear modulus (mu) in GPa
        /// </summary>
        public double GetMu()
        {
            double rho = DensityGPerCm3 * 1000.0; // kg/m^3
            double vs = VsKmPerS * 1000.0; // m/s
            return rho * vs * vs / 1e9; // GPa
        }

        /// <summary>
        /// Calculate bulk modulus (kappa) in GPa
        /// </summary>
        public double GetKappa()
        {
            double lambda = GetLambda();
            double mu = GetMu();
            return lambda + (2.0 / 3.0) * mu;
        }

        /// <summary>
        /// Calculate Poisson's ratio
        /// </summary>
        public double GetPoissonsRatio()
        {
            double vp = VpKmPerS;
            double vs = VsKmPerS;
            if (vs < 0.001) return 0.5; // Fluid
            double ratio = vp / vs;
            return (ratio * ratio - 2.0) / (2.0 * (ratio * ratio - 1.0));
        }
    }

    /// <summary>
    /// Grid cell with crustal information
    /// </summary>
    public class GridCell
    {
        [JsonPropertyName("lat")]
        public double Latitude { get; set; }

        [JsonPropertyName("lon")]
        public double Longitude { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("moho_depth_km")]
        public double MohoDepthKm { get; set; }
    }

    /// <summary>
    /// Grid data container
    /// </summary>
    public class GridData
    {
        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("resolution_degrees")]
        public double ResolutionDegrees { get; set; }

        [JsonPropertyName("cells")]
        public List<GridCell> Cells { get; set; } = new();
    }

    /// <summary>
    /// Crustal type definition
    /// </summary>
    public class CrustalType
    {
        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("layers")]
        public Dictionary<string, CrustalLayer> Layers { get; set; } = new();

        /// <summary>
        /// Get total crustal thickness
        /// </summary>
        public double GetTotalThickness()
        {
            double total = 0.0;
            foreach (var layer in Layers.Values)
            {
                total += layer.ThicknessKm;
            }
            return total;
        }

        /// <summary>
        /// Get layer at specific depth
        /// </summary>
        public (string name, CrustalLayer layer, double depthInLayer) GetLayerAtDepth(double depthKm)
        {
            double currentDepth = 0.0;
            foreach (var kvp in Layers)
            {
                if (depthKm >= currentDepth && depthKm < currentDepth + kvp.Value.ThicknessKm)
                {
                    return (kvp.Key, kvp.Value, depthKm - currentDepth);
                }
                currentDepth += kvp.Value.ThicknessKm;
            }
            // Return last layer if beyond total thickness
            var lastLayer = Layers["uppermost_mantle"];
            return ("uppermost_mantle", lastLayer, depthKm - currentDepth);
        }
    }

    /// <summary>
    /// Global crustal model for seismic simulations
    /// Based on CRUST1.0 specifications
    /// </summary>
    public class CrustalModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("grid_resolution_degrees")]
        public double GridResolutionDegrees { get; set; }

        [JsonPropertyName("layers")]
        public List<string> LayerNames { get; set; } = new();

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = "";

        [JsonPropertyName("crustal_types")]
        public Dictionary<string, CrustalType> CrustalTypes { get; set; } = new();

        [JsonPropertyName("grid_data")]
        public GridData? GridData { get; set; }

        // Spatial index for fast grid lookups
        private Dictionary<(int, int), GridCell>? _gridIndex;

        /// <summary>
        /// Build spatial index for grid data
        /// </summary>
        private void BuildGridIndex()
        {
            if (GridData == null || GridData.Cells == null || GridData.Cells.Count == 0)
                return;

            _gridIndex = new Dictionary<(int, int), GridCell>();

            foreach (var cell in GridData.Cells)
            {
                int latIdx = (int)Math.Round(cell.Latitude / GridData.ResolutionDegrees);
                int lonIdx = (int)Math.Round(cell.Longitude / GridData.ResolutionDegrees);
                _gridIndex[(latIdx, lonIdx)] = cell;
            }
        }

        /// <summary>
        /// Load crustal model from JSON file
        /// </summary>
        public static CrustalModel LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Crustal model file not found: {filePath}");
            }

            string json = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var model = JsonSerializer.Deserialize<CrustalModel>(json, options);
            if (model == null)
            {
                throw new InvalidOperationException("Failed to deserialize crustal model");
            }

            // Build spatial index for fast lookups
            model.BuildGridIndex();

            return model;
        }

        /// <summary>
        /// Get crustal type for a given location using the global grid
        /// </summary>
        public CrustalType GetCrustalType(double latitude, double longitude, string? typeOverride = null)
        {
            if (typeOverride != null && CrustalTypes.ContainsKey(typeOverride))
            {
                return CrustalTypes[typeOverride];
            }

            // Use grid data if available
            if (_gridIndex != null && GridData != null)
            {
                // Find nearest grid cell
                int latIdx = (int)Math.Round(latitude / GridData.ResolutionDegrees);
                int lonIdx = (int)Math.Round(longitude / GridData.ResolutionDegrees);

                // Clamp to valid range
                latIdx = Math.Clamp(latIdx, -45, 45);  // -90 to 90 degrees / 2
                lonIdx = Math.Clamp(lonIdx, -90, 90);  // -180 to 180 degrees / 2

                if (_gridIndex.TryGetValue((latIdx, lonIdx), out var cell))
                {
                    if (CrustalTypes.ContainsKey(cell.Type))
                    {
                        return CrustalTypes[cell.Type];
                    }
                }

                // Fallback: search nearby cells
                for (int dLat = -1; dLat <= 1; dLat++)
                {
                    for (int dLon = -1; dLon <= 1; dLon++)
                    {
                        if (_gridIndex.TryGetValue((latIdx + dLat, lonIdx + dLon), out var nearCell))
                        {
                            if (CrustalTypes.ContainsKey(nearCell.Type))
                            {
                                return CrustalTypes[nearCell.Type];
                            }
                        }
                    }
                }
            }

            // Fallback to heuristic if grid not available
            // Ocean regions (rough approximation)
            if (Math.Abs(latitude) < 60 &&
                ((longitude > 140 && longitude < 240) || // Pacific
                 (longitude > -80 && longitude < 20)))   // Atlantic
            {
                return CrustalTypes["oceanic"];
            }

            // Mountain belts
            if ((latitude > 25 && latitude < 45 && longitude > 60 && longitude < 100) || // Himalayas
                (latitude > 35 && latitude < 50 && longitude > -125 && longitude < -105)) // Rockies
            {
                return CrustalTypes["orogen"];
            }

            // Rift zones
            if ((latitude > -40 && latitude < 20 && longitude > 25 && longitude < 45)) // East African Rift
            {
                return CrustalTypes["rift"];
            }

            // Default to continental
            return CrustalTypes["continental"];
        }

        /// <summary>
        /// Get Moho depth at a location from grid data
        /// </summary>
        public double GetMohoDepth(double latitude, double longitude)
        {
            if (_gridIndex != null && GridData != null)
            {
                int latIdx = (int)Math.Round(latitude / GridData.ResolutionDegrees);
                int lonIdx = (int)Math.Round(longitude / GridData.ResolutionDegrees);

                latIdx = Math.Clamp(latIdx, -45, 45);
                lonIdx = Math.Clamp(lonIdx, -90, 90);

                if (_gridIndex.TryGetValue((latIdx, lonIdx), out var cell))
                {
                    return cell.MohoDepthKm;
                }
            }

            // Fallback to estimated values
            var crustalType = GetCrustalType(latitude, longitude);
            return crustalType.GetTotalThickness();
        }

        /// <summary>
        /// Get velocity profile at a location
        /// </summary>
        public (double[] depths, double[] vp, double[] vs, double[] density) GetVelocityProfile(
            double latitude, double longitude, int numPoints = 100, double maxDepthKm = 100.0)
        {
            var crustalType = GetCrustalType(latitude, longitude);

            double[] depths = new double[numPoints];
            double[] vp = new double[numPoints];
            double[] vs = new double[numPoints];
            double[] density = new double[numPoints];

            for (int i = 0; i < numPoints; i++)
            {
                double depth = (i / (double)(numPoints - 1)) * maxDepthKm;
                depths[i] = depth;

                var (layerName, layer, depthInLayer) = crustalType.GetLayerAtDepth(depth);
                vp[i] = layer.VpKmPerS;
                vs[i] = layer.VsKmPerS;
                density[i] = layer.DensityGPerCm3;
            }

            return (depths, vp, vs, density);
        }

        /// <summary>
        /// Calculate average properties for a depth range
        /// </summary>
        public (double avgVp, double avgVs, double avgDensity) GetAverageProperties(
            double latitude, double longitude, double minDepthKm, double maxDepthKm)
        {
            var crustalType = GetCrustalType(latitude, longitude);

            double sumVp = 0, sumVs = 0, sumDensity = 0;
            int samples = 20;

            for (int i = 0; i < samples; i++)
            {
                double depth = minDepthKm + (i / (double)(samples - 1)) * (maxDepthKm - minDepthKm);
                var (_, layer, _) = crustalType.GetLayerAtDepth(depth);

                sumVp += layer.VpKmPerS;
                sumVs += layer.VsKmPerS;
                sumDensity += layer.DensityGPerCm3;
            }

            return (sumVp / samples, sumVs / samples, sumDensity / samples);
        }
    }
}
