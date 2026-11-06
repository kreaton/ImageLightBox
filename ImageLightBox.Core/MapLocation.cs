using System.Globalization;
using System.Text.RegularExpressions;

namespace ImageLightBox.Core;

/// <summary>Where a photo or movie was taken, and how to show it on OpenStreetMap.</summary>
public sealed partial record MapLocation(double Latitude, double Longitude)
{
    public string PageUrl => string.Create(CultureInfo.InvariantCulture,
        $"https://www.openstreetmap.org/?mlat={Latitude:0.######}&mlon={Longitude:0.######}#map=16/{Latitude:0.######}/{Longitude:0.######}");

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.#####}, {Longitude:0.#####}");

    /// <summary>
    /// Null for an impossible position, and for exactly 0, 0, which is what some
    /// cameras write when they have no fix.
    /// </summary>
    public static MapLocation? Create(double latitude, double longitude) =>
        Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180 || (latitude == 0 && longitude == 0)
            ? null
            : new MapLocation(latitude, longitude);

    /// <summary>The ISO 6709 form that QuickTime keeps a movie's position in, such as +48.8584+002.2945+035.000/.</summary>
    public static MapLocation? ParseIso6709(string? text)
    {
        var match = text is null ? Match.Empty : Iso6709().Match(text);
        return match.Success
            ? Create(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : null;
    }

    [GeneratedRegex(@"^([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)")]
    private static partial Regex Iso6709();
}
