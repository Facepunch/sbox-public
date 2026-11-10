using Sandbox.SceneRenderer;

namespace Sandbox.SceneLab;

/// <summary>
/// Where a <see cref="SceneLabScene.Build"/> preset puts its objects, and where the camera looks at them from. The
/// window fits the floor, lights and fog to <see cref="Bounds"/>, as it does to a grid.
/// </summary>
internal sealed class LabLayout( RenderWorld world, Func<string, Func<Model>, RenderMesh> getMesh )
{
	BBox? bounds;

	public RenderWorld World => world;

	/// <summary>
	/// A model's mesh by path - or, with <paramref name="create"/>, a model built at runtime once and kept under that name.
	/// </summary>
	public RenderMesh Mesh( string path, Func<Model> create = null ) => getMesh( path, create );

	/// <summary>
	/// Add a mesh at a transform, drawn with <paramref name="material"/> rather than its own if there's one.
	/// </summary>
	public MeshObject Add( RenderMesh mesh, Transform transform, Material material = null )
	{
		var obj = new MeshObject( mesh, transform ) { MaterialOverride = material };
		world.Add( obj );

		var box = mesh.Bounds.Transform( transform );
		bounds = bounds?.AddBBox( box ) ?? box;
		return obj;
	}

	/// <summary>
	/// Add a mesh standing on the floor (z = 0) at <paramref name="position"/>, whatever its scale and rotation do to its
	/// bounds, raised by <paramref name="lift"/>.
	/// </summary>
	public MeshObject Stand( RenderMesh mesh, Vector3 position, Rotation rotation, Vector3 scale, float lift = 0 )
	{
		var transform = new Transform( position, rotation, scale );
		var box = mesh.Bounds.Transform( transform );
		return Add( mesh, transform.WithPosition( position + Vector3.Up * (lift - box.Mins.z) ) );
	}

	/// <summary>
	/// What the objects take up: the floor goes under it and the lights over it. Everything added, unless it's set.
	/// </summary>
	public BBox Bounds
	{
		get => bounds ?? BBox.FromPositionAndSize( Vector3.Zero, 100 );
		set => bounds = value;
	}

	/// <summary>
	/// Where the camera orbits, if not the middle of <see cref="Bounds"/>.
	/// </summary>
	public Vector3? Focus { get; set; }

	/// <summary>
	/// How far the camera is from <see cref="Focus"/>, if not fitted to <see cref="Bounds"/>.
	/// </summary>
	public float? Distance { get; set; }

	public float? Yaw { get; set; }
	public float? Pitch { get; set; }

	/// <summary>
	/// The camera's near and far planes, if not fitted to <see cref="Distance"/>.
	/// </summary>
	public float? ZNear { get; set; }
	public float? ZFar { get; set; }
}
