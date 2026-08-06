using System.Text.Json;
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
	/// Write a named block of data into the compiled resource. The name is a four
	/// character code, eg "LIPS". Read it back at runtime when the resource loads,
	/// in <see cref="Resource.OnLoaded"/>.
	/// </summary>
	public unsafe void WriteBlock( string blockName, ReadOnlySpan<byte> data )
	{
		if ( blockName is null || blockName.Length != 4 )
			throw new ArgumentException( "Block names are four character codes, eg \"LIPS\"", nameof( blockName ) );

		if ( data.IsEmpty )
			return;

		fixed ( byte* ptr = data )
		{
			WriteBlock( blockName, (IntPtr)ptr, data.Length );
		}
	}

	/// <summary>
	/// Serialize an object to json and write it to a named block in the compiled
	/// resource. Read it back at runtime in <see cref="Resource.OnLoaded"/>.
	/// </summary>
	public void WriteBlockJson( string blockName, object obj )
	{
		WriteBlock( blockName, JsonSerializer.SerializeToUtf8Bytes( obj, Json.options ) );
	}

	/// <summary>
	/// Add a reference. This means that the resource we're compiling depends on this resource.
	/// </summary>
	public abstract void AddRuntimeReference( string path );

	/// <summary>
	/// Add a reference that is needed to compile this resource, but isn't actually needed once compiled.
	/// Optional references may not exist - the compile won't fail without them, but if the file is
	/// created or modified later we'll recompile.
	/// </summary>
	public abstract void AddCompileReference( string path, bool optional = false );

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

	JsonObject _meta;
	bool _metaRead;

	/// <summary>
	/// The asset's .meta file as json, or null if it doesn't have one.
	/// </summary>
	public JsonObject ReadMeta()
	{
		if ( _metaRead )
			return _meta;

		_metaRead = true;

		var metaPath = AbsolutePath + ".meta";

		// Depend on the meta even when it doesn't exist yet - authoring one later
		// (eg adding visemes to a sound for the first time) should recompile us
		AddCompileReference( metaPath, optional: true );

		try
		{
			if ( System.IO.File.Exists( metaPath ) )
			{
				_meta = Json.ParseToJsonObject( System.IO.File.ReadAllText( metaPath ) );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't read meta file {metaPath}" );
		}

		return _meta;
	}

	/// <summary>
	/// Read a value from the asset's .meta file,
	/// eg <c>Context.ReadMeta&lt;List&lt;VisemeFrame&gt;&gt;( "visemes" )</c>.
	/// </summary>
	public T ReadMeta<T>( string key, T defaultValue = default )
	{
		var meta = ReadMeta();
		if ( meta is null || meta[key] is not JsonNode node )
			return defaultValue;

		try
		{
			return node.Deserialize<T>( Json.options );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't read '{key}' from {AbsolutePath}.meta" );
			return defaultValue;
		}
	}

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
