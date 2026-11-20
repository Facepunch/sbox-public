using Sandbox;

/// <summary>Builder payloads use the standard placement, undo, drag and spawn event pipeline.</summary>
public sealed class BuilderSpawner : ISpawner
{
	public string Data { get; }
	public string DisplayName => Piece?.Id ?? "Builder";
	public string Icon => null;
	public BBox Bounds => Model?.Bounds ?? default;
	public bool IsReady => Model is not null && Material is not null && !Material.IsError;
	public Task<bool> Loading { get; }
	public BuilderCatalog.Piece Piece { get; }
	public Model Model { get; private set; }
	public Material Material { get; private set; }

	public static string MakeData( string id, string material ) => $"{id}|{material}";
	public BuilderSpawner( string data )
	{
		Data = data;
		var parts = (data ?? "").Split( '|', 2 );
		Piece = BuilderCatalog.Find( parts[0] );
		Loading = Load( parts.Length == 2 ? parts[1] : BuilderCatalog.DefaultMaterial );
	}

	async Task<bool> Load( string path )
	{
		if ( Piece is null ) return false;
		Model = BuilderGeometry.GetModel( Piece );
		Material = await LoadMaterial( path );
		return IsReady;
	}

	public static Task<Material> LoadMaterial( string path ) => BuilderMaterials.LoadAsync( path );

	public void DrawPreview( Transform transform, Material overrideMaterial )
	{
		if ( !IsReady ) return;
		Game.ActiveScene.DebugOverlay.Model( Model, transform: transform, overlay: false, materialOveride: overrideMaterial ?? Material );
	}

	public Task<List<GameObject>> Spawn( Transform transform, Player player )
	{
		if ( !IsReady ) return Task.FromResult( new List<GameObject>() );
		transform.Position -= transform.Up * Bounds.Mins.z;
		var owner = player.IsValid() ? player.Network.Owner : null;
		var go = BuilderProp.Create( BuilderShapeKey.For( Piece ), Piece.Id, transform, Material, null, collisions: true, frozen: false, owner );
		return Task.FromResult( new List<GameObject> { go } );
	}
}
