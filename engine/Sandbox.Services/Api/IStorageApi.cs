using Refit;

namespace Sandbox.Services;

public partial class ServiceApi
{
	/// <summary>
	/// Provides access to player storage, shared records, and file uploads.
	/// </summary>
	[Headers( "Cache-Control: no-cache" )]
	public interface IStorageApi
	{
		/// <summary>
		/// Gets public record amounts and first/last creation times for up to 100 group keys.
		/// </summary>
		[Post( "/package/{package}/storage/{table}/records/summary" )]
		Task<StorageSummary> GetGroupSummaries( string package, string table, [Body] StorageSummaryRequest input );

		/// <summary>
		/// Lists public group records, or only your own records when mine is true.
		/// Continue with the returned NextId as afterId.
		/// </summary>
		[Get( "/package/{package}/storage/{table}/records" )]
		Task<StoragePage> ListRecords( string package, string table, string groupKey = null, bool mine = false, long afterId = 0, int count = 20 );

		/// <summary>
		/// Reads a public record or one owned by the current player.
		/// </summary>
		[Get( "/package/{package}/storage/{table}/records/{id}" )]
		Task<StorageEntry> GetRecord( string package, string table, long id );

		/// <summary>
		/// Creates a player-owned record. An existing key returns conflict.
		/// </summary>
		[Post( "/package/{package}/storage/{table}/records" )]
		Task<StorageEntry> CreateRecord( string package, string table, [Body] StorageCreate input );

		/// <summary>
		/// Changes your record's value and public visibility using its current revision.
		/// </summary>
		[Put( "/package/{package}/storage/{table}/records/{id}" )]
		Task<StorageEntry> UpdateRecord( string package, string table, long id, [Body] StorageUpdate input );

		/// <summary>
		/// Deletes a record as its owner or a moderator using the current revision.
		/// </summary>
		[Delete( "/package/{package}/storage/{table}/records/{id}" )]
		Task<bool> DeleteRecord( string package, string table, long id, [Header( "If-Match" )] string revision );

		/// <summary>
		/// Reads an entry owned by the authenticated player.
		/// </summary>
		[Get( "/package/{package}/storage/{table}/get" )]
		Task<StorageEntry?> Get( long steamid, string package, string table, string key );

		/// <summary>
		/// Sets an entry owned by the authenticated player.
		/// </summary>
		[Post( "/package/{package}/storage/{table}/set" )]
		Task Set( long steamid, string package, string table, string key, string value );

		/// <summary>
		/// Lists entries owned by the authenticated player.
		/// </summary>
		[Get( "/package/{package}/storage/{table}/query" )]
		Task<StorageEntry[]> Query( long steamid, string package, string table, string key, int take );

		/// <summary>
		/// Deletes an entry owned by the authenticated player.
		/// </summary>
		[Post( "/package/{package}/storage/{table}/delete" )]
		Task Delete( long steamid, string package, string table, string key );

		/// <summary>
		/// Deletes the authenticated player's entries in a table.
		/// </summary>
		[Post( "/package/{package}/storage/{table}/drop" )]
		Task Drop( long steamid, string package, string table );

		/// <summary>
		/// Deletes the authenticated player's storage for a package.
		/// </summary>
		[Post( "/package/{package}/storage/wipe" )]
		Task Wipe( long steamid, string package );

		/// <summary>
		/// Starts a file upload for the authenticated player.
		/// </summary>
		[Get( "/storage/upload/{guid}/start" )]
		Task<string> StartUpload( string guid );

		/// <summary>
		/// Completes a file upload for the authenticated player.
		/// </summary>
		[Get( "/storage/upload/{guid}/complete" )]
		Task<string> CompleteUpload( string guid );
	}
}
