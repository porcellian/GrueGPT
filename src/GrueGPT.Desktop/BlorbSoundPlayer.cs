using System.Diagnostics;
using System.Runtime.InteropServices;
using GrueGPT.Core.Runtime;

namespace GrueGPT.Desktop;

/// <summary>Plays decoded PCM samples with the native macOS or Windows sound player.</summary>
internal sealed class BlorbSoundPlayer : IZ3SoundIO, IDisposable
{
    private const uint SoundPlayAsync = 0x0001;
    private const uint PlaySoundLoop = 0x0008;
    private const uint PlaySoundFilename = 0x00020000;

    private readonly object _gate = new();
    private readonly Func<string, ValueTask> _reportError;
    private CancellationTokenSource? _playbackCancellation;
    private Process? _process;
    private string? _wavePath;
    private uint? _currentResource;

    public BlorbSoundPlayer(Func<string, ValueTask> reportError)
    {
        ArgumentNullException.ThrowIfNull(reportError);
        _reportError = reportError;
    }

    public ValueTask PlaySoundAsync(uint resourceNumber, ReadOnlyMemory<byte> waveData, bool repeat, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCurrent();
        string path = Path.Combine(Path.GetTempPath(), $"gruegpt-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, waveData.ToArray());

        lock (_gate)
        {
            _wavePath = path;
            _currentResource = resourceNumber;
            if (OperatingSystem.IsWindows())
            {
                uint flags = PlaySoundFilename | SoundPlayAsync | (repeat ? PlaySoundLoop : 0);
                if (!NativePlaySound(path, IntPtr.Zero, flags))
                {
                    CleanupFile();
                    throw new InvalidOperationException("Windows could not start Blorb sample playback.");
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                _playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                CancellationToken token = _playbackCancellation.Token;
                _ = PlayWithAfplayAsync(path, repeat, token);
            }
            else
            {
                CleanupFile();
                throw new PlatformNotSupportedException("Blorb audio playback is supported on macOS and Windows.");
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StopSoundAsync(uint resourceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (resourceNumber == 0 || _currentResource == resourceNumber)
            {
                StopCurrentLocked();
            }
        }

        return ValueTask.CompletedTask;
    }

    private async Task PlayWithAfplayAsync(string path, bool repeat, CancellationToken cancellationToken)
    {
        try
        {
            do
            {
                using Process process = Process.Start(new ProcessStartInfo("/usr/bin/afplay", path)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not start the macOS audio player.");
                lock (_gate)
                {
                    _process = process;
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"The macOS audio player exited with code {process.ExitCode}.");
                }
            }
            while (repeat && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await _reportError($"Blorb sample playback failed: {exception.Message}").ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (_wavePath == path)
                {
                    StopCurrentLocked();
                }
            }
        }
    }

    private void StopCurrent()
    {
        lock (_gate)
        {
            StopCurrentLocked();
        }
    }

    private void StopCurrentLocked()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = NativePlaySound(null, IntPtr.Zero, 0);
        }

        _playbackCancellation?.Cancel();
        _playbackCancellation?.Dispose();
        _playbackCancellation = null;
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        _process = null;
        CleanupFile();
        _currentResource = null;
    }

    private void CleanupFile()
    {
        if (_wavePath is not null)
        {
            try
            {
                File.Delete(_wavePath);
            }
            catch (IOException)
            {
            }

            _wavePath = null;
        }
    }

    public void Dispose() => StopCurrent();

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativePlaySound(string? sound, IntPtr module, uint flags);
}
