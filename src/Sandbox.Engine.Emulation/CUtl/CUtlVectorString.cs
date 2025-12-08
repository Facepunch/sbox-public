using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Common;
using NativeEngine;

namespace Sandbox.Engine.Emulation.CUtl;

/// <summary>
/// Module d'émulation pour CUtlVectorString (CtlVctrCtlStrng_*).
/// Gère les vecteurs de strings.
/// </summary>
public static unsafe class CUtlVectorString
{
    /// <summary>
    /// Initialise le module CUtlVectorString en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16083-16087 (1218-1222)
    /// </summary>
    public static void Init(void** native)
    {
        // Indices 1218-1222
        native[1218] = (void*)(delegate* unmanaged<IntPtr, void>)&CtlVctrCtlStrng_DeleteThis;
        native[1219] = (void*)(delegate* unmanaged<int, int, IntPtr>)&CtlVctrCtlStrng_Create;
        native[1220] = (void*)(delegate* unmanaged<IntPtr, int>)&CtlVctrCtlStrng_Count;
        native[1221] = (void*)(delegate* unmanaged<IntPtr, int, void>)&CtlVctrCtlStrng_SetCount;
        native[1222] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&CtlVctrCtlStrng_Element;
        
        Console.WriteLine("[NativeAOT] CUtlVectorString module initialized");
    }
    
    [UnmanagedCallersOnly]
    public static void CtlVctrCtlStrng_DeleteThis(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        var vectorData = HandleManager.Get<VectorStringData>((int)self);
        if (vectorData != null)
        {
            // Free string pointers
            foreach (var strPtr in vectorData.StringPointers)
            {
                if (strPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(strPtr);
                }
            }
            HandleManager.Unregister((int)self);
        }
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr CtlVctrCtlStrng_Create(int growsize, int initialcapacity)
    {
        var vectorData = new VectorStringData
        {
            GrowSize = growsize,
            Capacity = Math.Max(initialcapacity, 16),
            Count = 0,
            Strings = new List<string>(),
            StringPointers = new List<IntPtr>()
        };
        
        int handle = HandleManager.Register(vectorData);
        return handle == 0 ? IntPtr.Zero : (IntPtr)handle;
    }
    
    [UnmanagedCallersOnly]
    public static int CtlVctrCtlStrng_Count(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        var vectorData = HandleManager.Get<VectorStringData>((int)self);
        return vectorData?.Count ?? 0;
    }
    
    [UnmanagedCallersOnly]
    public static void CtlVctrCtlStrng_SetCount(IntPtr self, int count)
    {
        if (self == IntPtr.Zero || count < 0)
            return;
        
        var vectorData = HandleManager.Get<VectorStringData>((int)self);
        if (vectorData == null)
            return;
        
        // Resize if needed
        while (vectorData.Strings.Count < count)
        {
            vectorData.Strings.Add("");
            vectorData.StringPointers.Add(IntPtr.Zero);
        }
        
        while (vectorData.Strings.Count > count)
        {
            int lastIndex = vectorData.Strings.Count - 1;
            if (vectorData.StringPointers[lastIndex] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(vectorData.StringPointers[lastIndex]);
            }
            vectorData.Strings.RemoveAt(lastIndex);
            vectorData.StringPointers.RemoveAt(lastIndex);
        }
        
        vectorData.Count = count;
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr CtlVctrCtlStrng_Element(IntPtr self, int i)
    {
        if (self == IntPtr.Zero || i < 0)
            return IntPtr.Zero;
        
        var vectorData = HandleManager.Get<VectorStringData>((int)self);
        if (vectorData == null || i >= vectorData.Count)
            return IntPtr.Zero;
        
        // Allocate or reuse string pointer
        if (vectorData.StringPointers[i] == IntPtr.Zero)
        {
            string str = vectorData.Strings[i] ?? "";
            vectorData.StringPointers[i] = Marshal.StringToHGlobalAnsi(str);
        }
        
        return vectorData.StringPointers[i];
    }
    
    public class VectorStringData
    {
        public int GrowSize { get; set; }
        public int Capacity { get; set; }
        public int Count { get; set; }
        public List<string> Strings { get; set; } = new();
        public List<IntPtr> StringPointers { get; set; } = new();
    }
}

