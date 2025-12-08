using System;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Common;

namespace Sandbox.Engine.Emulation.CUtl;

/// <summary>
/// Module d'émulation pour CUtlBuffer (CUtlBuffer_*).
/// Gère les buffers de données.
/// </summary>
public static unsafe class CUtlBuffer
{
    /// <summary>
    /// Initialise le module CUtlBuffer en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16078-16081 (1213-1216)
    /// </summary>
    public static void Init(void** native)
    {
        // Indices 1213-1216
        native[1213] = (void*)(delegate* unmanaged<IntPtr>)&CUtlBuffer_Create;
        native[1214] = (void*)(delegate* unmanaged<IntPtr, void>)&CUtlBuffer_Dispose;
        native[1215] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CUtlBuffer_Base;
        native[1216] = (void*)(delegate* unmanaged<IntPtr, int>)&CUtlBuffer_TellMaxPut;
        
        Console.WriteLine("[NativeAOT] CUtlBuffer module initialized");
    }
    
    /// <summary>
    /// Create a new CUtlBuffer instance.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CUtlBuffer_Create()
    {
        var bufferData = new BufferData
        {
            DataPtr = IntPtr.Zero,
            Size = 0,
            MaxPut = 0
        };
        
        int handle = HandleManager.Register(bufferData);
        return handle == 0 ? IntPtr.Zero : (IntPtr)handle;
    }
    
    /// <summary>
    /// Dispose a CUtlBuffer instance.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CUtlBuffer_Dispose(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        var bufferData = HandleManager.Get<BufferData>((int)self);
        if (bufferData != null)
        {
            if (bufferData.DataPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(bufferData.DataPtr);
            }
            HandleManager.Unregister((int)self);
        }
    }
    
    /// <summary>
    /// Get the base pointer of the buffer.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CUtlBuffer_Base(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return IntPtr.Zero;
        
        var bufferData = HandleManager.Get<BufferData>((int)self);
        return bufferData?.DataPtr ?? IntPtr.Zero;
    }
    
    /// <summary>
    /// Get the maximum put position.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CUtlBuffer_TellMaxPut(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        var bufferData = HandleManager.Get<BufferData>((int)self);
        return bufferData?.MaxPut ?? 0;
    }
    
    public class BufferData
    {
        public IntPtr DataPtr { get; set; } = IntPtr.Zero;
        public int Size { get; set; }
        public int MaxPut { get; set; }
    }
}

