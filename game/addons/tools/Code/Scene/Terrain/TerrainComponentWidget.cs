namespace Editor.TerrainEditor;

[CustomEditor( typeof( Terrain ) )]
partial class TerrainComponentWidget : ComponentEditorWidget
{
	public TerrainComponentWidget( SerializedObject obj ) : base( obj )
	{
		SetSizeMode( SizeMode.Default, SizeMode.Default );

		Layout = Layout.Column();
		BuildUI();
	}

	void BuildUI()
	{
		Layout.Clear( true );

		var terrain = SerializedObject.Targets.FirstOrDefault() as Terrain;
		if ( !terrain.IsValid() ) return;

		if ( terrain.Storage is null )
		{
			var storage = new TerrainStorage();
			storage.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };
			terrain.Storage = storage;
			terrain.UpdateMaterialsBuffer();
		}

		Layout.Add( SettingsPage() );
	}
}
