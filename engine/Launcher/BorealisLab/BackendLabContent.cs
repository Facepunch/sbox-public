using Borealis;
using Borealis.Backend;
using Borealis.Source2.Models;

namespace Sandbox.BorealisLab;

/// <summary>
/// A <see cref="Lab.LabWorld"/>'s content on a Borealis.Gpu device: compiled models, materials and textures read by
/// Borealis.Source2 through <see cref="BackendDevice"/>. Models made in code (<see cref="Lab.LodTestModel"/>) are saved by the
/// engine and read back, and skinned objects are posed by a native <see cref="SceneModel"/>: both take the engine, which
/// Borealis Lab starts beside the device. What that device can't draw yet stays out: the 2D sky (its material is null; the
/// default cubemap lights it). Cubemap fog gets a small sky cube made here (<see cref="SkyCube"/>): core's cubemaps are cube
/// arrays, which cubemap fog can't sample.
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

	GpuTexture skyCube;

	/// <summary>
	/// A small sky cube made here: blue overhead, pale at the horizon, dark below, warmer toward +x and cooler toward -x, so a
	/// fog that samples it the wrong way round shows.
	/// </summary>
	public GpuTexture SkyCube()
	{
		if ( skyCube is not null ) return skyCube;

		const int size = 64;
		skyCube = device.CreateTexture( new TextureDesc { Width = size, Height = size, Format = TextureFormat.RGBA8888, Mips = 1, Samples = 1, Cube = true }, "Lab sky cube" );
		var handle = BackendDevice.Unwrap( skyCube ).Texture;
		var face = new byte[size * size * 4];
		for ( int f = 0; f < 6; f++ )
		{
			var i = 0;
			for ( int y = 0; y < size; y++ )
			{
				for ( int x = 0; x < size; x++ )
				{
					var c = Sky( Direction( f, (x + 0.5f) / size * 2 - 1, (y + 0.5f) / size * 2 - 1 ).Normal ).ToColor32();
					face[i++] = c.r;
					face[i++] = c.g;
					face[i++] = c.b;
					face[i++] = 255;
				}
			}

			device.Gpu.WriteTexture( handle, face, 0, f );
		}

		((BackendTexture)skyCube).Layout = Borealis.Gpu.ImageLayout.ShaderRead;
		return skyCube;
	}

	// The direction a texel of a face looks along, in the cube's face order (+x, -x, +y, -y, +z, -z)
	static Vector3 Direction( int face, float u, float v ) => face switch
	{
		0 => new Vector3( 1, -v, -u ),
		1 => new Vector3( -1, -v, u ),
		2 => new Vector3( u, 1, v ),
		3 => new Vector3( u, -1, -v ),
		4 => new Vector3( u, -v, 1 ),
		_ => new Vector3( -u, -v, -1 ),
	};

	static Color Sky( Vector3 direction )
	{
		var horizon = new Color( 0.75f, 0.78f, 0.8f );
		var zenith = new Color( 0.25f, 0.45f, 0.85f );
		var ground = new Color( 0.22f, 0.2f, 0.18f );
		var sky = direction.z >= 0 ? Color.Lerp( horizon, zenith, direction.z ) : Color.Lerp( horizon, ground, MathF.Min( -direction.z * 4, 1 ) );
		var warmth = direction.x * 0.15f;
		return new Color( sky.r + warmth, sky.g, sky.b - warmth );
	}

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
		skyCube?.Dispose();
		skyCube = null;

		if ( animationWorld is null ) return;

		// Deleted now rather than at a frame's end, so nothing it posed with is still held at shutdown
		animationWorld.Delete();
		animationWorld = null;
		SceneWorld.FlushQueuedDeletes();
	}
}
