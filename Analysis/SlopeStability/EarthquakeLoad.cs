using System;
using System.Numerics;
using System.Collections.Generic;

namespace GeoscientistToolkit.Analysis.SlopeStability
{
    /// <summary>
    /// Earthquake loading for triggering slope failure.
    /// Implements seismic wave propagation with epicenter and intensity.
    /// </summary>
    public class EarthquakeLoad
    {
        // Earthquake properties
        public Vector3 Epicenter { get; set; }          // Location (x, y, z)
        public float Magnitude { get; set; }            // Moment magnitude (Mw)
        public float Depth { get; set; }                // Focal depth (meters)
        public float Duration { get; set; }             // Duration (seconds)
        public float StartTime { get; set; }            // Start time in simulation (seconds)

        // Ground motion parameters
        public float PeakGroundAcceleration { get; set; }   // PGA (m/s²)
        public float PeakGroundVelocity { get; set; }       // PGV (m/s)
        public float PeakGroundDisplacement { get; set; }   // PGD (m)

        // Frequency content
        public float DominantFrequency { get; set; }    // Hz
        public float FrequencyBandwidth { get; set; }   // Hz

        // Wave propagation
        public float PWaveVelocity { get; set; }        // m/s (primary wave)
        public float SWaveVelocity { get; set; }        // m/s (secondary wave)
        public float AttenuationFactor { get; set; }    // Q factor (dimensionless)

        // Direction
        public Vector3 PropagationDirection { get; set; }
        public bool UseRadialPropagation { get; set; }  // Radial from epicenter

        // Time function type
        public EarthquakeTimeFunction TimeFunction { get; set; }

        // Synthetic acceleration record (if precomputed)
        public List<float> AccelerationTimeSeries { get; set; }
        public float TimeStep { get; set; }

        public EarthquakeLoad()
        {
            Epicenter = Vector3.Zero;
            Magnitude = 5.0f;
            Depth = 5000.0f;  // 5 km
            Duration = 20.0f;
            StartTime = 1.0f;

            // Estimate PGA from magnitude (simplified empirical relation)
            UpdateGroundMotionFromMagnitude();

            DominantFrequency = 2.0f;  // 2 Hz typical
            FrequencyBandwidth = 5.0f;

            PWaveVelocity = 5000.0f;   // 5 km/s typical for rock
            SWaveVelocity = 3000.0f;   // 3 km/s
            AttenuationFactor = 100.0f; // Q = 100

            PropagationDirection = Vector3.UnitZ;
            UseRadialPropagation = true;

            TimeFunction = EarthquakeTimeFunction.RickerWavelet;

            AccelerationTimeSeries = new List<float>();
            TimeStep = 0.01f;
        }

        /// <summary>
        /// Updates ground motion parameters based on magnitude using empirical relations.
        /// Based on Campbell-Bozorgnia 2008 GMPE (simplified).
        /// </summary>
        public void UpdateGroundMotionFromMagnitude()
        {
            // PGA in g (simplified empirical formula)
            // log10(PGA) ≈ a*M + b
            float a = 0.5f;
            float b = -1.8f;
            float pgaG = MathF.Pow(10.0f, a * Magnitude + b);
            PeakGroundAcceleration = pgaG * 9.81f;  // Convert to m/s²

            // PGV (empirical relation)
            PeakGroundVelocity = PeakGroundAcceleration * 0.1f;  // Simplified

            // PGD
            PeakGroundDisplacement = PeakGroundVelocity * 0.1f;

            // Duration (Trifunac and Brady 1975)
            Duration = MathF.Pow(10.0f, -1.02f + 0.303f * Magnitude);
        }

        /// <summary>
        /// Generates synthetic acceleration time series.
        /// </summary>
        public void GenerateSyntheticAccelerogram(int seed = 12345)
        {
            int numSamples = (int)(Duration / TimeStep);
            AccelerationTimeSeries.Clear();

            Random random = new Random(seed);

            for (int i = 0; i < numSamples; i++)
            {
                float t = i * TimeStep;
                float acceleration = GenerateAccelerationAtTime(t, random);
                AccelerationTimeSeries.Add(acceleration);
            }
        }

        /// <summary>
        /// Generates acceleration at a specific time using the selected time function.
        /// </summary>
        private float GenerateAccelerationAtTime(float t, Random random)
        {
            float envelope = GetEnvelopeFunction(t);
            float harmonic = 0.0f;

            switch (TimeFunction)
            {
                case EarthquakeTimeFunction.Sine:
                    harmonic = MathF.Sin(2.0f * MathF.PI * DominantFrequency * t);
                    break;

                case EarthquakeTimeFunction.RickerWavelet:
                    float omega = 2.0f * MathF.PI * DominantFrequency;
                    float arg = omega * omega * t * t / 4.0f;
                    harmonic = (1.0f - 2.0f * arg) * MathF.Exp(-arg);
                    break;

                case EarthquakeTimeFunction.Stochastic:
                    // Band-limited white noise
                    float sum = 0.0f;
                    int numFreqs = 10;
                    for (int k = 0; k < numFreqs; k++)
                    {
                        float freq = DominantFrequency + (float)(random.NextDouble() * 2.0 - 1.0) * FrequencyBandwidth;
                        float phase = (float)(random.NextDouble() * 2.0 * Math.PI);
                        sum += MathF.Sin(2.0f * MathF.PI * freq * t + phase);
                    }
                    harmonic = sum / numFreqs;
                    break;

                case EarthquakeTimeFunction.KanaiTajimi:
                    // Kanai-Tajimi spectrum (simplified)
                    float omegaG = 2.0f * MathF.PI * DominantFrequency;
                    float zetaG = 0.6f;  // Damping ratio
                    float whiteNoise = (float)(random.NextDouble() * 2.0 - 1.0);
                    // This is simplified - proper implementation requires filtering
                    harmonic = whiteNoise * MathF.Exp(-zetaG * omegaG * t);
                    break;
            }

            return PeakGroundAcceleration * envelope * harmonic;
        }

        /// <summary>
        /// Envelope function to shape the earthquake duration (rise-steady-decay).
        /// </summary>
        private float GetEnvelopeFunction(float t)
        {
            float t1 = Duration * 0.2f;  // Rise time
            float t2 = Duration * 0.7f;  // Decay start time

            if (t < t1)
            {
                // Rise phase
                return MathF.Pow(t / t1, 2);
            }
            else if (t < t2)
            {
                // Steady phase
                return 1.0f;
            }
            else if (t < Duration)
            {
                // Decay phase
                float decay = (Duration - t) / (Duration - t2);
                return MathF.Pow(decay, 2);
            }
            else
            {
                return 0.0f;
            }
        }

        /// <summary>
        /// Calculates ground acceleration at a point in space and time.
        /// Accounts for wave propagation, attenuation, and directionality.
        /// </summary>
        public Vector3 GetAccelerationAtPoint(Vector3 position, float currentTime)
        {
            float t = currentTime - StartTime;

            if (t < 0 || t > Duration)
                return Vector3.Zero;

            // Distance from epicenter
            Vector3 toPoint = position - Epicenter;
            float distance = toPoint.Length();

            if (distance < 0.1f)
                distance = 0.1f;  // Avoid singularity

            // Wave arrival time (using S-wave as main contributor to damage)
            float arrivalTime = distance / SWaveVelocity;

            if (t < arrivalTime)
                return Vector3.Zero;  // Wave hasn't arrived yet

            float localTime = t - arrivalTime;

            // Get base acceleration magnitude
            float accelerationMagnitude;
            if (AccelerationTimeSeries.Count > 0)
            {
                // Use precomputed time series
                int index = (int)(localTime / TimeStep);
                if (index >= 0 && index < AccelerationTimeSeries.Count)
                    accelerationMagnitude = AccelerationTimeSeries[index];
                else
                    accelerationMagnitude = 0.0f;
            }
            else
            {
                // Generate on-the-fly
                accelerationMagnitude = GenerateAccelerationAtTime(localTime, new Random(42));
            }

            // Apply geometric attenuation (1/r)
            float geometricAttenuation = 1.0f / (distance + 1.0f);

            // Apply material attenuation (exp(-distance/Q))
            float materialAttenuation = MathF.Exp(-distance / (AttenuationFactor * SWaveVelocity));

            float totalAttenuation = geometricAttenuation * materialAttenuation;
            accelerationMagnitude *= totalAttenuation;

            // Direction of acceleration
            Vector3 direction;
            if (UseRadialPropagation)
            {
                // Radial from epicenter (horizontal component dominant)
                direction = Vector3.Normalize(new Vector3(toPoint.X, toPoint.Y, 0));
                if (direction.LengthSquared() < 0.01f)
                    direction = Vector3.UnitX;
            }
            else
            {
                direction = PropagationDirection;
            }

            return direction * accelerationMagnitude;
        }

        /// <summary>
        /// Creates a preset earthquake based on magnitude.
        /// </summary>
        public static EarthquakeLoad CreatePreset(float magnitude, Vector3 epicenter)
        {
            var earthquake = new EarthquakeLoad
            {
                Magnitude = magnitude,
                Epicenter = epicenter,
                Depth = 5000.0f + magnitude * 1000.0f,
                UseRadialPropagation = true
            };

            earthquake.UpdateGroundMotionFromMagnitude();
            earthquake.GenerateSyntheticAccelerogram();

            return earthquake;
        }
    }

    /// <summary>
    /// Type of time function for earthquake ground motion.
    /// </summary>
    public enum EarthquakeTimeFunction
    {
        Sine,           // Simple sinusoidal
        RickerWavelet,  // Ricker wavelet (Mexican hat)
        Stochastic,     // Band-limited white noise
        KanaiTajimi,    // Kanai-Tajimi stochastic model
        Custom          // User-defined time series
    }
}
