using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Sandbox.Services;

/// <summary>
/// Player-owned cloud records, organized into tables scoped to the calling package.
/// </summary>
public static class Database
{
	/// <summary>
	/// Access a database table owned by the calling package.
	/// Table names are case-sensitive and limited to 64 characters. Writes require a signed-in,
	/// eligible player; public records can be read by anyone.
	/// </summary>
	/// <param name="table">The table name within the calling package.</param>
	[MethodImpl( MethodImplOptions.NoInlining )]
	public static Table GetTable( string table )
	{
		var package = AssemblyMetadata.GetPackageIdent( Assembly.GetCallingAssembly() ) ?? Application.GameIdent;
		if ( string.IsNullOrWhiteSpace( package ) )
		{
			throw new InvalidOperationException( "Database access requires a calling package or an active game." );
		}

		return new Table( package, table, Backend.Storage ?? throw new InvalidOperationException( "The backend is not initialized." ) );
	}

	/// <summary>
	/// A database table. Operations always fetch current data and never cache records.
	/// </summary>
	public sealed class Table
	{
		readonly ServiceApi.IStorageApi api;

		/// <summary>
		/// The package that owns this table.
		/// </summary>
		public string Package { get; }

		/// <summary>
		/// The case-sensitive name of this table.
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// Creates a handle to a package's table.
		/// </summary>
		/// <param name="package">The package that owns the table.</param>
		/// <param name="name">The table name.</param>
		/// <param name="api">The backend storage client.</param>
		internal Table( string package, string name, ServiceApi.IStorageApi api )
		{
			ArgumentException.ThrowIfNullOrWhiteSpace( package );
			ArgumentException.ThrowIfNullOrWhiteSpace( name );
			if ( name.Length > 64 || name != name.Trim() || name.Any( char.IsControl ) )
			{
				throw new ArgumentException( "Table names must have at most 64 characters, without control characters or surrounding whitespace.", nameof( name ) );
			}

			Package = package;
			Name = name;
			this.api = api;
		}

		/// <summary>
		/// Read a public record or one owned by the signed-in player. Missing or inaccessible records fail with status 404.
		/// </summary>
		/// <param name="id">The stable ID of the record to read.</param>
		public async Task<Record> Get( long id )
		{
			return new Record( this, await Request( api.GetRecord( Package, Name, id ) ) );
		}

		/// <summary>
		/// Create a record without overwriting an existing key (status 409 on duplicate).
		/// Keys and optional immutable group keys are case-sensitive, up to 128 characters.
		/// Records are private by default. Public records can be read by anyone; only the owner can update them.
		/// </summary>
		/// <param name="key">A key unique within your package and table, up to 128 characters, regardless of group.</param>
		/// <param name="value">The text to store, up to 65536 UTF-8 bytes.</param>
		/// <param name="group">An optional immutable group label, up to 128 characters.</param>
		/// <param name="isPublic">Whether other players can read this record. Defaults to false.</param>
		public async Task<Record> Create( string key, string value, string group = null, bool isPublic = false )
		{
			return new Record( this, await Request( api.CreateRecord( Package, Name, new StorageCreate
			{
				Key = key,
				Value = value,
				GroupKey = group,
				PublicRead = isPublic
			} ) ) );
		}

		/// <summary>
		/// Get public records in one group. A null group selects ungrouped records, not every group.
		/// Page size must be 1–100. Pass NextCursor as cursor with the same group to get the next page.
		/// </summary>
		/// <param name="group">The group to list. Null selects only ungrouped records.</param>
		/// <param name="cursor">The previous page’s NextCursor, or zero for the first page.</param>
		/// <param name="pageSize">The maximum number of records to return, from 1 to 100.</param>
		public async Task<Page> List( string group = null, long cursor = 0, int pageSize = 20 )
		{
			var result = await Request( api.ListRecords( Package, Name, group, false, cursor, pageSize ) );
			return CreatePage( result );
		}

		/// <summary>
		/// Get your records in one group, including private records. A null group selects ungrouped records.
		/// Page size must be 1–100. Pass NextCursor as cursor with the same group to get the next page.
		/// </summary>
		/// <param name="group">The group to list. Null selects only ungrouped records.</param>
		/// <param name="cursor">The previous page’s NextCursor, or zero for the first page.</param>
		/// <param name="pageSize">The maximum number of records to return, from 1 to 100.</param>
		public async Task<Page> ListOwned( string group = null, long cursor = 0, int pageSize = 20 )
		{
			var result = await Request( api.ListRecords( Package, Name, group, true, cursor, pageSize ) );
			return CreatePage( result );
		}

		/// <summary>
		/// Get public record counts and creation-time ranges for up to 100 group keys.
		/// Empty groups are included with a zero count and null dates; duplicate keys appear once.
		/// </summary>
		/// <param name="groups">One to 100 group labels. Duplicate labels are counted once.</param>
		public async Task<IReadOnlyDictionary<string, GroupInfo>> GetPublicGroupInfo( params string[] groups )
		{
			var result = await Request( api.GetGroupSummaries( Package, Name, new StorageSummaryRequest { GroupKeys = groups } ) );
			var groupsInfo = new Dictionary<string, GroupInfo>( result.Groups.Count, StringComparer.Ordinal );
			foreach ( var group in result.Groups )
			{
				groupsInfo.Add( group.Key, new GroupInfo( group.Value ) );
			}

			return groupsInfo;
		}

		/// <summary>
		/// Wrap a response using one array for the returned records.
		/// </summary>
		/// <param name="result">The page returned by the backend.</param>
		Page CreatePage( StoragePage result )
		{
			var records = result.Items.Length == 0 ? Array.Empty<Record>() : new Record[result.Items.Length];
			for ( int i = 0; i < records.Length; i++ )
			{
				records[i] = new Record( this, result.Items[i] );
			}

			return new Page( records, result.NextId );
		}

		/// <summary>
		/// Updates a record using its captured revision.
		/// </summary>
		/// <param name="record">The snapshot containing the current revision.</param>
		/// <param name="value">The replacement text.</param>
		/// <param name="publicRead">Whether the updated record is publicly readable.</param>
		internal async Task<Record> UpdateRecord( Record record, string value, bool publicRead )
		{
			return new Record( this, await Request( api.UpdateRecord( Package, Name, record.Id, new StorageUpdate
			{
				Value = value,
				PublicRead = publicRead,
				Revision = record.Revision
			} ) ) );
		}

		/// <summary>
		/// Deletes a record using its captured revision.
		/// </summary>
		/// <param name="record">The snapshot containing the revision to delete.</param>
		internal Task DeleteRecord( Record record )
		{
			return Request( api.DeleteRecord( Package, Name, record.Id, record.Revision ) );
		}
	}

	/// <summary>
	/// An immutable snapshot of a record. Updates return a new snapshot; keep the returned record
	/// for subsequent changes. Concurrent edits fail with status 409 rather than overwriting data.
	/// </summary>
	public sealed class Record
	{
		readonly Table _table;
		readonly StorageEntry _entry;

		/// <summary>
		/// Creates a record snapshot from the backend response.
		/// </summary>
		/// <param name="table">The table containing this record.</param>
		/// <param name="entry">The backend record snapshot.</param>
		internal Record( Table table, StorageEntry entry )
		{
			this._table = table;
			this._entry = entry;
		}

		/// <summary>
		/// The stable ID of this record.
		/// </summary>
		public long Id => _entry.Id;

		/// <summary>
		/// The Steam ID of the player who owns this record.
		/// </summary>
		public long OwnerSteamId => _entry.SteamId;

		/// <summary>
		/// The record key, unique within its owner's package and table.
		/// </summary>
		public string Key => _entry.Key;

		/// <summary>
		/// The group containing this record, or null for an ungrouped record.
		/// </summary>
		public string Group => _entry.GroupKey;

		/// <summary>
		/// The stored text, which may contain serialized JSON.
		/// </summary>
		public string Value => _entry.Value;

		/// <summary>
		/// Whether anyone can read this record.
		/// </summary>
		public bool IsPublic => _entry.PublicRead;

		/// <summary>
		/// When this record was created.
		/// </summary>
		public DateTimeOffset CreatedAt => _entry.CreatedAt;

		/// <summary>
		/// When this record was last changed.
		/// </summary>
		public DateTimeOffset UpdatedAt => _entry.Updated;

		/// <summary>
		/// The revision captured when this snapshot was fetched, used to detect conflicting edits.
		/// </summary>
		public string Revision => _entry.Revision;

		/// <summary>
		/// Deserialize a JSON payload. Invalid JSON or an incompatible type throws.
		/// </summary>
		/// <typeparam name="T">The type to deserialize the JSON value into.</typeparam>
		public T Deserialize<T>() => JsonSerializer.Deserialize<T>( Value );

		/// <summary>
		/// Replace your record's value while preserving its visibility. Returns a new snapshot with the new revision.
		/// </summary>
		/// <param name="value">The replacement text, up to 65536 UTF-8 bytes.</param>
		public Task<Record> Update( string value )
		{
			return _table.UpdateRecord( this, value, IsPublic );
		}

		/// <summary>
		/// Allow anyone to read your record, preserving its value. Returns a new snapshot with the new revision.
		/// </summary>
		public Task<Record> MakePublic()
		{
			return _table.UpdateRecord( this, Value, true );
		}

		/// <summary>
		/// Allow only you to read your record, preserving its value. Returns a new snapshot with the new revision.
		/// </summary>
		public Task<Record> MakePrivate()
		{
			return _table.UpdateRecord( this, Value, false );
		}

		/// <summary>
		/// Delete this revision as its owner or a moderator. A stale revision fails with status 409.
		/// </summary>
		public Task Delete() => _table.DeleteRecord( this );
	}

	/// <summary>
	/// One page ordered by ascending record ID.
	/// </summary>
	public readonly struct Page
	{
		/// <summary>
		/// The records returned in this page.
		/// </summary>
		public IReadOnlyList<Record> Records => records ?? Array.Empty<Record>();

		readonly Record[] records;

		/// <summary>
		/// Cursor for the next page; null when there are no more records.
		/// </summary>
		public long? NextCursor { get; }

		/// <summary>
		/// Creates a page of records and its continuation cursor.
		/// </summary>
		/// <param name="records">The records in the page.</param>
		/// <param name="nextCursor">The next page cursor, or null at the end.</param>
		internal Page( Record[] records, long? nextCursor )
		{
			this.records = records;
			NextCursor = nextCursor;
		}
	}

	/// <summary>
	/// Public records currently in a group.
	/// </summary>
	public readonly struct GroupInfo
	{
		/// <summary>
		/// The number of public records in this group.
		/// </summary>
		public long Count { get; }

		/// <summary>
		/// When the oldest public record was created, or null for an empty group.
		/// </summary>
		public DateTimeOffset? OldestCreatedAt { get; }

		/// <summary>
		/// When the newest public record was created, or null for an empty group.
		/// </summary>
		public DateTimeOffset? NewestCreatedAt { get; }

		/// <summary>
		/// Creates group information from the backend response.
		/// </summary>
		/// <param name="summary">The backend count and creation-time range.</param>
		internal GroupInfo( StorageGroupSummary summary )
		{
			Count = summary.Amount;
			OldestCreatedAt = summary.First;
			NewestCreatedAt = summary.Last;
		}
	}

	/// <summary>
	/// Converts rejected backend requests into public database errors.
	/// </summary>
	/// <typeparam name="T">The backend response type.</typeparam>
	/// <param name="request">The in-flight backend request.</param>
	static async Task<T> Request<T>( Task<T> request )
	{
		try
		{
			return await request;
		}
		catch ( Refit.ApiException exception )
		{
			throw DatabaseException.FromResponse( exception );
		}
	}
}

/// <summary>
/// A rejected database operation. Network and JSON errors propagate separately.
/// </summary>
public sealed class DatabaseException : Exception
{
	/// <summary>
	/// HTTP status, e.g. 401 unauthenticated, 403 forbidden, 404 missing, 409 conflict, 413 too large or 428 missing revision.
	/// </summary>
	public int StatusCode { get; }

	/// <summary>
	/// How long to wait before retrying, or null when the server did not specify a delay.
	/// </summary>
	public TimeSpan? RetryAfter { get; }

	/// <summary>
	/// Creates an error from the backend response.
	/// </summary>
	/// <param name="exception">The failed backend request.</param>
	internal static DatabaseException FromResponse( Refit.ApiException exception )
	{
		var message = exception.Message;
		if ( !string.IsNullOrWhiteSpace( exception.Content ) )
		{
			try
			{
				using var json = JsonDocument.Parse( exception.Content );
				if ( json.RootElement.ValueKind == JsonValueKind.Object
					&& json.RootElement.TryGetProperty( "error", out var error )
					&& error.ValueKind == JsonValueKind.String
					&& !string.IsNullOrWhiteSpace( error.GetString() ) )
				{
					message = error.GetString();
				}
			}
			catch ( JsonException )
			{
				// Proxies can return HTML or plain text. Keep the original HTTP error in that case.
			}
		}

		var header = exception.Headers?.RetryAfter;
		var retryAfter = header?.Delta;
		if ( header?.Date is { } retryAt )
		{
			retryAfter = retryAt - DateTimeOffset.UtcNow;
		}
		if ( retryAfter < TimeSpan.Zero )
		{
			retryAfter = TimeSpan.Zero;
		}

		return new DatabaseException( (int)exception.StatusCode, message, retryAfter, exception );
	}

	/// <summary>
	/// Creates a rejected database operation with its backend details.
	/// </summary>
	/// <param name="statusCode">The HTTP response status.</param>
	/// <param name="message">The backend explanation, or the original HTTP error.</param>
	/// <param name="retryAfter">The suggested retry delay, if supplied.</param>
	/// <param name="innerException">The original backend exception.</param>
	internal DatabaseException( int statusCode, string message, TimeSpan? retryAfter, Exception innerException ) : base( message, innerException )
	{
		StatusCode = statusCode;
		RetryAfter = retryAfter;
	}
}
