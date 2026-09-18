using System.Security;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Starlight.Launcher.Services.Settings;

/// <summary>
/// Checks for folders the launcher can't write to (e.g. Program Files without admin rights).
/// </summary>
public static class DataDirectoryAccess
{
    private const int SqliteReadOnly = 8;
    private const int SqliteCantOpen = 14;

    private const int Win32AccessDenied = 5;
    private const int Win32WriteProtect = 19;

    /// <summary>
    /// Creates the folder if needed and tries to write a temporary file into it.
    /// </summary>
    public static bool CanWrite(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        try
        {
            _ = Directory.CreateDirectory(directory);

            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
        {
            Log.Warning(e, "Folder {Directory} is not writable", directory);
            return false;
        }
    }

    /// <summary>
    /// Whether the exception (or anything it wraps) is caused by missing file system permissions.
    /// </summary>
    public static bool IsAccessDenied(Exception? exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case UnauthorizedAccessException or SecurityException:
                case SqliteException { SqliteErrorCode: SqliteReadOnly or SqliteCantOpen }:
                case IOException when (e.HResult & 0xFFFF) is Win32AccessDenied or Win32WriteProtect:
                    return true;
                case AggregateException aggregate when aggregate.InnerExceptions.Any(IsAccessDenied):
                    return true;
            }
        }

        return false;
    }
}
