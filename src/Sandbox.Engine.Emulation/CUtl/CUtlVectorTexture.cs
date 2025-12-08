using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Common;
using NativeEngine;

namespace Sandbox.Engine.Emulation.CUtl;

/// <summary>
/// Module d'émulation pour CUtlVectorTexture (CtlVctrHRndrTxtr_*).
/// Gère les vecteurs de textures.
/// </summary>
public static unsafe class CUtlVectorTexture
{
    /// <summary>
    /// Initialise le module CUtlVectorTexture en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16093-16096 (1228-1231)
    /// </summary>
    public static void Init(void** native)
    {
        // Indices 1228-1231
        native[1228] = (void*)(delegate* unmanaged<IntPtr, void>)&CtlVctrHRndrTxtr_DeleteThis;
        native[1229] = (void*)(delegate* unmanaged<int, int, IntPtr>)&CtlVctrHRndrTxtr_Create;
        native[1230] = (void*)(delegate* unmanaged<IntPtr, int>)&CtlVctrHRndrTxtr_Count;
        native[1231] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&CtlVctrHRndrTxtr_Element;
        
        Console.WriteLine("[NativeAOT] CUtlVectorTexture module initialized");
    }
    
    [UnmanagedCallersOnly]
    public static void CtlVctrHRndrTxtr_DeleteThis(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        HandleManager.Unregister((int)self);
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr CtlVctrHRndrTxtr_Create(int growsize, int initialcapacity)
    {
        var vectorData = new VectorTextureData
        {
            GrowSize = growsize,
            Capacity = Math.Max(initialcapacity, 16),
            Count = 0,
            Textures = new List<IntPtr>()
        };
        
        int handle = HandleManager.Register(vectorData);
        return handle == 0 ? IntPtr.Zero : (IntPtr)handle;
    }
    
    [UnmanagedCallersOnly]
    public static int CtlVctrHRndrTxtr_Count(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        var vectorData = HandleManager.Get<VectorTextureData>((int)self);
        return vectorData?.Count ?? 0;
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr CtlVctrHRndrTxtr_Element(IntPtr self, int i)
    {
        if (self == IntPtr.Zero || i < 0)
            return IntPtr.Zero;
        
        var vectorData = HandleManager.Get<VectorTextureData>((int)self);
        if (vectorData == null || i >= vectorData.Textures.Count)
            return IntPtr.Zero;
        
        return vectorData.Textures[i];
    }
    
    public class VectorTextureData
    {
        public int GrowSize { get; set; }
        public int Capacity { get; set; }
        public int Count { get; set; }
        public List<IntPtr> Textures { get; set; } = new();
    }
}

