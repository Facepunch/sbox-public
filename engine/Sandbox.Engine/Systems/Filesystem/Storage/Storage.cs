namespace Sandbox;

public static partial class Storage
{
	/// <summary>
	/// Create a new storage entry of the given type.
	/// </summary>
	/// <param name="type">A name to categorize this type as. For example "dupe" or "save"</param>
	/// <returns>A new Entry</returns>
	public static Entry CreateEntry( string type )
	{
		return new Entry( type );
	}

	/// <summary>
	/// Create a storage entry from an existing filesystem. This is used when downloading workshop entries.
	/// </summary>
	internal static Entry CreateEntryFromFileSystem( BaseFileSystem fs )
	{
		try
		{
			return new Entry( fs );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, "Exception when creating StorageContent from workshop" );
			return null;
		}
	}

	/// <summary>
	/// This is what is saved to the .meta file
	/// </summary>
	internal class StorageMeta
	{
		public string Id { get; set; }
		public string Type { get; set; }
		public DateTimeOffset Timestamp { get; set; }
		public Dictionary<string, string> Meta { get; set; }
	}

	/// <summary>
	/// This matches ERemoteStoragePublishedFileVisibility in native
	/// </summary>
	[Expose]
	public enum Visibility
	{
		[Title( "Public" ), Icon( "public" ), Description( "Anyone can find it" )]
		Public = 0,

		[Title( "Friends Only" ), Icon( "group" ), Description( "Only your Steam friends can see or download it" )]
		FriendsOnly = 1,

		[Title( "Private" ), Icon( "lock" ), Description( "Only you can see or download it" )]
		Private = 2,

		[Title( "Unlisted" ), Icon( "link" ), Description( "Anyone can download it, but it isn't listed" )]
		Unlisted = 3,
	}

}

