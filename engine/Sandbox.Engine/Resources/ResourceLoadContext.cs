using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Lets a <see cref="Resource"/> read from its file while the file data
/// is in memory, during <see cref="Resource.OnLoaded"/>. Blocks are written at
/// compile time, eg with <see cref="Resources.ResourceCompileContext.WriteBlockJson"/>.
/// Only valid during the call - copy out anything you want to keep.
/// </summary>
public readonly ref struct ResourceLoadContext
{
	readonly string _resourceName;
	readonly IntPtr _header;
	readonly int _dataSize;
	readonly ReadOnlySpan<byte> _source;

	/// <summary>
	/// Whether this context contains an original source file instead of a compiled resource container.
	/// </summary>
	public bool IsUncompiled { get; }

	internal ResourceLoadContext( string resourceName, ReadOnlySpan<byte> source )
	{
		_resourceName = resourceName;
		_source = source;
		IsUncompiled = true;
	}

	internal ResourceLoadContext( string resourceName, IntPtr header, int dataSize )
	{
		_resourceName = resourceName;
		_header = header;
		_dataSize = dataSize;
	}

	/// <summary>
	/// Reads the complete uncompiled source file. The returned span is only valid during Resource.OnLoaded.
	/// </summary>
	public ReadOnlySpan<byte> ReadSource()
	{
		if ( !IsUncompiled )
			throw new InvalidOperationException( "This context contains compiled resource data, not a source file." );

		return _source;
	}

	/// <summary>
	/// Reads the uncompiled source file as UTF-8 text, removing an optional byte order mark.
	/// The returned string can be retained after Resource.OnLoaded returns.
	/// </summary>
	public string ReadSourceAsString()
	{
		var source = ReadSource();
		if ( source.StartsWith( new byte[] { 0xef, 0xbb, 0xbf } ) )
			source = source[3..];

		return System.Text.Encoding.UTF8.GetString( source );
	}

	/// <summary>
	/// Does the compiled file contain this block? Block names are four character
	/// codes, eg "LIPS".
	/// </summary>
	public bool Exists( string blockName )
	{
		return !ReadData( blockName ).IsEmpty;
	}

	/// <summary>
	/// Read a named block's data. The span points directly into the loaded file,
	/// so it's only valid during <see cref="Resource.OnLoaded"/> - copy anything
	/// you want to keep.
	/// </summary>
	public unsafe ReadOnlySpan<byte> ReadData( string blockName )
	{
		if ( _header == IntPtr.Zero || blockName is null || blockName.Length != 4 )
			return default;

		var pBlock = NativeEngine.EngineGlue.ReadCompiledResourceFileBlock( blockName, _header, _dataSize, out var size );
		if ( pBlock == IntPtr.Zero || size <= 0 )
			return default;

		return new ReadOnlySpan<byte>( (void*)pBlock, size );
	}

	/// <summary>
	/// Read a named block as a utf-8 string, or null if the block doesn't exist.
	/// </summary>
	public string ReadString( string blockName )
	{
		var data = ReadData( blockName );
		if ( data.IsEmpty )
			return null;

		return System.Text.Encoding.UTF8.GetString( data );
	}

	/// <summary>
	/// Read a named block as json, eg <c>context.ReadJson&lt;List&lt;VisemeFrame&gt;&gt;( "LIPS" )</c>.
	/// </summary>
	public T ReadJson<T>( string blockName, T defaultValue = default )
	{
		var data = ReadData( blockName );
		if ( data.IsEmpty )
			return defaultValue;

		try
		{
			return JsonSerializer.Deserialize<T>( data, Json.options );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't read block '{blockName}' from {_resourceName}" );
			return defaultValue;
		}
	}
}
