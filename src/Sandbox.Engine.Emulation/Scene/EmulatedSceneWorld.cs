using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox;

namespace Sandbox.Engine.Emulation.Scene;

/// <summary>
/// Emulated implementation of ISceneWorld exports (indices 2186-2199).
/// Holds minimal state so managed callers can query without stubs throwing.
/// </summary>
internal static unsafe class EmulatedSceneWorld
{
    private class WorldData
    {
        public string DebugName = "SceneWorld";
        public IntPtr DebugNamePtr = IntPtr.Zero;
        public bool DeleteAtEndOfFrame;
        public int SceneObjectCount;
        public IntPtr PvsPtr = IntPtr.Zero;
        public readonly HashSet<IntPtr> SkyboxWorlds = new();
        public Vector3 SkyboxOrigin = Vector3.Zero;
        public Angles SkyboxAngles = Angles.Zero;
        public float SkyboxScale = 1.0f;
    }

    private static readonly Dictionary<IntPtr, WorldData> _worlds = new();

    public static void Init(void** native)
    {
        native[2186] = (void*)(delegate* unmanaged<IntPtr, void>)&ISceneWorld_DeleteAllObjects;
        native[2187] = (void*)(delegate* unmanaged<IntPtr, void>)&ISceneWorld_Release;
        native[2188] = (void*)(delegate* unmanaged<IntPtr, int>)&ISceneWorld_GetSceneObjectCount;
        native[2189] = (void*)(delegate* unmanaged<IntPtr, int>)&ISceneWorld_IsEmpty;
        native[2190] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneWorld_GetWorldDebugName;
        native[2191] = (void*)(delegate* unmanaged<IntPtr, int, void>)&ISceneWorld_SetDeleteAtEndOfFrame;
        native[2192] = (void*)(delegate* unmanaged<IntPtr, int>)&ISceneWorld_GetDeleteAtEndOfFrame;
        native[2193] = (void*)(delegate* unmanaged<IntPtr, void>)&ISceneWorld_DeleteEndOfFrameObjects;
        native[2194] = (void*)(delegate* unmanaged<IntPtr, void*, MeshTraceOutput*, int>)&ISceneWorld_MeshTrace;
        native[2195] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneWorld_GetPVS;
        native[2196] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISceneWorld_SetPVS;
        native[2197] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISceneWorld_Add3DSkyboxWorld;
        native[2198] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISceneWorld_Remove3DSkyboxWorld;
        native[2199] = (void*)(delegate* unmanaged<IntPtr, Vector3*, Angles*, float, void>)&ISceneWorld_Set3DSkyboxParameters;
    }

    public static void TrackWorld(int handle, string debugName)
    {
        var key = (IntPtr)handle;
        lock (_worlds)
        {
            if (!_worlds.TryGetValue(key, out var data))
            {
                data = new WorldData();
                _worlds[key] = data;
            }
            data.DebugName = string.IsNullOrWhiteSpace(debugName) ? "SceneWorld" : debugName;
            if (data.DebugNamePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(data.DebugNamePtr);
            }
            data.DebugNamePtr = AllocUtf8(data.DebugName);
        }
    }

    public static void UntrackWorld(int handle)
    {
        var key = (IntPtr)handle;
        lock (_worlds)
        {
            if (_worlds.TryGetValue(key, out var data))
            {
                if (data.DebugNamePtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(data.DebugNamePtr);
                }
                _worlds.Remove(key);
            }
        }
    }

    private static WorldData GetWorld(IntPtr self)
    {
        if (self == IntPtr.Zero) self = (IntPtr)(-1);
        lock (_worlds)
        {
            if (!_worlds.TryGetValue(self, out var data))
            {
                data = new WorldData();
                data.DebugNamePtr = AllocUtf8(data.DebugName);
                _worlds[self] = data;
            }
            return data;
        }
    }

    private static IntPtr AllocUtf8(string value)
    {
        value ??= string.Empty;
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    [UnmanagedCallersOnly]
    public static void ISceneWorld_DeleteAllObjects(IntPtr self)
    {
        var data = GetWorld(self);
        data.SceneObjectCount = 0;
    }

    [UnmanagedCallersOnly]
    public static void ISceneWorld_Release(IntPtr self)
    {
        if (self == IntPtr.Zero) return;
        UntrackWorld((int)self);
    }

    [UnmanagedCallersOnly]
    public static int ISceneWorld_GetSceneObjectCount(IntPtr self) => GetWorld(self).SceneObjectCount;

    [UnmanagedCallersOnly]
    public static int ISceneWorld_IsEmpty(IntPtr self) => GetWorld(self).SceneObjectCount == 0 ? 1 : 0;

    [UnmanagedCallersOnly]
    public static IntPtr ISceneWorld_GetWorldDebugName(IntPtr self) => GetWorld(self).DebugNamePtr;

    [UnmanagedCallersOnly]
    public static void ISceneWorld_SetDeleteAtEndOfFrame(IntPtr self, int bDelete)
    {
        GetWorld(self).DeleteAtEndOfFrame = bDelete != 0;
    }

    [UnmanagedCallersOnly]
    public static int ISceneWorld_GetDeleteAtEndOfFrame(IntPtr self) => GetWorld(self).DeleteAtEndOfFrame ? 1 : 0;

    [UnmanagedCallersOnly]
    public static void ISceneWorld_DeleteEndOfFrameObjects(IntPtr self)
    {
        var data = GetWorld(self);
        data.SceneObjectCount = 0;
    }

    [UnmanagedCallersOnly]
    public static int ISceneWorld_MeshTrace(IntPtr self, void* input, MeshTraceOutput* output)
    {
        // No mesh data in emulation yet.
        return 0;
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneWorld_GetPVS(IntPtr self) => GetWorld(self).PvsPtr;

    [UnmanagedCallersOnly]
    public static void ISceneWorld_SetPVS(IntPtr self, IntPtr pPVS)
    {
        GetWorld(self).PvsPtr = pPVS;
    }

    [UnmanagedCallersOnly]
    public static void ISceneWorld_Add3DSkyboxWorld(IntPtr self, IntPtr world)
    {
        var data = GetWorld(self);
        lock (data.SkyboxWorlds)
        {
            data.SkyboxWorlds.Add(world);
        }
    }

    [UnmanagedCallersOnly]
    public static void ISceneWorld_Remove3DSkyboxWorld(IntPtr self, IntPtr world)
    {
        var data = GetWorld(self);
        lock (data.SkyboxWorlds)
        {
            data.SkyboxWorlds.Remove(world);
        }
    }

    [UnmanagedCallersOnly]
    public static void ISceneWorld_Set3DSkyboxParameters(IntPtr self, Vector3* origin, Angles* angle, float scale)
    {
        var data = GetWorld(self);
        if (origin != null) data.SkyboxOrigin = *origin;
        if (angle != null) data.SkyboxAngles = *angle;
        data.SkyboxScale = scale;
    }
}

