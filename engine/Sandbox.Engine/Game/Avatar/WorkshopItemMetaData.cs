namespace Sandbox;

/// <summary>
/// What a workshop item says it is. The item's Steam metadata has what's needed before it's downloaded - so a query
/// says which package it's for without downloading it, and Steam answers from its cache offline. The item ships all
/// of it in its root as <see cref="FileName"/>.
/// </summary>
public struct WorkshopItemMetaData
{
	/// <summary>
	/// What this is called in the root of a workshop item
	/// </summary>
	public const string FileName = "metadata.json";

	public string Title { get; set; }
	public string PackageIdent { get; set; }
	public ulong WorkshopId { get; set; }

	/// <summary>
	/// The package type, e.g. "map"
	/// </summary>
	public string TypeName { get; set; }

	/// <summary>
	/// The revision's meta json. All of it in <see cref="FileName"/>, only what's needed before it's downloaded in the
	/// item's Steam metadata - it can't be any longer than 5000 bytes.
	/// </summary>
	public string Meta { get; set; }

	/// <summary>
	/// The engine's <see cref="Engine.Protocol.Api"/> when it was published - one newer than ours can't be played
	/// </summary>
	public int ApiVersion { get; set; }

	/// <summary>
	/// Read the metadata from the root of an installed workshop item. Null if it doesn't have any.
	/// </summary>
	internal static WorkshopItemMetaData? Read( string folder )
	{
		if ( string.IsNullOrEmpty( folder ) ) return null;

		var path = System.IO.Path.Combine( folder, FileName );
		if ( !System.IO.File.Exists( path ) ) return null;

		try
		{
			return Json.Deserialize<WorkshopItemMetaData>( System.IO.File.ReadAllText( path ) );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Couldn't read {path} ({e.Message})" );
			return null;
		}
	}
}
