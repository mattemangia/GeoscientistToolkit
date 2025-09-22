// GeoscientistToolkit/Analysis/AcousticSimulation/VelocityTomographyGenerator.cs
using System;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.AcousticSimulation
{
    /// <summary>
    /// Generates 2D velocity tomography slices from 3D simulation results for visualization.
    /// </summary>
    public class VelocityTomographyGenerator : IDisposable
    {
        public byte[] Generate2DTomography(SimulationResults results, int axis, int sliceIndex)
        {
            if (results == null || results.WaveFieldVx == null) return null;
            
            int width, height;
            switch (axis)
            {
                case 0: // X slice
                    width = results.WaveFieldVy.GetLength(1);
                    height = results.WaveFieldVz.GetLength(2);
                    break;
                case 1: // Y slice
                    width = results.WaveFieldVx.GetLength(0);
                    height = results.WaveFieldVz.GetLength(2);
                    break;
                case 2: // Z slice
                    width = results.WaveFieldVx.GetLength(0);
                    height = results.WaveFieldVy.GetLength(1);
                    break;
                default:
                    return null;
            }
            
            var tomography = new byte[width * height * 4]; // RGBA
            
            float minVel = float.MaxValue, maxVel = float.MinValue;
            var velocities = new float[width * height];
            
            int idx = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float vx = 0, vy = 0, vz = 0;
                    
                    switch (axis)
                    {
                        case 0:
                            vx = results.WaveFieldVx[sliceIndex, x, y];
                            vy = results.WaveFieldVy[sliceIndex, x, y];
                            vz = results.WaveFieldVz[sliceIndex, x, y];
                            break;
                        case 1:
                            vx = results.WaveFieldVx[x, sliceIndex, y];
                            vy = results.WaveFieldVy[x, sliceIndex, y];
                            vz = results.WaveFieldVz[x, sliceIndex, y];
                            break;
                        case 2:
                            vx = results.WaveFieldVx[x, y, sliceIndex];
                            vy = results.WaveFieldVy[x, y, sliceIndex];
                            vz = results.WaveFieldVz[x, y, sliceIndex];
                            break;
                    }
                    
                    float velocity = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    velocities[idx] = velocity;
                    
                    if (velocity < minVel) minVel = velocity;
                    if (velocity > maxVel) maxVel = velocity;
                    idx++;
                }
            }
            
            float range = maxVel - minVel;
            if (range < 1e-6f) range = 1e-6f;
            
            idx = 0;
            for (int i = 0; i < velocities.Length; i++)
            {
                float normalized = (velocities[i] - minVel) / range;
                var color = GetJetColor(normalized);
                
                tomography[idx++] = (byte)(color.X * 255);
                tomography[idx++] = (byte)(color.Y * 255);
                tomography[idx++] = (byte)(color.Z * 255);
                tomography[idx++] = 255;
            }
            
            return tomography;
        }

        private Vector4 GetJetColor(float value)
        {
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