using System.Diagnostics;

namespace VstManager.Core.Services;

public class PluginVersionDetector
{
    public string? DetectFromFile(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException)
        {
            return null;
        }
    }
}
