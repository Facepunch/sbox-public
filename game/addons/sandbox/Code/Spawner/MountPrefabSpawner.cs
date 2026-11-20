/// <summary>
/// Spawns an entity prefab a mounted game builds, such as mount://hl2/entities/npcs/combine_soldier.prefab, with the
/// same rules as a scripted entity (see <see cref="EntitySpawner.SpawnPrefab"/>). See <see cref="MountedPrefabs"/>.
/// </summary>
public sealed class MountPrefabSpawner : ISpawner
{
	public string DisplayName { get; private set; }
	public string FullIdent => $"mountprefab:{Path}";
	public string Icon => Path;
	public string Data => Path;
	public BBox Bounds { get; private set; }
	public bool IsReady => PrefabFile is not null;
	public Task<bool> Loading { get; }
	public GameObject Prefab => PrefabFile is not null ? GameObject.GetPrefab( Path ) : null;

	public PrefabFile PrefabFile { get; private set; }
	public string Path { get; }

	public MountPrefabSpawner( string path )
	{
		Path = path;
		DisplayName = MountedPrefabs.Title( path );
		Loading = LoadAsync();
	}

	private async Task<bool> LoadAsync()
	{
		if ( !Sandbox.Mounting.MountUtility.TryParse( Path, out string ident ) )
			return false;

		var mount = await Sandbox.Mounting.Directory.Mount( ident );
		if ( mount?.IsMounted != true || !MountedPrefabs.IsListed( Path ) )
		{
			Log.Warning( $"{DisplayName} requires '{ident}' installed on the host." );
			return false;
		}

		PrefabFile = MountedPrefabs.Load( Path );
		if ( PrefabFile is null )
			return false;

		DisplayName = MountedPrefabs.Title( Path );

		// Its renderers stay empty until the mount loads their models, the preview model gives the size
		var scene = SceneUtility.GetPrefabScene( PrefabFile );
		Bounds = scene.GetLocalBounds();
		if ( PrefabFile.GetMetadata( "PreviewModel" ) is { Length: > 0 } preview && await Model.LoadAsync( preview ) is { IsError: false } model )
			Bounds = EntitySpawner.MountedBounds( scene, model );

		return true;
	}

	public void DrawPreview( Transform transform, Material overrideMaterial )
	{
		if ( !IsReady ) return;
		EntitySpawner.DrawBounds( Bounds, transform, overrideMaterial );
	}

	public Task<List<GameObject>> Spawn( Transform transform, Player player ) => Task.FromResult( EntitySpawner.SpawnPrefab( PrefabFile, Bounds, transform, player ) );

	public void PopulateContextMenu( Sandbox.UI.Menu menu, string ident )
	{
		if ( Prefab?.GetComponent<BaseCombatWeapon>( true ) is not null )
			menu.AddOption( "#spawner.spawn_in_world", "public", () => GameManager.Spawn( ident, null, forceWorld: true ) );
	}
}
