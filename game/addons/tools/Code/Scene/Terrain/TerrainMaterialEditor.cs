using Sandbox.Resources;
using System.IO;

namespace Editor.TerrainEditor;

class TerrainMaterialEditor : BaseResourceEditor<TerrainMaterial>
{
	SerializedObject Object;

	public TerrainMaterialEditor()
	{
		Layout = Layout.Column();
	}

	protected override void Initialize( Asset asset, TerrainMaterial resource )
	{
		Layout.Clear( true );

		Object = resource.GetSerialized();

		var sheet = new ControlSheet();
		sheet.AddObject( Object );
		Layout.Add( sheet );

		Object.OnPropertyChanged += ( p ) =>
		{
			TryAutoFill( p );
			NoteChanged( p );

			// Cascade to all terrains, feel free to code a TerrainMaterial modified -> TerrainStorage -> Terrain event chain
			UpdateSceneTerrain();
		};
	}

	void TryAutoFill( SerializedProperty p )
	{
		// Only work from albedo for now
		if ( p.Name != nameof( TerrainMaterial.AlbedoImage ) ) return;

		string albedoPath = GetImagePath( p.GetValue<Texture>() );
		if ( string.IsNullOrWhiteSpace( albedoPath ) ) return;

		var directoryname = Path.GetDirectoryName( albedoPath );
		var filename = Path.GetFileNameWithoutExtension( albedoPath );
		var extension = Path.GetExtension( albedoPath );

		if ( !filename.EndsWith( "_color" ) ) return;

		string baseName = filename.Remove( filename.Length - "_color".Length );

		var tries = new List<(string, string)>()
		{
			( "RoughnessImage", "_rough" ),
			( "RoughnessImage", "_roughness" ),
			( "NormalImage", "_normal" ),
			( "NormalImage", "_normalgl" ),
			( "HeightImage", "_displacement" ),
			( "HeightImage", "_height" ),
			( "AOImage", "_ao" ),
			( "AOImage", "_ambientocclusion" ),
		};

		foreach ( var a in tries )
		{
			var name = $"{directoryname}/{baseName}{a.Item2}{extension}".Replace( "\\", "/" );

			if ( !FileSystem.Content.FileExists( name ) ) continue;

			var property = Object.GetProperty( a.Item1 );
			property.SetValue( CreateImageTexture( name ) );
		}
	}

	/// <summary>
	/// The image file a source image slot was set up from, if it came from one.
	/// </summary>
	static string GetImagePath( Texture texture )
	{
		if ( texture?.EmbeddedResource is not { } embedded ) return null;
		if ( embedded.ResourceGenerator != "imagefile" ) return null;

		return embedded.Data?["FilePath"]?.GetValue<string>();
	}

	static Texture CreateImageTexture( string filePath )
	{
		var generator = new ImageFileGenerator { FilePath = filePath };
		return generator.Create( ResourceGenerator.Options.Default );
	}

	void UpdateSceneTerrain()
	{
		var terrains = SceneEditorSession.All
			.SelectMany( scene => scene.Scene.GetAllComponents<Terrain>() );

		foreach ( var terrain in terrains )
		{
			terrain.UpdateMaterialsBuffer();
		}
	}
}
