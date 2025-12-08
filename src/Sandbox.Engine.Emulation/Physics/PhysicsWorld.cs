using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using BepuPhysics;
using BepuPhysics.Constraints;
using Sandbox;
using Sandbox.Engine.Emulation.Common;
using NativeEngine;

namespace Sandbox.Engine.Emulation.Physics;

/// <summary>
/// Module d'émulation pour IPhysicsWorld (IPhysicsWorld_*).
/// Gère les opérations sur les mondes physiques (bodies, joints, simulation, etc.).
/// Utilise BepuPhysics pour l'implémentation.
/// </summary>
public static unsafe class PhysicsWorld
{
    // Dictionary to store debug scenes by world handle
    private static readonly Dictionary<int, IntPtr> _debugScenes = new();
    
    // Dictionary to store reference bodies by world handle
    private static readonly Dictionary<int, int> _referenceBodies = new();
    
    private enum JointKind
    {
        Weld,
        BallSocket
    }
    
    private static System.Numerics.Quaternion ToQuaternion(Rotation rot)
    {
        return rot;
    }
    
    private static System.Numerics.Vector3 ToVector3(global::Vector3 v)
    {
        return new System.Numerics.Vector3(v.x, v.y, v.z);
    }
    
    private static void GetLocalPose(Transform* frame, out System.Numerics.Vector3 pos, out System.Numerics.Quaternion rot)
    {
        if (frame == null)
        {
            pos = System.Numerics.Vector3.Zero;
            rot = System.Numerics.Quaternion.Identity;
            return;
        }
        
        pos = ToVector3(frame->Position);
        rot = ToQuaternion(frame->Rotation);
    }
    
    private struct JointData
    {
        public ConstraintHandle Handle;
        public int BodyAHandle;
        public int BodyBHandle;
        public JointKind Kind;
        public BepuPhysicsWorld World;
    }
    
    // Dictionary to store joints by handle
    private static readonly Dictionary<int, JointData> _joints = new();
    private static int _nextJointHandle = 10000;
    
    // Dictionary to store sleeping state by world handle
    private static readonly Dictionary<int, bool> _sleepingEnabled = new();
    
    // Dictionary to store maximum linear speed by world handle
    private static readonly Dictionary<int, float> _maxLinearSpeed = new();
    
    /// <summary>
    /// Initialise le module PhysicsWorld en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16916-16948 :
    /// - IPhysicsWorld_AddBody: 2051
    /// - IPhysicsWorld_RemoveBody: 2052
    /// - IPhysicsWorld_GetWorldReferenceBody: 2053
    /// - IPhysicsWorld_SetWorldReferenceBody: 2054
    /// - IPhysicsWorld_RemoveJoint: 2055
    /// - IPhysicsWorld_SetGravity: 2056
    /// - IPhysicsWorld_GetGravity: 2057
    /// - IPhysicsWorld_SetSimulation: 2058
    /// - IPhysicsWorld_GetSimulation: 2059
    /// - IPhysicsWorld_EnableSleeping: 2060
    /// - IPhysicsWorld_DisableSleeping: 2061
    /// - IPhysicsWorld_IsSleepingEnabled: 2062
    /// - IPhysicsWorld_SetMaximumLinearSpeed: 2063
    /// - IPhysicsWorld_AddWeldJoint: 2064
    /// - IPhysicsWorld_AddSpringJoint: 2065
    /// - IPhysicsWorld_AddRevoluteJoint: 2066
    /// - IPhysicsWorld_AddPrismaticJoint: 2067
    /// - IPhysicsWorld_AddSphericalJoint: 2068
    /// - IPhysicsWorld_AddMotorJoint: 2069
    /// - IPhysicsWorld_AddWheelJoint: 2070
    /// - IPhysicsWorld_SetCollisionRulesFromJson: 2071
    /// - IPhysicsWorld_StepSimulation: 2072
    /// - IPhysicsWorld_ProcessIntersections: 2073
    /// - IPhysicsWorld_DestroyAggregateInstance: 2074
    /// - IPhysicsWorld_CreateAggregateInstance: 2075
    /// - IPhysicsWorld_CreateAggregateInstance_1: 2076
    /// - IPhysicsWorld_SetDebugScene: 2077
    /// - IPhysicsWorld_GetDebugScene: 2078
    /// - IPhysicsWorld_Draw: 2079
    /// - IPhysicsWorld_ManagedObject: 2080
    /// - IPhysicsWorld_Query: 2081
    /// - IPhysicsWorld_Query_1: 2082
    /// - IPhysicsWorld_Query_2: 2083
    /// </summary>
    public static void Init(void** native)
    {
        // IPhysicsWorld_AddBody: Index 2051
        native[2051] = (void*)(delegate* unmanaged<IntPtr, int>)&IPhysicsWorld_AddBody;
        
        // IPhysicsWorld_RemoveBody: Index 2052
        native[2052] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&IPhysicsWorld_RemoveBody;
        
        // IPhysicsWorld_GetWorldReferenceBody: Index 2053
        native[2053] = (void*)(delegate* unmanaged<IntPtr, int>)&IPhysicsWorld_GetWorldReferenceBody;
        
        // IPhysicsWorld_SetWorldReferenceBody: Index 2054
        native[2054] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&IPhysicsWorld_SetWorldReferenceBody;
        
        // IPhysicsWorld_RemoveJoint: Index 2055
        native[2055] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&IPhysicsWorld_RemoveJoint;
        
        // IPhysicsWorld_SetGravity: Index 2056
        native[2056] = (void*)(delegate* unmanaged<IntPtr, Vector3*, void>)&IPhysicsWorld_SetGravity;
        
        // IPhysicsWorld_GetGravity: Index 2057
        native[2057] = (void*)(delegate* unmanaged<IntPtr, Vector3>)&IPhysicsWorld_GetGravity;
        
        // IPhysicsWorld_SetSimulation: Index 2058
        native[2058] = (void*)(delegate* unmanaged<IntPtr, long, void>)&IPhysicsWorld_SetSimulation;
        
        // IPhysicsWorld_GetSimulation: Index 2059
        native[2059] = (void*)(delegate* unmanaged<IntPtr, long>)&IPhysicsWorld_GetSimulation;
        
        // IPhysicsWorld_EnableSleeping: Index 2060
        native[2060] = (void*)(delegate* unmanaged<IntPtr, void>)&IPhysicsWorld_EnableSleeping;
        
        // IPhysicsWorld_DisableSleeping: Index 2061
        native[2061] = (void*)(delegate* unmanaged<IntPtr, void>)&IPhysicsWorld_DisableSleeping;
        
        // IPhysicsWorld_IsSleepingEnabled: Index 2062
        native[2062] = (void*)(delegate* unmanaged<IntPtr, int>)&IPhysicsWorld_IsSleepingEnabled;
        
        // IPhysicsWorld_SetMaximumLinearSpeed: Index 2063
        native[2063] = (void*)(delegate* unmanaged<IntPtr, float, void>)&IPhysicsWorld_SetMaximumLinearSpeed;
        
        // IPhysicsWorld_AddWeldJoint: Index 2064
        native[2064] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, Transform*, Transform*, int>)&IPhysicsWorld_AddWeldJoint;
        
        // IPhysicsWorld_AddSpringJoint: Index 2065
        native[2065] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, Transform*, Transform*, int>)&IPhysicsWorld_AddSpringJoint;
        
        // IPhysicsWorld_AddRevoluteJoint: Index 2066
        native[2066] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, Transform*, Transform*, int>)&IPhysicsWorld_AddRevoluteJoint;
        
        // IPhysicsWorld_AddPrismaticJoint: Index 2067
        native[2067] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, Transform*, Transform*, int>)&IPhysicsWorld_AddPrismaticJoint;
        
        // IPhysicsWorld_AddSphericalJoint: Index 2068
        native[2068] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, Transform*, Transform*, int>)&IPhysicsWorld_AddSphericalJoint;
        
        // IPhysicsWorld_AddMotorJoint: Index 2069
        native[2069] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, Transform*, Transform*, int>)&IPhysicsWorld_AddMotorJoint;
        
        // IPhysicsWorld_AddWheelJoint: Index 2070
        native[2070] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, Transform*, Transform*, int>)&IPhysicsWorld_AddWheelJoint;
        
        // IPhysicsWorld_SetCollisionRulesFromJson: Index 2071
        native[2071] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&IPhysicsWorld_SetCollisionRulesFromJson;
        
        // IPhysicsWorld_StepSimulation: Index 2072
        native[2072] = (void*)(delegate* unmanaged<IntPtr, float, int, void>)&IPhysicsWorld_StepSimulation;
        
        // IPhysicsWorld_ProcessIntersections: Index 2073
        native[2073] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&IPhysicsWorld_ProcessIntersections;
        
        // IPhysicsWorld_DestroyAggregateInstance: Index 2074
        native[2074] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&IPhysicsWorld_DestroyAggregateInstance;
        
        // IPhysicsWorld_CreateAggregateInstance: Index 2075
        native[2075] = (void*)(delegate* unmanaged<IntPtr, IntPtr, Transform*, ulong, long, int>)&IPhysicsWorld_CreateAggregateInstance;
        
        // IPhysicsWorld_CreateAggregateInstance_1: Index 2076
        native[2076] = (void*)(delegate* unmanaged<IntPtr, IntPtr, Transform*, ulong, long, int>)&IPhysicsWorld_CreateAggregateInstance_1;
        
        // IPhysicsWorld_SetDebugScene: Index 2077
        native[2077] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&IPhysicsWorld_SetDebugScene;
        
        // IPhysicsWorld_GetDebugScene: Index 2078
        native[2078] = (void*)(delegate* unmanaged<IntPtr, int>)&IPhysicsWorld_GetDebugScene;
        
        // IPhysicsWorld_Draw: Index 2079
        native[2079] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&IPhysicsWorld_Draw;
        
        // IPhysicsWorld_ManagedObject: Index 2080
        native[2080] = (void*)(delegate* unmanaged<IntPtr, int>)&IPhysicsWorld_ManagedObject;
        
        // IPhysicsWorld_Query: Index 2081
        native[2081] = (void*)(delegate* unmanaged<IntPtr, IntPtr, Vector3*, float, ushort, void>)&IPhysicsWorld_Query;
        
        // IPhysicsWorld_Query_1: Index 2082
        native[2082] = (void*)(delegate* unmanaged<IntPtr, IntPtr, BBox*, ushort, void>)&IPhysicsWorld_Query_1;
        
        // IPhysicsWorld_Query_2: Index 2083
        native[2083] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int, ushort, void>)&IPhysicsWorld_Query_2;
        
        Console.WriteLine("[NativeAOT] PhysicsWorld module initialized");
    }
    
    /// <summary>
    /// Add a body to the physics world.
    /// Creates a new BepuPhysicsBody and returns its handle.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_AddBody(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int worldHandle = (int)self;
        if (!PhysicsSystem.TryGetPhysicsWorld(worldHandle, out var world) || world == null)
            return 0;
        
        // Create a simple dynamic body using BepuPhysics
        BodyHandle bepuHandle = world.AddBody();
        
        // Create wrapper and register with HandleManager
        var body = new BepuPhysicsBody(world, bepuHandle);
        int bodyHandle = HandleManager.Register(body);
        
        Console.WriteLine($"[NativeAOT] IPhysicsWorld_AddBody: world={worldHandle}, body={bodyHandle}");
        
        return bodyHandle;
    }
    
    /// <summary>
    /// Remove a body from the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_RemoveBody(IntPtr self, IntPtr bodyPtr)
    {
        if (self == IntPtr.Zero || bodyPtr == IntPtr.Zero)
            return;
        
        int worldHandle = (int)self;
        int bodyHandle = (int)bodyPtr;
        
        if (!PhysicsSystem.TryGetPhysicsWorld(worldHandle, out var world) || world == null)
            return;
        
        var body = HandleManager.Get<BepuPhysicsBody>(bodyHandle);
        if (body != null)
        {
            // Remove body from BepuPhysics simulation
            world.RemoveBodyMapping(body.Handle);
            world.Simulation.Bodies.Remove(body.Handle);
            HandleManager.Unregister(bodyHandle);
            Console.WriteLine($"[NativeAOT] IPhysicsWorld_RemoveBody: world={worldHandle}, body={bodyHandle}");
        }
    }
    
    /// <summary>
    /// Get the world reference body (static body used as reference).
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_GetWorldReferenceBody(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int worldHandle = (int)self;
        if (!PhysicsSystem.TryGetPhysicsWorld(worldHandle, out var world) || world == null)
            return 0;
        
        // Check if we already have a reference body registered
        lock (_referenceBodies)
        {
            if (_referenceBodies.TryGetValue(worldHandle, out int existingHandle))
                return existingHandle;
            
            // Create a wrapper for the reference body (static)
            StaticHandle staticHandle = world.GetReferenceBody();
            // Note: BepuPhysicsBody is for dynamic bodies, we need a different approach for statics
            // For now, return 0 as static bodies are handled differently in BepuPhysics
            return 0;
        }
    }
    
    /// <summary>
    /// Set the world reference body.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_SetWorldReferenceBody(IntPtr self, IntPtr bodyPtr)
    {
        if (self == IntPtr.Zero)
            return;
        
        int worldHandle = (int)self;
        int bodyHandle = bodyPtr != IntPtr.Zero ? (int)bodyPtr : 0;
        
        lock (_referenceBodies)
        {
            if (bodyHandle != 0)
                _referenceBodies[worldHandle] = bodyHandle;
            else
                _referenceBodies.Remove(worldHandle);
        }
        
        // Note: In BepuPhysics, the reference body is a static body created at world creation
        // This function mainly tracks the reference for Source 2 compatibility
    }
    
    /// <summary>
    /// Remove a joint from the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_RemoveJoint(IntPtr self, IntPtr jointPtr)
    {
        if (self == IntPtr.Zero || jointPtr == IntPtr.Zero)
            return;
        
        int jointHandle = (int)jointPtr;
        
        lock (_joints)
        {
            if (_joints.TryGetValue(jointHandle, out var joint))
            {
                // Remove joint from BepuPhysics simulation
                joint.World.Simulation.Solver.Remove(joint.Handle);
                _joints.Remove(jointHandle);
                Console.WriteLine($"[NativeAOT] IPhysicsWorld_RemoveJoint: joint={jointHandle}");
            }
        }
    }
    
    /// <summary>
    /// Set gravity for the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_SetGravity(IntPtr self, Vector3* gravity)
    {
        if (self == IntPtr.Zero || gravity == null)
            return;
        
        int worldHandle = (int)self;
        if (!PhysicsSystem.TryGetPhysicsWorld(worldHandle, out var world) || world == null)
            return;
        
        world.Gravity = *gravity;
        
        // Update BepuPhysics callbacks if needed
        // Note: This requires recreating the simulation with new callbacks, simplified for now
        Console.WriteLine($"[NativeAOT] IPhysicsWorld_SetGravity: world={worldHandle}, gravity={*gravity}");
    }
    
    /// <summary>
    /// Get gravity for the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static Vector3 IPhysicsWorld_GetGravity(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return new Vector3(0, 0, -800);
        
        int worldHandle = (int)self;
        if (!PhysicsSystem.TryGetPhysicsWorld(worldHandle, out var world) || world == null)
            return new Vector3(0, 0, -800);
        
        return world.Gravity;
    }
    
    /// <summary>
    /// Set simulation mode for the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_SetSimulation(IntPtr self, long simulationMode)
    {
        if (self == IntPtr.Zero)
            return;
        
        // Note: BepuPhysics doesn't have different simulation modes like Source 2
        // This is mainly for Source 2 compatibility
        Console.WriteLine($"[NativeAOT] IPhysicsWorld_SetSimulation: world={(int)self}, mode={simulationMode}");
    }
    
    /// <summary>
    /// Get simulation mode for the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static long IPhysicsWorld_GetSimulation(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        // Return default simulation mode (0 = Continuous)
        return 0;
    }
    
    /// <summary>
    /// Enable sleeping for bodies in the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_EnableSleeping(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        int worldHandle = (int)self;
        lock (_sleepingEnabled)
        {
            _sleepingEnabled[worldHandle] = true;
        }
        
        // BepuPhysics handles sleeping automatically, this is mainly for tracking
        Console.WriteLine($"[NativeAOT] IPhysicsWorld_EnableSleeping: world={worldHandle}");
    }
    
    /// <summary>
    /// Disable sleeping for bodies in the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_DisableSleeping(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return;
        
        int worldHandle = (int)self;
        lock (_sleepingEnabled)
        {
            _sleepingEnabled[worldHandle] = false;
        }
        
        // BepuPhysics handles sleeping automatically, this is mainly for tracking
        Console.WriteLine($"[NativeAOT] IPhysicsWorld_DisableSleeping: world={worldHandle}");
    }
    
    /// <summary>
    /// Check if sleeping is enabled for the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_IsSleepingEnabled(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 1; // Default: enabled
        
        int worldHandle = (int)self;
        lock (_sleepingEnabled)
        {
            return _sleepingEnabled.TryGetValue(worldHandle, out bool enabled) && enabled ? 1 : 0;
        }
    }
    
    /// <summary>
    /// Set maximum linear speed for bodies in the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_SetMaximumLinearSpeed(IntPtr self, float flSpeed)
    {
        if (self == IntPtr.Zero)
            return;
        
        int worldHandle = (int)self;
        lock (_maxLinearSpeed)
        {
            _maxLinearSpeed[worldHandle] = flSpeed;
        }
        
        // Note: BepuPhysics doesn't have a global max speed limit
        // This would need to be enforced per-body if needed
        Console.WriteLine($"[NativeAOT] IPhysicsWorld_SetMaximumLinearSpeed: world={worldHandle}, speed={flSpeed}");
    }
    
    /// <summary>
    /// Add a weld joint to the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_AddWeldJoint(IntPtr self, IntPtr body1Ptr, IntPtr body2Ptr, Transform* localFrame1, Transform* localFrame2)
    {
        if (self == IntPtr.Zero || body1Ptr == IntPtr.Zero || body2Ptr == IntPtr.Zero)
            return 0;
        
        int worldHandle = (int)self;
        int body1Handle = (int)body1Ptr;
        int body2Handle = (int)body2Ptr;
        
        if (!PhysicsSystem.TryGetPhysicsWorld(worldHandle, out var world) || world == null)
            return 0;
        
        var body1 = HandleManager.Get<BepuPhysicsBody>(body1Handle);
        var body2 = HandleManager.Get<BepuPhysicsBody>(body2Handle);
        
        if (body1 == null || body2 == null)
            return 0;
        
        GetLocalPose(localFrame1, out var offsetA, out var orientA);
        GetLocalPose(localFrame2, out var offsetB, out var orientB);
        
        // Weld constraint wiring for Bepu v2 requires matching field names; not wired yet
        throw new NotImplementedException("IPhysicsWorld_AddWeldJoint: weld constraint not wired yet");
    }
    
    /// <summary>
    /// Add a spring joint to the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_AddSpringJoint(IntPtr self, IntPtr body1Ptr, IntPtr body2Ptr, Transform* localFrame1, Transform* localFrame2)
    {
        throw new NotImplementedException("IPhysicsWorld_AddSpringJoint: select and configure an appropriate Bepu constraint (e.g., BallSocket + spring) before enabling");
    }
    
    /// <summary>
    /// Add a revolute joint to the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_AddRevoluteJoint(IntPtr self, IntPtr body1Ptr, IntPtr body2Ptr, Transform* localFrame1, Transform* localFrame2)
    {
        throw new NotImplementedException("IPhysicsWorld_AddRevoluteJoint: hinge constraint not wired yet");
    }
    
    /// <summary>
    /// Add a prismatic joint to the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_AddPrismaticJoint(IntPtr self, IntPtr body1Ptr, IntPtr body2Ptr, Transform* localFrame1, Transform* localFrame2)
    {
        throw new NotImplementedException("IPhysicsWorld_AddPrismaticJoint: slider constraint not wired yet");
    }
    
    /// <summary>
    /// Add a spherical joint to the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_AddSphericalJoint(IntPtr self, IntPtr body1Ptr, IntPtr body2Ptr, Transform* localFrame1, Transform* localFrame2)
    {
        if (self == IntPtr.Zero || body1Ptr == IntPtr.Zero || body2Ptr == IntPtr.Zero)
            return 0;
        
        int worldHandle = (int)self;
        int body1Handle = (int)body1Ptr;
        int body2Handle = (int)body2Ptr;
        
        if (!PhysicsSystem.TryGetPhysicsWorld(worldHandle, out var world) || world == null)
            return 0;
        
        var body1 = HandleManager.Get<BepuPhysicsBody>(body1Handle);
        var body2 = HandleManager.Get<BepuPhysicsBody>(body2Handle);
        
        if (body1 == null || body2 == null)
        return 0;
        
        GetLocalPose(localFrame1, out var offsetA, out _);
        GetLocalPose(localFrame2, out var offsetB, out _);
        
        var desc = new BallSocket
        {
            LocalOffsetA = offsetA,
            LocalOffsetB = offsetB,
            SpringSettings = new SpringSettings(30f, 1f)
        };
        
        var handle = body1.World.Simulation.Solver.Add(body1.Handle, body2.Handle, desc);
        
        int jointHandleInt = Interlocked.Increment(ref _nextJointHandle);
        lock (_joints)
        {
            _joints[jointHandleInt] = new JointData
            {
                Handle = handle,
                BodyAHandle = body1Handle,
                BodyBHandle = body2Handle,
                Kind = JointKind.BallSocket,
                World = body1.World
            };
        }
        
        Console.WriteLine($"[NativeAOT] IPhysicsWorld_AddSphericalJoint: world={worldHandle}, body1={body1Handle}, body2={body2Handle}, joint={jointHandleInt}");
        return jointHandleInt;
    }
    
    /// <summary>
    /// Add a motor joint to the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_AddMotorJoint(IntPtr self, IntPtr body1Ptr, IntPtr body2Ptr, Transform* localFrame1, Transform* localFrame2)
    {
        throw new NotImplementedException("IPhysicsWorld_AddMotorJoint: motor constraint not wired yet");
    }
    
    /// <summary>
    /// Add a wheel joint to the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_AddWheelJoint(IntPtr self, IntPtr body1Ptr, IntPtr body2Ptr, Transform* localFrame1, Transform* localFrame2)
    {
        throw new NotImplementedException("IPhysicsWorld_AddWheelJoint is not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Set collision rules from JSON.
    /// 
    /// **Source 2 behavior**: Sets collision rules from a JSON string describing tag pairs and their collision behavior.
    /// **Emulation behavior**: Parses JSON and stores rules in BepuPhysicsWorld for use in collision callbacks.
    /// </summary>
    /// <param name="self">Handle to the physics world</param>
    /// <param name="rulesPtr">Pointer to UTF-8 JSON string</param>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_SetCollisionRulesFromJson(IntPtr self, IntPtr rulesPtr)
    {
        if (self == IntPtr.Zero || rulesPtr == IntPtr.Zero)
            return;
        
        int worldHandle = (int)self;
        if (!PhysicsSystem.TryGetPhysicsWorld(worldHandle, out var world) || world == null)
        {
            Console.WriteLine($"[NativeAOT] IPhysicsWorld_SetCollisionRulesFromJson: world not found, handle={worldHandle}");
            return;
        }
        
        // Read JSON string from pointer
        string? jsonString = Marshal.PtrToStringUTF8(rulesPtr);
        if (string.IsNullOrEmpty(jsonString))
        {
            Console.WriteLine("[NativeAOT] IPhysicsWorld_SetCollisionRulesFromJson: empty JSON string");
            return;
        }
        
        // Set collision rules in the world
        world.SetCollisionRulesFromJson(jsonString);
        Console.WriteLine($"[NativeAOT] IPhysicsWorld_SetCollisionRulesFromJson: world={worldHandle}, rules set");
    }
    
    /// <summary>
    /// Step the physics simulation.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_StepSimulation(IntPtr self, float flTimestep, int nNumSteps)
    {
        if (self == IntPtr.Zero)
            return;
        
        int worldHandle = (int)self;
        if (!PhysicsSystem.TryGetPhysicsWorld(worldHandle, out var world) || world == null)
            return;
        
        // Step simulation nNumSteps times
        for (int i = 0; i < nNumSteps; i++)
        {
            world.Step(flTimestep);
        }
    }
    
    /// <summary>
    /// Process intersections in the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_ProcessIntersections(IntPtr self, IntPtr callbackPtr)
    {
        // Placeholder: real intersection callbacks are not wired; avoid crashing
        if (self == IntPtr.Zero)
            return;
        // Console.WriteLine("[NativeAOT] IPhysicsWorld_ProcessIntersections: stubbed no-op");
    }
    
    /// <summary>
    /// Destroy an aggregate instance.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_DestroyAggregateInstance(IntPtr self, IntPtr aggregatePtr)
    {
        // No-op stub
        Console.WriteLine("[NativeAOT] IPhysicsWorld_DestroyAggregateInstance: stubbed no-op");
    }
    
    /// <summary>
    /// Create an aggregate instance from a resource name.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_CreateAggregateInstance(IntPtr self, IntPtr resourceNamePtr, Transform* tmStart, ulong nGSNHandle, long nMotionType)
    {
        Console.WriteLine("[NativeAOT] IPhysicsWorld_CreateAggregateInstance: stubbed, returning 0");
        return 0;
    }
    
    /// <summary>
    /// Create an aggregate instance from a model.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_CreateAggregateInstance_1(IntPtr self, IntPtr modelPtr, Transform* tmStart, ulong nGSNHandle, long nMotionType)
    {
        Console.WriteLine("[NativeAOT] IPhysicsWorld_CreateAggregateInstance_1: stubbed, returning 0");
        return 0;
    }
    
    /// <summary>
    /// Set debug scene for the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_SetDebugScene(IntPtr self, IntPtr scenePtr)
    {
        if (self == IntPtr.Zero)
            return;
        
        int worldHandle = (int)self;
        IntPtr scene = scenePtr;
        
        lock (_debugScenes)
        {
            if (scene != IntPtr.Zero)
                _debugScenes[worldHandle] = scene;
            else
                _debugScenes.Remove(worldHandle);
        }
        
        Console.WriteLine($"[NativeAOT] IPhysicsWorld_SetDebugScene: world={worldHandle}, scene=0x{scene.ToInt64():X}");
    }
    
    /// <summary>
    /// Get debug scene for the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_GetDebugScene(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        int worldHandle = (int)self;
        lock (_debugScenes)
        {
            if (_debugScenes.TryGetValue(worldHandle, out var scene))
                return (int)scene;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Draw debug visualization for the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_Draw(IntPtr self, IntPtr debugDrawFcn)
    {
        // No-op for now - debug drawing would require a callback system
        // In the future, this could call the debugDrawFcn for each body/shape
    }
    
    /// <summary>
    /// Get the managed object handle for the physics world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IPhysicsWorld_ManagedObject(IntPtr self)
    {
        if (self == IntPtr.Zero)
            return 0;
        
        // Return the world handle itself (it's already registered with HandleManager)
        return (int)self;
    }
    
    /// <summary>
    /// Query physics world for bodies in a sphere.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_Query(IntPtr self, IntPtr resultPtr, Vector3* vCenter, float flRadius, ushort nObjectSetMask)
    {
        // Stub: no results
        Console.WriteLine("[NativeAOT] IPhysicsWorld_Query: stubbed no-op");
    }
    
    /// <summary>
    /// Query physics world for bodies in a bounding box.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_Query_1(IntPtr self, IntPtr resultPtr, BBox* bounds, ushort nObjectSetMask)
    {
        // Stub: no results
        Console.WriteLine("[NativeAOT] IPhysicsWorld_Query_1: stubbed no-op");
    }
    
    /// <summary>
    /// Query physics world for bodies using point list.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IPhysicsWorld_Query_2(IntPtr self, IntPtr resultPtr, IntPtr pPoints, int nPoints, ushort nObjectSetMask)
    {
        // Stub: no results
        Console.WriteLine("[NativeAOT] IPhysicsWorld_Query_2: stubbed no-op");
    }
}

