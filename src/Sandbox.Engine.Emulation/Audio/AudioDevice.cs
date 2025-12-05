using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Silk.NET.OpenAL;

namespace Sandbox.Engine.Emulation.Audio;

/// <summary>
/// Module d'émulation pour AudioDevice (g_pAudioDevice_*).
/// Gère les opérations de base du périphérique audio (OpenAL sur Linux).
/// Signatures exactes depuis Interop.Engine.cs ligne 6623-6636 et indices depuis ligne 16151-16164.
/// </summary>
public static unsafe class AudioDevice
{
    // État du périphérique audio OpenAL
    private static AL? _al;
    private static ALContext? _alc;
    private static Device* _device;
    private static Context* _context;
    private static bool _isInitialized;
    private static bool _isValid;
    private static bool _isActive;
    private static bool _isMuted;
    private static string _deviceName = "OpenAL Soft";
    
    // Paramètres audio par défaut (compatibles avec Source 2)
    private const uint DefaultChannelCount = 2; // Stéréo
    private const uint DefaultMixChannelCount = 2;
    private const uint DefaultBitsPerSample = 16;
    private const uint DefaultBytesPerSample = 2; // 16 bits = 2 bytes
    private const uint DefaultSampleRate = 44100; // MIX_DEFAULT_SAMPLING_RATE
    
    // Cache pour le nom du périphérique (alloué une fois)
    private static IntPtr _cachedDeviceNamePtr = IntPtr.Zero;
    
    // Gestion des sources OpenAL pour chaque canal audio
    // Clé: handle vers AudioMixDeviceBuffers, Valeur: liste de sources OpenAL (une par canal)
    private static readonly Dictionary<IntPtr, List<uint>> _openalSources = new();
    
    // Buffers OpenAL pour le streaming (3 buffers par source pour éviter les gaps)
    private const int NumStreamBuffers = 3;

    /// <summary>
    /// Initialise le module AudioDevice en patchant toutes les fonctions natives.
    /// Signatures exactes depuis Interop.Engine.cs ligne 6623-6636.
    /// Indices depuis Interop.Engine.cs ligne 16151-16164.
    /// </summary>
    public static void Init(void** native)
    {
        // AudioDevice functions (indices 1404-1417 depuis Interop.Engine.cs ligne 16151-16164)
        native[1404] = (void*)(delegate* unmanaged<IntPtr>)&g_pAudioDevice_Name;
        native[1405] = (void*)(delegate* unmanaged<uint>)&g_pAudioDevice_ChannelCount;
        native[1406] = (void*)(delegate* unmanaged<uint>)&g_pAudioDevice_MixChannelCount;
        native[1407] = (void*)(delegate* unmanaged<uint>)&g_pAudioDevice_BitsPerSample;
        native[1408] = (void*)(delegate* unmanaged<uint>)&g_pAudioDevice_BytesPerSample;
        native[1409] = (void*)(delegate* unmanaged<uint>)&g_pAudioDevice_SampleRate;
        native[1410] = (void*)(delegate* unmanaged<int>)&g_pAudioDevice_IsActive;
        native[1411] = (void*)(delegate* unmanaged<void>)&g_pAudioDevice_CancelOutput;
        native[1412] = (void*)(delegate* unmanaged<void>)&g_pAudioDevice_WaitForComplete;
        native[1413] = (void*)(delegate* unmanaged<int, void>)&g_pAudioDevice_MuteDevice;
        native[1414] = (void*)(delegate* unmanaged<void>)&g_pAudioDevice_ClearBuffer;
        native[1415] = (void*)(delegate* unmanaged<void>)&g_pAudioDevice_OutputDebugInfo;
        native[1416] = (void*)(delegate* unmanaged<int>)&g_pAudioDevice_IsValid;
        native[1417] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pAudioDevice_SendOutput;
        
        // Initialiser le périphérique audio
        InitializeAudioDevice();
    }

    /// <summary>
    /// Initialise le périphérique audio OpenAL.
    /// </summary>
    private static void InitializeAudioDevice()
    {
        try
        {
            // Initialiser OpenAL
            _al = AL.GetApi();
            _alc = ALContext.GetApi();
            
            // Ouvrir le périphérique audio par défaut
            _device = _alc.OpenDevice(null);
            if (_device == null)
            {
                Console.WriteLine("[NativeAOT] AudioDevice: Failed to open OpenAL device");
                _isValid = false;
                return;
            }
            
            // Créer un contexte audio
            _context = _alc.CreateContext(_device, null);
            if (_context == null)
            {
                Console.WriteLine("[NativeAOT] AudioDevice: Failed to create OpenAL context");
                _alc.CloseDevice(_device);
                _isValid = false;
                return;
            }
            
            // Activer le contexte
            if (!_alc.MakeContextCurrent(_context))
            {
                Console.WriteLine("[NativeAOT] AudioDevice: Failed to make OpenAL context current");
                _alc.DestroyContext(_context);
                _alc.CloseDevice(_device);
                _isValid = false;
                return;
            }
            
            // Récupérer le nom du périphérique
            // Pour l'instant, on utilise le nom par défaut
            // Dans une implémentation complète, on utiliserait _alc.GetString() pour obtenir le nom réel
            _deviceName = "OpenAL Soft";
            
            _isValid = true;
            _isInitialized = true;
            _isActive = true;
            
            Console.WriteLine($"[NativeAOT] AudioDevice: Initialized OpenAL device '{_deviceName}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] AudioDevice: Failed to initialize OpenAL: {ex.Message}");
            _isValid = false;
            _isInitialized = false;
            
            // Nettoyer en cas d'erreur
            if (_context != null)
            {
                _alc?.DestroyContext(_context);
                _context = null;
            }
            if (_device != null)
            {
                _alc?.CloseDevice(_device);
                _device = null;
            }
        }
    }

    // ============================================================================
    // AudioDevice Functions (g_pAudioDevice_*)
    // Signatures exactes depuis Interop.Engine.cs ligne 6623-6636
    // ============================================================================

    /// <summary>
    /// Retourne le nom du périphérique audio.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr &gt;
    /// Retourne un pointeur vers une chaîne UTF-8 null-terminated.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pAudioDevice_Name()
    {
        // Allouer la mémoire pour le nom du périphérique si nécessaire
        if (_cachedDeviceNamePtr == IntPtr.Zero)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(_deviceName + "\0");
            _cachedDeviceNamePtr = Marshal.AllocHGlobal(nameBytes.Length);
            Marshal.Copy(nameBytes, 0, _cachedDeviceNamePtr, nameBytes.Length);
        }
        
        return _cachedDeviceNamePtr;
    }

    /// <summary>
    /// Retourne le nombre de canaux audio.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; uint &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint g_pAudioDevice_ChannelCount()
    {
        return DefaultChannelCount;
    }

    /// <summary>
    /// Retourne le nombre de canaux de mixage.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; uint &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint g_pAudioDevice_MixChannelCount()
    {
        return DefaultMixChannelCount;
    }

    /// <summary>
    /// Retourne le nombre de bits par échantillon.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; uint &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint g_pAudioDevice_BitsPerSample()
    {
        return DefaultBitsPerSample;
    }

    /// <summary>
    /// Retourne le nombre d'octets par échantillon.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; uint &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint g_pAudioDevice_BytesPerSample()
    {
        return DefaultBytesPerSample;
    }

    /// <summary>
    /// Retourne le taux d'échantillonnage (samples par seconde).
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; uint &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint g_pAudioDevice_SampleRate()
    {
        return DefaultSampleRate;
    }

    /// <summary>
    /// Indique si le périphérique audio est actif.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; int &gt;
    /// Retourne 1 si actif, 0 sinon.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pAudioDevice_IsActive()
    {
        return _isActive && _isValid ? 1 : 0;
    }

    /// <summary>
    /// Annule la sortie audio en cours.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pAudioDevice_CancelOutput()
    {
        if (_al == null || !_isValid) return;
        
        // Arrêter toutes les sources OpenAL
        foreach (var sources in _openalSources.Values)
        {
            foreach (var source in sources)
            {
                _al.SourceStop(source);
            }
        }
        
        Console.WriteLine("[NativeAOT] g_pAudioDevice_CancelOutput: All sources stopped");
    }

    /// <summary>
    /// Attend que la sortie audio soit complète.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pAudioDevice_WaitForComplete()
    {
        if (_al == null || !_isValid) return;
        
        // Attendre que toutes les sources OpenAL aient fini de jouer
        bool allStopped = false;
        int maxWaitIterations = 1000; // Éviter une boucle infinie
        int iterations = 0;
        
        while (!allStopped && iterations < maxWaitIterations)
        {
            allStopped = true;
            foreach (var sources in _openalSources.Values)
            {
                foreach (var source in sources)
                {
                    _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
                    if (state == (int)SourceState.Playing)
                    {
                        allStopped = false;
                        break;
                    }
                }
                if (!allStopped) break;
            }
            
            if (!allStopped)
            {
                Thread.Sleep(1); // Attendre 1ms
                iterations++;
            }
        }
        
        Console.WriteLine($"[NativeAOT] g_pAudioDevice_WaitForComplete: Completed after {iterations}ms");
    }

    /// <summary>
    /// Active ou désactive le mute du périphérique audio.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; int, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pAudioDevice_MuteDevice(int bMuteDevice)
    {
        _isMuted = bMuteDevice != 0;
        Console.WriteLine($"[NativeAOT] g_pAudioDevice_MuteDevice: {_isMuted}");
        
        // Dans Source 2, appeler MuteDevice démarre le système audio pour la première fois
        // Voir AudioEngine.cs ligne 75-79
        if (!_isInitialized && !_isMuted)
        {
            InitializeAudioDevice();
        }
        
        // Pour l'émulation, on peut simplement marquer l'état
        // Dans une implémentation complète avec OpenAL, on mettrait à jour le volume
    }

    /// <summary>
    /// Vide le buffer audio.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pAudioDevice_ClearBuffer()
    {
        if (_al == null || !_isValid) return;
        
        // Arrêter et vider toutes les sources OpenAL
        foreach (var sources in _openalSources.Values)
        {
            foreach (var source in sources)
            {
                _al.SourceStop(source);
                _al.GetSourceProperty(source, GetSourceInteger.BuffersQueued, out int queued);
                if (queued > 0)
                {
                    uint[] buffers = new uint[queued];
                    _al.SourceUnqueueBuffers(source, buffers);
                    _al.DeleteBuffers(buffers);
                }
            }
        }
        
        Console.WriteLine("[NativeAOT] g_pAudioDevice_ClearBuffer: All buffers cleared");
    }

    /// <summary>
    /// Affiche les informations de débogage du périphérique audio.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pAudioDevice_OutputDebugInfo()
    {
        Console.WriteLine($"[NativeAOT] AudioDevice Debug Info:");
        Console.WriteLine($"  Name: {_deviceName}");
        Console.WriteLine($"  Valid: {_isValid}");
        Console.WriteLine($"  Active: {_isActive}");
        Console.WriteLine($"  Muted: {_isMuted}");
        Console.WriteLine($"  Channels: {DefaultChannelCount}");
        Console.WriteLine($"  Sample Rate: {DefaultSampleRate} Hz");
        Console.WriteLine($"  Bits Per Sample: {DefaultBitsPerSample}");
    }

    /// <summary>
    /// Indique si le périphérique audio est valide.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; int &gt;
    /// Retourne 1 si valide, 0 sinon.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pAudioDevice_IsValid()
    {
        return _isValid ? 1 : 0;
    }

    /// <summary>
    /// Envoie les buffers audio mixés vers le périphérique de sortie.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, void &gt;
    /// Le paramètre buffers est un pointeur vers CAudioMixDeviceBuffers.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pAudioDevice_SendOutput(IntPtr buffers)
    {
        if (buffers == IntPtr.Zero)
        {
            Console.WriteLine("[NativeAOT] g_pAudioDevice_SendOutput: buffers is null");
            return;
        }
        
        if (!_isValid || _isMuted || _al == null)
        {
            // Si le périphérique n'est pas valide ou est muet, on ignore la sortie
            return;
        }
        
        // Récupérer AudioMixDeviceBuffersData depuis le handle
        int deviceHandle = (int)buffers;
        var deviceData = Common.HandleManager.Get<AudioMixDeviceBuffers.AudioMixDeviceBuffersData>(deviceHandle);
        if (deviceData == null)
        {
            Console.WriteLine($"[NativeAOT] g_pAudioDevice_SendOutput: Invalid device handle {deviceHandle}");
            return;
        }
        
        // Créer ou récupérer les sources OpenAL pour ce device
        if (!_openalSources.TryGetValue(buffers, out var sources))
        {
            sources = new List<uint>();
            _openalSources[buffers] = sources;
            
            // Créer une source OpenAL pour chaque canal
            for (int i = 0; i < deviceData.BufferHandles.Count; i++)
            {
                uint source = _al.GenSource();
                sources.Add(source);
                
                // Configurer la source pour le streaming
                _al.SetSourceProperty(source, SourceFloat.Pitch, 1.0f);
                _al.SetSourceProperty(source, SourceFloat.Gain, _isMuted ? 0.0f : 1.0f);
            }
        }
        
        // Pour chaque canal, envoyer les données audio vers OpenAL
        for (int channelIndex = 0; channelIndex < deviceData.BufferHandles.Count && channelIndex < sources.Count; channelIndex++)
        {
            int bufferHandle = deviceData.BufferHandles[channelIndex];
            var bufferData = Common.HandleManager.Get<AudioMixBuffer.AudioMixBufferData>(bufferHandle);
            if (bufferData == null) continue;
            
            uint source = sources[channelIndex];
            
            // Vérifier combien de buffers sont disponibles dans la queue
            _al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out int processed);
            if (processed > 0)
            {
                // Réutiliser les buffers traités
                uint[] processedBuffers = new uint[processed];
                _al.SourceUnqueueBuffers(source, processedBuffers);
                
                // Convertir les données float[] en int16 pour OpenAL
                short[] pcmData = ConvertFloatToInt16(bufferData.Data, DefaultSampleRate);
                
                // Mettre à jour le buffer avec les nouvelles données
                fixed (short* pcmPtr = pcmData)
                {
                    _al.BufferData(processedBuffers[0], BufferFormat.Mono16, pcmPtr, pcmData.Length * sizeof(short), (int)DefaultSampleRate);
                }
                
                // Remettre le buffer dans la queue
                _al.SourceQueueBuffers(source, processedBuffers);
            }
            else
            {
                // Créer de nouveaux buffers si nécessaire
                _al.GetSourceProperty(source, GetSourceInteger.BuffersQueued, out int queued);
                if (queued < NumStreamBuffers)
                {
                    int numNewBuffers = NumStreamBuffers - queued;
                    uint[] newBuffers = new uint[numNewBuffers];
                    fixed (uint* buffersPtr = newBuffers)
                    {
                        _al.GenBuffers(numNewBuffers, buffersPtr);
                    }
                    
                    // Convertir les données float[] en int16 pour OpenAL
                    short[] pcmData = ConvertFloatToInt16(bufferData.Data, DefaultSampleRate);
                    
                    // Remplir les buffers avec les données
                    fixed (short* pcmPtr = pcmData)
                    {
                        for (int i = 0; i < newBuffers.Length; i++)
                        {
                            _al.BufferData(newBuffers[i], BufferFormat.Mono16, pcmPtr, pcmData.Length * sizeof(short), (int)DefaultSampleRate);
                        }
                    }
                    
                    // Ajouter les buffers à la queue
                    _al.SourceQueueBuffers(source, newBuffers);
                }
            }
            
            // Démarrer la source si elle n'est pas déjà en cours de lecture
            _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
            if (state != (int)SourceState.Playing)
            {
                _al.SourcePlay(source);
            }
        }
    }
    
    /// <summary>
    /// Convertit un tableau de float[-1.0, 1.0] en PCM int16 pour OpenAL.
    /// </summary>
    private static short[] ConvertFloatToInt16(float[] floatData, uint _)
    {
        short[] pcmData = new short[floatData.Length];
        for (int i = 0; i < floatData.Length; i++)
        {
            // Clamper la valeur entre -1.0 et 1.0, puis convertir en int16
            float clamped = Math.Max(-1.0f, Math.Min(1.0f, floatData[i]));
            pcmData[i] = (short)(clamped * short.MaxValue);
        }
        return pcmData;
    }
}

