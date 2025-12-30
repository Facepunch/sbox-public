
namespace Editor.RectEditor;

public class RectAssetData
{
	public class Properties
	{
		public bool AllowRotation { get; set; }
		public bool AllowTiling { get; set; }
	}

	public class Subrect
	{
		public int[] Min { get; set; }
		public int[] Max { get; set; }

		public Properties Properties { get; set; }
	};

	public class SubrectSet
	{
		public string Name { get; set; }
		public List<Subrect> Rectangles { get; set; }
	};

	public List<SubrectSet> RectangleSets { get; set; }
	public Settings Settings { get; set; } = new Settings();

	public static RectAssetData Find( Asset asset )
	{
		if ( asset.AssetType == AssetType.Material )
		{
			asset = AssetSystem.FindByPath( asset.FindStringEditInfo( "SubrectDefinition" ) );
			if ( asset is null )
				return null;
		}
		else if ( asset.AssetType.FileExtension != "rect" )
		{
			return null;
		}

		var path = asset.GetSourceFile( true );
		if ( !System.IO.File.Exists( path ) )
			return null;

		var txt = System.IO.File.ReadAllText( path );
		if ( string.IsNullOrWhiteSpace( txt ) )
			return null;

		if ( txt.First() == '<' )
			txt = EditorUtility.KeyValues3ToJson( txt );

		if ( string.IsNullOrWhiteSpace( txt ) )
			return null;

		return Json.Deserialize<RectAssetData>( txt );
	}
}
