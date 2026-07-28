namespace Sandbox;

/// <summary>
/// Builds a physics body for a <see cref="Model"/>.
/// </summary>
public sealed class PhysicsBodyBuilder
{
	/// <summary>
	/// The mass of the body in kilograms.
	/// Set to <c>0</c> to calculate automatically from its shapes and density.
	/// </summary>
	public float Mass { get; set; }

	/// <summary>
	/// The surface properties applied to this body.
	/// </summary>
	public Surface Surface { get; set; }

	/// <summary>
	/// The bind pose transform used when attaching this body to a bone.
	/// </summary>
	public Transform BindPose { get; set; }

	/// <summary>
	/// The name of the bone this body is attached to, or <c>null</c> if not attached.
	/// </summary>
	public string BoneName { get; set; }

	internal struct BoxShape { public Vector3 Extents; public Transform Transform; }
	internal struct SphereShape { public Sphere Sphere; }
	internal struct CapsuleShape { public Capsule Capsule; }
	internal struct HullShape { public Vector3[] Points; public Transform Transform; public HullSimplify? Simplify; }
	internal struct MeshShape { public Vector3[] Vertices; public uint[] Indices; public byte[] Materials; }

	internal List<BoxShape> Boxes = [];
	internal List<SphereShape> Spheres = [];
	internal List<CapsuleShape> Capsules = [];
	internal List<HullShape> Hulls = [];
	internal List<MeshShape> Meshes = [];

	internal PhysicsBodyBuilder()
	{
	}

	/// <inheritdoc cref="Mass"/>
	/// <param name="mass">The body mass in kilograms.</param>
	public PhysicsBodyBuilder SetMass( float mass )
	{
		Mass = mass;
		return this;
	}

	/// <inheritdoc cref="Surface"/>
	/// <param name="surface">The surface properties applied to the body.</param>
	public PhysicsBodyBuilder SetSurface( Surface surface )
	{
		Surface = surface;
		return this;
	}

	/// <inheritdoc cref="BindPose"/>
	/// <param name="bindPose">The bind pose transform.</param>
	public PhysicsBodyBuilder SetBindPose( Transform bindPose )
	{
		BindPose = bindPose;
		return this;
	}

	/// <inheritdoc cref="BoneName"/>
	/// <param name="boneName">The attached bone name, or null to detach it.</param>
	public PhysicsBodyBuilder SetBoneName( string boneName )
	{
		BoneName = boneName;
		return this;
	}

	/// <summary>
	/// Adds a box shape.
	/// </summary>
	/// <param name="extents">The distance from the center to each side of the box.</param>
	/// <param name="transform">Optional local transform of the box relative to the body.</param>
	public PhysicsBodyBuilder AddBox( Vector3 extents, Transform? transform = default )
	{
		Boxes.Add( new BoxShape { Extents = extents, Transform = transform ?? Transform.Zero } );
		return this;
	}

	/// <summary>
	/// Adds a sphere shape.
	/// </summary>
	/// <param name="sphere">The sphere to add.</param>
	/// <param name="transform">Optional position and uniform scale applied to the sphere.</param>
	public PhysicsBodyBuilder AddSphere( Sphere sphere, Transform? transform = default )
	{
		if ( transform.HasValue )
		{
			sphere.Radius *= transform.Value.UniformScale;
			sphere.Center += transform.Value.Position;
		}
		Spheres.Add( new SphereShape { Sphere = sphere } );
		return this;
	}

	/// <summary>
	/// Adds a capsule shape.
	/// </summary>
	/// <param name="capsule">The capsule to add.</param>
	/// <param name="transform">Optional transform applied to the capsule.</param>
	public PhysicsBodyBuilder AddCapsule( Capsule capsule, Transform? transform = default )
	{
		if ( transform.HasValue )
		{
			capsule.Radius *= transform.Value.UniformScale;
			capsule.CenterA = transform.Value.PointToWorld( capsule.CenterA );
			capsule.CenterB = transform.Value.PointToWorld( capsule.CenterB );
		}
		Capsules.Add( new CapsuleShape { Capsule = capsule } );
		return this;
	}

	/// <summary>
	/// The method used to simplify a hull.
	/// </summary>
	public enum SimplifyMethod
	{
		/// <summary>Quadratic Error Metric - prioritizes preserving shape accuracy.</summary>
		QEM,

		/// <summary>Iterative Vertex Removal - removes vertices gradually.</summary>
		IVR,

		/// <summary>No simplification - use the exact points provided.</summary>
		None,

		/// <summary>Iterative Face Removal - removes faces to reduce complexity.</summary>
		IFR
	}

	/// <summary>
	/// Settings for simplifying a hull shape.
	/// </summary>
	public struct HullSimplify
	{
		/// <summary>Maximum allowed angle change between faces, in degrees.</summary>
		public float AngleTolerance;

		/// <summary>Maximum distance a vertex can be moved during simplification.</summary>
		public float DistanceTolerance;

		/// <summary>Maximum number of faces allowed after simplification.</summary>
		public int MaxFaces;

		/// <summary>Maximum number of edges allowed after simplification.</summary>
		public int MaxEdges;

		/// <summary>Maximum number of vertices allowed after simplification.</summary>
		public int MaxVerts;

		/// <summary>The simplification method to use.</summary>
		public SimplifyMethod Method;
	}

	/// <summary>
	/// Adds a convex hull shape to this body.
	/// </summary>
	/// <param name="points">The points making up the hull.</param>
	/// <param name="transform">Optional local transform of the hull relative to the body.</param>
	/// <param name="simplify">Optional settings to reduce the hull complexity. By default, the points are used without simplification.</param>
	/// <exception cref="ArgumentException">Fewer than three points were provided.</exception>
	public PhysicsBodyBuilder AddHull( Span<Vector3> points, Transform? transform = default, HullSimplify? simplify = default )
	{
		if ( points.Length < 3 )
			throw new ArgumentException( "Hull must have at least 3 points.", nameof( points ) );

		Hulls.Add( new HullShape { Points = points.ToArray(), Transform = transform ?? Transform.Zero, Simplify = simplify } );
		return this;
	}

	/// <summary>
	/// Adds a triangle mesh shape to this body.
	/// </summary>
	/// <param name="vertices">The mesh vertex positions.</param>
	/// <param name="indices">
	/// The mesh indices, grouped in triples to form triangles.
	/// Must be a multiple of 3.
	/// </param>
	/// <param name="materials">
	/// Optional per-triangle material indices.
	/// Length must match the number of triangles or be empty.
	/// </param>
	/// <exception cref="ArgumentException">
	/// The mesh has fewer than three vertices,
	/// the indices do not form triangles,
	/// or the material count does not match the triangle count.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">An index refers to a vertex outside <paramref name="vertices"/>.</exception>
	public PhysicsBodyBuilder AddMesh( Span<Vector3> vertices, Span<uint> indices, Span<byte> materials )
	{
		if ( vertices.Length < 3 )
			throw new ArgumentException( "Mesh must have at least 3 vertices.", nameof( vertices ) );

		if ( indices.Length < 3 || indices.Length % 3 != 0 )
			throw new ArgumentException( "Mesh indices length must be at least 3 and a multiple of 3 (triangles).", nameof( indices ) );

		var triangleCount = indices.Length / 3;
		if ( materials.Length > 0 && materials.Length != triangleCount )
			throw new ArgumentException( "Materials array length must match triangle count, or be empty.", nameof( materials ) );

		for ( int i = 0; i < indices.Length; i++ )
		{
			if ( indices[i] >= (uint)vertices.Length )
				throw new ArgumentOutOfRangeException( nameof( indices ), $"Index {indices[i]} is out of range for {vertices.Length} vertices." );
		}

		Meshes.Add( new MeshShape { Vertices = vertices.ToArray(), Indices = indices.ToArray(), Materials = materials.ToArray() } );
		return this;
	}
}
