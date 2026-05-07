using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	public interface IStorageApi
	{
		[Get( "/storage/get" )]
		Task<StorageEntry?> Get( long steamid, string package, string table, string key );

		[Post( "/storage/set" )]
		Task Set( long steamid, string package, string table, string key, string value );

		[Get( "/storage/query" )]
		Task<StorageEntry[]> Query( long steamid, string package, string table, string key, int take );

		[Post( "/storage/delete" )]
		Task Delete( long steamid, string package, string table, string key );

		[Post( "/storage/drop" )]
		Task Drop( long steamid, string package, string table );

		[Post( "/storage/wipe" )]
		Task Wipe( long steamid, string package );

		[Get( "/storage/upload/start" )]
		Task<string> StartUpload( string guid );

		[Get( "/storage/upload/complete" )]
		Task<string> CompleteUpload( string guid );
	}
}
