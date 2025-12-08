using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using System.Numerics;

namespace Sandbox.Engine.Emulation.Physics;

/// <summary>
/// Narrow phase callbacks for BepuPhysics collision detection.
/// Must be unmanaged for NativeAOT compatibility.
/// </summary>
public unsafe struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    // Dummy field to ensure struct has non-zero size and is unmanaged
    // NativeAOT requires structs to be unmanaged (only primitive types)
    private byte _dummy;
    
    public void Initialize(Simulation simulation)
    {
        // Note: Structs are passed by value, so we can't store simulation here.
        // Instead, we use BepuPhysicsWorld.GetWorldFromCollidable() in each method.
    }
    
    /// <summary>
    /// Allow contact generation between two collidables.
    /// Uses collision rules from BepuPhysicsWorld to filter collisions based on tags.
    /// 
    /// WARNING: This method is called from native code (BepuPhysics). 
    /// Avoid complex managed operations that might cause marshaling issues in NativeAOT.
    /// </summary>
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        // For now, always allow contact generation to avoid crashes
        // TODO: Re-enable collision rule checking once NativeAOT marshaling issues are resolved
        return true;
        
        /* Disabled due to NativeAOT marshaling issues:
        try
        {
            // Get the world from one of the collidables (they should be in the same world)
            var world = BepuPhysicsWorld.GetWorldFromCollidable(a) ?? BepuPhysicsWorld.GetWorldFromCollidable(b);
            if (world == null)
                return true; // Default: allow all contacts if world not found
            
            // Get collision rule for these two collidables
            var rule = world.GetCollisionRuleForCollidables(a, b);
            
            // Ignore: no contact generation
            if (rule == BepuPhysicsWorld.CollisionRuleResult.Ignore)
                return false;
            
            // Collide and Trigger: allow contact generation
            // Note: The difference between Collide and Trigger is handled elsewhere (in contact response)
            return true;
        }
        catch
        {
            // If anything goes wrong, default to allowing contact (safe fallback)
            return true;
        }
        */
    }
    
    /// <summary>
    /// Allow contact generation for child collidables in compound shapes.
    /// Uses collision rules from BepuPhysicsWorld.
    /// 
    /// WARNING: This method is called from native code (BepuPhysics).
    /// Avoid complex managed operations that might cause marshaling issues in NativeAOT.
    /// </summary>
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
    {
        // For now, always allow contact generation to avoid crashes
        // TODO: Re-enable collision rule checking once NativeAOT marshaling issues are resolved
        return true;
        
        /* Disabled due to NativeAOT marshaling issues:
        try
        {
            // Get the collidables from the pair
            var a = pair.A;
            var b = pair.B;
            
            // Get the world from one of the collidables
            var world = BepuPhysicsWorld.GetWorldFromCollidable(a) ?? BepuPhysicsWorld.GetWorldFromCollidable(b);
            if (world == null)
                return true;
            
            var rule = world.GetCollisionRuleForCollidables(a, b);
            return rule != BepuPhysicsWorld.CollisionRuleResult.Ignore;
        }
        catch
        {
            // If anything goes wrong, default to allowing contact (safe fallback)
            return true;
        }
        */
    }
    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial.FrictionCoefficient = 1f;
        pairMaterial.MaximumRecoveryVelocity = 2f;
        pairMaterial.SpringSettings = new SpringSettings(30, 1);
        return true;
    }
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;
    public void Dispose() { }
}

public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;
    private Vector3Wide gravityWide;
    public PoseIntegratorCallbacks(Vector3 gravity)
    {
        Gravity = gravity;
        gravityWide = default;
    }
    public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public bool AllowSubstepsForUnconstrainedBodies => false;
    public bool IntegrateVelocityForKinematics => false;
    public void Initialize(Simulation simulation) { }
    public void PrepareForIntegration(float dt)
    {
        // Prépare la version "wide" du vecteur gravité pour SIMD
        Vector3Wide.Broadcast(Gravity, out gravityWide);
    }
    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        // Utilise Vector3Wide.Scale pour appliquer la gravité à chaque lane
        Vector3Wide.Scale(gravityWide, dt, out var gravityDt);
        velocity.Linear += gravityDt;
    }
}
