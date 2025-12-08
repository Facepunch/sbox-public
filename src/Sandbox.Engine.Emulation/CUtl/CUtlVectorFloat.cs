using System;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Common;
using NativeEngine;

namespace Sandbox.Engine.Emulation.CUtl;

/// <summary>
/// Module d'émulation pour CUtlVectorFloat (CtlVctrflt_*).
/// Gère les vecteurs de floats avec support pour l'écriture directe en mémoire.
/// </summary>
public static unsafe class CUtlVectorFloat
{
    /// <summary>
    /// Initialise le module CUtlVectorFloat en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16088-16092 (1223-1227)
    /// </summary>
    public static void Init(void** native)
    {
        // Indices 1223-1227
        native[1223] = (void*)(delegate* unmanaged<IntPtr, void>)&CtlVctrflt_DeleteThis;
        native[1224] = (void*)(delegate* unmanaged<int, int, IntPtr>)&CtlVctrflt_Create;
        native[1225] = (void*)(delegate* unmanaged<IntPtr, int>)&CtlVctrflt_Count;
        native[1226] = (void*)(delegate* unmanaged<IntPtr, int, void>)&CtlVctrflt_SetCount;
        native[1227] = (void*)(delegate* unmanaged<IntPtr, int, float>)&CtlVctrflt_Element;
        
        Console.WriteLine("[NativeAOT] CUtlVectorFloat module initialized");
    }
    
    /// <summary>
    /// Delete a CUtlVectorFloat instance.
    /// 
    /// **Source 2 behavior**: Deletes the vector and frees its memory.
    /// **Emulation behavior**: Unregisters from HandleManager and frees memory.
    /// </summary>
    /// <param name="self">Handle to the CUtlVectorFloat</param>
    [UnmanagedCallersOnly]
    public static void CtlVctrflt_DeleteThis(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        // Get the vector data
        var vectorData = HandleManager.Get<VectorFloatData>((int)self);
        if (vectorData != null)
        {
            // Free the memory if allocated
            if (vectorData.DataPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(vectorData.DataPtr);
            }
            HandleManager.Unregister((int)self);
        }
    }
    
    /// <summary>
    /// Create a new CUtlVectorFloat instance.
    /// 
    /// **Source 2 behavior**: Creates a new vector with specified grow size and initial capacity.
    /// **Emulation behavior**: Creates a VectorFloatData and registers it with HandleManager.
    /// </summary>
    /// <param name="growsize">Size to grow by when capacity is exceeded</param>
    /// <param name="initialcapacity">Initial capacity</param>
    /// <returns>Handle to the CUtlVectorFloat</returns>
    [UnmanagedCallersOnly]
    public static IntPtr CtlVctrflt_Create(int growsize, int initialcapacity)
    {
        var vectorData = new VectorFloatData
        {
            GrowSize = growsize,
            Capacity = Math.Max(initialcapacity, 16), // Minimum capacity
            Count = 0
        };
        
        // Allocate memory for the float array
        int dataSize = vectorData.Capacity * sizeof(float);
        vectorData.DataPtr = Marshal.AllocHGlobal(dataSize);
        
        // Initialize to zero
        for (int i = 0; i < vectorData.Capacity; i++)
        {
            Marshal.WriteInt32(vectorData.DataPtr, i * sizeof(float), 0);
        }
        
        // Register with HandleManager
        int handle = HandleManager.Register(vectorData);
        if (handle == 0)
        {
            Marshal.FreeHGlobal(vectorData.DataPtr);
            return IntPtr.Zero;
        }
        
        return (IntPtr)handle;
    }
    
    /// <summary>
    /// Get the count of elements in the vector.
    /// 
    /// **Source 2 behavior**: Returns the number of elements.
    /// **Emulation behavior**: Returns the stored count.
    /// </summary>
    /// <param name="self">Handle to the CUtlVectorFloat</param>
    /// <returns>Number of elements</returns>
    [UnmanagedCallersOnly]
    public static int CtlVctrflt_Count(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        var vectorData = HandleManager.Get<VectorFloatData>((int)self);
        return vectorData?.Count ?? 0;
    }
    
    /// <summary>
    /// Set the count of elements in the vector (resizes if needed).
    /// 
    /// **Source 2 behavior**: Sets the count, resizing the vector if necessary.
    /// **Emulation behavior**: Resizes the internal array if needed.
    /// </summary>
    /// <param name="self">Handle to the CUtlVectorFloat</param>
    /// <param name="count">New count</param>
    [UnmanagedCallersOnly]
    public static void CtlVctrflt_SetCount(IntPtr self, int count)
    {
        if (self == IntPtr.Zero || count < 0)
            return;
        
        var vectorData = HandleManager.Get<VectorFloatData>((int)self);
        if (vectorData == null)
            return;
        
        // Resize if needed
        if (count > vectorData.Capacity)
        {
            int newCapacity = Math.Max(count, vectorData.Capacity + vectorData.GrowSize);
            int newDataSize = newCapacity * sizeof(float);
            IntPtr newDataPtr = Marshal.AllocHGlobal(newDataSize);
            
            // Copy existing data
            if (vectorData.DataPtr != IntPtr.Zero)
            {
                int copySize = Math.Min(vectorData.Count, count) * sizeof(float);
                Buffer.MemoryCopy(
                    (void*)vectorData.DataPtr,
                    (void*)newDataPtr,
                    newDataSize,
                    copySize
                );
                Marshal.FreeHGlobal(vectorData.DataPtr);
            }
            
            // Initialize new elements to zero
            for (int i = vectorData.Count; i < count; i++)
            {
                Marshal.WriteInt32(newDataPtr, i * sizeof(float), 0);
            }
            
            vectorData.DataPtr = newDataPtr;
            vectorData.Capacity = newCapacity;
        }
        
        vectorData.Count = count;
    }
    
    /// <summary>
    /// Get an element at the specified index.
    /// 
    /// **Source 2 behavior**: Returns the element at index i.
    /// **Emulation behavior**: Reads from the internal array.
    /// </summary>
    /// <param name="self">Handle to the CUtlVectorFloat</param>
    /// <param name="i">Index</param>
    /// <returns>Element value</returns>
    [UnmanagedCallersOnly]
    public static float CtlVctrflt_Element(IntPtr self, int i)
    {
        if (self == IntPtr.Zero || i < 0)
            return 0.0f;
        
        var vectorData = HandleManager.Get<VectorFloatData>((int)self);
        if (vectorData == null || i >= vectorData.Count || vectorData.DataPtr == IntPtr.Zero)
            return 0.0f;
        
        // Read float from memory
        return Marshal.PtrToStructure<float>(vectorData.DataPtr + (i * sizeof(float)));
    }
    
    /// <summary>
    /// Helper method to set the count (for use from managed code).
    /// This is used by VideoPlayer to set spectrum vector size.
    /// Note: This is a managed helper that does the work directly, not calling the UnmanagedCallersOnly function.
    /// </summary>
    public static void SetCountHelper(IntPtr self, int count)
    {
        if (self == IntPtr.Zero || count < 0)
            return;
        
        var vectorData = HandleManager.Get<VectorFloatData>((int)self);
        if (vectorData == null)
            return;
        
        // Resize if needed (same logic as CtlVctrflt_SetCount)
        if (count > vectorData.Capacity)
        {
            int newCapacity = Math.Max(count, vectorData.Capacity + vectorData.GrowSize);
            int newDataSize = newCapacity * sizeof(float);
            IntPtr newDataPtr = Marshal.AllocHGlobal(newDataSize);
            
            // Copy existing data
            if (vectorData.DataPtr != IntPtr.Zero)
            {
                int copySize = Math.Min(vectorData.Count, count) * sizeof(float);
                Buffer.MemoryCopy(
                    (void*)vectorData.DataPtr,
                    (void*)newDataPtr,
                    newDataSize,
                    copySize
                );
                Marshal.FreeHGlobal(vectorData.DataPtr);
            }
            
            // Initialize new elements to zero
            for (int i = vectorData.Count; i < count; i++)
            {
                Marshal.WriteInt32(newDataPtr, i * sizeof(float), 0);
            }
            
            vectorData.DataPtr = newDataPtr;
            vectorData.Capacity = newCapacity;
        }
        
        vectorData.Count = count;
    }
    
    /// <summary>
    /// Helper method to set an element at the specified index (for internal use).
    /// This is used by VideoPlayer to write spectrum data.
    /// </summary>
    public static void SetElement(IntPtr self, int i, float value)
    {
        if (self == IntPtr.Zero || i < 0)
            return;
        
        var vectorData = HandleManager.Get<VectorFloatData>((int)self);
        if (vectorData == null || i >= vectorData.Count || vectorData.DataPtr == IntPtr.Zero)
            return;
        
        // Write float to memory
        Marshal.StructureToPtr(value, vectorData.DataPtr + (i * sizeof(float)), false);
    }
    
    /// <summary>
    /// Helper non-UnmanagedCallersOnly to ajuster la taille depuis du code managé (ex: VideoPlayer).
    /// Reprend la logique de CtlVctrflt_SetCount sans l'attribut.
    /// </summary>
    public static void SetCountManaged(IntPtr self, int count)
    {
        if (self == IntPtr.Zero || count < 0)
            return;

        var vectorData = HandleManager.Get<VectorFloatData>((int)self);
        if (vectorData == null)
            return;

        if (count > vectorData.Capacity)
        {
            int newCapacity = Math.Max(count, vectorData.Capacity + vectorData.GrowSize);
            int newDataSize = newCapacity * sizeof(float);
            IntPtr newDataPtr = Marshal.AllocHGlobal(newDataSize);

            if (vectorData.DataPtr != IntPtr.Zero)
            {
                int copySize = Math.Min(vectorData.Count, count) * sizeof(float);
                Buffer.MemoryCopy(
                    (void*)vectorData.DataPtr,
                    (void*)newDataPtr,
                    newDataSize,
                    copySize
                );
                Marshal.FreeHGlobal(vectorData.DataPtr);
            }

            // Init nouveaux éléments à zéro
            for (int i = vectorData.Count; i < count; i++)
            {
                Marshal.WriteInt32(newDataPtr, i * sizeof(float), 0);
            }

            vectorData.DataPtr = newDataPtr;
            vectorData.Capacity = newCapacity;
        }

        vectorData.Count = count;
    }
    
    /// <summary>
    /// Internal data structure for CUtlVectorFloat.
    /// </summary>
    public class VectorFloatData
    {
        public int GrowSize { get; set; }
        public int Capacity { get; set; }
        public int Count { get; set; }
        public IntPtr DataPtr { get; set; } = IntPtr.Zero;
    }
}

