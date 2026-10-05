namespace KassenSync.Core.Services;

public sealed class FileStabilityService
{
    public async Task<bool> WaitUntilStableAsync(string path, int delayMilliseconds, CancellationToken cancellationToken = default)
    {
        const int attempts = 20;
        long? lastLength = null;
        DateTime? lastWrite = null;

        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return false;

                await using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var length = info.Length;
                var write = info.LastWriteTimeUtc;
                if (lastLength == length && lastWrite == write)
                    return true;

                lastLength = length;
                lastWrite = write;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(Math.Max(250, delayMilliseconds), cancellationToken);
        }

        return false;
    }
}
