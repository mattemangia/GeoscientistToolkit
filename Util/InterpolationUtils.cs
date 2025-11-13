// GeoscientistToolkit/Util/InterpolationUtils.cs

using System.Numerics;

namespace GeoscientistToolkit.Util
{
    /// <summary>
    /// Provides mathematical helper functions for interpolation.
    /// </summary>
    public static class InterpolationUtils
    {
        /// <summary>
        /// Performs Catmull-Rom interpolation between two vectors using the specified control points.
        /// </summary>
        /// <param name="p0">The first control point (before the segment).</param>
        /// <param name="p1">The second control point (the start of the segment).</param>
        /// <param name="p2">The third control point (the end of the segment).</param>
        /// <param name="p3">The fourth control point (after the segment).</param>
        /// <param name="t">The weighting factor.</param>
        /// <returns>The interpolated vector.</returns>
        public static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2.0f * p1) +
                (-p0 + p2) * t +
                (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
                (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
            );
        }
    }
}