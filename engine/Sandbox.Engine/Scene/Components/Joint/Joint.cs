using System.Text.Json.Serialization;

namespace Sandbox;

[Expose]
public abstract class Joint : Component, Component.ExecuteInEditor
{
	private PhysicsJoint _joint;
	private PhysicsBody _worldBody;
	private GameObject _body;

	private bool started;
	private bool enableCollision;
	private float strength;
	private float angularStrength;

	public enum AttachmentMode
	{
		/// <summary>
		/// Local frames are calculated automatically using this component transform and connected body transform.
		/// </summary>
		Auto,

		/// <summary>
		/// Local frames are set manually. See <see cref="LocalFrame1"/>, <see cref="LocalFrame2"/>
		/// </summary>
		LocalFrames,
	}

	/// <summary>
	/// Are local frames calculated automatically or set manually. See <see cref="LocalFrame1"/>, <see cref="LocalFrame2"/>
	/// </summary>
	[Property, Hide]
	public AttachmentMode Attachment { get; set; }

	/// <summary>
	/// Only used on joint creation. See <see cref="AttachmentMode.LocalFrames"/>
	/// </summary>
	[Property, Hide]
	public Transform LocalFrame1 { get; set; }

	/// <summary>
	/// Only used on joint creation. See <see cref="AttachmentMode.LocalFrames"/>
	/// </summary>
	[Property, Hide]
	public Transform LocalFrame2 { get; set; }

	/// <summary>
	/// Game object to find the body to attach this joint to.
	/// </summary>
	[Property]
	public GameObject Body
	{
		get => _body;
		set
		{
			if ( value == _body )
				return;

			_body = value;

			CreateJoint();
		}
	}

	/// <summary>
	/// Enable or disable collision between the two bodies.
	/// </summary>
	[Property]
	public bool EnableCollision
	{
		get => enableCollision;
		set
		{
			enableCollision = value;
			if ( _joint.IsValid() )
				_joint.Collisions = enableCollision;
		}
	}

	/// <summary>
	/// Is the joint broken on start.
	/// </summary>
	[Property, Group( "Breaking" )]
	public bool StartBroken { get; set; }

	/// <summary>
	/// Strength of the linear constraint. If it takes any more energy than this, it'll break.
	/// </summary>
	[Property, Group( "Breaking" )]
	public float BreakForce
	{
		get => strength;
		set
		{
			strength = value;
			if ( _joint.IsValid() )
				_joint.Strength = value;
		}
	}

	/// <summary>
	/// Strength of the angular constraint. If it takes any more energy than this, it'll break.
	/// </summary>
	[Property, Group( "Breaking" )]
	public float BreakTorque
	{
		get => angularStrength;
		set
		{
			angularStrength = value;
			if ( _joint.IsValid() )
				_joint.AngularStrength = value;
		}
	}

	/// <summary>
	/// Called when the joint breaks.
	/// </summary>
	[Property, Group( "Breaking" )] public Action OnBreak { get; set; }

	/// <summary>
	/// Current linear stress applied to the joint.
	/// </summary>
	[Group( "Breaking" )]
	[Property, JsonIgnore]
	public float LinearStress => _joint.IsValid() ? _joint.LinearImpulse : default;

	/// <summary>
	/// Current angular stress applied to the joint.
	/// </summary>
	[Group( "Breaking" )]
	[Property, JsonIgnore]
	public float AngularStress => _joint.IsValid() ? _joint.AngularImpulse : default;

	/// <summary>
	/// Is the joint currently broken and inactive.
	/// </summary>
	[Group( "Breaking" )]
	[Property, JsonIgnore]
	public bool IsBroken { get; private set; }

	/// <summary>
	/// The source physics body this joint is attached to.
	/// </summary>
	public PhysicsBody Body1 => _joint.IsValid() ? _joint.Body1 : null; // I would like to hide this

	/// <summary>
	/// The source GameObject we're connected to
	/// </summary>
	public GameObject Object1 => Body1?.GameObject; // purposely not named GameObject1 to avoid confusion with Component.GameObject

	/// <summary>
	/// The target physics body this joint is constraining.
	/// </summary>
	public PhysicsBody Body2 => _joint.IsValid() ? _joint.Body2 : null; // I would like to hide this

	/// <summary>
	/// The target GameObject we're connected to
	/// </summary>
	public GameObject Object2 => Body2?.GameObject; // purposely not named GameObject2 to avoid confusion with Component.GameObject

	/// <summary>
	/// A specific point this joint is attached at on <see cref="Body1"/>
	/// </summary>
	public PhysicsPoint Point1
	{
		get => _joint.IsValid() ? _joint.Point1 : default;
		set
		{
			if ( _joint.IsValid() )
				_joint.Point1 = value;
		}
	}

	/// <summary>
	/// A specific point this joint is attached at on <see cref="Body2"/>
	/// </summary>
	public PhysicsPoint Point2
	{
		get => _joint.IsValid() ? _joint.Point2 : default;
		set
		{
			if ( _joint.IsValid() )
				_joint.Point2 = value;
		}
	}

	protected override void OnStart()
	{
		base.OnStart();

		started = true;

		IsBroken = StartBroken;

		CreateJoint();
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();

		started = false;

		DestroyJoint();
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();

		CreateJoint();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		DestroyJoint();
	}

	/// <summary>
	/// Joint type implementation.
	/// </summary>
	protected abstract PhysicsJoint CreateJoint( PhysicsPoint point1, PhysicsPoint point2 );

	internal static PhysicsBody FindPhysicsBody( GameObject go, GameObject source = null )
	{
		if ( go is null ) return null;
		if ( go is Scene ) return null;

		if ( go.Components.TryGet<Rigidbody>( out var rb ) )
		{
			if ( rb.PhysicsBody is not null )
				return rb.PhysicsBody;
		}

		if ( go.Components.TryGet<Collider>( out var collider ) )
		{
			if ( collider.KeyBody is not null )
				return collider.KeyBody;
		}

		return FindPhysicsBody( go.Parent, source );
	}

	private void CreateJoint()
	{
		if ( !started )
			return;

		DestroyJoint();

		if ( IsBroken )
			return;

		var body1 = FindPhysicsBody( GameObject, GameObject );
		if ( !body1.IsValid() )
			return;

		var body2 = FindPhysicsBody( Body, Body );
		if ( !body2.IsValid() )
			body2 = Scene?.PhysicsWorld?.Body;

		if ( !body2.IsValid() )
		{
			// Create a new world reference body if all else fails.
			// This shouldn't be needed when scenes sets the world reference body.
			_worldBody = new PhysicsBody( body1.World );
			body2 = _worldBody;
		}

		var world = WorldTransform;

		// Anchor is this component transform
		var anchor1 = body1.Transform.ToLocal( world );

		// Connected anchor is the connected body transform if it exists, otherwise this component transform
		var anchor2 = Body.IsValid() ? body2.Transform.ToLocal( Body.WorldTransform ) : body2.Transform.ToLocal( world );

		var point1 = new PhysicsPoint( body1, anchor1.Position, anchor1.Rotation );
		var point2 = new PhysicsPoint( body2, anchor2.Position, anchor2.Rotation );

		_joint = CreateJoint( point1, point2 );
		if ( _joint.IsValid() )
		{
			body1?.AddJoint( this );
			body2?.AddJoint( this );

			_joint.Collisions = EnableCollision;
			_joint.Strength = BreakForce;
			_joint.AngularStrength = BreakTorque;
			_joint.OnBreak += Break;
		}
	}

	protected virtual void DestroyJoint()
	{
		_joint?.Body1?.RemoveJoint( this );
		_joint?.Body2?.RemoveJoint( this );

		_joint?.Remove();
		_joint = null;

		_worldBody?.Remove();
		_worldBody = null;
	}

	[Button, Group( "Breaking" ), ShowIf( nameof( IsBroken ), false )]
	public void Break()
	{
		if ( Scene.IsEditor )
			return;

		if ( !_joint.IsValid() )
			return;

		if ( IsBroken )
			return;

		OnBreak?.Invoke();

		DestroyJoint();

		IsBroken = true;
	}

	[Button, Group( "Breaking" ), ShowIf( nameof( IsBroken ), true )]
	public void Unbreak()
	{
		if ( Scene.IsEditor )
			return;

		if ( !IsBroken )
			return;

		IsBroken = false;

		CreateJoint();
	}

	protected override void DrawGizmos()
	{
		if ( !Body.IsValid() )
			return;

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsSelected ? 1.0f : 0.75f );

		var a = Vector3.Zero;
		var b = WorldTransform.PointToLocal( Body.WorldPosition );

		Gizmo.Draw.Sprite( a, 0.2f, Texture.White );
		Gizmo.Draw.Line( a, b );
		Gizmo.Draw.Sprite( b, 0.2f, Texture.White );
	}
}
