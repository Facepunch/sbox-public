using System;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Common;
using NativeEngine;

namespace Sandbox.Engine.Emulation.CUtl;

/// <summary>
/// Module d'émulation pour CUtlVectorTraceResult (CtlVctrPhyscsTrc_Result_*).
/// Gère les vecteurs de PhysicsTrace.Result.
/// </summary>
public static unsafe class CUtlVectorTraceResult
{
    /// <summary>
    /// Initialise le module CUtlVectorTraceResult en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16097-16100 (1232-1235)
    /// </summary>
    public static void Init(void** native)
    {
        // Indices 1232-1235
        native[1232] = (void*)(delegate* unmanaged<IntPtr, void>)&CtlVctrPhyscsTrc_Result_DeleteThis;
        native[1233] = (void*)(delegate* unmanaged<int, int, IntPtr>)&CtlVctrPhyscsTrc_Result_Create;
        native[1234] = (void*)(delegate* unmanaged<IntPtr, int>)&CtlVctrPhyscsTrc_Result_Count;
        // Note: PhysicsTrace.Result is not blittable, so we use void* and cast in Interop.Engine.cs
        native[1235] = (void*)(delegate* unmanaged<IntPtr, int, void*>)&CtlVctrPhyscsTrc_Result_Element;
        
        Console.WriteLine("[NativeAOT] CUtlVectorTraceResult module initialized");
    }
    
    [UnmanagedCallersOnly]
    public static void CtlVctrPhyscsTrc_Result_DeleteThis(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        HandleManager.Unregister((int)self);
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr CtlVctrPhyscsTrc_Result_Create(int growsize, int initialcapacity)
    {
        var vectorData = new VectorTraceResultData
        {
            GrowSize = growsize,
            Capacity = Math.Max(initialcapacity, 16),
            Count = 0,
            Results = new System.Collections.Generic.List<object>()
        };
        
        int handle = HandleManager.Register(vectorData);
        return handle == 0 ? IntPtr.Zero : (IntPtr)handle;
    }
    
    [UnmanagedCallersOnly]
    public static int CtlVctrPhyscsTrc_Result_Count(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        var vectorData = HandleManager.Get<VectorTraceResultData>((int)self);
        return vectorData?.Count ?? 0;
    }
    
    [UnmanagedCallersOnly]
    public static void* CtlVctrPhyscsTrc_Result_Element(IntPtr self, int i)
    {
        if (self == IntPtr.Zero || i < 0)
            return null;
        
        var vectorData = HandleManager.Get<VectorTraceResultData>((int)self);
        if (vectorData == null || i >= vectorData.Results.Count)
            return null;
        
        // Minimal native struct for trace result (position, normal, fraction, entity)
        TraceResultNative resultNative;
        
        if (vectorData.Results[i] is TraceResultNative native)
        {
            resultNative = native;
        }
        else
        {
            // Best-effort fallback: zeroed result
            resultNative = default;
        }
        
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<TraceResultNative>());
        Marshal.StructureToPtr(resultNative, ptr, false);
        return ptr.ToPointer();
    }
    
    public class VectorTraceResultData
    {
        public int GrowSize { get; set; }
        public int Capacity { get; set; }
        public int Count { get; set; }
        // Note: Using object list since PhysicsTrace.Result is not accessible
        public System.Collections.Generic.List<object> Results { get; set; } = new();
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct TraceResultNative
    {
        public System.Numerics.Vector3 Position;
        public System.Numerics.Vector3 Normal;
        public float Fraction;
        public int Entity;
    }
}

