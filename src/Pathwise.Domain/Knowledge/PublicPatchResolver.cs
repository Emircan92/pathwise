namespace Pathwise.Domain.Knowledge;

public sealed class PublicPatchResolver
{
    private const string Internal1618Basis =
        "Internal version family 16.18 is associated with public patch 26.18 using the accepted audit, CommunityDragon release metadata, and Riot Data Dragon version metadata.";
    private const string Public2618Basis =
        "Public version family 26.18 directly identifies public patch 26.18.";

    public PatchResolution Resolve(string rawGameVersion)
    {
        ArgumentNullException.ThrowIfNull(rawGameVersion);

        if (!TryReadFamily(rawGameVersion, out var major, out var minor))
            return new(rawGameVersion, null, "The raw game version is malformed and no public patch could be resolved.");

        return (major, minor) switch
        {
            (16, 18) => new(rawGameVersion, new(26, 18), Internal1618Basis),
            (26, 18) => new(rawGameVersion, new(26, 18), Public2618Basis),
            _ => new(rawGameVersion, null, $"Version family {major}.{minor} has no accepted public-patch mapping.")
        };
    }

    private static bool TryReadFamily(string value, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        var parts = value.Split('.', StringSplitOptions.None);
        return parts.Length >= 2 &&
            parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit)) &&
            int.TryParse(parts[0], out major) &&
            int.TryParse(parts[1], out minor);
    }
}
