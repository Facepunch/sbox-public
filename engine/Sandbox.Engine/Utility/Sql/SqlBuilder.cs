using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.Utility;

/// <summary>
/// A fluent SQL query builder for constructing safe, parameterized SQL queries.
/// </summary>
/// <example>
/// <code>
/// // SELECT query
/// var query = new SqlBuilder()
///     .Select( "id", "name", "score" )
///     .From( "users" )
///     .Where( "score > @minScore", new { minScore = 100 } )
///     .OrderBy( "score DESC" )
///     .Limit( 10 );
/// 
/// var results = Sql.Query( query.Build(), query.Parameters );
/// 
/// // INSERT query
/// var insert = new SqlBuilder()
///     .InsertInto( "users", "name", "score" )
///     .Values( new { name = "John", score = 500 } );
/// 
/// Sql.Query( insert.Build(), insert.Parameters );
/// </code>
/// </example>
public sealed class SqlBuilder
{
	private readonly StringBuilder _query = new();
	private int _parameterIndex;

	/// <summary>
	/// Gets the parameters dictionary for use with <see cref="Sql.Query"/>.
	/// </summary>
	public Dictionary<string, object> Parameters { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Creates a new SQL query builder.
	/// </summary>
	public SqlBuilder()
	{
	}

	/// <summary>
	/// Adds a SELECT clause to the query.
	/// </summary>
	/// <param name="columns">The columns to select. Use "*" for all columns.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Select( params string[] columns )
	{
		_query.Append( "SELECT " );
		_query.Append( columns.Length == 0 ? "*" : string.Join( ", ", columns ) );
		return this;
	}

	/// <summary>
	/// Adds a SELECT DISTINCT clause to the query.
	/// </summary>
	/// <param name="columns">The columns to select.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder SelectDistinct( params string[] columns )
	{
		_query.Append( "SELECT DISTINCT " );
		_query.Append( columns.Length == 0 ? "*" : string.Join( ", ", columns ) );
		return this;
	}

	/// <summary>
	/// Adds a FROM clause to the query.
	/// </summary>
	/// <param name="table">The table name.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder From( string table )
	{
		_query.Append( " FROM " );
		_query.Append( table );
		return this;
	}

	/// <summary>
	/// Adds a JOIN clause to the query.
	/// </summary>
	/// <param name="table">The table to join.</param>
	/// <param name="condition">The join condition.</param>
	/// <param name="parameters">Optional parameters for the condition.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Join( string table, string condition, object parameters = null )
	{
		_query.Append( " JOIN " );
		_query.Append( table );
		_query.Append( " ON " );
		_query.Append( ProcessCondition( condition, parameters ) );
		return this;
	}

	/// <summary>
	/// Adds a LEFT JOIN clause to the query.
	/// </summary>
	/// <param name="table">The table to join.</param>
	/// <param name="condition">The join condition.</param>
	/// <param name="parameters">Optional parameters for the condition.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder LeftJoin( string table, string condition, object parameters = null )
	{
		_query.Append( " LEFT JOIN " );
		_query.Append( table );
		_query.Append( " ON " );
		_query.Append( ProcessCondition( condition, parameters ) );
		return this;
	}

	/// <summary>
	/// Adds a WHERE clause to the query.
	/// </summary>
	/// <param name="condition">The WHERE condition.</param>
	/// <param name="parameters">Optional parameters for the condition.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Where( string condition, object parameters = null )
	{
		_query.Append( " WHERE " );
		_query.Append( ProcessCondition( condition, parameters ) );
		return this;
	}

	/// <summary>
	/// Adds an AND clause to an existing WHERE condition.
	/// </summary>
	/// <param name="condition">The AND condition.</param>
	/// <param name="parameters">Optional parameters for the condition.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder And( string condition, object parameters = null )
	{
		_query.Append( " AND " );
		_query.Append( ProcessCondition( condition, parameters ) );
		return this;
	}

	/// <summary>
	/// Adds an OR clause to an existing WHERE condition.
	/// </summary>
	/// <param name="condition">The OR condition.</param>
	/// <param name="parameters">Optional parameters for the condition.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Or( string condition, object parameters = null )
	{
		_query.Append( " OR " );
		_query.Append( ProcessCondition( condition, parameters ) );
		return this;
	}

	/// <summary>
	/// Adds an ORDER BY clause to the query.
	/// </summary>
	/// <param name="orderBy">The ORDER BY expression (e.g., "name ASC", "score DESC").</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder OrderBy( string orderBy )
	{
		_query.Append( " ORDER BY " );
		_query.Append( orderBy );
		return this;
	}

	/// <summary>
	/// Adds a GROUP BY clause to the query.
	/// </summary>
	/// <param name="columns">The columns to group by.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder GroupBy( params string[] columns )
	{
		_query.Append( " GROUP BY " );
		_query.Append( string.Join( ", ", columns ) );
		return this;
	}

	/// <summary>
	/// Adds a HAVING clause to the query (used with GROUP BY).
	/// </summary>
	/// <param name="condition">The HAVING condition.</param>
	/// <param name="parameters">Optional parameters for the condition.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Having( string condition, object parameters = null )
	{
		_query.Append( " HAVING " );
		_query.Append( ProcessCondition( condition, parameters ) );
		return this;
	}

	/// <summary>
	/// Adds a LIMIT clause to the query.
	/// </summary>
	/// <param name="count">The maximum number of rows to return.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Limit( int count )
	{
		_query.Append( " LIMIT " );
		_query.Append( count );
		return this;
	}

	/// <summary>
	/// Adds an OFFSET clause to the query.
	/// </summary>
	/// <param name="offset">The number of rows to skip.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Offset( int offset )
	{
		_query.Append( " OFFSET " );
		_query.Append( offset );
		return this;
	}

	/// <summary>
	/// Adds an INSERT INTO clause to the query.
	/// </summary>
	/// <param name="table">The table to insert into.</param>
	/// <param name="columns">The columns to insert values into.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder InsertInto( string table, params string[] columns )
	{
		_query.Append( "INSERT INTO " );
		_query.Append( table );

		if ( columns.Length > 0 )
		{
			_query.Append( " (" );
			_query.Append( string.Join( ", ", columns ) );
			_query.Append( ')' );
		}

		return this;
	}

	/// <summary>
	/// Adds an INSERT OR REPLACE clause to the query (upsert).
	/// </summary>
	/// <param name="table">The table to insert into.</param>
	/// <param name="columns">The columns to insert values into.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder InsertOrReplace( string table, params string[] columns )
	{
		_query.Append( "INSERT OR REPLACE INTO " );
		_query.Append( table );

		if ( columns.Length > 0 )
		{
			_query.Append( " (" );
			_query.Append( string.Join( ", ", columns ) );
			_query.Append( ')' );
		}

		return this;
	}

	/// <summary>
	/// Adds a VALUES clause with parameters from an object.
	/// </summary>
	/// <param name="values">An anonymous object containing the values.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Values( object values )
	{
		var type = values.GetType();
		var valueList = new List<string>();

		foreach ( var property in type.GetProperties() )
		{
			var paramName = AddParameter( property.GetValue( values ) );
			valueList.Add( paramName );
		}

		foreach ( var field in type.GetFields() )
		{
			var paramName = AddParameter( field.GetValue( values ) );
			valueList.Add( paramName );
		}

		_query.Append( " VALUES (" );
		_query.Append( string.Join( ", ", valueList ) );
		_query.Append( ')' );

		return this;
	}

	/// <summary>
	/// Adds an UPDATE clause to the query.
	/// </summary>
	/// <param name="table">The table to update.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Update( string table )
	{
		_query.Append( "UPDATE " );
		_query.Append( table );
		return this;
	}

	/// <summary>
	/// Adds a SET clause with values from an object.
	/// </summary>
	/// <param name="values">An anonymous object containing the column-value pairs.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Set( object values )
	{
		var type = values.GetType();
		var assignments = new List<string>();

		foreach ( var property in type.GetProperties() )
		{
			var paramName = AddParameter( property.GetValue( values ) );
			assignments.Add( $"{property.Name} = {paramName}" );
		}

		foreach ( var field in type.GetFields() )
		{
			var paramName = AddParameter( field.GetValue( values ) );
			assignments.Add( $"{field.Name} = {paramName}" );
		}

		_query.Append( " SET " );
		_query.Append( string.Join( ", ", assignments ) );

		return this;
	}

	/// <summary>
	/// Adds a DELETE FROM clause to the query.
	/// </summary>
	/// <param name="table">The table to delete from.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder DeleteFrom( string table )
	{
		_query.Append( "DELETE FROM " );
		_query.Append( table );
		return this;
	}

	/// <summary>
	/// Adds raw SQL to the query. Use with caution - prefer parameterized methods.
	/// </summary>
	/// <param name="sql">The raw SQL to append.</param>
	/// <returns>This builder for method chaining.</returns>
	public SqlBuilder Raw( string sql )
	{
		_query.Append( sql );
		return this;
	}

	/// <summary>
	/// Builds and returns the final SQL query string.
	/// </summary>
	/// <returns>The constructed SQL query.</returns>
	public string Build()
	{
		return _query.ToString();
	}

	/// <summary>
	/// Returns the constructed SQL query string.
	/// </summary>
	public override string ToString() => Build();

	private string AddParameter( object value )
	{
		var name = $"@p{_parameterIndex++}";
		Parameters[name.TrimStart( '@' )] = value;
		return name;
	}

	private string ProcessCondition( string condition, object parameters )
	{
		if ( parameters is null )
			return condition;

		var type = parameters.GetType();
		var result = condition;

		foreach ( var property in type.GetProperties() )
		{
			var newName = AddParameter( property.GetValue( parameters ) );
			var pattern = $@"@{property.Name}(?![a-zA-Z0-9_])";
			result = Regex.Replace( result, pattern, newName, RegexOptions.IgnoreCase );
		}

		foreach ( var field in type.GetFields() )
		{
			var newName = AddParameter( field.GetValue( parameters ) );
			var pattern = $@"@{field.Name}(?![a-zA-Z0-9_])";
			result = Regex.Replace( result, pattern, newName, RegexOptions.IgnoreCase );
		}

		return result;
	}
}
