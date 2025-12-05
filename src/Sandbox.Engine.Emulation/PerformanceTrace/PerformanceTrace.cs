using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Engine.Emulation.PerformanceTrace;

/// <summary>
/// Module d'émulation pour PerformanceTrace (PerformanceTrace_*).
/// Gère les événements de profiling et de traçage de performance.
/// </summary>
public static unsafe class PerformanceTrace
{
    /// <summary>
    /// Initialise le module PerformanceTrace en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 17252-17253 (2387-2388)
    /// </summary>
    public static void Init(void** native)
    {
        // PerformanceTrace functions (indices 2387-2388 depuis Interop.Engine.cs)
        native[2387] = (void*)(delegate* unmanaged<IntPtr, IntPtr, uint, void>)&PerformanceTrace_BeginEvent;
        native[2388] = (void*)(delegate* unmanaged<void>)&PerformanceTrace_EndEvent;
        
        Console.WriteLine("[NativeAOT] PerformanceTrace module initialized");
    }
    
    /// <summary>
    /// Begin a performance trace event (profiling).
    /// Signature exacte depuis Interop.Engine.cs ligne 17252: delegate* unmanaged[SuppressGCTransition]&lt; IntPtr, IntPtr, uint, void &gt;
    /// 
    /// **Comportement Source 2** : Enregistre un événement de profiling avec nom, données et couleur.
    /// **Comportement émulation** : No-op pour l'instant (profiling non critique). Peut être implémenté avec une bibliothèque de profiling si nécessaire.
    /// </summary>
    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void PerformanceTrace_BeginEvent(IntPtr name, IntPtr data, uint color)
    {
        // No-op for now - profiling is not critical for engine functionality
        // In the future, this could be implemented with a profiling library if needed
    }
    
    /// <summary>
    /// End a performance trace event (profiling).
    /// Signature exacte depuis Interop.Engine.cs ligne 17253: delegate* unmanaged[SuppressGCTransition]&lt; void &gt;
    /// 
    /// **Comportement Source 2** : Termine l'événement de profiling en cours.
    /// **Comportement émulation** : No-op pour l'instant (profiling non critique). Peut être implémenté avec une bibliothèque de profiling si nécessaire.
    /// </summary>
    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void PerformanceTrace_EndEvent()
    {
        // No-op for now - profiling is not critical for engine functionality
        // In the future, this could be implemented with a profiling library if needed
    }
}

