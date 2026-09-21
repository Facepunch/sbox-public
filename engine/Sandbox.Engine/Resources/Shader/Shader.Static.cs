using NativeEngine;

namespace Sandbox;

public partial class Shader
{
	/// <summary>
	/// Try to make it so only one Shader class exists for each shader
	/// </summary>
	internal static Shader FromNative( CVfx native, string name = null )
	{
		if ( native.IsNull || !native.IsStrongHandleValid() )
			return null;

		var instanceId = native.GetBindingPtr().ToInt64();
		if ( NativeResourceCache.TryGetValue<Shader>( instanceId, out var shader ) )
		{
			// If we're using a cached one we don't need this handle, we'll leak
			native.DestroyStrongHandle();

			return shader;
		}

		shader = new Shader( native, name ?? native.GetFilename() );
		NativeResourceCache.Add( instanceId, shader );

		return shader;
	}

	/// <summary>
	/// Load a shader by file path.
	/// </summary>
	/// <param name="filename">The file path to load as a shader.</param>
	/// <returns>The loaded shader, or null</returns>
	public static Shader Load( string filename ) => Load( (ResourceId)filename );

	internal static Shader Load( ResourceId id )
	{
		ThreadSafe.AssertIsMainThread();

		if ( id.Guid is Guid guid )
		{
			if ( Game.Resources.TryGet<Shader>( guid, out var resource ) )
				return resource;

			var native = NativeGlue.Resources.GetShader( id.Path, guid );
			return FromNative( native, name: native.IsError() ? id.Path : null );
		}

		return FromNative( NativeGlue.Resources.GetShader( id.Path, Guid.Empty ), id.Path );
	}

}
