using Microsoft.Data.Sqlite;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.Utility;

/// <summary>
/// Handles native SQLite library initialization for the engine.
/// </summary>
internal static class SqliteNative
{
	private static bool s_initialized;
	private static readonly Lock Lock = new();
	private static IntPtr s_nativeHandle;

	/// <summary>
	/// Path to the native SQLite library relative to the managed assembly directory.
	/// </summary>
	private static string GetNativeLibraryPath()
	{
		var managedDir = Path.GetDirectoryName( typeof( SqliteNative ).Assembly.Location );
		var runtimesPath = Path.Combine( managedDir, "runtimes" );

		if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
		{
			var rid = RuntimeInformation.ProcessArchitecture switch
			{
				Architecture.X64 => "win-x64",
				Architecture.X86 => "win-x86",
				Architecture.Arm64 => "win-arm64",
				_ => "win-x64"
			};
			return Path.Combine( runtimesPath, rid, "native", "e_sqlite3.dll" );
		}

		if ( RuntimeInformation.IsOSPlatform( OSPlatform.Linux ) )
		{
			var rid = RuntimeInformation.ProcessArchitecture switch
			{
				Architecture.X64 => "linux-x64",
				Architecture.Arm64 => "linux-arm64",
				_ => "linux-x64"
			};
			return Path.Combine( runtimesPath, rid, "native", "libe_sqlite3.so" );
		}

		if ( RuntimeInformation.IsOSPlatform( OSPlatform.OSX ) )
		{
			var rid = RuntimeInformation.ProcessArchitecture switch
			{
				Architecture.Arm64 => "osx-arm64",
				Architecture.X64 => "osx-x64",
				_ => "osx-x64"
			};
			return Path.Combine( runtimesPath, rid, "native", "libe_sqlite3.dylib" );
		}

		return null;
	}

	/// <summary>
	/// Ensures the native SQLite library is loaded and SQLitePCL is initialized.
	/// Must be called before any SQLite operations.
	/// </summary>
	internal static void Initialize()
	{
		if ( s_initialized )
			return;

		lock ( Lock )
		{
			if ( s_initialized )
				return;

			var nativeLibPath = GetNativeLibraryPath();
			if ( string.IsNullOrEmpty( nativeLibPath ) || !File.Exists( nativeLibPath ) )
			{
				throw new DllNotFoundException( $"Could not find native SQLite library at: {nativeLibPath}" );
			}

			// Pre-load the native library so it's available when SQLitePCL needs it
			if ( !NativeLibrary.TryLoad( nativeLibPath, out s_nativeHandle ) )
			{
				throw new DllNotFoundException( $"Failed to load native SQLite library from: {nativeLibPath}" );
			}

			AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

			foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
			{
				TrySetResolver( assembly );
			}

			InitializeSqlitePcl();

			s_initialized = true;
		}
	}

	private static void OnAssemblyLoad( object sender, AssemblyLoadEventArgs args )
	{
		TrySetResolver( args.LoadedAssembly );
	}

	private static void TrySetResolver( Assembly assembly )
	{
		var name = assembly.GetName().Name;
		if ( name == null || !name.StartsWith( "SQLitePCL", StringComparison.OrdinalIgnoreCase ) )
			return;

		try
		{
			NativeLibrary.SetDllImportResolver( assembly, ResolveSqliteNative );
		}
		catch ( InvalidOperationException )
		{
			// Resolver already set
		}
	}

	private static IntPtr ResolveSqliteNative( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
		return libraryName == "e_sqlite3" && s_nativeHandle != IntPtr.Zero ? s_nativeHandle : IntPtr.Zero;
	}

	private static void InitializeSqlitePcl()
	{
		var providerType = Type.GetType( "SQLitePCL.SQLite3Provider_e_sqlite3, SQLitePCLRaw.provider.e_sqlite3" );
		var rawType = Type.GetType( "SQLitePCL.raw, SQLitePCLRaw.core" );

		if ( providerType == null || rawType == null )
		{
			throw new InvalidOperationException( "SQLitePCL assemblies not found. Did you check if Microsoft.Data.Sqlite is referenced?" );
		}

		var provider = Activator.CreateInstance( providerType );
		var setProviderMethod = rawType.GetMethod( "SetProvider", BindingFlags.Public | BindingFlags.Static );
		setProviderMethod?.Invoke( null, [provider] );
	}

	/// <summary>
	/// Frees the native SQLite library. Called on shutdown.
	/// </summary>
	internal static void Shutdown()
	{
		AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;

		if ( s_nativeHandle != IntPtr.Zero )
		{
			NativeLibrary.Free( s_nativeHandle );
			s_nativeHandle = IntPtr.Zero;
		}
		s_initialized = false;
	}
}

/// <summary>
/// Represents an SQLite database connection that can be used for custom database operations.
/// For most use cases, prefer the static <see cref="Sql"/> class which provides access to the default game database.
/// </summary>
/// <example>
/// <code>
/// // Create a custom database
/// using var db = new SqlDatabase( "path/to/my/database.db" );
/// 
/// // Create a table
/// db.Query( "CREATE TABLE IF NOT EXISTS settings ( key TEXT PRIMARY KEY, value TEXT )" );
/// 
/// // Insert data
/// db.Query( "INSERT OR REPLACE INTO settings (key, value) VALUES (@k, @v)", new { k = "volume", v = "0.8" } );
/// 
/// // Query data
/// var volume = db.QueryValue&lt;string&gt;( "SELECT value FROM settings WHERE key = @k", new { k = "volume" } );
/// </code>
/// </example>
public sealed class SqlDatabase : IDisposable
{
	private SqliteConnection _connection;
	private readonly string _connectionString;
	private readonly Lock _lock = new();
	private bool _disposed;

	/// <summary>
	/// Gets the file path to the database, or ":memory:" for an in-memory database.
	/// </summary>
	public string DatabasePath { get; }

	/// <summary>
	/// Creates a new SQLite database connection.
	/// </summary>
	/// <param name="path">
	/// The file path to the database file. The file will be created if it doesn't exist.
	/// Use ":memory:" for an in-memory database.
	/// </param>
	/// <exception cref="ArgumentNullException">Thrown if path is null.</exception>
	public SqlDatabase( string path )
	{
		ArgumentNullException.ThrowIfNull( path );

		SqliteNative.Initialize();

		DatabasePath = path;

		// Ensure directory exists for file-based databases
		if ( path != ":memory:" )
		{
			var directory = Path.GetDirectoryName( path );
			if ( !string.IsNullOrEmpty( directory ) )
			{
				Directory.CreateDirectory( directory );
			}
		}

		_connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = path,
			Mode = path == ":memory:" ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
			Cache = path == ":memory:" ? SqliteCacheMode.Private : SqliteCacheMode.Shared
		}.ToString();

		_connection = new SqliteConnection( _connectionString );
		_connection.Open();

		// Enable foreign keys
		using var cmd = _connection.CreateCommand();
		cmd.CommandText = "PRAGMA foreign_keys = ON;";
		cmd.ExecuteNonQuery();
	}

	/// <summary>
	/// Creates an in-memory SQLite database.
	/// </summary>
	/// <returns>A new in-memory SqlDatabase instance.</returns>
	public static SqlDatabase CreateInMemory()
	{
		return new SqlDatabase( ":memory:" );
	}

	/// <summary>
	/// Executes a SQL query and returns all results.
	/// </summary>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="parameters">Optional anonymous object containing parameter values.</param>
	/// <returns>A list of dictionaries where each dictionary represents a row.</returns>
	public List<Dictionary<string, object>> Query( string query, object parameters = null )
	{
		ThrowIfDisposed();

		lock ( _lock )
		{
			using var cmd = CreateCommand( query, parameters );
			using var reader = cmd.ExecuteReader();

			var results = new List<Dictionary<string, object>>();

			while ( reader.Read() )
			{
				var row = new Dictionary<string, object>( StringComparer.OrdinalIgnoreCase );

				for ( int i = 0; i < reader.FieldCount; i++ )
				{
					var name = reader.GetName( i );
					var value = reader.IsDBNull( i ) ? null : reader.GetValue( i );
					row[name] = value;
				}

				results.Add( row );
			}

			return results;
		}
	}

	/// <summary>
	/// Executes a SQL query asynchronously and returns all results.
	/// </summary>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="parameters">Optional anonymous object containing parameter values.</param>
	/// <returns>A task containing a list of dictionaries where each dictionary represents a row.</returns>
	public async Task<List<Dictionary<string, object>>> QueryAsync( string query, object parameters = null )
	{
		ThrowIfDisposed();

		// Note: We acquire the lock synchronously to avoid deadlocks.
		// The actual database operation is async.
		lock ( _lock )
		{
			// Create command inside lock to ensure thread safety
			var cmd = CreateCommand( query, parameters );
			return QueryAsyncInternal( cmd ).GetAwaiter().GetResult();
		}
	}

	private async Task<List<Dictionary<string, object>>> QueryAsyncInternal( SqliteCommand cmd )
	{
		using ( cmd )
		using ( var reader = await cmd.ExecuteReaderAsync() )
		{
			var results = new List<Dictionary<string, object>>();

			while ( await reader.ReadAsync() )
			{
				var row = new Dictionary<string, object>( StringComparer.OrdinalIgnoreCase );

				for ( int i = 0; i < reader.FieldCount; i++ )
				{
					var name = reader.GetName( i );
					var value = reader.IsDBNull( i ) ? null : reader.GetValue( i );
					row[name] = value;
				}

				results.Add( row );
			}

			return results;
		}
	}

	/// <summary>
	/// Executes a SQL query and returns a single row.
	/// </summary>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="row">The row index to return (0-based).</param>
	/// <param name="parameters">Optional anonymous object containing parameter values.</param>
	/// <returns>A dictionary representing the row, or null if not found.</returns>
	public Dictionary<string, object> QueryRow( string query, int row = 0, object parameters = null )
	{
		var results = Query( query, parameters );
		return results.Count <= row ? null : results[row];
	}

	/// <summary>
	/// Executes a SQL query and returns a single value.
	/// </summary>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="parameters">Optional anonymous object containing parameter values.</param>
	/// <returns>The value of the first column of the first row, or null if no rows or the value is NULL.</returns>
	public object QueryValue( string query, object parameters = null )
	{
		ThrowIfDisposed();

		lock ( _lock )
		{
			using var cmd = CreateCommand( query, parameters );
			var result = cmd.ExecuteScalar();
			return result is DBNull ? null : result;
		}
	}

	/// <summary>
	/// Executes a SQL query and returns a single value cast to the specified type.
	/// </summary>
	/// <typeparam name="T">The type to cast the result to.</typeparam>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="parameters">Optional anonymous object containing parameter values.</param>
	/// <returns>The value cast to type T, or default(T).</returns>
	public T QueryValue<T>( string query, object parameters = null )
	{
		var value = QueryValue( query, parameters );

		if ( value is null || value is DBNull )
			return default;

		try
		{
			return (T)Convert.ChangeType( value, typeof( T ) );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"SQL: Failed to convert '{value}' to {typeof( T ).Name}: {ex.Message}" );
			return default;
		}
	}

	/// <summary>
	/// Executes a non-query SQL command (INSERT, UPDATE, DELETE, etc.).
	/// </summary>
	/// <param name="query">The SQL command to execute.</param>
	/// <param name="parameters">Optional anonymous object containing parameter values.</param>
	/// <returns>The number of rows affected.</returns>
	public int Execute( string query, object parameters = null )
	{
		ThrowIfDisposed();

		lock ( _lock )
		{
			using var cmd = CreateCommand( query, parameters );
			return cmd.ExecuteNonQuery();
		}
	}

	/// <summary>
	/// Checks if a table exists in the database.
	/// </summary>
	/// <param name="tableName">The name of the table.</param>
	/// <returns>True if the table exists.</returns>
	public bool TableExists( string tableName )
	{
		var result = QueryValue<long>(
			"SELECT COUNT(*) FROM sqlite_master WHERE name = @name AND type = 'table'",
			new { name = tableName } );

		return result > 0;
	}

	/// <summary>
	/// Checks if an index exists in the database.
	/// </summary>
	/// <param name="indexName">The name of the index.</param>
	/// <returns>True if the index exists.</returns>
	public bool IndexExists( string indexName )
	{
		var result = QueryValue<long>(
			"SELECT COUNT(*) FROM sqlite_master WHERE name = @name AND type = 'index'",
			new { name = indexName } );

		return result > 0;
	}

	/// <summary>
	/// Gets the row ID of the last inserted row.
	/// </summary>
	/// <returns>The last insert row ID.</returns>
	public long LastInsertRowId()
	{
		return QueryValue<long>( "SELECT last_insert_rowid()" );
	}

	/// <summary>
	/// Gets the number of rows affected by the last data modification.
	/// </summary>
	/// <returns>The number of rows changed.</returns>
	public int RowsAffected()
	{
		return QueryValue<int>( "SELECT changes()" );
	}

	/// <summary>
	/// Begins a transaction.
	/// </summary>
	/// <returns>A transaction object that should be used with using statement.</returns>
	public SqlTransaction BeginTransaction()
	{
		ThrowIfDisposed();
		return new SqlTransaction( this );
	}

	/// <summary>
	/// Executes an action within a transaction. The transaction is automatically
	/// committed if the action succeeds, or rolled back if an exception is thrown.
	/// </summary>
	/// <param name="action">The action to execute within the transaction.</param>
	public void InTransaction( Action action )
	{
		using var transaction = BeginTransaction();
		action();
		transaction.Commit();
	}

	/// <summary>
	/// Executes a function within a transaction and returns its result.
	/// </summary>
	/// <typeparam name="T">The return type.</typeparam>
	/// <param name="func">The function to execute within the transaction.</param>
	/// <returns>The result of the function.</returns>
	public T InTransaction<T>( Func<T> func )
	{
		using var transaction = BeginTransaction();
		var result = func();
		transaction.Commit();
		return result;
	}

	private SqliteCommand CreateCommand( string query, object parameters )
	{
		var cmd = _connection.CreateCommand();
		cmd.CommandText = query;

		if ( parameters is not null )
		{
			AddParameters( cmd, parameters );
		}

		return cmd;
	}

	private static void AddParameters( SqliteCommand cmd, object parameters )
	{
		if ( parameters is IDictionary<string, object> dict )
		{
			foreach ( var kvp in dict )
			{
				var paramName = kvp.Key.StartsWith( '@' ) ? kvp.Key : $"@{kvp.Key}";
				cmd.Parameters.AddWithValue( paramName, kvp.Value ?? DBNull.Value );
			}
			return;
		}

		var type = parameters.GetType();

		foreach ( var property in type.GetProperties() )
		{
			var value = property.GetValue( parameters );
			var paramName = $"@{property.Name}";
			cmd.Parameters.AddWithValue( paramName, value ?? DBNull.Value );
		}

		foreach ( var field in type.GetFields() )
		{
			var value = field.GetValue( parameters );
			var paramName = $"@{field.Name}";
			cmd.Parameters.AddWithValue( paramName, value ?? DBNull.Value );
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf( _disposed, this );
	}

	/// <summary>
	/// Closes the database connection and releases all resources.
	/// </summary>
	public void Dispose()
	{
		if ( _disposed )
			return;

		_disposed = true;

		lock ( _lock )
		{
			_connection?.Close();
			_connection?.Dispose();
			_connection = null;
		}
	}

	internal void ExecuteRaw( string query )
	{
		lock ( _lock )
		{
			using var cmd = _connection.CreateCommand();
			cmd.CommandText = query;
			cmd.ExecuteNonQuery();
		}
	}
}

/// <summary>
/// Represents a database transaction that can be committed or rolled back.
/// </summary>
public sealed class SqlTransaction : IDisposable
{
	private readonly SqlDatabase _database;
	private bool _completed;
	private bool _disposed;

	internal SqlTransaction( SqlDatabase database )
	{
		_database = database;
		_database.ExecuteRaw( "BEGIN TRANSACTION" );
	}

	/// <summary>
	/// Commits all changes made during the transaction.
	/// </summary>
	public void Commit()
	{
		if ( _completed )
			return;

		_database.ExecuteRaw( "COMMIT" );
		_completed = true;
	}

	/// <summary>
	/// Discards all changes made during the transaction.
	/// </summary>
	public void Rollback()
	{
		if ( _completed )
			return;

		_database.ExecuteRaw( "ROLLBACK" );
		_completed = true;
	}

	/// <summary>
	/// Disposes the transaction, rolling back if not already committed.
	/// </summary>
	public void Dispose()
	{
		if ( _disposed )
			return;

		_disposed = true;

		if ( !_completed )
		{
			try
			{
				Rollback();
			}
			catch ( Exception ex )
			{
				Log.Warning( $"SQL: Transaction rollback failed during dispose: {ex.Message}" );
			}
		}
	}
}
