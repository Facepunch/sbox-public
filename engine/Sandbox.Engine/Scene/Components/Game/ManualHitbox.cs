namespace Sandbox;

/// <summary>
/// A hitbox that can be placed manually on a GameObject, instead of coming from a model
/// </summary>
[Expose]
[Title( "Hitbox" )]
[Category( "Game" )]
[Icon( "psychology_alt" )]
public sealed class ManualHitbox : Component, Component.ExecuteInEditor
{
	internal Hitbox Hitbox { get; private set; }

	HitboxSystem system;
	GameObject _target;

	/// <summary>
	/// The target GameObject to report in trace hits. If this is unset we'll default to the gameobject on which this component is.
	/// </summary>
	[Property]
	public GameObject Target
	{
		get => _target;
		set
		{
			if ( _target == value ) return;

			_target = value;
			Rebuild();
		}
	}

	public enum HitboxShape
	{
		Sphere,
		Capsule,
		Box,
		Cylinder
	}

	[Property] public HitboxShape Shape { get; set; } = HitboxShape.Sphere;
	[Property, HideIf( nameof( Shape ), HitboxShape.Box )] public float Radius { get; set; } = 10.0f;
	[Property] public Vector3 CenterA { get; set; }
	[Property, HideIf( nameof( Shape ), HitboxShape.Sphere )] public Vector3 CenterB { get; set; }
	[Property] public TagSet HitboxTags { get; set; } = new();

	protected override void OnAwake()
	{
		Scene.GetSystem( out system );
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		if ( Shape == HitboxShape.Sphere )
		{
			Gizmo.Draw.LineSphere( CenterA, Radius );
		}

		if ( Shape == HitboxShape.Capsule )
		{
			Gizmo.Draw.LineCapsule( new( CenterA, CenterB, Radius ) );
		}

		if ( Shape == HitboxShape.Box )
		{
			Gizmo.Draw.LineBBox( BBox.FromPositionAndSize( CenterA, CenterB ) );
		}

		if ( Shape == HitboxShape.Cylinder )
		{
			Gizmo.Draw.LineCylinder( CenterA, CenterB, Radius, Radius, 16 );
		}
	}

	protected override void OnDirty()
	{
		Rebuild();
	}

	protected override void OnEnabled()
	{
		Rebuild();
	}

	protected override void OnDisabled()
	{
		Hitbox?.Dispose();
		Hitbox = null;
	}

	public void Rebuild()
	{
		if ( system is null )
			return;

		Hitbox?.Dispose();
		Hitbox = null;

		var body = new PhysicsBody( system.PhysicsWorld );
		var tx = WorldTransform;

		PhysicsShape shape = null;

		Hitbox = new Hitbox( Target ?? GameObject, HitboxTags, body );

		if ( Shape == HitboxShape.Sphere )
		{
			shape = body.AddSphereShape( CenterA * tx.Scale, Radius * tx.UniformScale );
		}
		else if ( Shape == HitboxShape.Capsule )
		{
			shape = body.AddCapsuleShape( CenterA * tx.Scale, CenterB * tx.Scale, Radius * tx.UniformScale );
		}
		else if ( Shape == HitboxShape.Box )
		{
			shape = body.AddBoxShape( CenterA, Rotation.Identity, CenterB * 0.5f );
			Hitbox.Bounds = new( CenterA, CenterB );
		}
		else if ( Shape == HitboxShape.Cylinder )
		{
			var axis = CenterB - CenterA;
			var height = axis.Length;
			var position = (CenterA + CenterB) * 0.5f;
			var rotation = Rotation.LookAt( axis.Normal, Vector3.Up );
			shape = body.AddCylinderShape( position, rotation, height, Radius );
			Hitbox.Bounds = new( CenterA, CenterB );
		}

		if ( shape is not null )
		{
			shape.Tags.SetFrom( GameObject.Tags );

			body.Transform = tx.WithScale( 1 );
			body.Component = this;
		}
		else
		{
			Hitbox?.Dispose();
			Hitbox = null;
		}
	}

	public void UpdatePositions()
	{
		if ( Hitbox is null ) return;

		Hitbox.Body.Transform = WorldTransform;
	}

	/// <summary>
	/// Tags have been updated
	/// </summary>
	protected override void OnTagsChanged()
	{
		OnPropertyDirty();
	}

}


