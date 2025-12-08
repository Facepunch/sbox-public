using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Common;

namespace Sandbox.Engine.Emulation.CUtl;

/// <summary>
/// Module d'émulation pour CUtlSymbolTable (CUtlSymbolTable_*).
/// Gère les tables de symboles.
/// </summary>
public static unsafe class CUtlSymbolTable
{
    /// <summary>
    /// Initialise le module CUtlSymbolTable en patchant les fonctions natives.
    /// Indice depuis Interop.Engine.cs ligne 16082 (1217)
    /// </summary>
    public static void Init(void** native)
    {
        // Indice 1217
        native[1217] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CUtlSymbolTable_AddString;
        
        Console.WriteLine("[NativeAOT] CUtlSymbolTable module initialized");
    }
    
    [UnmanagedCallersOnly]
    public static void CUtlSymbolTable_AddString(IntPtr self, IntPtr pString)
    {
        if (self == IntPtr.Zero || pString == IntPtr.Zero)
            return;
        
        // Stub implementation - symbol tables are typically managed by the engine
        // This is a no-op for now
    }
}

