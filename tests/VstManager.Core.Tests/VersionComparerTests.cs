using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class VersionComparerTests
{
    [Theory]
    [InlineData("4.14", "4.13", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("4.13", "4.13", false)]
    [InlineData("4.12", "4.13", false)]
    [InlineData("v2.1.5", "2.0.8", true)]
    [InlineData("2.1.5", "V2.1.5", false)]
    public void IsNewer_ComparesParsedVersions(string latest, string current, bool expected)
    {
        Assert.Equal(expected, VersionComparer.IsNewer(latest, current));
    }

    [Fact]
    public void IsNewer_ShorterAndLongerFormsOfSameVersion_AreEqual()
    {
        Assert.False(VersionComparer.IsNewer("4.13.0", "4.13"));
        Assert.False(VersionComparer.IsNewer("4.13", "4.13.0.0"));
    }

    [Fact]
    public void IsNewer_BareMajorVersion_IsPadded()
    {
        Assert.True(VersionComparer.IsNewer("5", "4.9"));
        Assert.False(VersionComparer.IsNewer("4", "4.0"));
    }

    [Theory]
    [InlineData(null, "1.0")]
    [InlineData("1.0", null)]
    [InlineData("", "1.0")]
    [InlineData("beta", "1.0")]
    [InlineData("2.0", "unknown")]
    public void IsNewer_UnparseableOrMissingInput_NeverFlagsOutdated(string? latest, string? current)
    {
        Assert.False(VersionComparer.IsNewer(latest, current));
    }

    // Windows VERSIONINFO metadata commonly reports comma-separated versions, which is what
    // the file-based detector reads for plugins like Serum and OTT.
    [Theory]
    [InlineData("1.3.7", "1,3,7,0", false)]
    [InlineData("1.4.0", "1,3,7,0", true)]
    [InlineData("1.3.6", "1,3,7,0", false)]
    public void IsNewer_CommaSeparatedInstalledVersion_ComparesCorrectly(string latest, string current, bool expected)
    {
        Assert.Equal(expected, VersionComparer.IsNewer(latest, current));
    }

    [Theory]
    [InlineData("2.8.8", "2.8.7c", true)]
    [InlineData("2.8.7", "2.8.7c", false)]
    [InlineData("5.5.5", "5.5.4 64 bit", true)]
    [InlineData("5.5.4", "5.5.4 64 bit", false)]
    public void IsNewer_TrailingQualifiers_AreIgnoredNotMisread(string latest, string current, bool expected)
    {
        Assert.Equal(expected, VersionComparer.IsNewer(latest, current));
    }

    [Fact]
    public void IsNewer_MoreThanFourComponents_DoesNotThrowAndCompares()
    {
        Assert.True(VersionComparer.IsNewer("1.2.3.4.5", "1.2.3.3"));
        Assert.False(VersionComparer.IsNewer("1.2.3.4.5", "1.2.3.4"));
    }
}
