using Borealis;
using Borealis.Gpu.Source2;

namespace Sandbox.Lab;

/// <summary>
/// A lab world's content through the engine: models, materials and textures loaded as resources and wrapped for Borealis's
/// Source 2 device. Models are kept for the life of this object - a frame still on the GPU can be reading an old world's
/// buffers. Skinned objects are posed by a native <see cref="SceneModel"/>, which needs the animation system.
/// </summary>
internal sealed class Source2LabContent : ILabContent, IDisposable
{
	readonly string logName;
	readonly Dictionary<string, (Model Model, RenderMesh Mesh)> meshes = new();

	// Made once, like the models - a frame still on the GPU can be using it
	Texture skyCube;

	// Where skinned objects are posed - never rendered, only animated
	SceneWorld animationWorld;

	/// <param name="logName">What the log lines start with.</param>
	public Source2LabContent( string logName ) => this.logName = logName;

	public RenderMesh Mesh( string path )
	{
		if ( meshes.TryGetValue( path, out var entry ) ) return entry.Mesh;

		var model = path == LodTestModel.Path ? LodTestModel.Create() : Model.Load( path );
		var mesh = Source2Mesh.FromModel( model );
		meshes[path] = (model, mesh);

		var info = model.MeshInfo;
		Log.Info( $"{logName}: {path}: {info.LodCount} LODs, {info.TotalTriangles} triangles across them, LOD 0 is {mesh.TriangleCount} triangles" );
		return mesh;
	}

	/// <summary>
	/// The engine model each loaded mesh came from.
	/// </summary>
	public Dictionary<RenderMesh, Model> ModelsByMesh() => meshes.Values.ToDictionary( x => x.Mesh, x => x.Model );

	public GpuMaterial Material( string path ) => path is null ? null : Source2Material.From( Sandbox.Material.Load( path ) );

	public GpuTexture Texture( string path ) => Source2Texture.From( Sandbox.Texture.Load( path ) );

	public (GpuMaterial Material, GpuTexture Cubemap) Sky( string path )
	{
		var sky = Sandbox.Material.Load( path );
		return (Source2Material.From( sky ), Source2Texture.From( sky.GetTexture( "g_tSkyTexture" ) ));
	}

	public GpuTexture SkyCube() => Source2Texture.From( skyCube ??= Lab.SkyCube.Create() );

	/// <summary>
	/// A native <see cref="SceneModel"/> in a world that's never rendered evaluates the sequence, and the object takes its
	/// bones and bounds. SceneLab's native reference then takes the object's bones, so both draw the same pose.
	/// </summary>
	public void Pose( MeshObject obj, string sequence, float time )
	{
		animationWorld ??= new SceneWorld();

		var animator = new SceneModel( animationWorld, obj.Mesh.Geometry.Engine(), obj.Transform ) { UseAnimGraph = false, PlaybackRate = 0 };
		animator.CurrentSequence.Name = sequence;
		animator.CurrentSequence.TimeNormalized = time;
		animator.Update( 0 );

		var bones = new Transform[obj.Mesh.Geometry.Engine().BoneCount];
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
		animationWorld?.Delete();
		animationWorld = null;
		meshes.Clear();

		// Made here rather than loaded, so nothing else lets it go
		skyCube?.Dispose();
		skyCube = null;
	}
}
