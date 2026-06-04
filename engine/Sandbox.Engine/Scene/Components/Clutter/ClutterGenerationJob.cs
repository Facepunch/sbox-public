using static Sandbox.Clutter.ClutterGridSystem;

namespace Sandbox.Clutter;

/// <summary>
/// Defines who owns the generated clutter instances.
/// </summary>
enum ClutterOwnership
{
	/// <summary>
	/// Component owns instances. Models stored in component's Storage, prefabs saved with scene.
	/// Used for volume mode.
	/// </summary>
	Component,

	/// <summary>
	/// GridSystem owns instances. Prefabs are unsaved/hidden, tiles manage cleanup.
	/// Used for infinite streaming mode.
	/// </summary>
	GridSystem
}

/// <summary>
/// Unified job for clutter generation.
/// </summary>
class ClutterGenerationJob
{
	/// <summary>
	/// The clutter definition containing entries and scatterer.
	/// </summary>
	public required ClutterDefinition Clutter { get; init; }

	/// <summary>
	/// Parent GameObject for spawned prefabs.
	/// </summary>
	public required GameObject Parent { get; init; }

	/// <summary>
	/// Bounds to scatter within.
	/// </summary>
	public required BBox Bounds { get; init; }

	/// <summary>
	/// Random seed for deterministic generation.
	/// </summary>
	public required int Seed { get; init; }

	/// <summary>
	/// Who owns the generated instances.
	/// </summary>
	public required ClutterOwnership Ownership { get; init; }

	/// <summary>
	/// Layer for batched model rendering.
	/// </summary>
	public ClutterLayer Layer { get; init; }

	/// <summary>
	/// Tile data for infinite mode (null for volume mode).
	/// </summary>
	public ClutterTile Tile { get; init; }

	/// <summary>
	/// Storage for component-owned model instances
	/// </summary>
	public ClutterStorage Storage { get; init; }

	/// <summary>
	/// Optional list to collect physics bodies created for component-owned (volume) instances.
	/// </summary>
	public List<PhysicsBody> BodyList { get; init; }

	/// <summary>
	/// Optional callback when job completes (for volume mode progress tracking).
	/// </summary>
	public Action OnComplete { get; init; }

	public BBox? LocalBounds { get; init; }
	public Transform? VolumeTransform { get; init; }

	/// <summary>
	/// Execute the generation job.
	/// </summary>
	public void Execute()
	{
		try
		{
			if ( !Parent.IsValid() )
				return;

			int seed = Seed;
			if ( Tile != null )
			{
				Tile.Destroy();
				Layer?.ClearTileModelInstances( Tile.Coordinates );

				seed = Scatterer.GenerateSeed( Tile.SeedOffset, Tile.Coordinates.x, Tile.Coordinates.y );
			}

			var instances = Clutter.Scatterer.HasValue
				? Clutter.Scatterer.Value.Scatter( Bounds, Clutter, seed, Parent.Scene )
				: null;

			if ( LocalBounds.HasValue && VolumeTransform.HasValue )
			{
				var volumeTransform = VolumeTransform.Value;
				var localBounds = LocalBounds.Value;
				instances?.RemoveAll( i => !localBounds.Contains( volumeTransform.PointToLocal( i.Transform.Position ) ) );
			}

			if ( instances is { Count: > 0 } )
				SpawnInstances( instances );

			if ( Tile != null )
			{
				Tile.IsPopulated = true;
				Layer?.OnTilePopulated( Tile );
			}
		}
		finally
		{
			OnComplete?.Invoke();
		}
	}

	internal static PhysicsBody CreateStaticBodyForVolume( Model model, Transform transform, Scene scene )
	{
		return CreateStaticBody( model, transform, scene );
	}

	private static PhysicsBody CreateStaticBody( Model model, Transform transform, Scene scene )
	{
		var world = scene?.PhysicsWorld;
		if ( world == null ) return null;

		var body = new PhysicsBody( world );
		body.BodyType = PhysicsBodyType.Static;
		body.Position = transform.Position;
		body.Rotation = transform.Rotation;

		var local = new Transform( Vector3.Zero, Rotation.Identity, transform.Scale.x );
		foreach ( var part in model.Physics.Parts )
		{
			var partTransform = local.ToWorld( part.Transform );
			foreach ( var sphere in part.Spheres )
				body.AddSphereShape( partTransform.PointToWorld( sphere.Sphere.Center ), sphere.Sphere.Radius * partTransform.UniformScale );
			foreach ( var capsule in part.Capsules )
				body.AddCapsuleShape( partTransform.PointToWorld( capsule.Capsule.CenterA ), partTransform.PointToWorld( capsule.Capsule.CenterB ), capsule.Capsule.Radius * partTransform.UniformScale );
			foreach ( var hull in part.Hulls )
				body.AddShape( hull, partTransform );
			foreach ( var mesh in part.Meshes )
				body.AddShape( mesh, partTransform, false );
		}

		return body;
	}

	private void SpawnInstances( List<ClutterInstance> instances )
	{
		var isComponentOwned = Ownership == ClutterOwnership.Component;
		var tileCoord = Tile?.Coordinates ?? Vector2Int.Zero;

		using ( Parent.Scene.Push() )
		{
			foreach ( var instance in instances )
			{
				if ( instance.IsModel )
				{
					Layer?.AddModelInstance( tileCoord, instance );

					// Component ownership: also store in component's storage for persistence
					if ( isComponentOwned )
					{
						Storage.AddInstance(
							instance.Entry.Model.ResourcePath,
							instance.Transform.Position,
							instance.Transform.Rotation,
							instance.Transform.Scale.x
						);
					}

					// Spawn a static physics body if the model has physics data
					if ( instance.Entry.Model.Physics?.Parts.Count > 0 )
					{
						var body = CreateStaticBody( instance.Entry.Model, instance.Transform, Parent.Scene );
						if ( body != null )
						{
							if ( isComponentOwned )
								BodyList?.Add( body );
							else
								Tile?.AddBody( body );
						}
					}

					continue;
				}

				if ( instance.Entry.Prefab == null )
					continue;

				var obj = instance.Entry.Prefab.Clone( instance.Transform, Parent.Scene );
				obj.Tags.Add( "clutter" );
				obj.SetParent( Parent );

				if ( !isComponentOwned )
				{
					obj.Flags |= GameObjectFlags.NotSaved;
					obj.Flags |= GameObjectFlags.Hidden;
					Tile?.AddObject( obj );
				}
			}
		}
	}
}
