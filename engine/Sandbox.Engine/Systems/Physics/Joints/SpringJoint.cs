namespace Sandbox.Physics;

/// <summary>
/// A rope-like constraint that is has springy/bouncy.
/// </summary>
public partial class SpringJoint : PhysicsJoint
{
	internal SpringJoint( PhysicsJointInternal joint ) : base( joint ) { }

	/// <summary>
	/// How springy and tight the joint will be
	/// </summary>
	public PhysicsSpring SpringLinear
	{
		get => _joint?.SpringLinear ?? default;
		set => _joint?.SpringLinear = value;
	}

	/// <summary>
	/// Maximum length it should be allowed to go
	/// </summary>
	public float MaxLength
	{
		get => _joint?.MaxLength ?? 0;
		set => _joint?.MaxLength = value;
	}

	/// <summary>
	/// Minimum length it should be allowed to go. At which point it acts a bit like a rod.
	/// </summary>
	public float MinLength
	{
		get => _joint?.MinLength ?? 0;
		set => _joint?.MinLength = value;
	}

	/// <summary>
	/// Maximum force it should be allowed to go. Set to zero to only allow stretching.
	/// </summary>
	public float MaxForce
	{
		get => _joint?.MaxForce ?? 0;
		set => _joint?.MaxForce = value;
	}

	/// <summary>
	/// Minimum force it should be allowed to go.
	/// </summary>
	public float MinForce
	{
		get => _joint?.MinForce ?? 0;
		set => _joint?.MinForce = value;
	}

	[Obsolete( "doesn't exist, not used" )]
	public float ReferenceMass
	{
		get => default;
		set { }
	}
}
