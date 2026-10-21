namespace Alveara.Api.Tests;

/// <summary>
/// Removes a test's storage folder. A file that is still held for a moment (a stream closing, antivirus scanning it) is retried a few times; if the folder still cannot be removed the failure is raised,
/// never swallowed, so a leak shows up as a failed test and not as a silently growing data folder. Removing a folder that does not exist is a no-op, so calling it twice is safe.
/// </summary>
internal static class TestStorageCleanup
{
    public static void Delete(string folder, int attempts = 5, TimeSpan? pause = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                return;
            }
            catch (IOException ex) when (attempt < attempts)
            {
                Thread.Sleep(pause ?? TimeSpan.FromMilliseconds(200));
                _ = ex;                                              // retried; the last attempt's failure is raised below
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"The test storage folder could not be removed after {attempt} attempt(s): {ex.Message}", ex);
            }
        }
    }
}
