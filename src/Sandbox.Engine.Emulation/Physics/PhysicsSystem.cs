using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Sandbox.Engine.Emulation.Common;
using Sandbox.Engine.Emulation.Generated;
using Sandbox.Engine.Emulation;

namespace Sandbox.Engine.Emulation.Physics;

/// <summary>
/// Module d'émulation pour PhysicsSystem (g_pPhysicsSystem_*).
/// Gère les propriétés de surface physique et le contrôleur de propriétés.
/// </summary>
public static unsafe class PhysicsSystem
{
    private static IntPtr _surfacePropertyController = IntPtr.Zero;
    private static int _nextControllerHandle = 1;
    private static int _nextSurfacePropertyHandle = 1000;
    
    // Dictionary to store surface properties by handle
    private static readonly Dictionary<IntPtr, SurfacePropertyData> _surfaceProperties = new();
    
    // Dictionary to store controller data
    private static readonly Dictionary<IntPtr, SurfacePropertyControllerData> _controllers = new();
    
    private struct PhysicsWorldEntry
    {
        public BepuPhysicsWorld World;
        public int Handle;         // HandleManager handle (impair)
        public int BindingHandle;  // HandleManager binding handle (pair)
    }

    // Dictionary keyed by BindingHandle (pair) → entry
    private static readonly Dictionary<int, PhysicsWorldEntry> _physicsWorlds = new();
    private static int _nextWorldHandle = 1;
    private static uint _physicsWorldTypeId;
    
    static PhysicsSystem()
    {
        // Calculate Type ID manually using MurmurHash2
        // This matches Sandbox.StringToken.MurmurHash2("PhysicsWorld", true)
        _physicsWorldTypeId = Common.HashUtils.MurmurHash2("PhysicsWorld", true);
        Console.WriteLine($"[NativeAOT] Calculated PhysicsWorld Type ID: {_physicsWorldTypeId}");
    }
    
    /// <summary>
    /// Initialise le module PhysicsSystem en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16328-16334 :
    /// - g_pPhysicsSystem_NumWorlds: 1463
    /// - g_pPhysicsSystem_CreateWorld: 1464
    /// - g_pPhysicsSystem_DestroyWorld: 1465
    /// - g_pPhysicsSystem_GetSurfacePropertyController: 1466
    /// - g_pPhysicsSystem_CastHeightField: 1467
    /// - g_pPhysicsSystem_GetAggregateData: 1468
    /// - g_pPhysicsSystem_UpdateSurfaceProperties: 1469
    /// - IPhysSurfacePropertyController functions: 2084-2086
    /// </summary>
    public static void Init(void** native)
    {
        // g_pPhysicsSystem_NumWorlds: Index 1463
        native[1463] = (void*)(delegate* unmanaged<int>)&g_pPhysicsSystem_NumWorlds;
        
        // g_pPhysicsSystem_CreateWorld: Index 1464
        native[1464] = (void*)(delegate* unmanaged<int>)&g_pPhysicsSystem_CreateWorld;
        
        // g_pPhysicsSystem_DestroyWorld: Index 1465
        native[1465] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pPhysicsSystem_DestroyWorld;
        
        // g_pPhysicsSystem_GetSurfacePropertyController: Index 1466
        native[1466] = (void*)(delegate* unmanaged<IntPtr>)&g_pPhysicsSystem_GetSurfacePropertyController;
        
        // g_pPhysicsSystem_CastHeightField: Index 1467
        // Note: Signature uses Vector3* for output (not 'out') to be compatible with UnmanagedCallersOnly
        native[1467] = (void*)(delegate* unmanaged<System.Numerics.Vector3*, System.Numerics.Vector3*, System.Numerics.Vector3*, IntPtr, int, int, float, float, int>)&g_pPhysicsSystem_CastHeightField;
        
        // g_pPhysicsSystem_GetAggregateData: Index 1468
        native[1468] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&g_pPhysicsSystem_GetAggregateData;
        
        // g_pPhysicsSystem_UpdateSurfaceProperties: Index 1469
        native[1469] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pPhysicsSystem_UpdateSurfaceProperties;
        
        // IPhysSurfacePropertyController functions: Indices 2084-2086
        native[2084] = (void*)(delegate* unmanaged<IntPtr, int>)&PhysSrfcPrprtyCn_GetSurfacePropCount;
        native[2085] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&PhysSrfcPrprtyCn_GetSurfaceProperty;
        native[2086] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&PhysSrfcPrprtyCn_AddProperty;
        
        // CPhysSurfaceProperties functions (654-670) are now handled by PhysicSurfaceProperties.Init()
        PhysicSurfaceProperties.Init(native);
        
        // Initialize PhysicsWorld module
        PhysicsWorld.Init(native);
        
        Console.WriteLine("[NativeAOT] PhysicsSystem module initialized");
    }
    
    /// <summary>
    /// Public method to access surface property data from other modules (e.g., PhysicSurfaceProperties).
    /// </summary>
    internal static bool TryGetSurfaceProperty(IntPtr handle, out SurfacePropertyData property)
    {
        if (handle == IntPtr.Zero || !_surfaceProperties.TryGetValue(handle, out property!))
        {
            property = null!;
            return false;
        }
        return true;
    }
    
    /// <summary>
    /// Internal helper for getting the surface property controller (can be called from managed code).
    /// </summary>
    internal static IntPtr GetSurfacePropertyControllerInternal()
    {
        if (_surfacePropertyController == IntPtr.Zero)
        {
            _surfacePropertyController = new IntPtr(_nextControllerHandle++);
            _controllers[_surfacePropertyController] = new SurfacePropertyControllerData
            {
                Handle = _surfacePropertyController,
                Properties = new List<IntPtr>()
            };
        }
        return _surfacePropertyController;
    }
    
    /// <summary>
    /// Get the number of active physics worlds.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pPhysicsSystem_NumWorlds()
    {
        lock (_physicsWorlds)
        {
            return _physicsWorlds.Count;
        }
    }
    
    /// <summary>
    /// Create a new physics world.
    /// Returns a handle that can be used with IPhysicsWorld functions.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pPhysicsSystem_CreateWorld()
    {
        Console.WriteLine("[NativeAOT] g_pPhysicsSystem_CreateWorld (Bepu)");
        var world = new BepuPhysicsWorld();

        // Always register through the central HandleManager
        int handle = Common.HandleManager.Register(world);
        if (handle == 0)
        {
            Console.WriteLine("[NativeAOT] Error: HandleManager.Register returned 0 for PhysicsWorld");
            return 0;
        }

        int bindingHandle = Common.HandleManager.GetBindingHandle(handle);
        if (bindingHandle == 0)
        {
            Console.WriteLine("[NativeAOT] Error: BindingHandle is 0 for PhysicsWorld");
            Common.HandleManager.Unregister(handle);
            return 0;
        }

        lock (_physicsWorlds)
        {
            _physicsWorlds[bindingHandle] = new PhysicsWorldEntry
            {
                World = world,
                Handle = handle,
                BindingHandle = bindingHandle
            };
        }

        // Optional registration with HandleIndex for managed lookup
        if (_physicsWorldTypeId != 0 && EngineGlue.Imports._ptr_Sandbox_HandleIndex_RegisterHandle != null)
        {
            var registerFn = (delegate* unmanaged<IntPtr, uint, int>)EngineGlue.Imports._ptr_Sandbox_HandleIndex_RegisterHandle;
            int result = registerFn((IntPtr)bindingHandle, _physicsWorldTypeId);
            Console.WriteLine($"[NativeAOT] Registered PhysicsWorld in HandleIndex. Binding={bindingHandle}, Return={result}, TypeID={_physicsWorldTypeId}");
        }
        else
        {
            Console.WriteLine($"[NativeAOT] Warning: HandleIndex registration unavailable. TypeID={_physicsWorldTypeId}, Ptr={(IntPtr)EngineGlue.Imports._ptr_Sandbox_HandleIndex_RegisterHandle}");
        }

        return bindingHandle;
    }
    
    /// <summary>
    /// Destroy a physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pPhysicsSystem_DestroyWorld(IntPtr worldPtr)
    {
        int bindingHandle = (int)worldPtr;
        PhysicsWorldEntry entry;

        lock (_physicsWorlds)
        {
            if (!_physicsWorlds.TryGetValue(bindingHandle, out entry))
            {
                Console.WriteLine($"[NativeAOT] g_pPhysicsSystem_DestroyWorld: world not found, binding={bindingHandle}");
                return;
            }

            _physicsWorlds.Remove(bindingHandle);
        }

        entry.World.Dispose();
        Common.HandleManager.Unregister(entry.Handle);
        Console.WriteLine($"[NativeAOT] g_pPhysicsSystem_DestroyWorld (Bepu) binding={bindingHandle}, handle={entry.Handle}");
    }
    
    /// <summary>
    /// Get the surface property controller.
    /// Returns a singleton controller instance.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pPhysicsSystem_GetSurfacePropertyController()
    {
        return GetSurfacePropertyControllerInternal();
    }
    
    /// <summary>
    /// Cast a ray against a height field.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pPhysicsSystem_CastHeightField(
        System.Numerics.Vector3* vOut,
        System.Numerics.Vector3* vRayStart,
        System.Numerics.Vector3* vRayDelta,
        IntPtr Heights,
        int SizeX,
        int SizeY,
        float SizeScale,
        float HeightScale)
    {
        if (vOut == null)
            return 0;
        
        *vOut = System.Numerics.Vector3.Zero;
        throw new NotImplementedException("g_pPhysicsSystem_CastHeightField is not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Get aggregate data for a resource name.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pPhysicsSystem_GetAggregateData(IntPtr resourceNamePtr)
    {
        throw new NotImplementedException("g_pPhysicsSystem_GetAggregateData is not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Update surface properties.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pPhysicsSystem_UpdateSurfaceProperties(IntPtr pSurfaceProperties)
    {
        // No-op for now - surface properties are managed internally
        // In the future, this could update BepuPhysics material properties
    }
    
    /// <summary>
    /// Internal helper to get a physics world by handle.
    /// </summary>
    internal static bool TryGetPhysicsWorld(int bindingHandle, out BepuPhysicsWorld? world)
    {
        lock (_physicsWorlds)
        {
            if (_physicsWorlds.TryGetValue(bindingHandle, out var entry))
            {
                world = entry.World;
                return true;
            }
        }

        world = null;
        return false;
    }
    
    /// <summary>
    /// Get the count of surface properties in the controller.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int PhysSrfcPrprtyCn_GetSurfacePropCount(IntPtr self)
    {
        if (self == IntPtr.Zero || !_controllers.TryGetValue(self, out var controller))
            return 0;
        
        return controller.Properties.Count;
    }
    
    /// <summary>
    /// Get a surface property by index.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr PhysSrfcPrprtyCn_GetSurfaceProperty(IntPtr self, int nIndex)
    {
        if (self == IntPtr.Zero || !_controllers.TryGetValue(self, out var controller))
            return IntPtr.Zero;
        
        if (nIndex < 0 || nIndex >= controller.Properties.Count)
            return IntPtr.Zero;
        
        return controller.Properties[nIndex];
    }
    
    /// <summary>
    /// Add a new surface property to the controller.
    /// Creates a new SurfacePropertyData and returns its handle.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr PhysSrfcPrprtyCn_AddProperty(IntPtr self, IntPtr namePtr, IntPtr basenamePtr, IntPtr descriptionPtr)
    {
        if (self == IntPtr.Zero)
            return IntPtr.Zero;
        
        string name = Marshal.PtrToStringUTF8(namePtr) ?? "";
        string basename = Marshal.PtrToStringUTF8(basenamePtr) ?? "default";
        string description = Marshal.PtrToStringUTF8(descriptionPtr) ?? "";
        
        // Create new surface property
        IntPtr handle = new IntPtr(_nextSurfacePropertyHandle++);
        var propertyData = new SurfacePropertyData
        {
            Handle = handle,
            Name = name,
            BaseName = basename,
            Description = description,
            NameHash = HashUtils.MurmurHash2(name, lowercase: true),
            BaseNameHash = HashUtils.MurmurHash2(basename, lowercase: true),
            Index = _controllers.TryGetValue(self, out var controller) ? controller.Properties.Count : 0,
            BaseIndex = 0, // Default base index
            Friction = 0.7f,
            Elasticity = 0.0f,
            Density = 1.0f,
            RollingResistance = 0.0f,
            BounceThreshold = 40.0f,
            AudioSurface = 0,
            IsHidden = false
        };
        
        _surfaceProperties[handle] = propertyData;
        
        // Add to controller
        if (_controllers.TryGetValue(self, out var ctrl))
        {
            ctrl.Properties.Add(handle);
        }
        
        return handle;
    }
    
    /// <summary>
    /// Internal data structure for surface property controller.
    /// </summary>
    private class SurfacePropertyControllerData
    {
        public IntPtr Handle { get; set; }
        public List<IntPtr> Properties { get; set; } = new();
    }
    
    /// <summary>
    /// Internal data structure for surface properties.
    /// Made internal so PhysicSurfaceProperties can access it.
    /// </summary>
    internal class SurfacePropertyData
    {
        public IntPtr Handle { get; set; }
        public string Name { get; set; } = "";
        public string BaseName { get; set; } = "";
        public string Description { get; set; } = "";
        public uint NameHash { get; set; }
        public uint BaseNameHash { get; set; }
        public int Index { get; set; }
        public int BaseIndex { get; set; }
        public float Friction { get; set; }
        public float Elasticity { get; set; }
        public float Density { get; set; }
        public float RollingResistance { get; set; }
        public float BounceThreshold { get; set; }
        public int AudioSurface { get; set; }
        public bool IsHidden { get; set; }
    }
}

