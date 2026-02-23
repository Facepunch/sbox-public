using System.Text.Json.Nodes;

namespace Sandbox.Resources;

public abstract class ResourceCompileContext
{
	/// <summary>
	/// The absolute path to the resource on disk
	/// </summary>
	public abstract string AbsolutePath { get; }

	/// <summary>
	/// The path relative to the assets folder
	/// </summary>
	public abstract string RelativePath { get; }

	/// <summary>
	/// The resource version can be important
	/// </summary>
	public abstract int ResourceVersion { get; set; }

	internal abstract int WriteBlock( string blockName, IntPtr data, int count );

	/// <summary>
	/// Add a reference. This means that the resource we're compiling depends on this resource.
	/// </summary>
	public abstract void AddRuntimeReference( string path );

	/// <summary>
	/// Add a reference that is needed to compile this resource, but isn't actually needed once compiled.
	/// </summary>
	public abstract void AddCompileReference( string path );

	/// <summary>
	/// Add a game file reference. This file will be included in packages but is not a native resource.
	/// Use this for arbitrary data files that are loaded by managed code (e.g. navdata files).
	/// </summary>
	public abstract void AddGameFileReference( string path );

	/// <summary>
	/// Get the streaming data to write to
	/// </summary>
	public DataStream StreamingData { get; internal set; }

	/// <summary>
	/// Get the data to write to
	/// </summary>
	public DataStream Data { get; internal set; }

	/// <summary>
	/// Create a child resource
	/// </summary>
	public abstract Child CreateChild( string absolutePath );

	/// <summary>
	/// Load the json and scan it for paths or any embedded resources
	/// </summary>
	public abstract string ScanJson( string json );

	/// <summary>
	/// Read the source, either from in memory, or from disk
	/// </summary>
	public abstract byte[] ReadSource();

	/// <summary>
	/// Read the source, either from in memory, or from disk
	/// </summary>
	public string ReadSourceAsString()
	{
		var data = ReadSource();
		return System.Text.Encoding.UTF8.GetString( data );
	}

	/// <summary>
	/// Read the source, either from in memory, or from disk
	/// </summary>
	public JsonObject ReadSourceAsJson()
	{
		try
		{
			var jsonString = ReadSourceAsString();
			if ( string.IsNullOrWhiteSpace( jsonString ) ) return null;

			return Json.ParseToJsonObject( jsonString );
		}
		catch
		{
			return null;
		}
	}

	public abstract class Child
	{
		public abstract bool Compile();
		public abstract void SetInputData( string data );
	}

	public abstract class DataStream
	{
		internal readonly ResourceCompileContext source;

		public abstract void Write( byte[] bytes );

		/// <summary>
		/// Write a string with a null terminator
		/// </summary>
		public void Write( string strValue )
		{
			Write( System.Text.Encoding.UTF8.GetBytes( strValue ) );
			Write( new byte[] { 0 } );
		}

		//public abstract void SetAlignment( int v );
	}
}
