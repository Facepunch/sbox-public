#if !ANDROID
using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Sandbox.Engine.Emulation.Texture;
using Sandbox.Engine.Emulation.Audio;

namespace Sandbox.Engine.Emulation.Video;

/// <summary>
/// Backend desktop FFmpeg + (optionnel) OpenAL/OpenGL.
/// Implémentation minimale mais réelle : ouvre et décode, conserve le buffer RGBA et PCM pour amplitude/spectrum.
/// Upload GL/AL est prévu mais peut être désactivé si le contexte n'est pas prêt.
/// </summary>
public sealed unsafe class FfmpegBackend : IVideoBackend
{
    private const int SpectrumSize = 256;

    // FFmpeg state
    private AVFormatContext* _fmt;
    private AVCodecContext* _videoCodec;
    private AVCodecContext* _audioCodec;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private SwsContext* _sws;
    private SwrContext* _swr;
    private int _videoStreamIndex = -1;
    private int _audioStreamIndex = -1;

    // Buffers
    private byte[]? _rgbaBuffer;
    private int _rgbaStride;
    private float[] _audioBuffer = Array.Empty<float>(); // interleaved stereo
    private int _audioSamples;
    private static Silk.NET.OpenGL.GL? _sharedGl;

    public static void SetGL(Silk.NET.OpenGL.GL gl)
    {
        _sharedGl = gl;
    }

    public bool Open(string url, string extension, VideoPlayer.VideoPlayerData data)
    {
        if (!FfmpegLoader.EnsureLoaded())
            return false;

        ffmpeg.avformat_network_init();

        _fmt = ffmpeg.avformat_alloc_context();
        if (_fmt == null) return false;

        AVFormatContext* fmtLocal = _fmt;
        if (ffmpeg.avformat_open_input(&fmtLocal, url, null, null) != 0)
            return false;
        _fmt = fmtLocal;

        if (ffmpeg.avformat_find_stream_info(_fmt, null) < 0)
            return false;

        for (int i = 0; i < _fmt->nb_streams; i++)
        {
            var stream = _fmt->streams[i];
            if (stream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && _videoStreamIndex < 0)
            {
                _videoStreamIndex = i;
            }
            else if (stream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && _audioStreamIndex < 0)
            {
                _audioStreamIndex = i;
            }
        }

        if (_videoStreamIndex >= 0)
        {
            var vpar = _fmt->streams[_videoStreamIndex]->codecpar;
            var vcodec = ffmpeg.avcodec_find_decoder(vpar->codec_id);
            _videoCodec = ffmpeg.avcodec_alloc_context3(vcodec);
            ffmpeg.avcodec_parameters_to_context(_videoCodec, vpar);
            ffmpeg.avcodec_open2(_videoCodec, vcodec, null);

            data.Width = vpar->width;
            data.Height = vpar->height;
            data.Duration = data.Duration > 0
                ? data.Duration
                : (float)ffmpeg.av_q2d(_fmt->streams[_videoStreamIndex]->time_base) * _fmt->streams[_videoStreamIndex]->duration;

            // RGBA buffer
            _rgbaStride = data.Width * 4;
            _rgbaBuffer = new byte[_rgbaStride * data.Height];
            _sws = ffmpeg.sws_getContext(
                data.Width, data.Height, (AVPixelFormat)_videoCodec->pix_fmt,
                data.Width, data.Height, AVPixelFormat.AV_PIX_FMT_RGBA,
                ffmpeg.SWS_BILINEAR, null, null, null);
        }

        if (_audioStreamIndex >= 0)
        {
            var apar = _fmt->streams[_audioStreamIndex]->codecpar;
            var acodec = ffmpeg.avcodec_find_decoder(apar->codec_id);
            _audioCodec = ffmpeg.avcodec_alloc_context3(acodec);
            ffmpeg.avcodec_parameters_to_context(_audioCodec, apar);
            ffmpeg.avcodec_open2(_audioCodec, acodec, null);

            // target: stereo float 44100 (aligné avec AudioDevice)
            long inLayout = (long)(_audioCodec->channel_layout != 0 ? (long)_audioCodec->channel_layout : ffmpeg.av_get_default_channel_layout(_audioCodec->channels));
            _swr = ffmpeg.swr_alloc_set_opts(
                null,
                ffmpeg.AV_CH_LAYOUT_STEREO, AVSampleFormat.AV_SAMPLE_FMT_FLT, 44100,
                inLayout,
                _audioCodec->sample_fmt,
                _audioCodec->sample_rate,
                0, null);
            ffmpeg.swr_init(_swr);
            _audioBuffer = new float[44100]; // 1s tampon
        }

        _frame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();

        // Texture : laisser à VideoPlayer/RenderDevice (placeholder si non fourni)
        if (data.TextureHandle == IntPtr.Zero)
        {
            var texName = $"video::{data.Url}";
            data.TextureHandle = TextureSystem.CreateTextureFromResourceHelper(texName);
            if (data.TextureHandle == IntPtr.Zero)
            {
                data.TextureHandle = TextureSystem.CreateTextureFromResourceHelper("video-placeholder");
            }
        }

        data.HasAudio = _audioStreamIndex >= 0;
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
            if (delta > 0)
                data.PlaybackTime += delta;
        }
        data.LastUpdateUtc = now;

        // Décoder jusqu'à couvrir PlaybackTime
        DecodeUntil(data.PlaybackTime, data);

        // Gestion boucle/fin
        if (data.Duration > 0 && data.PlaybackTime >= data.Duration)
        {
            if (data.Repeat)
            {
                Seek(data, 0);
                data.PlaybackTime = 0;
            }
            else
            {
                data.IsPlaying = false;
                data.IsPaused = false;
            }
        }
    }

    public void Pause(VideoPlayer.VideoPlayerData data)
    {
        data.IsPaused = true;
    }

    public void Resume(VideoPlayer.VideoPlayerData data)
    {
        data.IsPaused = false;
    }

    public void Stop(VideoPlayer.VideoPlayerData data)
    {
        data.IsPlaying = false;
        data.IsPaused = false;
        Cleanup();
    }

    public void Seek(VideoPlayer.VideoPlayerData data, double timeSeconds)
    {
        if (_fmt == null || _videoStreamIndex < 0)
        {
            data.PlaybackTime = (float)timeSeconds;
            return;
        }

        long ts = (long)(timeSeconds / ffmpeg.av_q2d(_fmt->streams[_videoStreamIndex]->time_base));
        ffmpeg.av_seek_frame(_fmt, _videoStreamIndex, ts, ffmpeg.AVSEEK_FLAG_BACKWARD);
        ffmpeg.avcodec_flush_buffers(_videoCodec);
        if (_audioCodec != null) ffmpeg.avcodec_flush_buffers(_audioCodec);
        data.PlaybackTime = (float)timeSeconds;
    }

    public void FillSpectrum(VideoPlayer.VideoPlayerData data, float[] target)
    {
        // Spectre simplifié sur le dernier tampon audio stocké
        if (_audioSamples <= 0)
        {
            Array.Clear(target, 0, Math.Min(target.Length, SpectrumSize));
            return;
        }

        int n = Math.Min(_audioSamples, target.Length);
        // Fenêtre simple : remplir avec valeurs absolues (approx énergie)
        for (int i = 0; i < n; i++)
        {
            target[i] = Math.Abs(_audioBuffer[i]) * 0.5f;
        }
        for (int i = n; i < target.Length; i++) target[i] = 0;
    }

    public float GetAmplitude(VideoPlayer.VideoPlayerData data)
    {
        if (_audioSamples <= 0 || data.IsMuted || !data.IsPlaying) return 0f;
        double sum = 0;
        int n = _audioSamples;
        for (int i = 0; i < n; i++)
        {
            var v = _audioBuffer[i];
            sum += v * v;
        }
        return (float)Math.Sqrt(sum / n);
    }

    public void Dispose()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (_packet != null) { AVPacket* pkt = _packet; ffmpeg.av_packet_free(&pkt); _packet = null; }
        if (_frame != null) { AVFrame* frm = _frame; ffmpeg.av_frame_free(&frm); _frame = null; }
        if (_videoCodec != null) { AVCodecContext* ctx = _videoCodec; ffmpeg.avcodec_free_context(&ctx); _videoCodec = null; }
        if (_audioCodec != null) { AVCodecContext* ctxa = _audioCodec; ffmpeg.avcodec_free_context(&ctxa); _audioCodec = null; }
        if (_fmt != null)
        {
            AVFormatContext* tmp = _fmt;
            ffmpeg.avformat_close_input(&tmp);
            _fmt = null;
        }
        if (_sws != null) { ffmpeg.sws_freeContext(_sws); _sws = null; }
        if (_swr != null) { SwrContext* swr = _swr; ffmpeg.swr_free(&swr); _swr = null; }
    }

    private void DecodeUntil(float targetSeconds, VideoPlayer.VideoPlayerData data)
    {
        if (_fmt == null || _frame == null || _packet == null) return;

        byte** outBufAudio = stackalloc byte*[1];

        while (true)
        {
            if (ffmpeg.av_read_frame(_fmt, _packet) < 0)
            {
                // EOF
                break;
            }

            if (_packet->stream_index == _videoStreamIndex)
            {
                if (SendReceive(_videoCodec, _packet) && _frame->data[0] != null && _rgbaBuffer != null)
                {
                    // Convertir vers RGBA
                    fixed (byte* dst = _rgbaBuffer)
                    {
                        var dstData = new byte_ptrArray8();
                        dstData[0] = dst;
                        var dstLinesize = new int_array8();
                        dstLinesize[0] = _rgbaStride;

                        ffmpeg.sws_scale(_sws, _frame->data, _frame->linesize, 0, _frame->height, dstData, dstLinesize);
                    }
                }
            }
            else if (_packet->stream_index == _audioStreamIndex)
            {
                if (SendReceive(_audioCodec, _packet))
                {
                    // Convertir audio vers float stéréo
                    int outSamples = ffmpeg.swr_get_out_samples(_swr, _frame->nb_samples);
                    int needed = outSamples * 2; // stereo
                    if (_audioBuffer.Length < needed)
                        _audioBuffer = new float[needed];

                    fixed (float* outPtr = _audioBuffer)
                    {
                        outBufAudio[0] = (byte*)outPtr;
                        int converted = ffmpeg.swr_convert(_swr, outBufAudio, outSamples, _frame->extended_data, _frame->nb_samples);
                        _audioSamples = converted * 2;
                    }

                    PushAudioToEngine(data);
                }
            }

            // Stop si on a dépassé la cible temporelle (approx)
            if (_videoStreamIndex >= 0 && _fmt->streams[_videoStreamIndex]->time_base.den != 0)
            {
                double pts = _frame->best_effort_timestamp * ffmpeg.av_q2d(_fmt->streams[_videoStreamIndex]->time_base);
                ffmpeg.av_packet_unref(_packet);
                if (pts >= targetSeconds) break;
            }
            else
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }
    }

    private void UploadTextureIfPossible(VideoPlayer.VideoPlayerData data)
    {
        var gl = _sharedGl;
        if (gl == null || _rgbaBuffer == null) return;
        var texData = TextureSystem.GetTextureData(data.TextureHandle);
        if (texData == null || texData.OpenGLHandle == 0) return;

        try
        {
            fixed (byte* ptr = _rgbaBuffer)
            {
                gl.BindTexture(Silk.NET.OpenGL.TextureTarget.Texture2D, texData.OpenGLHandle);
                gl.TexSubImage2D(Silk.NET.OpenGL.TextureTarget.Texture2D, 0, 0, 0,
                    (uint)data.Width, (uint)data.Height,
                    Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, ptr);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] UploadTextureIfPossible failed: {ex.Message}");
        }
    }

    private void EnsureAudioDevice(VideoPlayer.VideoPlayerData data)
    {
        if (data.AudioDeviceHandle != IntPtr.Zero)
            return;

        var dev = new AudioMixDeviceBuffers.AudioMixDeviceBuffersData(2);
        int h = Common.HandleManager.Register(dev);
        data.AudioDeviceHandle = (IntPtr)h;
    }

    private void PushAudioToEngine(VideoPlayer.VideoPlayerData data)
    {
        if (_audioSamples <= 0)
            return;

        EnsureAudioDevice(data);
        var dev = Common.HandleManager.Get<AudioMixDeviceBuffers.AudioMixDeviceBuffersData>((int)data.AudioDeviceHandle);
        if (dev == null || dev.BufferHandles.Count < 2)
            return;

        int offsetSamples = 0;
        while (_audioSamples - offsetSamples * 2 > 0)
        {
            int remainingSamples = (_audioSamples - offsetSamples * 2) / 2;
            int samplesPerChannel = Math.Min(remainingSamples, AudioMixBuffer.AudioMixBufferData.BufferSize);
            var leftBuf = Common.HandleManager.Get<AudioMixBuffer.AudioMixBufferData>(dev.BufferHandles[0]);
            var rightBuf = Common.HandleManager.Get<AudioMixBuffer.AudioMixBufferData>(dev.BufferHandles[1]);
            if (leftBuf == null || rightBuf == null) break;

            int baseIdx = offsetSamples * 2;
            for (int i = 0; i < samplesPerChannel; i++)
            {
                leftBuf.Data[i] = _audioBuffer[baseIdx + i * 2];
                rightBuf.Data[i] = _audioBuffer[baseIdx + i * 2 + 1];
            }
            for (int i = samplesPerChannel; i < AudioMixBuffer.AudioMixBufferData.BufferSize; i++)
            {
                leftBuf.Data[i] = 0;
                rightBuf.Data[i] = 0;
            }

            AudioDevice.SendOutputManaged(data.AudioDeviceHandle);
            offsetSamples += samplesPerChannel;
            if (samplesPerChannel < AudioMixBuffer.AudioMixBufferData.BufferSize) break;
        }

        // Décaler les données restantes en tête du tampon
        int consumed = offsetSamples * 2;
        int remaining = _audioSamples - consumed;
        if (remaining > 0)
        {
            Buffer.BlockCopy(_audioBuffer, consumed * sizeof(float), _audioBuffer, 0, remaining * sizeof(float));
        }
        _audioSamples = remaining;
    }

    private bool SendReceive(AVCodecContext* codec, AVPacket* pkt)
    {
        if (codec == null || _frame == null) return false;
        int r = ffmpeg.avcodec_send_packet(codec, pkt);
        if (r < 0) return false;
        return ffmpeg.avcodec_receive_frame(codec, _frame) == 0;
    }
}
#endif

