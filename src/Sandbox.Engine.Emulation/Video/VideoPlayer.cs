using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Sandbox.Engine.Emulation.Common;
using Sandbox.Engine.Emulation.CUtl;
using Sandbox.Engine.Emulation.Texture;

namespace Sandbox.Engine.Emulation.Video;

/// <summary>
/// Module d'émulation pour CVideoPlayer (CVideoPlayer_*).
/// Gère la lecture de vidéos avec support audio et texture.
/// </summary>
public static unsafe class VideoPlayer
{
    private static readonly IVideoBackend Backend;
    public static void SetSharedGL(Silk.NET.OpenGL.GL gl)
    {
        if (Backend is FfmpegBackend fb)
        {
            FfmpegBackend.SetGL(gl);
        }
    }

    static VideoPlayer()
    {
        // Choix backend selon plateforme
#if ANDROID
        Backend = new MediaCodecBackend();
#else
        Backend = new FfmpegBackend();
#endif
    }

    /// <summary>
    /// Données internes pour un VideoPlayer émulé.
    /// </summary>
    public class VideoPlayerData
    {
        public string Url { get; set; } = "";
        public string Extension { get; set; } = "";
        public bool IsPlaying { get; set; } = false;
        public bool IsPaused { get; set; } = false;
        public bool IsMuted { get; set; } = false;
        public bool Repeat { get; set; } = false;
        public bool VideoOnly { get; set; } = false;
        public bool HasAudio { get; set; } = false;
        public float Duration { get; set; } = 0.0f;
        public float PlaybackTime { get; set; } = 0.0f;
        public int Width { get; set; } = 0;
        public int Height { get; set; } = 0;
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
        public IntPtr TextureHandle { get; set; } = IntPtr.Zero; // Handle de la texture de la vidéo
        public int AudioStreamHandle { get; set; } = 0; // Handle du stream audio
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTime? StartTimeUtc { get; set; }
        public DateTime? LastUpdateUtc { get; set; }
        public IntPtr AudioDeviceHandle { get; set; } = IntPtr.Zero;
        
        // État interne pour le spectre audio (pour GetSpectrum)
        private float[]? _spectrumData;
        
        public float[] GetSpectrum(float timeSeconds)
        {
            const int SpectrumSize = 256;
            _spectrumData ??= new float[SpectrumSize];

            for (int i = 0; i < SpectrumSize; i++)
            {
                float f = (float)i / SpectrumSize;
                _spectrumData[i] = 0.02f * (float)Math.Abs(Math.Sin(2 * Math.PI * f * (1.0f + timeSeconds * 0.1f)));
            }

            return _spectrumData;
        }
        
        public float GetAmplitude(bool isMuted, bool isPlaying)
        {
            if (!isPlaying || isMuted)
            return 0.0f;

            // Amplitude faible mais non nulle pour signaler de l'activité
            return 0.05f;
        }
    }
    
    /// <summary>
    /// Initialise le module VideoPlayer en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16151-16174 (1286-1309)
    /// </summary>
    public static void Init(void** native)
    {
        // Indices 1286-1309
        native[1286] = (void*)(delegate* unmanaged<uint, IntPtr>)&CVideoPlayer_Create;
        native[1287] = (void*)(delegate* unmanaged<IntPtr, void>)&CVideoPlayer_Destroy;
        native[1288] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int>)&CVideoPlayer_Play;
        native[1289] = (void*)(delegate* unmanaged<IntPtr, void>)&CVideoPlayer_Resume;
        native[1290] = (void*)(delegate* unmanaged<IntPtr, void>)&CVideoPlayer_Stop;
        native[1291] = (void*)(delegate* unmanaged<IntPtr, void>)&CVideoPlayer_Pause;
        native[1292] = (void*)(delegate* unmanaged<IntPtr, double, void>)&CVideoPlayer_Seek;
        native[1293] = (void*)(delegate* unmanaged<IntPtr, int>)&CVideoPlayer_GetRepeat;
        native[1294] = (void*)(delegate* unmanaged<IntPtr, int, void>)&CVideoPlayer_SetRepeat;
        native[1295] = (void*)(delegate* unmanaged<IntPtr, float>)&CVideoPlayer_GetDuration;
        native[1296] = (void*)(delegate* unmanaged<IntPtr, float>)&CVideoPlayer_GetPlaybackTime;
        native[1297] = (void*)(delegate* unmanaged<IntPtr, int>)&CVideoPlayer_HasAudioStream;
        native[1298] = (void*)(delegate* unmanaged<IntPtr, void>)&CVideoPlayer_Update;
        native[1299] = (void*)(delegate* unmanaged<IntPtr, int>)&CVideoPlayer_IsPaused;
        native[1300] = (void*)(delegate* unmanaged<IntPtr, int>)&CVideoPlayer_IsMuted;
        native[1301] = (void*)(delegate* unmanaged<IntPtr, int, void>)&CVideoPlayer_SetMuted;
        native[1302] = (void*)(delegate* unmanaged<IntPtr, int>)&CVideoPlayer_GetWidth;
        native[1303] = (void*)(delegate* unmanaged<IntPtr, int>)&CVideoPlayer_GetHeight;
        native[1304] = (void*)(delegate* unmanaged<IntPtr, void>)&CVideoPlayer_SetVideoOnly;
        native[1305] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&CVideoPlayer_GetMetadata;
        native[1306] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CVideoPlayer_GetSpectrum;
        native[1307] = (void*)(delegate* unmanaged<IntPtr, float>)&CVideoPlayer_GetAmplitude;
        native[1308] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CVideoPlayer_GetTexture;
        native[1309] = (void*)(delegate* unmanaged<IntPtr, int>)&CVideoPlayer_GetAudioStream;
        
        Console.WriteLine("[NativeAOT] VideoPlayer module initialized");
    }
    
    /// <summary>
    /// Create a new VideoPlayer instance.
    /// 
    /// **Source 2 behavior**: Creates a new video player with a managed object reference.
    /// **Emulation behavior**: Creates a VideoPlayerData and registers it with HandleManager.
    /// </summary>
    /// <param name="managedObject">Address of the managed VideoPlayer object (from InteropSystem)</param>
    /// <returns>Handle to the VideoPlayer (IntPtr)</returns>
    [UnmanagedCallersOnly]
    public static IntPtr CVideoPlayer_Create(uint managedObject)
    {
        var videoPlayerData = new VideoPlayerData();
        
        // Enregistrer dans HandleManager pour obtenir un handle unique
        int handle = HandleManager.Register(videoPlayerData);
        if (handle == 0)
        {
            Console.WriteLine("[NativeAOT] CVideoPlayer_Create: Failed to register VideoPlayerData");
            return IntPtr.Zero;
        }
        
        int bindingHandle = HandleManager.GetBindingHandle(handle);
        videoPlayerData.BindingPtr = (IntPtr)bindingHandle;
        
        Console.WriteLine($"[NativeAOT] CVideoPlayer_Create: created handle={handle}, bindingHandle={bindingHandle}");
        return (IntPtr)handle;
    }
    
    /// <summary>
    /// Destroy a VideoPlayer instance.
    /// 
    /// **Source 2 behavior**: Destroys the video player and releases resources.
    /// **Emulation behavior**: Unregisters the VideoPlayerData from HandleManager.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_Destroy(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data != null)
        {
            Backend.Stop(data);
            Console.WriteLine($"[NativeAOT] CVideoPlayer_Destroy: destroying handle={handle}");
            HandleManager.Unregister(handle);
        }
    }
    
    /// <summary>
    /// Play a video from a URL.
    /// 
    /// **Source 2 behavior**: Starts playing a video from the given URL.
    /// **Emulation behavior**: Sets the video state to playing (stub implementation).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <param name="pUrl">Pointer to UTF-8 URL string</param>
    /// <param name="pExt">Pointer to UTF-8 extension string</param>
    /// <returns>1 if successful, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int CVideoPlayer_Play(IntPtr self, IntPtr pUrl, IntPtr pExt)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0;
        
        string? url = Marshal.PtrToStringUTF8(pUrl);
        string? ext = Marshal.PtrToStringUTF8(pExt);
        if (string.IsNullOrWhiteSpace(url))
            return 0;
        
        data.Url = url!;
        data.Extension = ext ?? string.Empty;
        data.IsPlaying = true;
        data.IsPaused = false;
        data.VideoOnly = false; // SetVideoOnly peut modifier ensuite
        data.HasAudio = true;
        data.PlaybackTime = 0.0f;
        data.StartTimeUtc = DateTime.UtcNow;
        data.LastUpdateUtc = data.StartTimeUtc;

        // Estimation grossière de durée si non fournie : 60s, ou basée sur taille fichier locale
        float estimatedDuration = 60.0f;
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile && File.Exists(uri.LocalPath))
            {
                var fi = new FileInfo(uri.LocalPath);
                estimatedDuration = Math.Max(30.0f, (float)(fi.Length / (0.5 * 1024 * 1024))); // ~0.5 MB/s
            }
        }
        catch
        {
            // ignorer les erreurs d'IO
        }

        data.Duration = data.Duration > 0 ? data.Duration : estimatedDuration;

        if (data.Width <= 0) data.Width = 512;
        if (data.Height <= 0) data.Height = 512;

        // Déléguer l'ouverture au backend (FFmpeg Desktop ou MediaCodec Android)
        bool ok = Backend.Open(data.Url, data.Extension, data);
        if (!ok)
        {
            Console.WriteLine($"[NativeAOT] CVideoPlayer_Play: backend failed for handle={handle}, url={data.Url}");
            return 0;
        }

        data.Metadata["url"] = data.Url;
        data.Metadata["ext"] = data.Extension;
        data.Metadata["startedUtc"] = data.StartTimeUtc?.ToString("O") ?? string.Empty;

        Console.WriteLine($"[NativeAOT] CVideoPlayer_Play: handle={handle}, url={data.Url}, ext={data.Extension}, duration={data.Duration}s");
        return 1;
    }
    
    /// <summary>
    /// Resume video playback.
    /// 
    /// **Source 2 behavior**: Resumes a paused video.
    /// **Emulation behavior**: Sets the video state to playing.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_Resume(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return;
        
        data.IsPaused = false;
        data.IsPlaying = true;
        Console.WriteLine($"[NativeAOT] CVideoPlayer_Resume: handle={handle}");
        Backend.Resume(data);
    }
    
    /// <summary>
    /// Stop video playback.
    /// 
    /// **Source 2 behavior**: Stops the video playback.
    /// **Emulation behavior**: Sets the video state to stopped.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_Stop(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return;
        
        data.IsPlaying = false;
        data.IsPaused = false;
        data.PlaybackTime = 0.0f;
        Backend.Stop(data);
        Console.WriteLine($"[NativeAOT] CVideoPlayer_Stop: handle={handle}");
    }
    
    /// <summary>
    /// Pause video playback.
    /// 
    /// **Source 2 behavior**: Pauses the video playback.
    /// **Emulation behavior**: Sets the video state to paused.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_Pause(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return;
        
        data.IsPaused = true;
        Console.WriteLine($"[NativeAOT] CVideoPlayer_Pause: handle={handle}");
        Backend.Pause(data);
    }
    
    /// <summary>
    /// Seek to a specific time in the video.
    /// 
    /// **Source 2 behavior**: Seeks to the specified time position.
    /// **Emulation behavior**: Updates the playback time (stub implementation).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <param name="time">Time in seconds</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_Seek(IntPtr self, double time)
    {
        if (self == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return;
        
        data.PlaybackTime = (float)time;
        Console.WriteLine($"[NativeAOT] CVideoPlayer_Seek: handle={handle}, time={time}");
        Backend.Seek(data, time);
    }
    
    /// <summary>
    /// Get whether the video should repeat.
    /// 
    /// **Source 2 behavior**: Returns if the video is set to repeat.
    /// **Emulation behavior**: Returns the stored repeat state.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>1 if repeating, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int CVideoPlayer_GetRepeat(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0;
        
        return data.Repeat ? 1 : 0;
    }
    
    /// <summary>
    /// Set whether the video should repeat.
    /// 
    /// **Source 2 behavior**: Sets the repeat state.
    /// **Emulation behavior**: Stores the repeat state.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <param name="bRepeat">1 to enable repeat, 0 to disable</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_SetRepeat(IntPtr self, int bRepeat)
    {
        if (self == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return;
        
        data.Repeat = (bRepeat != 0);
        Console.WriteLine($"[NativeAOT] CVideoPlayer_SetRepeat: handle={handle}, repeat={data.Repeat}");
    }
    
    /// <summary>
    /// Get the duration of the video in seconds.
    /// 
    /// **Source 2 behavior**: Returns the video duration.
    /// **Emulation behavior**: Returns the stored duration (0 if not loaded).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>Duration in seconds</returns>
    [UnmanagedCallersOnly]
    public static float CVideoPlayer_GetDuration(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0.0f;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0.0f;
        
        return data.Duration;
    }
    
    /// <summary>
    /// Get the current playback time in seconds.
    /// 
    /// **Source 2 behavior**: Returns the current playback position.
    /// **Emulation behavior**: Returns the stored playback time.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>Playback time in seconds</returns>
    [UnmanagedCallersOnly]
    public static float CVideoPlayer_GetPlaybackTime(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0.0f;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0.0f;
        
        return data.PlaybackTime;
    }
    
    /// <summary>
    /// Check if the video has an audio stream.
    /// 
    /// **Source 2 behavior**: Returns if the video has audio.
    /// **Emulation behavior**: Returns the stored HasAudio state.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>1 if has audio, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int CVideoPlayer_HasAudioStream(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0;
        
        return data.HasAudio ? 1 : 0;
    }
    
    /// <summary>
    /// Update the video player (should be called each frame).
    /// 
    /// **Source 2 behavior**: Updates video playback state and texture.
    /// **Emulation behavior**: No-op for now (stub implementation).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_Update(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return;
        
        Backend.Update(data);
    }
    
    /// <summary>
    /// Check if the video is paused.
    /// 
    /// **Source 2 behavior**: Returns if the video is paused.
    /// **Emulation behavior**: Returns the stored IsPaused state.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>1 if paused, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int CVideoPlayer_IsPaused(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0;
        
        return data.IsPaused ? 1 : 0;
    }
    
    /// <summary>
    /// Check if the video is muted.
    /// 
    /// **Source 2 behavior**: Returns if the video is muted.
    /// **Emulation behavior**: Returns the stored IsMuted state.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>1 if muted, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int CVideoPlayer_IsMuted(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0;
        
        return data.IsMuted ? 1 : 0;
    }
    
    /// <summary>
    /// Set whether the video is muted.
    /// 
    /// **Source 2 behavior**: Sets the mute state.
    /// **Emulation behavior**: Stores the mute state.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <param name="bMuted">1 to mute, 0 to unmute</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_SetMuted(IntPtr self, int bMuted)
    {
        if (self == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return;
        
        data.IsMuted = (bMuted != 0);
        Console.WriteLine($"[NativeAOT] CVideoPlayer_SetMuted: handle={handle}, muted={data.IsMuted}");
    }
    
    /// <summary>
    /// Get the width of the video in pixels.
    /// 
    /// **Source 2 behavior**: Returns the video width.
    /// **Emulation behavior**: Returns the stored width (0 if not loaded).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>Width in pixels</returns>
    [UnmanagedCallersOnly]
    public static int CVideoPlayer_GetWidth(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0;
        
        return data.Width;
    }
    
    /// <summary>
    /// Get the height of the video in pixels.
    /// 
    /// **Source 2 behavior**: Returns the video height.
    /// **Emulation behavior**: Returns the stored height (0 if not loaded).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>Height in pixels</returns>
    [UnmanagedCallersOnly]
    public static int CVideoPlayer_GetHeight(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0;
        
        return data.Height;
    }
    
    /// <summary>
    /// Set video-only mode (disable audio).
    /// 
    /// **Source 2 behavior**: Disables audio playback.
    /// **Emulation behavior**: Sets VideoOnly flag.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_SetVideoOnly(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return;
        
        data.VideoOnly = true;
        Console.WriteLine($"[NativeAOT] CVideoPlayer_SetVideoOnly: handle={handle}");
    }
    
    /// <summary>
    /// Get metadata value for a key.
    /// 
    /// **Source 2 behavior**: Returns metadata string for the given key.
    /// **Emulation behavior**: Returns stored metadata or empty string.
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <param name="key">Pointer to UTF-8 key string</param>
    /// <returns>Pointer to UTF-8 metadata string, or IntPtr.Zero</returns>
    [UnmanagedCallersOnly]
    public static IntPtr CVideoPlayer_GetMetadata(IntPtr self, IntPtr key)
    {
        if (self == IntPtr.Zero || key == IntPtr.Zero)
            return IntPtr.Zero;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return IntPtr.Zero;
        
        string? keyStr = Marshal.PtrToStringUTF8(key);
        if (string.IsNullOrEmpty(keyStr))
            return IntPtr.Zero;
        
        if (data.Metadata.TryGetValue(keyStr, out string? value))
        {
            // Allocate and return the string
            // Note: The caller is responsible for freeing this memory
            // In Source 2, this is typically managed by the engine
            IntPtr result = Marshal.StringToHGlobalAnsi(value);
            return result;
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Get the audio spectrum data.
    /// 
    /// **Source 2 behavior**: Fills outSpectrum with frequency spectrum data.
    /// **Emulation behavior**: Fills with empty data (stub implementation).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <param name="outSpectrum">Pointer to CUtlVectorFloat to fill</param>
    [UnmanagedCallersOnly]
    public static void CVideoPlayer_GetSpectrum(IntPtr self, IntPtr outSpectrum)
    {
        if (self == IntPtr.Zero || outSpectrum == IntPtr.Zero)
            return;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return;
        
        const int SpectrumSize = 256;
        CUtlVectorFloat.SetCountManaged(outSpectrum, SpectrumSize);

        var spectrum = new float[SpectrumSize];
        Backend.FillSpectrum(data, spectrum);
        int count = Math.Min(SpectrumSize, spectrum.Length);
        for (int i = 0; i < count; i++)
        {
            CUtlVectorFloat.SetElement(outSpectrum, i, spectrum[i]);
        }

    }
    
    /// <summary>
    /// Get the audio amplitude.
    /// 
    /// **Source 2 behavior**: Returns the current audio amplitude.
    /// **Emulation behavior**: Returns 0 (stub implementation).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>Amplitude value (0.0 to 1.0)</returns>
    [UnmanagedCallersOnly]
    public static float CVideoPlayer_GetAmplitude(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0.0f;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0.0f;
        
        return Backend.GetAmplitude(data);
    }
    
    /// <summary>
    /// Get the texture handle for the video frame.
    /// 
    /// **Source 2 behavior**: Returns the texture handle for the current video frame.
    /// **Emulation behavior**: Returns the stored texture handle (IntPtr.Zero if not set).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>Texture handle (IntPtr)</returns>
    [UnmanagedCallersOnly]
    public static IntPtr CVideoPlayer_GetTexture(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return IntPtr.Zero;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return IntPtr.Zero;
        
        return data.TextureHandle;
    }
    
    /// <summary>
    /// Get the audio stream handle.
    /// 
    /// **Source 2 behavior**: Returns the handle to the audio stream.
    /// **Emulation behavior**: Returns the stored audio stream handle (0 if not set).
    /// </summary>
    /// <param name="self">Handle to the VideoPlayer</param>
    /// <returns>Audio stream handle</returns>
    [UnmanagedCallersOnly]
    public static int CVideoPlayer_GetAudioStream(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int handle = (int)self;
        var data = HandleManager.Get<VideoPlayerData>(handle);
        if (data == null)
            return 0;
        
        return data.AudioStreamHandle;
    }
}

