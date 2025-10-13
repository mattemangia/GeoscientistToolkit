// GeoscientistToolkit/Analysis/AcousticSimulation/VelocityTomographyGenerator.cs

using System.Numerics;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

/// <summary>
///     Generates 2D velocity tomography slices from 3D simulation results for visualization.
/// </summary>
public class VelocityTomographyGenerator : IDisposable
{
    public void Dispose()
    {
        // No unmanaged resources to dispose in this class
    }

    /// <summary>
    ///     Applies a simple 3x3 box blur to smooth the velocity data and reduce checkerboarding.
    /// </summary>
    private float[] ApplyBoxBlur(float[] data, int width, int height)
    {
        var smoothedData = new float[data.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                float sum = 0;
                int count = 0;
                for (var j = -1; j <= 1; j++)
                {
                    for (var i = -1; i <= 1; i++)
                    {
                        var nx = x + i;
                        var ny = y + j;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            sum += data[ny * width + nx];
                            count++;
                        }
                    }
                }
                smoothedData[y * width + x] = count > 0 ? sum / count : 0;
            }
        }
        return smoothedData;
    }

    /// <summary>
    ///     Generates a 2D tomography image from a slice of the 3D simulation results.
    /// </summary>
    /// <param name="results">The simulation results containing the wave fields.</param>
    /// <param name="axis">The axis to slice along (0=X, 1=Y, 2=Z).</param>
    /// <param name="sliceIndex">The index of the slice along the chosen axis.</param>
    /// <param name="labels">The 3D volume of material labels.</param>
    /// <param name="selectedMaterialIDs">A set of material IDs to show.</param>
    /// <param name="showOnlySelected">
    ///     If true, non-selected materials will be rendered as transparent, and the color scale
    ///     will be based only on the visible materials.
    /// </param>
    /// <returns>A tuple containing the RGBA pixel data, and the min/max velocities used for the color bar.</returns>
    public (byte[] pixelData, float minVelocity, float maxVelocity)? Generate2DTomography(
        SimulationResults results,
        int axis,
        int sliceIndex,
        byte[,,] labels,
        ISet<byte> selectedMaterialIDs,
        bool showOnlySelected
    )
    {
        if (results == null || results.WaveFieldVx == null || labels == null || selectedMaterialIDs == null)
            return null;

        int width, height;
        switch (axis)
        {
            case 0:
                width = results.WaveFieldVy.GetLength(1);
                height = results.WaveFieldVz.GetLength(2);
                break;
            case 1:
                width = results.WaveFieldVx.GetLength(0);
                height = results.WaveFieldVz.GetLength(2);
                break;
            case 2:
                width = results.WaveFieldVx.GetLength(0);
                height = results.WaveFieldVy.GetLength(1);
                break;
            default: return null;
        }

        var tomography = new byte[width * height * 4]; // RGBA
        var velocities = new float[width * height];

        // Step 1: Calculate velocity magnitude for each pixel
        var flat_idx = 0;
        for (var y_slice = 0; y_slice < height; y_slice++)
        for (var x_slice = 0; x_slice < width; x_slice++)
        {
            float vx = 0, vy = 0, vz = 0;
            switch (axis)
            {
                case 0:
                    vx = results.WaveFieldVx[sliceIndex, x_slice, y_slice];
                    vy = results.WaveFieldVy[sliceIndex, x_slice, y_slice];
                    vz = results.WaveFieldVz[sliceIndex, x_slice, y_slice];
                    break;
                case 1:
                    vx = results.WaveFieldVx[x_slice, sliceIndex, y_slice];
                    vy = results.WaveFieldVy[x_slice, sliceIndex, y_slice];
                    vz = results.WaveFieldVz[x_slice, sliceIndex, y_slice];
                    break;
                case 2:
                    vx = results.WaveFieldVx[x_slice, y_slice, sliceIndex];
                    vy = results.WaveFieldVy[x_slice, y_slice, sliceIndex];
                    vz = results.WaveFieldVz[x_slice, y_slice, sliceIndex];
                    break;
            }

            velocities[flat_idx++] = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
        }

        // --- FIX START: Apply smoothing to reduce checkerboarding ---
        var smoothedVelocities = ApplyBoxBlur(velocities, width, height);
        // --- FIX END ---
        
        // Step 2: Calculate min/max based on filtering AND visible material
        var minVelForScale = float.MaxValue;
        var maxVelForScale = float.MinValue;
        var foundAnyRelevantVoxels = false;

        flat_idx = 0;
        for (var y_slice = 0; y_slice < height; y_slice++)
        for (var x_slice = 0; x_slice < width; x_slice++)
        {
            var shouldIncludeInScale = !showOnlySelected;
            if (showOnlySelected)
            {
                byte currentLabel = 0;
                switch (axis)
                {
                    case 0: currentLabel = labels[sliceIndex, x_slice, y_slice]; break;
                    case 1: currentLabel = labels[x_slice, sliceIndex, y_slice]; break;
                    case 2: currentLabel = labels[x_slice, y_slice, sliceIndex]; break;
                }

                if (selectedMaterialIDs.Contains(currentLabel))
                    shouldIncludeInScale = true;
            }

            if (shouldIncludeInScale)
            {
                // --- FIX: Use smoothed velocities for scale calculation ---
                var v = smoothedVelocities[flat_idx];

                // FIX: Only include non-zero values in the scale calculation
                // This prevents low velocities from being washed out by zeros
                if (v > 1e-10f)
                {
                    if (v < minVelForScale) minVelForScale = v;
                    if (v > maxVelForScale) maxVelForScale = v;
                    foundAnyRelevantVoxels = true;
                }
            }

            flat_idx++;
        }

        // Handle edge cases
        if (!foundAnyRelevantVoxels || maxVelForScale < 1e-10f)
        {
            minVelForScale = 0;
            maxVelForScale = 1e-10f; // Minimum non-zero value
        }

        // FIX: Use logarithmic scale for better visualization of wide dynamic ranges
        var useLogScale = maxVelForScale / (minVelForScale + 1e-15f) > 100.0f;

        if (useLogScale)
        {
            minVelForScale = MathF.Log10(minVelForScale + 1e-15f);
            maxVelForScale = MathF.Log10(maxVelForScale + 1e-15f);
        }

        var range = maxVelForScale - minVelForScale;
        if (range < 1e-10f) range = 1e-10f;

        // Step 3: Generate the image with normalized colors
        var pixel_idx = 0;
        for (var y_slice = 0; y_slice < height; y_slice++)
        for (var x_slice = 0; x_slice < width; x_slice++)
        {
            var current_flat_idx = y_slice * width + x_slice;
            // --- FIX: Use smoothed velocities for color generation ---
            var velocity = smoothedVelocities[current_flat_idx];

            // Apply log scale if needed
            var velForColor = velocity;
            if (useLogScale && velocity > 1e-15f)
                velForColor = MathF.Log10(velocity);
            else if (useLogScale)
                velForColor = minVelForScale;

            var normalized = (velForColor - minVelForScale) / range;
            normalized = Math.Clamp(normalized, 0.0f, 1.0f);

            var color = GetJetColor(normalized);

            tomography[pixel_idx++] = (byte)(color.X * 255);
            tomography[pixel_idx++] = (byte)(color.Y * 255);
            tomography[pixel_idx++] = (byte)(color.Z * 255);

            // Apply transparency for filtered materials
            byte currentLabel = 0;
            switch (axis)
            {
                case 0: currentLabel = labels[sliceIndex, x_slice, y_slice]; break;
                case 1: currentLabel = labels[x_slice, sliceIndex, y_slice]; break;
                case 2: currentLabel = labels[x_slice, y_slice, sliceIndex]; break;
            }

            tomography[pixel_idx++] =
                showOnlySelected && !selectedMaterialIDs.Contains(currentLabel) ? (byte)0 : (byte)255;
        }

        // Step 4: Return actual min/max (not log-scaled) for display
        var displayMin = useLogScale ? MathF.Pow(10, minVelForScale) : minVelForScale;
        var displayMax = useLogScale ? MathF.Pow(10, maxVelForScale) : maxVelForScale;

        return (tomography, displayMin, displayMax);
    }

    public Vector4 GetJetColor(float value)
    {
        value = Math.Clamp(value, 0.0f, 1.0f);
        float r, g, b;
        if (value < 0.25f)
        {
            r = 0;
            g = 4 * value;
            b = 1;
        }
        else if (value < 0.5f)
        {
            r = 0;
            g = 1;
            b = 1 - 4 * (value - 0.25f);
        }
        else if (value < 0.75f)
        {
            r = 4 * (value - 0.5f);
            g = 1;
            b = 0;
        }
        else
        {
            r = 1;
            g = 1 - 4 * (value - 0.75f);
            b = 0;
        }

        return new Vector4(r, g, b, 1);
    }
}