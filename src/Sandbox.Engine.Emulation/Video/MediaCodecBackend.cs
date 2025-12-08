#if ANDROID
using System;
using Android.Media;
using Android.Opengl;
using Android.Graphics;
using Sandbox.Engine.Emulation.Texture;

namespace Sandbox.Engine.Emulation.Video;

/// <summary>
/// Implémentation Android basée sur MediaCodec/AudioTrack. Simplifiée : décode vers SurfaceTexture et pousse l'audio PCM vers AudioTrack.
/// </summary>
public sealed class MediaCodecBackend : IVideoBackend
{
    private MediaExtractor? _extractor;
    private MediaCodec? _videoCodec;
    private MediaCodec? _audioCodec;
    private AudioTrack? _audioTrack;
    private SurfaceTexture? _surfaceTexture;
    private int _videoTrack = -1;
    private int _audioTrack = -1;
    private bool _isPlaying;
    private long _durationUs;

    public bool Open(string url, string extension, VideoPlayer.VideoPlayerData data)
    {
        try
        {
            _extractor = new MediaExtractor();
            _extractor.SetDataSource(url);

            for (int i = 0; i < _extractor.TrackCount; i++)
            {
                var format = _extractor.GetTrackFormat(i);
                var mime = format.GetString(MediaFormat.KeyMime);
                if (mime != null && mime.StartsWith("video/") && _videoTrack < 0)
                {
                    _videoTrack = i;
                    data.Width = format.GetInteger(MediaFormat.KeyWidth);
                    data.Height = format.GetInteger(MediaFormat.KeyHeight);
                    _durationUs = format.ContainsKey(MediaFormat.KeyDuration) ? format.GetLong(MediaFormat.KeyDuration) : 0;
                }
                else if (mime != null && mime.StartsWith("audio/") && _audioTrack < 0)
                {
                    _audioTrack = i;
                    data.HasAudio = true;
                }
            }

            if (_videoTrack >= 0)
            {
                _extractor.SelectTrack(_videoTrack);
                var vfmt = _extractor.GetTrackFormat(_videoTrack);
                string? mime = vfmt.GetString(MediaFormat.KeyMime);
                if (mime != null)
                {
                    _videoCodec = MediaCodec.CreateDecoderByType(mime);
                    // SurfaceTexture : créer une texture GL externe
                    _surfaceTexture = new SurfaceTexture(0);
                    var surface = new Surface(_surfaceTexture);
                    _videoCodec.Configure(vfmt, surface, null, MediaCodecConfigFlags.None);
                    _videoCodec.Start();
                }
            }

            if (_audioTrack >= 0)
            {
                _extractor.SelectTrack(_audioTrack);
                var afmt = _extractor.GetTrackFormat(_audioTrack);
                string? mime = afmt.GetString(MediaFormat.KeyMime);
                if (mime != null)
                {
                    _audioCodec = MediaCodec.CreateDecoderByType(mime);
                    _audioCodec.Configure(afmt, null, null, MediaCodecConfigFlags.None);
                    _audioCodec.Start();

                    int sampleRate = afmt.ContainsKey(MediaFormat.KeySampleRate) ? afmt.GetInteger(MediaFormat.KeySampleRate) : 44100;
                    int channels = afmt.ContainsKey(MediaFormat.KeyChannelCount) ? afmt.GetInteger(MediaFormat.KeyChannelCount) : 2;
                    int minBuf = AudioTrack.GetMinBufferSize(sampleRate, channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo, Android.Media.Encoding.Pcm16bit);
                    _audioTrack = new AudioTrack(Stream.Music, sampleRate, channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo, Android.Media.Encoding.Pcm16bit, minBuf, AudioTrackMode.Stream);
                    _audioTrack.Play();
                }
            }

            data.Duration = data.Duration > 0 ? data.Duration : (_durationUs > 0 ? _durationUs / 1_000_000f : 0f);
            _isPlaying = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] MediaCodecBackend Open failed: {ex.Message}");
            return false;
        }
    }

    public void Update(VideoPlayer.VideoPlayerData data)
    {
        if (!_isPlaying || _extractor == null) return;

        // Décodage simple non bloquant
        DecodeVideo();
        DecodeAudio();

        data.PlaybackTime += 0.016f; // approx 60fps
        if (data.Duration > 0 && data.PlaybackTime >= data.Duration)
        {
            if (data.Repeat)
            {
                Seek(data, 0);
                data.PlaybackTime = 0;
            }
            else
            {
                _isPlaying = false;
                data.IsPlaying = false;
            }
        }
    }

    public void Pause(VideoPlayer.VideoPlayerData data)
    {
        _isPlaying = false;
        _audioTrack?.Pause();
    }

    public void Resume(VideoPlayer.VideoPlayerData data)
    {
        _isPlaying = true;
        _audioTrack?.Play();
    }

    public void Stop(VideoPlayer.VideoPlayerData data)
    {
        _isPlaying = false;
        _audioTrack?.Stop();
        Dispose();
    }

    public void Seek(VideoPlayer.VideoPlayerData data, double timeSeconds)
    {
        if (_extractor == null) return;
        long posUs = (long)(timeSeconds * 1_000_000);
        _extractor.SeekTo(posUs, MediaSeekMode.Accurate);
    }

    public void FillSpectrum(VideoPlayer.VideoPlayerData data, float[] target)
    {
        Array.Clear(target, 0, Math.Min(target.Length, 256));
    }

    public float GetAmplitude(VideoPlayer.VideoPlayerData data)
    {
        return _isPlaying ? 0.05f : 0f;
    }

    public void Dispose()
    {
        try { _videoCodec?.Stop(); _videoCodec?.Release(); } catch { }
        try { _audioCodec?.Stop(); _audioCodec?.Release(); } catch { }
        try { _audioTrack?.Release(); } catch { }
        _videoCodec = null;
        _audioCodec = null;
        _audioTrack = null;
        _extractor = null;
    }

    private void DecodeVideo()
    {
        if (_videoCodec == null || _extractor == null || _videoTrack < 0) return;
        var inIndex = _videoCodec.DequeueInputBuffer(0);
        if (inIndex >= 0)
        {
            var buf = _videoCodec.GetInputBuffer(inIndex);
            int sampleSize = _extractor.ReadSampleData(buf, 0);
            if (sampleSize > 0)
            {
                long time = _extractor.SampleTime;
                _videoCodec.QueueInputBuffer(inIndex, 0, sampleSize, time, 0);
                _extractor.Advance();
            }
            else
            {
                _videoCodec.QueueInputBuffer(inIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
            }
        }

        var info = new MediaCodec.BufferInfo();
        int outIndex = _videoCodec.DequeueOutputBuffer(info, 0);
        if (outIndex >= 0)
        {
            _videoCodec.ReleaseOutputBuffer(outIndex, true); // render to surface
        }
    }

    private void DecodeAudio()
    {
        if (_audioCodec == null || _extractor == null || _audioTrack == null || _audioTrack.State != AudioTrackState.Initialized) return;

        var inIndex = _audioCodec.DequeueInputBuffer(0);
        if (inIndex >= 0)
        {
            var buf = _audioCodec.GetInputBuffer(inIndex);
            int sampleSize = _extractor.ReadSampleData(buf, 0);
            if (sampleSize > 0)
            {
                long time = _extractor.SampleTime;
                _audioCodec.QueueInputBuffer(inIndex, 0, sampleSize, time, 0);
                _extractor.Advance();
            }
            else
            {
                _audioCodec.QueueInputBuffer(inIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
            }
        }

        var info = new MediaCodec.BufferInfo();
        int outIndex = _audioCodec.DequeueOutputBuffer(info, 0);
        if (outIndex >= 0)
        {
            var outBuf = _audioCodec.GetOutputBuffer(outIndex);
            if (outBuf != null && info.Size > 0)
            {
                byte[] pcm = new byte[info.Size];
                outBuf.Get(pcm, 0, info.Size);
                _audioTrack.Write(pcm, 0, info.Size);
            }
            _audioCodec.ReleaseOutputBuffer(outIndex, false);
        }
    }
}
#endif
using System;

namespace Sandbox.Engine.Emulation.Video;

/// <summary>
/// Backend Android placeholder (MediaCodec/AudioTrack).
/// À implémenter côté Android (JNI/NDK). Pour l’instant, avance synthétique comme fallback.
/// </summary>
public sealed class MediaCodecBackend : IVideoBackend
{
    private const int SpectrumSize = 256;

    public bool Open(string url, string extension, VideoPlayer.VideoPlayerData data)
    {
        // TODO ANDROID: Implémenter ouverture MediaExtractor/MediaCodec + AudioTrack.
        data.HasAudio = true;
        if (data.Width <= 0) data.Width = 512;
        if (data.Height <= 0) data.Height = 512;
        if (data.Duration <= 0) data.Duration = 60.0f;
        return true;
    }

    public void Update(VideoPlayer.VideoPlayerData data)
    {
        if (data.IsPaused || !data.IsPlaying)
            return;

        var now = DateTime.UtcNow;
        if (data.LastUpdateUtc.HasValue)
        {
            var delta = (float)(now - data.LastUpdateUtc.Value).TotalSeconds;
            data.PlaybackTime += delta;
            if (data.Duration > 0 && data.PlaybackTime >= data.Duration)
            {
                if (data.Repeat)
                {
                    data.PlaybackTime = 0.0f;
                    data.StartTimeUtc = now;
                }
                else
                {
                    data.IsPlaying = false;
                    data.IsPaused = false;
                }
            }
        }
        data.LastUpdateUtc = now;
    }

    public void Pause(VideoPlayer.VideoPlayerData data)
    {
        // TODO ANDROID: Pause AudioTrack/MediaCodec
    }

    public void Resume(VideoPlayer.VideoPlayerData data)
    {
        // TODO ANDROID: Resume AudioTrack/MediaCodec
    }

    public void Stop(VideoPlayer.VideoPlayerData data)
    {
        // TODO ANDROID: Release codecs/resources
    }

    public void Seek(VideoPlayer.VideoPlayerData data, double timeSeconds)
    {
        data.PlaybackTime = (float)timeSeconds;
        data.StartTimeUtc = DateTime.UtcNow - TimeSpan.FromSeconds(data.PlaybackTime);
    }

    public void FillSpectrum(VideoPlayer.VideoPlayerData data, float[] target)
    {
        var spectrum = data.GetSpectrum(data.PlaybackTime);
        int n = Math.Min(SpectrumSize, Math.Min(spectrum.Length, target.Length));
        for (int i = 0; i < n; i++)
            target[i] = spectrum[i];
    }

    public float GetAmplitude(VideoPlayer.VideoPlayerData data)
    {
        return data.GetAmplitude(data.IsMuted, data.IsPlaying);
    }

    public void Dispose()
    {
    }
}

