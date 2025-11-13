// GeoscientistToolkit/Business/GIS/TopographyPresets.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.ProfileGenerator;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
/// Provides preset topography configurations for 2D geological profiles
/// </summary>
public static class TopographyPresets
{
    public enum PresetType
    {
        Flat,
        Valley,
        Mountain,
        SynclineValley,
        AnticlineRidge,
        AsymmetricValley,
        Plateau,
        Hills,
        Canyon,
        CoastalCliff,
        Escarpment,
        GentleSlope,
        MultipleRidges
    }

    /// <summary>
    /// Generate topography profile points based on preset type
    /// </summary>
    public static void ApplyPreset(TopographicProfile profile, PresetType type, float amplitude = 1.0f)
    {
        profile.Points.Clear();
        var numPoints = 100; // More points for smoother curves
        var totalDistance = profile.TotalDistance;
        
        for (int i = 0; i <= numPoints; i++)
        {
            var distance = i / (float)numPoints * totalDistance;
            var normalizedPos = i / (float)numPoints; // 0 to 1
            var elevation = CalculateElevation(type, normalizedPos, totalDistance, amplitude);
            
            profile.Points.Add(new ProfilePoint
            {
                Position = new Vector2(distance, elevation),
                Distance = distance,
                Elevation = elevation,
                Features = new List<GeologicalMapping.GeologicalFeature>()
            });
        }
        
        // Update min/max elevations
        UpdateElevationRange(profile);
    }
    
    private static float CalculateElevation(PresetType type, float normalizedPos, float totalDistance, float amplitude)
    {
        var baseAmplitude = 200f * amplitude; // Base amplitude in meters
        
        return type switch
        {
            PresetType.Flat => 0f,
            
            PresetType.Valley => ValleyProfile(normalizedPos, baseAmplitude),
            
            PresetType.Mountain => MountainProfile(normalizedPos, baseAmplitude * 2f),
            
            PresetType.SynclineValley => SynclineProfile(normalizedPos, baseAmplitude),
            
            PresetType.AnticlineRidge => AnticlineProfile(normalizedPos, baseAmplitude),
            
            PresetType.AsymmetricValley => AsymmetricValleyProfile(normalizedPos, baseAmplitude),
            
            PresetType.Plateau => PlateauProfile(normalizedPos, baseAmplitude),
            
            PresetType.Hills => HillsProfile(normalizedPos, totalDistance, baseAmplitude * 0.5f),
            
            PresetType.Canyon => CanyonProfile(normalizedPos, baseAmplitude * 1.5f),
            
            PresetType.CoastalCliff => CoastalCliffProfile(normalizedPos, baseAmplitude),
            
            PresetType.Escarpment => EscarpmentProfile(normalizedPos, baseAmplitude),
            
            PresetType.GentleSlope => GentleSlopeProfile(normalizedPos, baseAmplitude * 0.5f),
            
            PresetType.MultipleRidges => MultipleRidgesProfile(normalizedPos, totalDistance, baseAmplitude),
            
            _ => 0f
        };
    }
    
    #region Profile Calculations
    
    private static float ValleyProfile(float x, float amplitude)
    {
        // U-shaped valley
        var sharpness = 3f;
        var valleyShape = MathF.Pow(2f * x - 1f, sharpness * 2f);
        return -amplitude * valleyShape;
    }
    
    private static float MountainProfile(float x, float amplitude)
    {
        // Triangular mountain peak
        if (x < 0.5f)
            return amplitude * (2f * x);
        else
            return amplitude * (2f - 2f * x);
    }
    
    private static float SynclineProfile(float x, float amplitude)
    {
        // Smooth syncline (downward fold)
        var foldWidth = 0.6f;
        var centerX = 0.5f;
        var distFromCenter = MathF.Abs(x - centerX);
        
        if (distFromCenter < foldWidth / 2f)
        {
            var normalizedDist = distFromCenter / (foldWidth / 2f);
            return -amplitude * (1f - normalizedDist * normalizedDist);
        }
        return 0f;
    }
    
    private static float AnticlineProfile(float x, float amplitude)
    {
        // Smooth anticline (upward fold)
        var foldWidth = 0.6f;
        var centerX = 0.5f;
        var distFromCenter = MathF.Abs(x - centerX);
        
        if (distFromCenter < foldWidth / 2f)
        {
            var normalizedDist = distFromCenter / (foldWidth / 2f);
            return amplitude * (1f - normalizedDist * normalizedDist);
        }
        return 0f;
    }
    
    private static float AsymmetricValleyProfile(float x, float amplitude)
    {
        // Asymmetric valley (steeper on one side)
        if (x < 0.3f)
            return -amplitude * (x / 0.3f);
        else if (x < 0.7f)
            return -amplitude;
        else
            return -amplitude * (1f - (x - 0.7f) / 0.3f);
    }
    
    private static float PlateauProfile(float x, float amplitude)
    {
        // Flat plateau with slopes on sides
        if (x < 0.2f)
            return amplitude * (x / 0.2f);
        else if (x < 0.8f)
            return amplitude;
        else
            return amplitude * (1f - (x - 0.8f) / 0.2f);
    }
    
    private static float HillsProfile(float x, float totalDistance, float amplitude)
    {
        // Multiple gentle hills
        var hill1 = amplitude * MathF.Sin(x * MathF.PI * 2f);
        var hill2 = amplitude * 0.5f * MathF.Sin(x * MathF.PI * 4f);
        var hill3 = amplitude * 0.25f * MathF.Sin(x * MathF.PI * 8f);
        return hill1 + hill2 + hill3;
    }
    
    private static float CanyonProfile(float x, float amplitude)
    {
        // Deep V-shaped canyon
        var centerDist = MathF.Abs(x - 0.5f) * 2f;
        if (centerDist < 0.6f)
            return -amplitude * (1f - centerDist / 0.6f);
        return 0f;
    }
    
    private static float CoastalCliffProfile(float x, float amplitude)
    {
        // Coastal cliff (steep drop on one side)
        if (x < 0.1f)
            return amplitude;
        else if (x < 0.3f)
            return amplitude * (1f - (x - 0.1f) / 0.2f);
        else
            return 0f;
    }
    
    private static float EscarpmentProfile(float x, float amplitude)
    {
        // Step-like escarpment
        if (x < 0.4f)
            return 0f;
        else if (x < 0.6f)
            return amplitude * (x - 0.4f) / 0.2f;
        else
            return amplitude;
    }
    
    private static float GentleSlopeProfile(float x, float amplitude)
    {
        // Simple linear slope
        return amplitude * (1f - x);
    }
    
    private static float MultipleRidgesProfile(float x, float totalDistance, float amplitude)
    {
        // Multiple ridge-and-valley pattern
        var baseElevation = 0f;
        
        // Main ridges
        for (int i = 0; i < 4; i++)
        {
            var ridgeCenter = (i + 0.5f) / 4f;
            var distFromRidge = MathF.Abs(x - ridgeCenter);
            if (distFromRidge < 0.1f)
            {
                baseElevation += amplitude * (1f - distFromRidge / 0.1f);
            }
        }
        
        return baseElevation;
    }
    
    #endregion
    
    private static void UpdateElevationRange(TopographicProfile profile)
    {
        if (profile.Points.Count == 0) return;
        
        profile.MinElevation = profile.Points.Min(p => p.Elevation);
        profile.MaxElevation = profile.Points.Max(p => p.Elevation);
    }
    
    /// <summary>
    /// Get user-friendly name for preset type
    /// </summary>
    public static string GetPresetName(PresetType type) => type switch
    {
        PresetType.Flat => "Flat Plain",
        PresetType.Valley => "U-Shaped Valley",
        PresetType.Mountain => "Mountain Peak",
        PresetType.SynclineValley => "Syncline Valley",
        PresetType.AnticlineRidge => "Anticline Ridge",
        PresetType.AsymmetricValley => "Asymmetric Valley",
        PresetType.Plateau => "Plateau",
        PresetType.Hills => "Rolling Hills",
        PresetType.Canyon => "Canyon",
        PresetType.CoastalCliff => "Coastal Cliff",
        PresetType.Escarpment => "Escarpment",
        PresetType.GentleSlope => "Gentle Slope",
        PresetType.MultipleRidges => "Multiple Ridges",
        _ => "Unknown"
    };
    
    /// <summary>
    /// Get description for preset type
    /// </summary>
    public static string GetPresetDescription(PresetType type) => type switch
    {
        PresetType.Flat => "Flat horizontal surface at elevation 0",
        PresetType.Valley => "Glacially-carved U-shaped valley",
        PresetType.Mountain => "Simple triangular mountain peak",
        PresetType.SynclineValley => "Valley formed by syncline folding",
        PresetType.AnticlineRidge => "Ridge formed by anticline folding",
        PresetType.AsymmetricValley => "Valley with one steep side and one gentle side",
        PresetType.Plateau => "Elevated flat region with slopes on sides",
        PresetType.Hills => "Gentle undulating terrain with multiple hills",
        PresetType.Canyon => "Deep V-shaped canyon",
        PresetType.CoastalCliff => "Steep cliff along coastline",
        PresetType.Escarpment => "Long cliff-like ridge (fault scarp)",
        PresetType.GentleSlope => "Simple gentle slope from high to low",
        PresetType.MultipleRidges => "Series of parallel ridges and valleys",
        _ => "Unknown topography type"
    };
}