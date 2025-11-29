using NativeEngine;
using Sandbox.Mounting;

namespace Sandbox;

public partial class Material
{
	/// <summary>
	/// Load a material from disk. Has internal cache.
	/// </summary>
	/// <param name="filename">The filepath to load the material from.</param>
	/// <returns>The loaded material, or null</returns>
	public static Material Load( string filename )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !string.IsNullOrWhiteSpace( filename ) && Directory.TryLoad( filename, ResourceType.Material, out object model ) && model is Material m )
			return m;

		return FromNative( NativeGlue.Resources.GetMaterial( filename ), filename );
	}

	/// <summary>
	/// Load a material from disk. Has internal cache.
	/// </summary>
	/// <param name="filename">The filepath to load the material from.</param>
	/// <returns>The loaded material, or null</returns>
	public static async Task<Material> LoadAsync( string filename )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !string.IsNullOrWhiteSpace( filename ) && await Directory.TryLoadAsync( filename, ResourceType.Material ) is Material m )
			return m;

		using var manifest = AsyncResourceLoader.Load( filename );
		if ( manifest is not null )
		{
			await manifest.WaitForLoad();
		}

		return FromNative( NativeGlue.Resources.GetMaterial( filename ) );
	}

	/// <summary>
	/// Try to make it so only one Material class exists for each material
	/// </summary>
	internal static Material FromNative( IMaterial native, string name = null )
	{
		if ( native.IsNull || !native.IsStrongHandleValid() )
			return null;

		var instanceId = native.GetBindingPtr().ToInt64();
		if ( NativeResourceCache.TryGetValue<Material>( instanceId, out var material ) )
		{
			// The already loaded Material has it's own strong handle, we need to destroy the one just given to us to prevent leak.
			native.DestroyStrongHandle();
			return material;
		}

		material = new Material( native, name ?? native.GetName() );
		NativeResourceCache.Add( instanceId, material );

		return material;
	}
}
