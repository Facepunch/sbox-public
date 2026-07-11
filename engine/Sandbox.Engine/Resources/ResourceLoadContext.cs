using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Lets a <see cref="Resource"/> read from its compiled file while the file data
/// is in memory, during <see cref="Resource.OnLoaded"/>. Blocks are written at
/// compile time, eg with <see cref="Resources.ResourceCompileContext.WriteBlockJson"/>.
/// Only valid during the call - copy out anything you want to keep.
/// </summary>
public readonly ref struct ResourceLoadContext
{
	readonly string _resourceName;
	readonly IntPtr _header;

	internal ResourceLoadContext( string resourceName, IntPtr header )
	{
		_resourceName = resourceName;
		_header = header;
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

		var pBlock = NativeEngine.EngineGlue.ReadCompiledResourceFileBlock( blockName, _header, out var size );
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
