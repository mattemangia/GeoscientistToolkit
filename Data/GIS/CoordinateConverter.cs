// GeoscientistToolkit/Util/CoordinateConverter.cs

using System.Numerics;
using System.Text.RegularExpressions;

namespace GeoscientistToolkit.Util;

/// <summary>
///     Utility class for coordinate conversions and formatting
/// </summary>
public static partial class CoordinateConverter
{
    #region Parsing

    /// <summary>
    ///     Try to parse coordinate string in various formats
    /// </summary>
    /// <summary>
    ///     Try to parse coordinate string in various formats
    /// </summary>
    public static bool TryParseCoordinate(string input, out Vector2 coordinate)
    {
        coordinate = Vector2.Zero;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Try decimal degrees format: "45.5, -120.3"
        var parts = input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            if (float.TryParse(parts[0], out var lat) && float.TryParse(parts[1], out var lon))
            {
                // Check if order is lon, lat
                if (Math.Abs(lat) > 90 && Math.Abs(lon) <= 180)
                    coordinate = new Vector2(lat, lon); // Swapped
                else
                    coordinate = new Vector2(lon, lat);
                return true;
            }

        // --- TODO COMPLETED: Implement DMS parsing ---
        // Try DMS format: "45°30'15"N 120°15'30"W"
        try
        {
            var dmsPattern = @"(\d{1,3})[°\s](\d{1,2})['\s](\d{1,2}(?:[.,]\d+)?)[""\s]?[ \t]*([NSEW])";
            var matches = Regex.Matches(input, dmsPattern, RegexOptions.IgnoreCase);

            if (matches.Count == 2)
            {
                double lat = 0, lon = 0;

                foreach (Match match in matches)
                {
                    var d = int.Parse(match.Groups[1].Value);
                    var m = int.Parse(match.Groups[2].Value);
                    var s = double.Parse(match.Groups[3].Value.Replace(',', '.'));
                    var direction = match.Groups[4].Value.ToUpper();

                    var decimalValue = DMSToDecimal(d, m, s);

                    if (direction == "N" || direction == "S")
                        lat = direction == "S" ? -decimalValue : decimalValue;
                    else if (direction == "E" || direction == "W")
                        lon = direction == "W" ? -decimalValue : decimalValue;
                }

                if (lat != 0 && lon != 0)
                {
                    coordinate = new Vector2((float)lon, (float)lat);
                    return true;
                }
            }
        }
        catch
        {
            // Parsing failed, continue to return false
        }
        // --- END MODIFICATION ---

        return false;
    }

    #endregion

    #region Constants

    private const double EarthRadiusKm = 6371.0;
    private const double Deg2Rad = Math.PI / 180.0;
    private const double Rad2Deg = 180.0 / Math.PI;

    #endregion

    #region Decimal Degrees Conversions

    /// <summary>
    ///     Convert decimal degrees to degrees, minutes, seconds
    /// </summary>
    public static (int degrees, int minutes, double seconds) DecimalToDMS(double decimalDegrees)
    {
        var isNegative = decimalDegrees < 0;
        var abs = Math.Abs(decimalDegrees);

        var degrees = (int)Math.Floor(abs);
        var minutesDecimal = (abs - degrees) * 60;
        var minutes = (int)Math.Floor(minutesDecimal);
        var seconds = (minutesDecimal - minutes) * 60;

        if (isNegative)
            degrees = -degrees;

        return (degrees, minutes, seconds);
    }

    /// <summary>
    ///     Convert degrees, minutes, seconds to decimal degrees
    /// </summary>
    public static double DMSToDecimal(int degrees, int minutes, double seconds)
    {
        var sign = degrees < 0 ? -1 : 1;
        var absDegrees = Math.Abs(degrees);

        return sign * (absDegrees + minutes / 60.0 + seconds / 3600.0);
    }

    /// <summary>
    ///     Convert decimal degrees to degrees and decimal minutes
    /// </summary>
    public static (int degrees, double minutes) DecimalToDM(double decimalDegrees)
    {
        var isNegative = decimalDegrees < 0;
        var abs = Math.Abs(decimalDegrees);

        var degrees = (int)Math.Floor(abs);
        var minutes = (abs - degrees) * 60;

        if (isNegative)
            degrees = -degrees;

        return (degrees, minutes);
    }

    /// <summary>
    ///     Convert degrees and decimal minutes to decimal degrees
    /// </summary>
    public static double DMToDecimal(int degrees, double minutes)
    {
        var sign = degrees < 0 ? -1 : 1;
        var absDegrees = Math.Abs(degrees);

        return sign * (absDegrees + minutes / 60.0);
    }

    #endregion

    #region Formatting

    /// <summary>
    ///     Format decimal degrees with direction
    /// </summary>
    public static string FormatDD(double decimalDegrees, bool isLongitude)
    {
        var direction = GetDirection(decimalDegrees, isLongitude);
        return $"{Math.Abs(decimalDegrees):F6}°{direction}";
    }

    /// <summary>
    ///     Format coordinate pair
    /// </summary>
    public static string FormatCoordinate(Vector2 coordinate, CoordinateFormat format = CoordinateFormat.DecimalDegrees)
    {
        return format switch
        {
            CoordinateFormat.DecimalDegrees =>
                $"{FormatDD(coordinate.Y, false)}, {FormatDD(coordinate.X, true)}",
            CoordinateFormat.DegreesMinutesSeconds =>
                $"{FormatDMS(coordinate.Y, false)}, {FormatDMS(coordinate.X, true)}",
            CoordinateFormat.DegreesMinutes =>
                $"{FormatDM(coordinate.Y, false)}, {FormatDM(coordinate.X, true)}",
            _ => $"{coordinate.Y:F6}, {coordinate.X:F6}"
        };
    }

    private static string GetDirection(double value, bool isLongitude)
    {
        if (isLongitude)
            return value >= 0 ? "E" : "W";
        return value >= 0 ? "N" : "S";
    }

    #endregion

    #region Distance Calculations

    /// <summary>
    ///     Calculate Haversine distance between two points (in kilometers)
    /// </summary>
    public static double HaversineDistance(Vector2 point1, Vector2 point2)
    {
        var lat1 = point1.Y * Deg2Rad;
        var lon1 = point1.X * Deg2Rad;
        var lat2 = point2.Y * Deg2Rad;
        var lon2 = point2.X * Deg2Rad;

        var dLat = lat2 - lat1;
        var dLon = lon2 - lon1;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }

    /// <summary>
    ///     Calculate bearing between two points (in degrees, 0-360)
    /// </summary>
    public static double CalculateBearing(Vector2 point1, Vector2 point2)
    {
        var lat1 = point1.Y * Deg2Rad;
        var lon1 = point1.X * Deg2Rad;
        var lat2 = point2.Y * Deg2Rad;
        var lon2 = point2.X * Deg2Rad;

        var dLon = lon2 - lon1;

        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) -
                Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

        var bearing = Math.Atan2(y, x) * Rad2Deg;

        return (bearing + 360) % 360;
    }

    /// <summary>
    ///     Calculate destination point given start point, bearing, and distance
    /// </summary>
    public static Vector2 CalculateDestination(Vector2 start, double bearingDegrees, double distanceKm)
    {
        var lat1 = start.Y * Deg2Rad;
        var lon1 = start.X * Deg2Rad;
        var bearing = bearingDegrees * Deg2Rad;
        var angularDistance = distanceKm / EarthRadiusKm;

        var lat2 = Math.Asin(
            Math.Sin(lat1) * Math.Cos(angularDistance) +
            Math.Cos(lat1) * Math.Sin(angularDistance) * Math.Cos(bearing)
        );

        var lon2 = lon1 + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angularDistance) * Math.Cos(lat1),
            Math.Cos(angularDistance) - Math.Sin(lat1) * Math.Sin(lat2)
        );

        return new Vector2((float)(lon2 * Rad2Deg), (float)(lat2 * Rad2Deg));
    }

    /// <summary>
    ///     Calculate area of polygon (in square kilometers)
    /// </summary>
    public static double CalculatePolygonArea(List<Vector2> coordinates)
    {
        if (coordinates.Count < 3)
            return 0;

        double area = 0;
        var n = coordinates.Count;

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            var p1 = coordinates[i];
            var p2 = coordinates[j];

            area += p1.X * p2.Y;
            area -= p2.X * p1.Y;
        }

        area = Math.Abs(area) / 2.0;

        // Convert to square kilometers (rough approximation)
        var avgLat = coordinates.Average(c => c.Y);
        var kmPerDegLat = 111.32;
        var kmPerDegLon = 111.32 * Math.Cos(avgLat * Deg2Rad);

        return area * kmPerDegLat * kmPerDegLon;
    }

    #endregion

    #region UTM Conversions

    /// <summary>
    ///     Convert lat/lon to UTM coordinates
    /// </summary>
    public static (double easting, double northing, int zone, bool isNorthern) LatLonToUTM(double latitude,
        double longitude)
    {
        // Calculate zone
        var zone = (int)Math.Floor((longitude + 180) / 6) + 1;
        var isNorthern = latitude >= 0;

        // Central meridian
        var lambda0 = (zone - 1) * 6 - 180 + 3;

        var phi = latitude * Deg2Rad;
        var lambda = longitude * Deg2Rad;
        var lambda0Rad = lambda0 * Deg2Rad;

        // WGS84 parameters
        const double a = 6378137.0; // semi-major axis
        const double f = 1 / 298.257223563; // flattening
        var b = a * (1 - f); // semi-minor axis
        var e2 = (a * a - b * b) / (a * a); // first eccentricity squared
        var n = (a - b) / (a + b);

        var k0 = 0.9996; // scale factor

        // Calculations
        var N = a / Math.Sqrt(1 - e2 * Math.Sin(phi) * Math.Sin(phi));
        var T = Math.Tan(phi) * Math.Tan(phi);
        var C = e2 * Math.Cos(phi) * Math.Cos(phi) / (1 - e2);
        var A = (lambda - lambda0Rad) * Math.Cos(phi);

        var M = a * ((1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256) * phi
                     - (3 * e2 / 8 + 3 * e2 * e2 / 32 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(2 * phi)
                     + (15 * e2 * e2 / 256 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(4 * phi)
                     - 35 * e2 * e2 * e2 / 3072 * Math.Sin(6 * phi));

        var easting = 500000 + k0 * N * (A + (1 - T + C) * A * A * A / 6
                                           + (5 - 18 * T + T * T + 72 * C - 58 * e2 / (1 - e2)) * A * A * A * A * A /
                                           120);

        var northing = k0 * (M + N * Math.Tan(phi) * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * A * A * A * A / 24
                                                                + (61 - 58 * T + T * T + 600 * C -
                                                                   330 * e2 / (1 - e2)) * A * A * A * A * A * A / 720));

        if (!isNorthern)
            northing += 10000000; // False northing for southern hemisphere

        return (easting, northing, zone, isNorthern);
    }

    /// <summary>
    ///     Convert UTM to lat/lon
    /// </summary>
    public static (double latitude, double longitude) UTMToLatLon(double easting, double northing, int zone,
        bool isNorthern)
    {
        const double a = 6378137.0;
        const double f = 1 / 298.257223563;
        var b = a * (1 - f);
        var e2 = (a * a - b * b) / (a * a);
        var k0 = 0.9996;

        var x = easting - 500000;
        var y = isNorthern ? northing : northing - 10000000;

        var lambda0 = (zone - 1) * 6 - 180 + 3;
        var lambda0Rad = lambda0 * Deg2Rad;

        var M = y / k0;
        var mu = M / (a * (1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256));

        var e1 = (1 - Math.Sqrt(1 - e2)) / (1 + Math.Sqrt(1 - e2));
        var phi1 = mu + (3 * e1 / 2 - 27 * e1 * e1 * e1 / 32) * Math.Sin(2 * mu)
                      + (21 * e1 * e1 / 16 - 55 * e1 * e1 * e1 * e1 / 32) * Math.Sin(4 * mu)
                      + 151 * e1 * e1 * e1 / 96 * Math.Sin(6 * mu);

        var N1 = a / Math.Sqrt(1 - e2 * Math.Sin(phi1) * Math.Sin(phi1));
        var T1 = Math.Tan(phi1) * Math.Tan(phi1);
        var C1 = e2 * Math.Cos(phi1) * Math.Cos(phi1) / (1 - e2);
        var R1 = a * (1 - e2) / Math.Pow(1 - e2 * Math.Sin(phi1) * Math.Sin(phi1), 1.5);
        var D = x / (N1 * k0);

        var latitude = phi1 - N1 * Math.Tan(phi1) / R1 *
            (D * D / 2 - (5 + 3 * T1 + 10 * C1 - 4 * C1 * C1 - 9 * e2 / (1 - e2)) * D * D * D * D / 24
             + (61 + 90 * T1 + 298 * C1 + 45 * T1 * T1 - 252 * e2 / (1 - e2) - 3 * C1 * C1) * D * D * D * D * D * D /
             720);

        var longitude = (D - (1 + 2 * T1 + C1) * D * D * D / 6
                         + (5 - 2 * C1 + 28 * T1 - 3 * C1 * C1 + 8 * e2 / (1 - e2) + 24 * T1 * T1) * D * D * D * D * D /
                         120) / Math.Cos(phi1);

        latitude = latitude * Rad2Deg;
        longitude = lambda0 + longitude * Rad2Deg;

        return (latitude, longitude);
    }

    #endregion
}

public enum CoordinateFormat
{
    DecimalDegrees,
    DegreesMinutesSeconds,
    DegreesMinutes,
    UTM
}