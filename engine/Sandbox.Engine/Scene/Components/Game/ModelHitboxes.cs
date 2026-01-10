
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

	/// <summary>
	/// The target SkinnedModelRenderer that holds the model/skeleton you want to 
	/// take the hitboxes from.
	/// </summary>
	[Property]
	public SkinnedModelRenderer Renderer
	{
		get;
		set
		{
			if ( field == value ) return;

			Clear();

			field = value;

			AddFrom( Renderer );
		}
	}

	/// <summary>
	/// The target GameObject to report in trace hits. If this is unset we'll defaault to the gameobject on which this component is.
	/// </summary>
	[Property]
	public GameObject Target
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

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

	/// <summary>
	/// Invoked when the hitboxes have been rebuilt.
	/// </summary>
	public Action HitboxesRebuilt;

	public void Rebuild()
	{
		Clear();
		AddFrom( Renderer );
		HitboxesRebuilt?.Invoke();
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

	/// <summary>
	/// Adds a hitbox to the models hitbox list
	/// </summary>
	/// <param name="hitbox"></param>
	public void AddHitbox( Hitbox hitbox )
	{
		Hitboxes.Add( hitbox );
	}

	/// <summary>
	/// Removes a hitbox from the models hitbox list.
	/// </summary>
	/// <param name="hitbox"></param>
	public void RemoveHitbox( Hitbox hitbox )
	{
		Hitboxes.Remove( hitbox );
	}

	/// <summary>
	/// Returns every hitbox on this model.
	/// </summary>
	/// <returns></returns>
	public IReadOnlyList<Hitbox> GetHitboxes() => Hitboxes;

	/// <summary>
	/// Retrieves the hitbox associated with the bone index.
	/// </summary>
	/// <param name="boneIndex">The bone index to retrieve.</param>
	/// <returns>null if no matching hitbox is found.</returns>
	public Hitbox GetHitbox( int boneIndex ) => Hitboxes.FirstOrDefault( x => x.Bone.Index == boneIndex );

	/// <summary>
	/// Retrieves the hitbox associated with the specified bone.
	/// </summary>
	/// <param name="bone">The bone for which to find the corresponding hitbox.</param>
	/// <returns>null if no matching hitbox is found.</returns>
	public Hitbox GetHitbox( BoneCollection.Bone bone ) => Hitboxes.FirstOrDefault( x => x.Bone == bone );

	/// <summary>
	/// Retrieves the hitbox associated with the specified bone name.
	/// </summary>
	/// <param name="boneName">The name of the bone for which to retrieve the hitbox.</param>
	/// <returns>null if no matching hitbox is found.</returns>
	public Hitbox GetHitbox( string boneName ) => Hitboxes.FirstOrDefault( x => x.Bone.Name == boneName );

	/// <summary>
	/// Retrieves the hitbox associated with the specified physics body.
	/// </summary>
	/// <param name="physicsBody">The physics body for which to find the corresponding hitbox.</param>
	/// <returns>null if no matching hitbox is found.</returns>
	public Hitbox GetHitbox( PhysicsBody physicsBody ) => Hitboxes.FirstOrDefault( x => x.Body == physicsBody );

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
