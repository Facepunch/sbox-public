using Basalt.Backend;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// An engine resource as what the mirror's world draws with on the device Basalt renders on (<see cref="RenderHost.Device"/>).
/// On Source 2's device it's the resource's native handle, wrapped. On a Basalt.Gpu device (<c>-basaltgpu</c>) it's the
/// compiled file Basalt.Source2 reads by the resource's path: what was made at run time (procedural models, materials and
/// textures made in code) and attributes have no file, so they're null there.
/// </summary>
internal static class MirrorContent
{
	/// <summary>The Basalt.Gpu device Basalt renders on, or null when it's Source 2's.</summary>
	public static BackendDevice Gpu => RenderHost.Current.Device as BackendDevice;

	/// <summary>A model's mesh, every mesh group in it (an object's draws hide those it doesn't draw).</summary>
	public static RenderMesh Mesh( Model model )
	{
		if ( Gpu is not { } gpu ) return Source2Mesh.FromModel( model );

		var path = model?.ResourcePath;
		return string.IsNullOrEmpty( path ) ? null : gpu.LoadModel( path, ulong.MaxValue );
	}

	public static GpuMaterial Material( Material material )
	{
		if ( Gpu is not { } gpu ) return Source2Material.From( material );

		var path = material?.ResourcePath;
		return string.IsNullOrEmpty( path ) ? null : gpu.LoadMaterial( path );
	}

	public static GpuTexture Texture( Texture texture )
	{
		if ( Gpu is not { } gpu ) return Source2Texture.From( texture );

		var path = texture?.ResourcePath;
		if ( !string.IsNullOrEmpty( path ) ) return gpu.LoadTexture( path );

		// A material's texture (a sky's cubemap for fog and sky lighting) is the material's, read by its path
		return texture is not null && Sandbox.Texture.MaterialOrigins.TryGetValue( texture, out var origin ) && origin is Sandbox.Texture.MaterialOrigin o
			? gpu.MaterialTexture( o.Material, o.Parameter )
			: null;
	}

	/// <summary>Attributes, which only Source 2's device reads.</summary>
	public static ShaderParameters Parameters( RenderAttributes attributes ) => Gpu is null ? Source2Parameters.From( attributes ) : null;
}
