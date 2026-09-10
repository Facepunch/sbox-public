namespace Sandbox;

[Expose]
public struct PhysicsLock
{
	public PhysicsLock()
	{

	}

	public bool X { get; set; }
	public bool Y { get; set; }
	public bool Z { get; set; }
	public bool Pitch { get; set; }
	public bool Yaw { get; set; }
	public bool Roll { get; set; }
}


/// <summary>
/// Represents a physics object. An entity can have multiple physics objects. See <see cref="PhysicsGroup">PhysicsGroup</see>.
/// A physics objects consists of one or more <see cref="PhysicsShape">PhysicsShape</see>s.
/// </summary>
internal abstract partial class PhysicsBodyInternal
{
	PhysicsLock _locks;

	public virtual PhysicsLock Locking
	{
		get => _locks;
		set => _locks = value;
	}
}
