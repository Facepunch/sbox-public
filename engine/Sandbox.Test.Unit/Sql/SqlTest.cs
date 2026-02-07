using Sandbox.Utility;

namespace Sql;

[TestClass]
public class SqlTest
{
	[TestMethod]
	public void CreateInMemoryDatabase()
	{
		using var db = SqlDatabase.CreateInMemory();
		Assert.IsNotNull( db );
		Assert.AreEqual( ":memory:", db.DatabasePath );
	}

	[TestMethod]
	public void CreateTable()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE test ( id INTEGER PRIMARY KEY, name TEXT )" );

		Assert.IsTrue( db.TableExists( "test" ) );
		Assert.IsFalse( db.TableExists( "nonexistent" ) );
	}

	[TestMethod]
	public void InsertAndQuery()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT, score INTEGER )" );
		db.Execute( "INSERT INTO users (name, score) VALUES (@name, @score)", new { name = "Alice", score = 100 } );
		db.Execute( "INSERT INTO users (name, score) VALUES (@name, @score)", new { name = "Bob", score = 200 } );

		var results = db.Query( "SELECT * FROM users ORDER BY id" );

		Assert.AreEqual( 2, results.Count );
		Assert.AreEqual( "Alice", results[0]["name"] );
		Assert.AreEqual( 100L, results[0]["score"] );
		Assert.AreEqual( "Bob", results[1]["name"] );
		Assert.AreEqual( 200L, results[1]["score"] );
	}

	[TestMethod]
	public void QueryRow()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );
		db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );
		db.Execute( "INSERT INTO users (name) VALUES ('Bob')" );

		var firstRow = db.QueryRow( "SELECT * FROM users ORDER BY id" );
		Assert.AreEqual( "Alice", firstRow["name"] );

		var secondRow = db.QueryRow( "SELECT * FROM users ORDER BY id", row: 1 );
		Assert.AreEqual( "Bob", secondRow["name"] );

		var noRow = db.QueryRow( "SELECT * FROM users WHERE id = 999" );
		Assert.IsNull( noRow );
	}

	[TestMethod]
	public void QueryValue()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );
		db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );
		db.Execute( "INSERT INTO users (name) VALUES ('Bob')" );

		var count = db.QueryValue<long>( "SELECT COUNT(*) FROM users" );
		Assert.AreEqual( 2L, count );

		var name = db.QueryValue<string>( "SELECT name FROM users WHERE id = 1" );
		Assert.AreEqual( "Alice", name );
	}

	[TestMethod]
	public void ParameterizedQuery()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );
		db.Execute( "INSERT INTO users (name) VALUES (@name)", new { name = "Test User" } );

		var results = db.Query( "SELECT * FROM users WHERE name = @name", new { name = "Test User" } );

		Assert.AreEqual( 1, results.Count );
		Assert.AreEqual( "Test User", results[0]["name"] );
	}

	[TestMethod]
	public void LastInsertRowId()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );
		db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );

		var rowId = db.LastInsertRowId();
		Assert.AreEqual( 1L, rowId );

		db.Execute( "INSERT INTO users (name) VALUES ('Bob')" );
		rowId = db.LastInsertRowId();
		Assert.AreEqual( 2L, rowId );
	}

	[TestMethod]
	public void RowsAffected()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );
		db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );
		db.Execute( "INSERT INTO users (name) VALUES ('Bob')" );
		db.Execute( "INSERT INTO users (name) VALUES ('Charlie')" );

		db.Execute( "DELETE FROM users WHERE id > 1" );
		var affected = db.RowsAffected();

		Assert.AreEqual( 2, affected );
	}

	[TestMethod]
	public void Transaction()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );

		using ( var transaction = db.BeginTransaction() )
		{
			db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );
			db.Execute( "INSERT INTO users (name) VALUES ('Bob')" );
			transaction.Commit();
		}

		var count = db.QueryValue<long>( "SELECT COUNT(*) FROM users" );
		Assert.AreEqual( 2L, count );
	}

	[TestMethod]
	public void TransactionRollback()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );

		using ( var transaction = db.BeginTransaction() )
		{
			db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );
			db.Execute( "INSERT INTO users (name) VALUES ('Bob')" );
			transaction.Rollback();
		}

		var count = db.QueryValue<long>( "SELECT COUNT(*) FROM users" );
		Assert.AreEqual( 0L, count );
	}

	[TestMethod]
	public void TransactionAutoRollbackOnDispose()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );

		using ( db.BeginTransaction() )
		{
			db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );
			// No commit, let it dispose
		}

		var count = db.QueryValue<long>( "SELECT COUNT(*) FROM users" );
		Assert.AreEqual( 0L, count );
	}

	[TestMethod]
	public void InTransactionHelper()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );

		db.InTransaction( () =>
		{
			db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );
			db.Execute( "INSERT INTO users (name) VALUES ('Bob')" );
		} );

		var count = db.QueryValue<long>( "SELECT COUNT(*) FROM users" );
		Assert.AreEqual( 2L, count );
	}

	[TestMethod]
	public void IndexExists()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );
		db.Execute( "CREATE INDEX idx_users_name ON users (name)" );

		Assert.IsTrue( db.IndexExists( "idx_users_name" ) );
		Assert.IsFalse( db.IndexExists( "nonexistent_index" ) );
	}

	[TestMethod]
	public void NullValues()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT, bio TEXT )" );
		db.Execute( "INSERT INTO users (name, bio) VALUES (@name, @bio)", new { name = "Alice", bio = (string)null } );

		var row = db.QueryRow( "SELECT * FROM users WHERE id = 1" );

		Assert.AreEqual( "Alice", row["name"] );
		Assert.IsNull( row["bio"] );
	}

	[TestMethod]
	public void CaseInsensitiveColumnNames()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( ID INTEGER PRIMARY KEY, Name TEXT )" );
		db.Execute( "INSERT INTO users (Name) VALUES ('Alice')" );

		var row = db.QueryRow( "SELECT * FROM users" );

		// Should work regardless of case
		Assert.AreEqual( "Alice", row["name"] );
		Assert.AreEqual( "Alice", row["NAME"] );
		Assert.AreEqual( "Alice", row["Name"] );
	}

	[TestMethod]
	public async Task QueryAsync()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT )" );
		db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );
		db.Execute( "INSERT INTO users (name) VALUES ('Bob')" );

		var results = await db.QueryAsync( "SELECT * FROM users ORDER BY id" );

		Assert.AreEqual( 2, results.Count );
		Assert.AreEqual( "Alice", results[0]["name"] );
		Assert.AreEqual( "Bob", results[1]["name"] );
	}

	[TestMethod]
	public void ErrorHandling_InvalidSql()
	{
		using var db = SqlDatabase.CreateInMemory();

		Assert.ThrowsException<Microsoft.Data.Sqlite.SqliteException>( () =>
		{
			db.Query( "INVALID SQL SYNTAX" );
		} );
	}

	[TestMethod]
	public void ErrorHandling_NonExistentTable()
	{
		using var db = SqlDatabase.CreateInMemory();

		Assert.ThrowsException<Microsoft.Data.Sqlite.SqliteException>( () =>
		{
			db.Query( "SELECT * FROM nonexistent_table" );
		} );
	}

	[TestMethod]
	public void ErrorHandling_ConstraintViolation()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT UNIQUE )" );
		db.Execute( "INSERT INTO users (name) VALUES ('Alice')" );

		Assert.ThrowsException<Microsoft.Data.Sqlite.SqliteException>( () =>
		{
			db.Execute( "INSERT INTO users (name) VALUES ('Alice')" ); // Duplicate
		} );
	}
}

[TestClass]
public class SqlBuilderTest
{
	[TestMethod]
	public void SelectAll()
	{
		var query = new SqlBuilder()
			.Select()
			.From( "users" )
			.Build();

		Assert.AreEqual( "SELECT * FROM users", query );
	}

	[TestMethod]
	public void SelectColumns()
	{
		var query = new SqlBuilder()
			.Select( "id", "name", "email" )
			.From( "users" )
			.Build();

		Assert.AreEqual( "SELECT id, name, email FROM users", query );
	}

	[TestMethod]
	public void SelectWithWhere()
	{
		var builder = new SqlBuilder()
			.Select()
			.From( "users" )
			.Where( "id = @id", new { id = 5 } );

		var query = builder.Build();

		Assert.AreEqual( "SELECT * FROM users WHERE id = @p0", query );
		Assert.AreEqual( 5, builder.Parameters["p0"] );
	}

	[TestMethod]
	public void SelectWithMultipleConditions()
	{
		var builder = new SqlBuilder()
			.Select()
			.From( "users" )
			.Where( "score > @min", new { min = 100 } )
			.And( "score < @max", new { max = 500 } );

		var query = builder.Build();

		Assert.AreEqual( "SELECT * FROM users WHERE score > @p0 AND score < @p1", query );
		Assert.AreEqual( 100, builder.Parameters["p0"] );
		Assert.AreEqual( 500, builder.Parameters["p1"] );
	}

	[TestMethod]
	public void SelectWithOrderAndLimit()
	{
		var query = new SqlBuilder()
			.Select()
			.From( "users" )
			.OrderBy( "score DESC" )
			.Limit( 10 )
			.Offset( 5 )
			.Build();

		Assert.AreEqual( "SELECT * FROM users ORDER BY score DESC LIMIT 10 OFFSET 5", query );
	}

	[TestMethod]
	public void InsertWithValues()
	{
		var builder = new SqlBuilder()
			.InsertInto( "users", "name", "score" )
			.Values( new { name = "Alice", score = 100 } );

		var query = builder.Build();

		Assert.AreEqual( "INSERT INTO users (name, score) VALUES (@p0, @p1)", query );
		Assert.AreEqual( "Alice", builder.Parameters["p0"] );
		Assert.AreEqual( 100, builder.Parameters["p1"] );
	}

	[TestMethod]
	public void Update()
	{
		var builder = new SqlBuilder()
			.Update( "users" )
			.Set( new { name = "Bob", score = 200 } )
			.Where( "id = @id", new { id = 1 } );

		var query = builder.Build();

		Assert.AreEqual( "UPDATE users SET name = @p0, score = @p1 WHERE id = @p2", query );
	}

	[TestMethod]
	public void Delete()
	{
		var builder = new SqlBuilder()
			.DeleteFrom( "users" )
			.Where( "id = @id", new { id = 1 } );

		var query = builder.Build();

		Assert.AreEqual( "DELETE FROM users WHERE id = @p0", query );
	}

	[TestMethod]
	public void Join()
	{
		var query = new SqlBuilder()
			.Select( "u.name", "p.title" )
			.From( "users u" )
			.Join( "posts p", "p.user_id = u.id" )
			.Build();

		Assert.AreEqual( "SELECT u.name, p.title FROM users u JOIN posts p ON p.user_id = u.id", query );
	}

	[TestMethod]
	public void GroupByHaving()
	{
		var builder = new SqlBuilder()
			.Select( "user_id", "COUNT(*) as post_count" )
			.From( "posts" )
			.GroupBy( "user_id" )
			.Having( "COUNT(*) > @min", new { min = 5 } );

		var query = builder.Build();

		Assert.AreEqual( "SELECT user_id, COUNT(*) as post_count FROM posts GROUP BY user_id HAVING COUNT(*) > @p0", query );
	}

	[TestMethod]
	public void ExecuteWithDatabase()
	{
		using var db = SqlDatabase.CreateInMemory();

		db.Execute( "CREATE TABLE users ( id INTEGER PRIMARY KEY, name TEXT, score INTEGER )" );

		// Insert with builder
		var insertBuilder = new SqlBuilder()
			.InsertInto( "users", "name", "score" )
			.Values( new { name = "Alice", score = 100 } );

		db.Execute( insertBuilder.Build(), insertBuilder.Parameters );

		// Query with builder
		var selectBuilder = new SqlBuilder()
			.Select()
			.From( "users" )
			.Where( "score >= @min", new { min = 50 } );

		var results = db.Query( selectBuilder.Build(), selectBuilder.Parameters );

		Assert.AreEqual( 1, results.Count );
		Assert.AreEqual( "Alice", results[0]["name"] );
	}
}

[TestClass]
public class SqlEscapeTest
{
	[TestMethod]
	public void EscapeSimpleString()
	{
		var result = Sandbox.Utility.Sql.Escape( "hello" );
		Assert.AreEqual( "'hello'", result );
	}

	[TestMethod]
	public void EscapeStringWithQuotes()
	{
		var result = Sandbox.Utility.Sql.Escape( "it's a test" );
		Assert.AreEqual( "'it''s a test'", result );
	}

	[TestMethod]
	public void EscapeStringWithMultipleQuotes()
	{
		var result = Sandbox.Utility.Sql.Escape( "it's John's test" );
		Assert.AreEqual( "'it''s John''s test'", result );
	}

	[TestMethod]
	public void EscapeStringWithoutQuotes()
	{
		var result = Sandbox.Utility.Sql.Escape( "hello", includeQuotes: false );
		Assert.AreEqual( "hello", result );
	}

	[TestMethod]
	public void EscapeNullString()
	{
		var result = Sandbox.Utility.Sql.Escape( null );
		Assert.AreEqual( "''", result );

		var resultNoQuotes = Sandbox.Utility.Sql.Escape( null, includeQuotes: false );
		Assert.AreEqual( "", resultNoQuotes );
	}

	[TestMethod]
	public void EscapeStringWithNullCharacter()
	{
		var result = Sandbox.Utility.Sql.Escape( "hello\0world" );
		Assert.AreEqual( "'hello'", result );
	}
}
