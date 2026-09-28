namespace VstManager.Core;

/// <summary>
/// Name of the per-user data folder under %APPDATA%. The NoStatus build (NO_LICENSE_STATUS) has
/// no Legit/Cracked field at all, so saving through it would strip those from a shared
/// library.json — it keeps its data in a folder of its own instead.
/// </summary>
public static class AppDataLocation
{
#if NO_LICENSE_STATUS
    public const string FolderName = "VstManagerNoStatus";
#else
    public const string FolderName = "VstManager";
#endif
}
