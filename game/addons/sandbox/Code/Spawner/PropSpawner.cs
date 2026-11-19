/// <summary>
/// Payload for spawning a prop model from a cloud ident.
/// </summary>
public sealed class PropSpawner : ISpawner
{
	public string DisplayName { get; private set; }
	public string FullIdent => Path is not null && !Path.EndsWith( ".vmdl" ) && !Path.EndsWith( ".vmdl_c" ) ? Path : null;
	public string Icon => Path;
	public string Data => Path;
	public BBox Bounds => Model?.Bounds ?? default;
	public bool IsReady => Model is not null && !Model.IsError;
	public Task<bool> Loading { get; }

	public Model Model { get; private set; }
	public string Path { get; }

	public PropSpawner( string path )
	{
		Path = path;
		DisplayName = null;
		Loading = LoadAsync();
	}

	private async Task<bool> LoadAsync()
	{
		// Try local/installed first, then fall back to cloud
		if ( Path.EndsWith( ".vmdl" ) || Path.EndsWith( ".vmdl_c" ) )
		{
			Model = await ResourceLibrary.LoadAsync<Model>( Path );
			if ( Model is not null )
			{
				DisplayName = LocalProps.All.FirstOrDefault( entry =>
					string.Equals( entry.Path, Path.TrimStart( '/' ), StringComparison.OrdinalIgnoreCase ) )?.Title ?? Model.ResourceName;
				return true;
			}
		}

		Model = await Cloud.Load<Model>( Path );

		if ( Model is not null )
		{
			DisplayName = Model.ResourceName ?? DisplayName;
		}

		return IsReady;
	}

	public void DrawPreview( Transform transform, Material overrideMaterial )
	{
		if ( !IsReady ) return;

		Game.ActiveScene.DebugOverlay.Model( Model, transform: transform, overlay: false, materialOveride: overrideMaterial );
	}

	public Task<List<GameObject>> Spawn( Transform transform, Player player )
	{
		var depth = -Bounds.Mins.z;
		transform.Position += transform.Up * depth;

		var go = new GameObject( false, "prop" );
		go.Tags.Add( "removable" );
		go.WorldTransform = transform;

		var prop = go.AddComponent<Prop>();
		prop.Model = Model;

		if ( player.IsValid() )
			Ownable.Set( go, player.Network.Owner );

		if ( (Model.Physics?.Parts?.Count ?? 0) == 0 )
		{
			var collider = go.AddComponent<BoxCollider>();
			collider.Scale = Model.Bounds.Size;
			collider.Center = Model.Bounds.Center;
			go.AddComponent<Rigidbody>();
		}

		go.NetworkSpawn( true, null );

		return Task.FromResult( new List<GameObject> { go } );
	}
}
