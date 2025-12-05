using BepuPhysics;
using System.Numerics;

namespace Sandbox.Engine.Emulation.Physics;

public class BepuPhysicsBody
{
    public BodyHandle Handle { get; }
    public BepuPhysicsWorld World { get; }

    public BepuPhysicsBody(BepuPhysicsWorld world, BodyHandle handle)
    {
        World = world;
        Handle = handle;
    }

    public Vector3 GetPosition()
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        return bodyRef.Pose.Position;
    }

    public void SetPosition(Vector3 pos)
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        bodyRef.Pose.Position = pos;
    }

    public System.Numerics.Quaternion GetOrientation()
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        return bodyRef.Pose.Orientation;
    }

    public void SetOrientation(System.Numerics.Quaternion orientation)
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        bodyRef.Pose.Orientation = orientation;
    }

    public Vector3 GetLinearVelocity()
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        return bodyRef.Velocity.Linear;
    }

    public void SetLinearVelocity(Vector3 velocity)
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        bodyRef.Velocity.Linear = velocity;
    }

    public Vector3 GetAngularVelocity()
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        return bodyRef.Velocity.Angular;
    }

    public void SetAngularVelocity(Vector3 velocity)
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        bodyRef.Velocity.Angular = velocity;
    }

    public float GetMass()
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        return 1.0f / bodyRef.LocalInertia.InverseMass;
    }

    public void SetMass(float mass)
    {
        var bodyRef = World.Simulation.Bodies.GetBodyReference(Handle);
        bodyRef.LocalInertia.InverseMass = 1.0f / mass;
    }

    public void Enable()
    {
        World.Simulation.Awakener.AwakenBody(Handle);
    }

    public void Disable()
    {
        // Le sommeil est automatique dans BepuPhysics, pas de méthode directe.
    }
}
