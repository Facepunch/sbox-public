using Facepunch.ActionGraphs;
using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// A prefab which is loaded and accessible via GameObject.GetPrefab( x )
/// </summary>
internal partial class PrefabCacheScene : PrefabScene
{
	internal PrefabCacheScene() : base( false )
	{
	}

	/// <summary>
	/// Contains the the JSON for the prefab after loading it's cached scene and expanding all prefab instances.
	/// We cache this since we use this quite often to resolve nested prefab instance overrides.
	/// </summary>
	internal JsonObject FullPrefabInstanceJson { get; set; }

	/// <summary>
	/// Contains all the prefab files that are referenced by this prefab scene.
	/// </summary>
	private HashSet<PrefabFile> referencedPrefabs = new();

	public override bool Load( GameResource resource )
	{
		Assert.NotNull( resource );

		Clear();

		if ( resource is not PrefabFile file )
		{
			Log.Warning( "Resource is not a PrefabFile" );
			return false;
		}

		Source = file;

		using var sourceScope = ActionGraph.PushSerializationOptions( file.SerializationOptions with { ForceUpdateCached = IsEditor } );
		using var sceneScope = Push();

		if ( file.RootObject is null )
		{
			file.RootObject = new GameObject( file.ResourceName ).Serialize();
			// Need to clear again because GO will be added to the scene, leading to id conflicts when deserializing it.
			Clear();
		}

		using ( CallbackBatch.Isolated() )
		{
			Deserialize( file.RootObject );
		}

		FullPrefabInstanceJson = Serialize( new SerializeOptions { SerializePrefabForDiff = true } );

		// Iterate all gameobjects in scene and find prefab instances, add them to reference set
		referencedPrefabs = GetAllObjects( false ).Where( o => o.IsPrefabInstanceRoot ).Select( p => ResourceLibrary.Get<PrefabFile>( p.PrefabInstanceSource ) ).ToHashSet();

		return true;
	}

	/// <summary>
	/// Don't try and do this. You can't destroy a PrefabCacheScene.
	/// </summary>
	public override void Destroy()
	{
		throw new InvalidOperationException( $"Destroying a {nameof( PrefabCacheScene )} is not allowed. Did you mean to destroy the GameObject instance?" );
	}

	internal void Refresh( PrefabFile file )
	{
		Load( file );
		UpdateDependencies( file );
	}

	private void UpdateDependencies( PrefabFile file )
	{
		var dependantSet = new HashSet<PrefabFile>();
		BuildDependantSet( file, dependantSet );

		foreach ( var dependant in dependantSet )
		{
			dependant.CachedScene?.Load( dependant );
		}
	}

	private void BuildDependantSet( PrefabFile file, HashSet<PrefabFile> prefabScenesRequiringUpdate )
	{
		var prefabFiles = ResourceLibrary.GetAll<PrefabFile>().ToArray();

		foreach ( var pf in prefabFiles )
		{
			if ( prefabScenesRequiringUpdate.Contains( pf ) )
			{
				continue;
			}

			// Only check prefabs that already have a cached scene loaded.
			// Prefabs without a cached scene will load fresh data when first accessed,
			// so we don't need to force-load them just to check dependencies.
			if ( pf.CachedScene is not PrefabCacheScene prefabScene )
			{
				continue;
			}

			if ( !prefabScene.IsValid() )
			{
				Log.Warning( $"Failed to update prefab dependencies, prefab {pf.ResourceName} is not valid" );
				continue;
			}

			if ( prefabScene == this )
			{
				continue;
			}

			if ( !prefabScene.referencedPrefabs.Contains( file ) )
			{
				continue;
			}

			prefabScenesRequiringUpdate.Add( pf );

			BuildDependantSet( pf, prefabScenesRequiringUpdate );
		}
	}
}


