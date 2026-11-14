using Borealis;
using Borealis.Backend;

namespace Sandbox.BorealisLab;

/// <summary>
/// A <see cref="Lab.LabWorld"/>'s content on a Borealis.Gpu device: compiled models, materials and textures read by
/// Borealis.Source2 through <see cref="BackendDevice"/>. What that device can't draw yet stays out: the 2D sky (its
/// material is null), the runtime sky cube (a black placeholder) and poses (skinned meshes draw in their bind pose).
/// </summary>
internal sealed class BackendLabContent( BackendDevice device ) : Lab.ILabContent
{
	readonly Dictionary<string, RenderMesh> meshes = new( StringComparer.OrdinalIgnoreCase );

	public RenderMesh Mesh( string path )
	{
		if ( meshes.TryGetValue( path, out var mesh ) ) return mesh;
		return meshes[path] = device.LoadModel( path ) ?? throw new InvalidOperationException( $"{path} isn't built, or isn't a model Borealis.Source2 reads" );
	}

	public GpuMaterial Material( string path ) => path is null ? null : device.LoadMaterial( path );

	public GpuTexture Texture( string path ) => device.LoadTexture( path );

	public (GpuMaterial Material, GpuTexture Cubemap) Sky( string path ) => (null, device.LoadTexture( "textures/cubemaps/default.vtex" ));

	public GpuTexture SkyCube() => device.LoadTexture( "textures/cubemaps/default.vtex" );

	public void Pose( MeshObject obj, string sequence, float time ) { }
}
