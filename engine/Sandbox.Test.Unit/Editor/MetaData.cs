using System.IO;

namespace EditorTests;

[TestClass]
public partial class MetaData
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
		var fn = Path.Combine( System.Environment.CurrentDirectory, "unittest/metadata_large.json" );

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
}
