// GeoscientistToolkit/Analysis/AcousticSimulation/VelocityTomographyGenerator.cs
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.AcousticSimulation
{
    /// <summary>
    /// Generates 2D velocity tomography slices from 3D simulation results for visualization.
    /// </summary>
    public class VelocityTomographyGenerator : IDisposable
    {
        /// <summary>
        /// Generates a 2D tomography image from a slice of the 3D simulation results.
        /// </summary>
        /// <param name="results">The simulation results containing the wave fields.</param>
        /// <param name="axis">The axis to slice along (0=X, 1=Y, 2=Z).</param>
        /// <param name="sliceIndex">The index of the slice along the chosen axis.</param>
        /// <param name="labels">The 3D volume of material labels.</param>
        /// <param name="selectedMaterialIDs">A set of material IDs to show.</param>
        /// <param name="showOnlySelected">If true, non-selected materials will be rendered as transparent, and the color scale will be based only on the visible materials.</param>
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
            if (results == null || results.WaveFieldVx == null || labels == null || selectedMaterialIDs == null) return null;
            
            int width, height;
            switch (axis)
            {
                case 0: width = results.WaveFieldVy.GetLength(1); height = results.WaveFieldVz.GetLength(2); break;
                case 1: width = results.WaveFieldVx.GetLength(0); height = results.WaveFieldVz.GetLength(2); break;
                case 2: width = results.WaveFieldVx.GetLength(0); height = results.WaveFieldVy.GetLength(1); break;
                default: return null;
            }
            
            var tomography = new byte[width * height * 4]; // RGBA
            var velocities = new float[width * height];
            
            // Step 1: Calculate and store the velocity for every single pixel in the slice.
            int flat_idx = 0;
            for (int y_slice = 0; y_slice < height; y_slice++)
            {
                for (int x_slice = 0; x_slice < width; x_slice++)
                {
                    float vx = 0, vy = 0, vz = 0;
                    switch (axis)
                    {
                        case 0:
                            vx = results.WaveFieldVx[sliceIndex, x_slice, y_slice]; vy = results.WaveFieldVy[sliceIndex, x_slice, y_slice]; vz = results.WaveFieldVz[sliceIndex, x_slice, y_slice];
                            break;
                        case 1:
                            vx = results.WaveFieldVx[x_slice, sliceIndex, y_slice]; vy = results.WaveFieldVy[x_slice, sliceIndex, y_slice]; vz = results.WaveFieldVz[x_slice, sliceIndex, y_slice];
                            break;
                        case 2:
                            vx = results.WaveFieldVx[x_slice, y_slice, sliceIndex]; vy = results.WaveFieldVy[x_slice, y_slice, sliceIndex]; vz = results.WaveFieldVz[x_slice, y_slice, sliceIndex];
                            break;
                    }
                    velocities[flat_idx++] = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                }
            }
            
            // --- DYNAMIC COLOR SCALE FIX ---
            // Step 2: Determine the min/max velocity for the color scale. This range is calculated
            // based on the user's filtering choice.
            float minVelForScale = float.MaxValue;
            float maxVelForScale = float.MinValue;
            bool foundAnyRelevantVoxels = false;

            flat_idx = 0;
            for (int y_slice = 0; y_slice < height; y_slice++)
            {
                for (int x_slice = 0; x_slice < width; x_slice++)
                {
                    bool shouldIncludeInScale = !showOnlySelected;
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
                        {
                            shouldIncludeInScale = true;
                        }
                    }

                    if (shouldIncludeInScale)
                    {
                        float v = velocities[flat_idx];
                        if (v < minVelForScale) minVelForScale = v;
                        if (v > maxVelForScale) maxVelForScale = v;
                        foundAnyRelevantVoxels = true;
                    }
                    flat_idx++;
                }
            }

            // Handle case where a slice contains no selected materials
            if (!foundAnyRelevantVoxels)
            {
                minVelForScale = 0;
                maxVelForScale = 0;
            }

            float range = maxVelForScale - minVelForScale;
            if (range < 1e-6f) range = 1e-6f; // Prevent division by zero
            
            // Step 3: Generate the image, normalizing colors using the calculated dynamic range.
            int pixel_idx = 0;
            for (int y_slice = 0; y_slice < height; y_slice++)
            {
                for (int x_slice = 0; x_slice < width; x_slice++)
                {
                    int current_flat_idx = y_slice * width + x_slice;
                    float normalized = (velocities[current_flat_idx] - minVelForScale) / range;
                    var color = GetJetColor(normalized);
                    
                    tomography[pixel_idx++] = (byte)(color.X * 255);
                    tomography[pixel_idx++] = (byte)(color.Y * 255);
                    tomography[pixel_idx++] = (byte)(color.Z * 255);

                    // Apply transparency based on filtering choice
                    byte currentLabel = 0;
                    switch (axis)
                    {
                        case 0: currentLabel = labels[sliceIndex, x_slice, y_slice]; break;
                        case 1: currentLabel = labels[x_slice, sliceIndex, y_slice]; break;
                        case 2: currentLabel = labels[x_slice, y_slice, sliceIndex]; break;
                    }

                    tomography[pixel_idx++] = (showOnlySelected && !selectedMaterialIDs.Contains(currentLabel)) ? (byte)0 : (byte)255;
                }
            }
            
            // Step 4: Return the dynamically calculated min/max for the legend.
            return (tomography, minVelForScale, maxVelForScale);
        }

        public Vector4 GetJetColor(float value)
        {
            value = Math.Clamp(value, 0.0f, 1.0f);
            float r, g, b;
            if (value < 0.25f) { r = 0; g = 4 * value; b = 1; }
            else if (value < 0.5f) { r = 0; g = 1; b = 1 - 4 * (value - 0.25f); }
            else if (value < 0.75f) { r = 4 * (value - 0.5f); g = 1; b = 0; }
            else { r = 1; g = 1 - 4 * (value - 0.75f); b = 0; }
            return new Vector4(r, g, b, 1);
        }

        public void Dispose()
        {
            // No unmanaged resources to dispose in this class
        }
    }
}