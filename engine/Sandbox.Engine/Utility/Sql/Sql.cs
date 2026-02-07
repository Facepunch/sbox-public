using System.Threading;
using Microsoft.Data.Sqlite;

namespace Sandbox.Utility;

/// <summary>
/// Provides SQLite database functionality.
/// This is the primary interface for interacting with a per-game SQLite database.
/// </summary>
public static class Sql
{
	private static SqlDatabase s_serverDatabase;
	private static SqlDatabase s_clientDatabase;
	private static readonly Lock Lock = new();

	/// <summary>
	/// The last error that occurred during a query, or null if no error.
	/// </summary>
	public static string LastError { get; private set; }

	/// <summary>
	/// Gets the default SQLite database for the current context.
	/// Uses cl.db when connected as client, sv.db when hosting.
	/// </summary>
	internal static SqlDatabase Database
	{
		get
		{
			if ( Networking.IsClient )
			{
				lock ( Lock )
					return s_clientDatabase ??= OpenDatabase( "cl.db" );
			}
			else
			{
				lock ( Lock )
					return s_serverDatabase ??= OpenDatabase( "sv.db" );
			}
		}
	}

	/// <summary>
	/// Gets the server-side database (sv.db). Use this when you explicitly need
	/// server storage regardless of the current network context.
	/// </summary>
	public static SqlDatabase Server
	{
		get
		{
			lock ( Lock )
				return s_serverDatabase ??= OpenDatabase( "sv.db" );
		}
	}

	/// <summary>
	/// Gets the client-side database (cl.db). Use this when you explicitly need
	/// client storage regardless of the current network context.
	/// </summary>
	public static SqlDatabase Client
	{
		get
		{
			lock ( Lock )
				return s_clientDatabase ??= OpenDatabase( "cl.db" );
		}
	}

	private static SqlDatabase OpenDatabase( string filename )
	{
		var dataPath = GetDatabasePath( filename );
		var db = new SqlDatabase( dataPath );
		return db;
	}

	private static string GetDatabasePath( string filename )
	{
		// Use the game's data directory for storing the SQLite database
		var dataFolder = EngineFileSystem.Root.GetFullPath( "data" );
		System.IO.Directory.CreateDirectory( dataFolder );
		return System.IO.Path.Combine( dataFolder, filename );
	}

	/// <summary>
	/// Executes a SQL query and returns the results as a list of dictionaries.
	/// Each dictionary represents a row where keys are column names and values are the cell values.
	/// </summary>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="parameters">Optional parameters for parameterized queries.</param>
	/// <returns>
	/// A list of dictionaries for SELECT queries, an empty list for non-SELECT queries that succeed,
	/// or null if an error occurred (check <see cref="LastError"/> for details).
	/// </returns>
	/// <example>
	/// <code>
	/// // Simple query
	/// var results = Sql.Query( "SELECT * FROM users WHERE id = @id", new { id = 1 } );
	/// 
	/// // Iterate results
	/// foreach ( var row in results )
	/// {
	///     Log.Info( $"Name: {row["name"]}" );
	/// }
	/// </code>
	/// </example>
	public static List<Dictionary<string, object>> Query( string query, object parameters = null )
	{
		LastError = null;

		try
		{
			return Database.Query( query, parameters );
		}
		catch ( SqliteException ex )
		{
			LastError = ex.Message;
			Log.Warning( $"SQL Error: {ex.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Executes a non-query SQL command (INSERT, UPDATE, DELETE, CREATE, etc.).
	/// </summary>
	/// <param name="query">The SQL command to execute.</param>
	/// <param name="parameters">Optional parameters for parameterized queries.</param>
	/// <returns>The number of rows affected, or -1 if an error occurred.</returns>
	/// <example>
	/// <code>
	/// Sql.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );
	/// Sql.Execute( "INSERT INTO users (name) VALUES (@name)", new { name = "John" } );
	/// var affected = Sql.Execute( "DELETE FROM users WHERE id > @id", new { id = 10 } );
	/// </code>
	/// </example>
	public static int Execute( string query, object parameters = null )
	{
		LastError = null;

		try
		{
			return Database.Execute( query, parameters );
		}
		catch ( SqliteException ex )
		{
			LastError = ex.Message;
			Log.Warning( $"SQL Error: {ex.Message}" );
			return -1;
		}
	}

	/// <summary>
	/// Executes a SQL query asynchronously and returns the results as a list of dictionaries.
	/// </summary>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="parameters">Optional parameters for parameterized queries.</param>
	/// <returns>
	/// A task containing a list of dictionaries for SELECT queries, an empty list for non-SELECT queries that succeed,
	/// or null if an error occurred (check <see cref="LastError"/> for details).
	/// </returns>
	public static async Task<List<Dictionary<string, object>>> QueryAsync( string query, object parameters = null )
	{
		LastError = null;

		try
		{
			return await Database.QueryAsync( query, parameters );
		}
		catch ( SqliteException ex )
		{
			LastError = ex.Message;
			Log.Warning( $"SQL Error: {ex.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Executes a SQL query and returns a single row as a dictionary.
	/// </summary>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="row">The row index to return (0-based). Defaults to 0 (first row).</param>
	/// <param name="parameters">Optional parameters for parameterized queries.</param>
	/// <returns>
	/// A dictionary representing the requested row, or null if no rows exist or an error occurred.
	/// </returns>
	/// <example>
	/// <code>
	/// var user = Sql.QueryRow( "SELECT * FROM users WHERE id = @id", parameters: new { id = 1 } );
	/// if ( user != null )
	/// {
	///     Log.Info( $"User name: {user["name"]}" );
	/// }
	/// </code>
	/// </example>
	public static Dictionary<string, object> QueryRow( string query, int row = 0, object parameters = null )
	{
		var results = Query( query, parameters );
		return results is null || results.Count <= row ? null : results[row];
	}

	/// <summary>
	/// Executes a SQL query and returns a single value from the first column of the first row.
	/// </summary>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="parameters">Optional parameters for parameterized queries.</param>
	/// <returns>
	/// The value of the first column of the first row, or null if no rows exist or an error occurred.
	/// </returns>
	/// <example>
	/// <code>
	/// var count = Sql.QueryValue( "SELECT COUNT(*) FROM users" );
	/// Log.Info( $"Total users: {count}" );
	/// </code>
	/// </example>
	public static object QueryValue( string query, object parameters = null )
	{
		LastError = null;

		try
		{
			return Database.QueryValue( query, parameters );
		}
		catch ( SqliteException ex )
		{
			LastError = ex.Message;
			Log.Warning( $"SQL Error: {ex.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Executes a SQL query and returns a single value cast to the specified type.
	/// </summary>
	/// <typeparam name="T">The type to cast the result to.</typeparam>
	/// <param name="query">The SQL query to execute.</param>
	/// <param name="parameters">Optional parameters for parameterized queries.</param>
	/// <returns>
	/// The value cast to type T, or default(T) if no rows exist, an error occurred, or the cast failed.
	/// </returns>
	/// <example>
	/// <code>
	/// var count = Sql.QueryValue&lt;int&gt;( "SELECT COUNT(*) FROM users" );
	/// var name = Sql.QueryValue&lt;string&gt;( "SELECT name FROM users WHERE id = @id", new { id = 1 } );
	/// </code>
	/// </example>
	public static T QueryValue<T>( string query, object parameters = null )
	{
		var value = QueryValue( query, parameters );

		if ( value is null )
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
	/// Checks if a table with the specified name exists in the database.
	/// </summary>
	/// <param name="tableName">The name of the table to check.</param>
	/// <returns>True if the table exists, false otherwise.</returns>
	/// <example>
	/// <code>
	/// if ( !Sql.TableExists( "users" ) )
	/// {
	///     Sql.Query( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );
	/// }
	/// </code>
	/// </example>
	public static bool TableExists( string tableName )
	{
		var result = Query( "SELECT name FROM sqlite_master WHERE name = @name AND type = 'table'",
			new { name = tableName } );

		return result is not null && result.Count > 0;
	}

	/// <summary>
	/// Checks if an index with the specified name exists in the database.
	/// </summary>
	/// <param name="indexName">The name of the index to check.</param>
	/// <returns>True if the index exists, false otherwise.</returns>
	public static bool IndexExists( string indexName )
	{
		var result = Query( "SELECT name FROM sqlite_master WHERE name = @name AND type = 'index'",
			new { name = indexName } );

		return result is not null && result.Count > 0;
	}

	/// <summary>
	/// Escapes a string for safe use in SQL queries by escaping single quotes.
	/// For new code, prefer using parameterized queries instead.
	/// </summary>
	/// <param name="input">The string to escape.</param>
	/// <param name="includeQuotes">If true, wraps the result in single quotes. Defaults to true.</param>
	/// <returns>The escaped string, optionally wrapped in single quotes.</returns>
	/// <example>
	/// <code>
	/// // Legacy style (not recommended)
	/// var safeName = Sql.Escape( userInput );
	/// Sql.Query( $"SELECT * FROM users WHERE name = {safeName}" );
	/// 
	/// // Preferred: Use parameterized queries
	/// Sql.Query( "SELECT * FROM users WHERE name = @name", new { name = userInput } );
	/// </code>
	/// </example>
	public static string Escape( string input, bool includeQuotes = true )
	{
		if ( input is null )
			return includeQuotes ? "''" : "";

		var escaped = input.Replace( "'", "''" );

		// Truncate at null character for safety
		var nullIndex = escaped.IndexOf( '\0' );
		if ( nullIndex >= 0 )
		{
			escaped = escaped[..nullIndex];
		}

		return includeQuotes ? $"'{escaped}'" : escaped;
	}

	/// <summary>
	/// Begins a transaction. Use this before performing many insert operations
	/// for significantly improved performance.
	/// </summary>
	/// <remarks>
	/// Always call <see cref="Commit"/> after completing your operations, or <see cref="Rollback"/> to cancel them.
	/// </remarks>
	/// <example>
	/// <code>
	/// Sql.Begin();
	/// try
	/// {
	///     for ( int i = 0; i &lt; 1000; i++ )
	///     {
	///         Sql.Query( "INSERT INTO data (value) VALUES (@v)", new { v = i } );
	///     }
	///     Sql.Commit();
	/// }
	/// catch
	/// {
	///     Sql.Rollback();
	///     throw;
	/// }
	/// </code>
	/// </example>
	public static void Begin()
	{
		Database.Execute( "BEGIN TRANSACTION" );
	}

	/// <summary>
	/// Commits a transaction, writing all changes to disk.
	/// </summary>
	public static void Commit()
	{
		Database.Execute( "COMMIT" );
	}

	/// <summary>
	/// Rolls back a transaction, discarding all changes since the last <see cref="Begin"/> call.
	/// </summary>
	public static void Rollback()
	{
		Database.Execute( "ROLLBACK" );
	}

	/// <summary>
	/// Gets the row ID of the last inserted row.
	/// </summary>
	/// <returns>The row ID of the last inserted row, or 0 if no insert has been performed.</returns>
	public static long LastInsertRowId()
	{
		return QueryValue<long>( "SELECT last_insert_rowid()" );
	}

	/// <summary>
	/// Gets the number of rows affected by the last INSERT, UPDATE, or DELETE statement.
	/// </summary>
	/// <returns>The number of rows affected.</returns>
	public static int RowsAffected()
	{
		return QueryValue<int>( "SELECT changes()" );
	}

	/// <summary>
	/// Gets all table names in the database.
	/// </summary>
	/// <returns>A list of table names.</returns>
	public static List<string> GetTables()
	{
		var results = Query( "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name" );
		var tables = new List<string>();

		if ( results is not null )
		{
			foreach ( var row in results )
			{
				if ( row.TryGetValue( "name", out var name ) && name is string tableName )
				{
					tables.Add( tableName );
				}
			}
		}

		return tables;
	}

	/// <summary>
	/// Gets column information for a specified table.
	/// </summary>
	/// <param name="tableName">The name of the table.</param>
	/// <returns>A list of column information dictionaries.</returns>
	/// <remarks>
	/// Warning: This method uses string interpolation for the table name because SQLite PRAGMA
	/// statements don't support parameterized queries. Do not pass untrusted user input as the
	/// table name. Table names should come from your code, not from user input.
	/// </remarks>
	public static List<Dictionary<string, object>> GetTableColumns( string tableName )
	{
		return Query( $"PRAGMA table_info({tableName})" );
	}

	/// <summary>
	/// Shuts down the default database connection. Called automatically when the engine shuts down.
	/// </summary>
	internal static void Shutdown()
	{
		lock ( Lock )
		{
			s_serverDatabase?.Dispose();
			s_serverDatabase = null;

			s_clientDatabase?.Dispose();
			s_clientDatabase = null;
		}
	}
}
