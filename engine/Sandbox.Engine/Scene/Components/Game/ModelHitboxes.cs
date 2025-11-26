
namespace Sandbox;

/// <summary>
/// Hitboxes from a model
/// </summary>
[Expose]
[Title( "Hitboxes From Model" )]
[Category( "Game" )]
[Icon( "psychology_alt" )]
public sealed class ModelHitboxes : Component, Component.ExecuteInEditor
{
	HitboxSystem system;

	SkinnedModelRenderer _renderer;

	/// <summary>
	/// The target SkinnedModelRenderer that holds the model/skeleton you want to 
	/// take the hitboxes from.
	/// </summary>
	[Property]
	public SkinnedModelRenderer Renderer
	{
		get => _renderer;
		set
		{
			if ( _renderer == value ) return;

			Clear();

			_renderer = value;

			AddFrom( Renderer );
		}
	}

	GameObject _target;

	/// <summary>
	/// The target GameObject to report in trace hits. If this is unset we'll defaault to the gameobject on which this component is.
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

	protected override void OnAwake()
	{
		Scene.GetSystem( out system );
	}

	protected override void OnEnabled()
	{
		Rebuild();
	}

	protected override void OnDisabled()
	{
		Clear();
	}

	protected override void OnDestroy()
	{
		Clear();
	}

	public void Rebuild()
	{
		Clear();
		AddFrom( Renderer );
	}

	void Clear()
	{
		if ( Renderer.IsValid() )
		{
			Renderer.ModelChanged -= Rebuild;
		}

		foreach ( var h in Hitboxes )
		{
			h.Dispose();
		}

		Hitboxes.Clear();
	}

	private void AddFrom( SkinnedModelRenderer anim )
	{
		if ( system is null ) return;
		if ( !Active ) return;
		if ( !anim.IsValid() ) return;

		anim.ModelChanged += Rebuild;

		if ( anim.Model is null ) return;

		foreach ( var hb in anim.Model.HitboxSet.All )
		{
			if ( hb.Bone is null )
				continue;

			anim.TryGetBoneTransform( hb.Bone, out var tx );

			var body = new PhysicsBody( system.PhysicsWorld );
			PhysicsShape shape = null;

			var hitbox = new Hitbox( Target ?? GameObject, hb.Bone, hb.Tags, body );

			if ( hb.Shape is Sphere sphere )
			{
				shape = body.AddSphereShape( sphere.Center, sphere.Radius );
			}
			else if ( hb.Shape is Capsule capsule )
			{
				shape = body.AddCapsuleShape( capsule.CenterA, capsule.CenterB, capsule.Radius );
				shape.Tags.SetFrom( GameObject.Tags );
			}
			else if ( hb.Shape is BBox box )
			{
				shape = body.AddBoxShape( box.Center, Rotation.Identity, box.Extents );
				shape.Tags.SetFrom( GameObject.Tags );
				hitbox.Bounds = box;
			}

			if ( shape is not null )
			{
				shape.SurfaceMaterial = hb.SurfaceName;
				shape.Tags.SetFrom( GameObject.Tags );
				shape.BoneIndex = hb.Bone.Index;

				body.Transform = tx;
				body.Component = this;

				AddHitbox( hitbox );
			}
			else
			{
				body.Remove();
			}
		}
	}

	public void UpdatePositions()
	{
		if ( Renderer is null ) return;

		foreach ( var hitbox in Hitboxes )
		{
			if ( Renderer.TryGetBoneTransform( hitbox.Bone, out var tx ) )
			{
				hitbox.Body.Transform = tx;
			}
		}
	}

	internal readonly List<Hitbox> Hitboxes = new();

	public void AddHitbox( Hitbox hitbox )
	{
		Hitboxes.Add( hitbox );
	}

	public void RemoveHitbox( Hitbox hitbox )
	{
		Hitboxes.Remove( hitbox );
	}	

	public IReadOnlyList<Hitbox> GetHitboxes() => Hitboxes;

	/// <summary>
	/// The gameobject tags have changed, update collision tags on the target objects
	/// </summary>
	protected override void OnTagsChanged()
	{
		foreach ( var box in Hitboxes )
		{
			if ( box is null ) continue;

			foreach ( var shape in box.Body.Shapes )
			{
				shape.Tags.SetFrom( GameObject.Tags );
			}
		}
	}
}
