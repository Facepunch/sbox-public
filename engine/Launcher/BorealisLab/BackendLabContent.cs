using Borealis;
using Borealis.Backend;
using Borealis.Source2.Models;

namespace Sandbox.BorealisLab;

/// <summary>
/// A <see cref="Lab.LabWorld"/>'s content on a Borealis.Gpu device: compiled models, materials and textures read by
/// Borealis.Source2 through <see cref="BackendDevice"/>. Models made in code (<see cref="Lab.LodTestModel"/>) are saved by the
/// engine and read back, and skinned objects are posed by a native <see cref="SceneModel"/>: both take the engine, which
/// Borealis Lab starts beside the device. What that device can't draw yet stays out: the 2D sky (its material is null) and
/// the runtime sky cube (the default cubemap stands in).
/// </summary>
internal sealed class BackendLabContent( BackendDevice device ) : Lab.ILabContent, IDisposable
{
	readonly Dictionary<string, RenderMesh> meshes = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<RenderMesh, string> paths = new( ReferenceEqualityComparer.Instance );

	public RenderMesh Mesh( string path )
	{
		if ( meshes.TryGetValue( path, out var mesh ) ) return mesh;

		mesh = path == Lab.LodTestModel.Path
			? BackendModel.Create( device, ModelFile.Read( Lab.LodTestModel.Create().SaveToVmdl(), device.Content.TryRead ), meshGroups: ulong.MaxValue, drawFastPath: true )
			: device.LoadModel( path ) ?? throw new InvalidOperationException( $"{path} isn't built, or isn't a model Borealis.Source2 reads" );
		paths[mesh] = path;
		return meshes[path] = mesh;
	}

	public GpuMaterial Material( string path ) => path is null ? null : device.LoadMaterial( path );

	public GpuTexture Texture( string path ) => device.LoadTexture( path );

	public (GpuMaterial Material, GpuTexture Cubemap) Sky( string path ) => (null, device.LoadTexture( "textures/cubemaps/default.vtex" ));

	public GpuTexture SkyCube() => device.LoadTexture( "textures/cubemaps/default.vtex" );

	// Where skinned objects are posed - never rendered, only animated
	SceneWorld animationWorld;

	/// <summary>
	/// A native <see cref="SceneModel"/> in a world that's never rendered evaluates the sequence, and the object takes its
	/// bones and bounds.
	/// </summary>
	public void Pose( MeshObject obj, string sequence, float time )
	{
		if ( !paths.TryGetValue( obj.Mesh, out var path ) || Model.Load( path ) is not { IsError: false } model ) return;

		animationWorld ??= new SceneWorld();
		var animator = new SceneModel( animationWorld, model, obj.Transform ) { UseAnimGraph = false, PlaybackRate = 0 };
		animator.CurrentSequence.Name = sequence;
		animator.CurrentSequence.TimeNormalized = time;
		animator.Update( 0 );

		var bones = new Transform[model.BoneCount];
		animator.GetBoneWorldTransforms( bones );
		obj.SetBones( bones );

		// Grid objects are only placed, not rotated or scaled, so local bounds are the world bounds moved back
		var bounds = animator.Bounds;
		var position = obj.Transform.Position;
		obj.LocalBounds = new BBox( bounds.Mins - position, bounds.Maxs - position );

		animator.Delete();
	}

	public void Dispose()
	{
		if ( animationWorld is null ) return;

		// Deleted now rather than at a frame's end, so nothing it posed with is still held at shutdown
		animationWorld.Delete();
		animationWorld = null;
		SceneWorld.FlushQueuedDeletes();
	}
}
