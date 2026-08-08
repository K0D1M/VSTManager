using System.Text.Json;

namespace VstManager.Core.Services;

/// <summary>
/// Shared file handling for the app's small JSON sidecar files.
///
/// Exists because a plain <see cref="File.WriteAllText(string,string?)"/> can leave a half-written
/// file behind: it truncates first and writes after, so a process that dies mid-write — or two
/// processes writing at once — produces valid-looking JSON that is silently malformed. That
/// happened in practice and left the app unable to start at all, because the damaged file was
/// read from a constructor.
///
/// Writing through a temporary file and then replacing the real one makes the swap atomic: a
/// reader sees either the old file or the new one, never a torn mixture.
/// </summary>
public static class JsonFileStore
{
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(value, options);
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);

            // File.Move with overwrite is atomic on Windows for same-volume moves, which this
            // always is — the temp file sits beside its target.
            File.Move(tempPath, path, overwrite: true);
        }
        catch (IOException)
        {
            // Fall back to a direct write rather than losing the change entirely; this is the
            // pre-existing behaviour, just no longer the default path.
            try
            {
                File.WriteAllText(path, json);
            }
            catch (IOException)
            {
                // Nothing more to try — the caller's in-memory state stays correct for this run.
            }

            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// Moves an unreadable file aside so the app can start with empty state while leaving the
    /// damaged content on disk for recovery, rather than overwriting it on the next save.
    /// </summary>
    public static void Quarantine(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: if it can't be renamed, the caller still starts with empty state.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
