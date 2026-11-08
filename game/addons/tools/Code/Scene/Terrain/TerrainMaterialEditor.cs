using System.IO;
using System.Text.Json;

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

		// Which optional images are bound, at a glance: an empty image slot reads like a missing one otherwise
		var channels = new Label( null );
		channels.SetStyles( $"color: {Theme.TextLight.Hex}; padding: 8px 12px;" );
		Layout.Add( channels );
		UpdateChannels( channels, resource );

		var sheet = new ControlSheet();
		sheet.AddObject( Object );
		Layout.Add( sheet );

		Object.OnPropertyChanged += ( p ) =>
		{
			TryAutoFill( p );
			NoteChanged( p );
			UpdateChannels( channels, resource );

			// Cascade to all terrains, feel free to code a TerrainMaterial modified -> TerrainStorage -> Terrain event chain
			UpdateSceneTerrain();
		};
	}

	static bool IsBound( string image ) => !string.IsNullOrEmpty( image ) && image != "materials/default/default_ao.tga";

	static void UpdateChannels( Label label, TerrainMaterial m )
	{
		var roughness = !IsBound( m.RoughnessImage ) ? $"flat {m.Roughness:0.##}"
			: m.RoughnessImage == "materials/default/default_rough.tga" ? $"default image × {m.Roughness:0.##}"
			: $"image × {m.Roughness:0.##}";

		label.Text = $"Roughness: {roughness}    Height: {(IsBound( m.HeightImage ) ? "image" : "none")}    AO: {(IsBound( m.AOImage ) ? "image" : "none")}    Tiling: {(m.NoTiling ? "no tiling" : "repeating")}";
	}

	void TryAutoFill( SerializedProperty p )
	{
		// Only work from albedo for now
		if ( p.Name != nameof( TerrainMaterial.AlbedoImage ) ) return;

		string albedoPath = p.GetValue<string>();

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
			property.SetValue<string>( name );
		}
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
