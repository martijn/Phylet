using System.Diagnostics;

namespace Phylet.Data.Library;

public sealed class FfmpegAudioDecoder : IAudioDecoder
{
    private readonly object _sync = new();
    private AudioDecoderAvailability? _cachedAvailability;

    public AudioDecoderAvailability GetAvailability()
    {
        lock (_sync)
        {
            _cachedAvailability ??= ProbeAvailability();
            return _cachedAvailability;
        }
    }

    public Stream OpenCueTrackStream(string sourceFilePath, long startMs, long? durationMs)
    {
        var availability = GetAvailability();
        if (!availability.IsAvailable)
        {
            throw new InvalidOperationException(availability.Reason ?? "ffmpeg is not available.");
        }

        var arguments = BuildArguments(sourceFilePath, startMs, durationMs);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("ffmpeg failed to start.");
            }
        }
        catch
        {
            process.Dispose();
            throw;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await process.StandardError.ReadToEndAsync();
            }
            catch
            {
            }
        });

        return new ProcessOutputStream(process, process.StandardOutput.BaseStream);
    }

    private static AudioDecoderAvailability ProbeAvailability()
    {
        return TryRunVersion("ffmpeg", out var ffmpegError)
            ? new AudioDecoderAvailability(true)
            : new AudioDecoderAvailability(false, ffmpegError);
    }

    private static bool TryRunVersion(string command, out string? error)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                error = $"{command} did not exit in time.";
                return false;
            }

            error = process.ExitCode == 0 ? null : $"{command} exited with code {process.ExitCode}.";
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            error = $"{command} is unavailable: {ex.Message}";
            return false;
        }
    }

    private static string BuildArguments(string sourceFilePath, long startMs, long? durationMs)
    {
        var escapedPath = sourceFilePath.Replace("\"", "\\\"", StringComparison.Ordinal);
        var parts = new List<string>
        {
            "-v error",
            $"-ss {FormatTimestamp(startMs)}"
        };
        if (durationMs is > 0)
        {
            parts.Add($"-t {FormatTimestamp(durationMs.Value)}");
        }

        parts.Add($"-i \"{escapedPath}\"");
        parts.Add("-map 0:a:0 -vn -sn -dn -f wav -acodec pcm_s16le pipe:1");
        return string.Join(' ', parts);
    }

    private static string FormatTimestamp(long milliseconds) =>
        TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss\.fff");

    private sealed class ProcessOutputStream(Process process, Stream innerStream) : Stream
    {
        private readonly Process _process = process;
        private readonly Stream _innerStream = innerStream;

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _innerStream.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _innerStream.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream.Dispose();
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }

                _process.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _innerStream.DisposeAsync();
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            _process.Dispose();
            await base.DisposeAsync();
        }
    }
}
