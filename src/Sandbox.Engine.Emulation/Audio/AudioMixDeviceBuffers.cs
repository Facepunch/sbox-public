using System.Runtime.InteropServices;
using System.Collections.Generic;
using Sandbox.Engine.Emulation.Common;

namespace Sandbox.Engine.Emulation.Audio;

/// <summary>
/// Module d'émulation pour AudioMixDeviceBuffers (CdMxDvcBffrs_*).
/// Gère un ensemble de buffers de mixage pour plusieurs canaux audio (jusqu'à 8 canaux).
/// CdMxDvcBffrs = CAudioMixDeviceBuffers (abréviation).
/// Signatures exactes depuis Interop.Engine.cs ligne 239-241 et indices depuis ligne 14776-14778.
/// </summary>
public static unsafe class AudioMixDeviceBuffers
{
    /// <summary>
    /// Initialise le module AudioMixDeviceBuffers en patchant toutes les fonctions natives.
    /// Signatures exactes depuis Interop.Engine.cs ligne 239-241.
    /// Indices depuis Interop.Engine.cs ligne 14776-14778.
    /// </summary>
    public static void Init(void** native)
    {
        // AudioMixDeviceBuffers functions (indices 29-31 depuis Interop.Engine.cs ligne 14776-14778)
        native[29] = (void*)(delegate* unmanaged<int, IntPtr>)&CdMxDvcBffrs_Create;
        native[30] = (void*)(delegate* unmanaged<IntPtr, void>)&CdMxDvcBffrs_Destroy;
        native[31] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&CdMxDvcBffrs_GetBuffer;
    }

    // ============================================================================
    // AudioMixDeviceBuffers Functions (CdMxDvcBffrs_*)
    // Signatures exactes depuis Interop.Engine.cs ligne 239-241
    // ============================================================================

    /// <summary>
    /// Crée un nouvel ensemble de buffers de mixage pour plusieurs canaux.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged[SuppressGCTransition]&lt; int, IntPtr &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CdMxDvcBffrs_Create(int count)
    {
        if (count <= 0 || count > 8)
        {
            Console.WriteLine($"[NativeAOT] CdMxDvcBffrs_Create: Invalid channel count {count}, must be 1-8");
            return IntPtr.Zero;
        }

        var dev = new AudioMixDeviceBuffersData(count);
        int handle = HandleManager.Register(dev);
        Console.WriteLine($"[NativeAOT] CdMxDvcBffrs_Create: channels={count}, handle={handle}");
        return (IntPtr)handle;
    }

    /// <summary>
    /// Libère un ensemble de buffers de mixage.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged[SuppressGCTransition]&lt; IntPtr, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CdMxDvcBffrs_Destroy(IntPtr devicePtr)
    {
        int handle = (int)devicePtr;
        HandleManager.Unregister(handle);
        Console.WriteLine($"[NativeAOT] CdMxDvcBffrs_Destroy: handle={handle}");
    }

    /// <summary>
    /// Récupère un buffer de mixage pour un canal spécifique.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged[SuppressGCTransition]&lt; IntPtr, int, IntPtr &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CdMxDvcBffrs_GetBuffer(IntPtr devicePtr, int index)
    {
        int handle = (int)devicePtr;
        var dev = HandleManager.Get<AudioMixDeviceBuffersData>(handle);
        if (dev == null) return IntPtr.Zero;
        
        int bufHandle = dev.GetBuffer(index);
        return (IntPtr)bufHandle;
    }

    /// <summary>
    /// Données d'un ensemble de buffers de mixage pour plusieurs canaux.
    /// </summary>
    internal class AudioMixDeviceBuffersData
    {
        public List<int> BufferHandles = new List<int>();

        public AudioMixDeviceBuffersData(int count)
        {
            // Créer un buffer de mixage pour chaque canal
            for (int i = 0; i < count; i++)
            {
                var buf = new AudioMixBuffer.AudioMixBufferData();
                int h = HandleManager.Register(buf);
                BufferHandles.Add(h);
            }
        }

        public int GetBuffer(int index)
        {
            return index >= 0 && index < BufferHandles.Count ? BufferHandles[index] : 0;
        }
    }
}

