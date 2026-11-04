using System.IO;

namespace EditorTests;

[TestClass]
public partial class MetaDataTest
{
	/// <summary>
	/// File shouldn't get created on pure read
	/// </summary>
	[TestMethod]
	public void NoFileCreateOnRead()
	{
		var fn = Path.Combine( System.Environment.CurrentDirectory, "output_file.json" );
		var md = new Editor.MetaData( fn );

		Assert.IsNull( md.Get<string>( "My Key Name", null ) );
		Assert.AreEqual( md.Get( "My Key Name", "missing" ), "missing" );

		Assert.IsNotNull( md );
		Assert.IsFalse( System.IO.File.Exists( fn ) );
	}

	/// <summary>
	/// Can we read values from an existing file?
	/// </summary>
	[TestMethod]
	public void Reading()
	{
		var fn = Path.Combine( System.Environment.CurrentDirectory, "unittest/metadata_read_test.json" );
		Assert.IsTrue( System.IO.File.Exists( fn ) );

		var md = new Editor.MetaData( fn );
		Assert.IsNotNull( md );

		Assert.IsTrue( md.GetElement( "string_value" ).HasValue );

		Assert.AreEqual( "string!", md.Get( "string_value", "missing" ) );

		Assert.AreEqual( 567, md.Get<int>( "int_value" ) );
		Assert.AreEqual( 567, md.Get<long>( "int_value" ) );
		Assert.AreEqual( 567ul, md.Get<ulong>( "int_value" ) );
		Assert.AreEqual( 567u, md.Get<uint>( "int_value" ) );
		Assert.AreEqual( 567, md.GetInt( "int_value", 0 ) );

		Assert.AreEqual( 567.89f, md.Get<float>( "float_value" ) );
		Assert.AreEqual( 567.89, md.Get<double>( "float_value" ) );
		Assert.AreEqual( 567.89f, md.GetFloat( "float_value", 0 ) );

		var intArray = md.Get<int[]>( "int_array" );
		Assert.AreEqual( 2, intArray[0] );
		Assert.AreEqual( 3, intArray[1] );
		Assert.AreEqual( 4, intArray[2] );
		Assert.AreEqual( 5, intArray[3] );
		Assert.AreEqual( 6, intArray[4] );

	}

	/// <summary>
	/// Can we write to a file without it getting corrupted?
	/// </summary>
	[TestMethod]
	public void Writing()
	{
		var fn = Path.Combine( System.Environment.CurrentDirectory, ".source2", "meta_written.json" );

		if ( System.IO.File.Exists( fn ) )
			System.IO.File.Delete( fn );

		Assert.IsFalse( System.IO.File.Exists( fn ) );

		// First write
		{
			var md = new Editor.MetaData( fn );

			md.Set( "Hello", "Hello Sir" );
			md.Set( "PersistantValue", "Still Here" );
		}

		// Make sure it wrote properly
		{
			var md = new Editor.MetaData( fn );

			Assert.AreEqual( "Hello Sir", md.GetString( "Hello" ) );
			Assert.AreEqual( "Still Here", md.GetString( "PersistantValue" ) );
		}

		// Overwrite, and add
		{
			var md = new Editor.MetaData( fn );

			md.Set( "Hello", 345 );
			md.Set( "Two", 678 );

			Assert.AreEqual( "Still Here", md.GetString( "PersistantValue" ) );
		}

		// Make sure it wrote properly
		{
			var md = new Editor.MetaData( fn );

			Assert.AreEqual( null, md.GetString( "Hello" ) );
			Assert.AreEqual( 345, md.GetInt( "Hello" ) );
			Assert.AreEqual( "Still Here", md.GetString( "PersistantValue" ) );
		}

		Assert.IsTrue( System.IO.File.Exists( fn ) );
	}

	/// <summary>
	/// Does it all break down when reading and writing values to an already large file?
	/// </summary>
	[TestMethod]
	public void ReadWriteLargeFile()
	{
		// Don't mutate the git-tracked fixture - copy it into .source2 and work on the copy
		var source = Path.Combine( System.Environment.CurrentDirectory, "unittest/metadata_large.json" );
		Assert.IsTrue( System.IO.File.Exists( source ) );

		var fn = Path.Combine( System.Environment.CurrentDirectory, ".source2", "meta_large.json" );
		System.IO.Directory.CreateDirectory( Path.GetDirectoryName( fn ) );
		System.IO.File.Copy( source, fn, true );

		Assert.IsTrue( System.IO.File.Exists( fn ) );

		// First write
		{
			var md = new Editor.MetaData( fn );

			md.Set( "Hello", "Hello Sir" );
			md.Set( "PersistantValue", "Still Here" );
		}

		// Make sure it wrote properly
		{
			var md = new Editor.MetaData( fn );

			Assert.AreEqual( "Hello Sir", md.GetString( "Hello" ) );
			Assert.AreEqual( "Still Here", md.GetString( "PersistantValue" ) );
		}

		// Overwrite, and add
		{
			var md = new Editor.MetaData( fn );

			md.Set( "Hello", 345 );
			md.Set( "Two", 678 );

			Assert.AreEqual( "Still Here", md.GetString( "PersistantValue" ) );
		}

		// Make sure it wrote properly
		{
			var md = new Editor.MetaData( fn );

			Assert.AreEqual( null, md.GetString( "Hello" ) );
			Assert.AreEqual( 345, md.GetInt( "Hello" ) );
			Assert.AreEqual( "Still Here", md.GetString( "PersistantValue" ) );
		}

		Assert.IsTrue( System.IO.File.Exists( fn ) );
	}

	static string CreateTempMetaPath( string name )
	{
		var fn = Path.Combine( System.Environment.CurrentDirectory, ".source2", name );
		System.IO.Directory.CreateDirectory( Path.GetDirectoryName( fn ) );

		if ( System.IO.File.Exists( fn ) )
			System.IO.File.Delete( fn );

		return fn;
	}

	/// <summary>
	/// Write the file from outside of MetaData, bumping the write time so the change is always visible
	/// </summary>
	static void WriteExternally( string fn, string json )
	{
		var previous = System.IO.File.Exists( fn ) ? System.IO.File.GetLastWriteTimeUtc( fn ) : System.DateTime.UtcNow;

		System.IO.File.WriteAllText( fn, json );
		System.IO.File.SetLastWriteTimeUtc( fn, previous.AddSeconds( 10 ) );
	}

	/// <summary>
	/// A Set on the same instance is visible to the next Get, even though reads are cached
	/// </summary>
	[TestMethod]
	public void SetThenGetSameInstance()
	{
		var fn = CreateTempMetaPath( "meta_cache_set.json" );
		var md = new Editor.MetaData( fn );

		Assert.AreEqual( "missing", md.Get( "Value", "missing" ) );

		md.Set( "Value", "one" );
		Assert.AreEqual( "one", md.GetString( "Value" ) );

		// same length as before
		md.Set( "Value", "two" );
		Assert.AreEqual( "two", md.GetString( "Value" ) );

		md.Set<string>( "Value", null );
		Assert.AreEqual( "missing", md.Get( "Value", "missing" ) );
	}

	/// <summary>
	/// Changes made to the file by something else are picked up
	/// </summary>
	[TestMethod]
	public void ExternalChangeIsRead()
	{
		var fn = CreateTempMetaPath( "meta_cache_external.json" );
		WriteExternally( fn, """{ "Value": "one" }""" );

		var md = new Editor.MetaData( fn );
		Assert.AreEqual( "one", md.GetString( "Value" ) );

		// same length, only the write time tells them apart
		WriteExternally( fn, """{ "Value": "two" }""" );
		Assert.AreEqual( "two", md.GetString( "Value" ) );

		// a different MetaData instance writing to the same file
		new Editor.MetaData( fn ).Set( "Value", "three" );
		Assert.AreEqual( "three", md.GetString( "Value" ) );
	}

	/// <summary>
	/// Deleting the file and creating it again is picked up
	/// </summary>
	[TestMethod]
	public void DeleteAndRecreateIsRead()
	{
		var fn = CreateTempMetaPath( "meta_cache_delete.json" );
		WriteExternally( fn, """{ "Value": "one" }""" );

		var md = new Editor.MetaData( fn );
		Assert.AreEqual( "one", md.GetString( "Value" ) );

		System.IO.File.Delete( fn );
		Assert.IsNull( md.GetString( "Value" ) );

		WriteExternally( fn, """{ "Value": "two" }""" );
		Assert.AreEqual( "two", md.GetString( "Value" ) );
	}

	/// <summary>
	/// A broken file reads as empty, and reads properly again once it's fixed
	/// </summary>
	[TestMethod]
	public void InvalidJsonThenFixed()
	{
		var fn = CreateTempMetaPath( "meta_cache_invalid.json" );
		WriteExternally( fn, """{ "Value": "one" """ );

		var md = new Editor.MetaData( fn );
		Assert.AreEqual( "missing", md.Get( "Value", "missing" ) );
		Assert.AreEqual( "missing", md.Get( "Value", "missing" ) );

		WriteExternally( fn, """{ "Value": "one" }""" );
		Assert.AreEqual( "one", md.GetString( "Value" ) );
	}
}
