namespace Sandbox;

/// <summary>
/// Provides ability to generate a physics body for a <see cref="Model"/> at runtime.
/// See <see cref="ModelBuilder.AddBody"/>
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

	internal struct SphereShape { public Sphere Sphere; }
	internal struct CapsuleShape { public Capsule Capsule; }
	internal struct HullShape { public Vector3[] Points; public Transform Transform; public HullSimplify? Simplify; }
	internal struct MeshShape { public Vector3[] Vertices; public uint[] Indices; public byte[] Materials; }

	internal List<SphereShape> Spheres = [];
	internal List<CapsuleShape> Capsules = [];
	internal List<HullShape> Hulls = [];
	internal List<MeshShape> Meshes = [];

	internal PhysicsBodyBuilder()
	{
	}

	/// <inheritdoc cref="Mass"/>
	public PhysicsBodyBuilder SetMass( float mass )
	{
		Mass = mass;
		return this;
	}

	/// <inheritdoc cref="Surface"/>
	public PhysicsBodyBuilder SetSurface( Surface surface )
	{
		Surface = surface;
		return this;
	}

	/// <inheritdoc cref="BindPose"/>
	public PhysicsBodyBuilder SetBindPose( Transform bindPose )
	{
		BindPose = bindPose;
		return this;
	}

	/// <inheritdoc cref="BoneName"/>
	public PhysicsBodyBuilder SetBoneName( string boneName )
	{
		BoneName = boneName;
		return this;
	}

	/// <summary>
	/// Add a sphere shape.
	/// </summary>
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
	/// Add a capsule shape.
	/// </summary>
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
		/// <summary>Quadratic Error Metric � prioritizes preserving shape accuracy.</summary>
		QEM,

		/// <summary>Iterative Vertex Removal � removes vertices gradually.</summary>
		IVR,

		/// <summary>No simplification � use the exact points provided.</summary>
		None,

		/// <summary>Iterative Face Removal � removes faces to reduce complexity.</summary>
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
	/// <param name="simplify">Optional settings to reduce the complexity of the hull.</param>
	/// <exception cref="ArgumentException">Thrown if less than 3 points are provided.</exception>
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
	/// Optional per-vertex material IDs.  
	/// Length must match <paramref name="vertices"/> count or be empty.
	/// </param>
	/// <exception cref="ArgumentException">
	/// Thrown if the mesh has fewer than 3 vertices,  
	/// if indices are not a multiple of 3,  
	/// or if material count does not match vertex count.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if any index refers to a vertex that does not exist.
	/// </exception>
	public PhysicsBodyBuilder AddMesh( Span<Vector3> vertices, Span<uint> indices, Span<byte> materials )
	{
		if ( vertices.Length < 3 )
			throw new ArgumentException( "Mesh must have at least 3 vertices.", nameof( vertices ) );

		if ( indices.Length < 3 || indices.Length % 3 != 0 )
			throw new ArgumentException( "Mesh indices length must be at least 3 and a multiple of 3 (triangles).", nameof( indices ) );

		if ( materials.Length > 0 && materials.Length != vertices.Length )
			throw new ArgumentException( "Materials array length must match vertex count, or be empty.", nameof( materials ) );

		for ( int i = 0; i < indices.Length; i++ )
		{
			if ( indices[i] >= (uint)vertices.Length )
				throw new ArgumentOutOfRangeException( nameof( indices ), $"Index {indices[i]} is out of range for {vertices.Length} vertices." );
		}

		Meshes.Add( new MeshShape { Vertices = vertices.ToArray(), Indices = indices.ToArray(), Materials = materials.ToArray() } );
		return this;
	}
}

partial class ModelBuilder
{
	private readonly List<PhysicsBodyBuilder> _bodies = [];

	/// <summary>
	/// Adds a new physics body to this object.
	/// </summary>
	/// <param name="mass">The mass of the body. Default is <c>0</c>.</param>
	/// <param name="surface">The surface properties to apply. Default is <c>default</c>.</param>
	/// <param name="boneName">
	/// Optional name of the bone this body is attached to.  
	/// Leave empty for non-skeletal bodies.
	/// </param>
	/// <returns>
	/// A new <see cref="PhysicsBodyBuilder"/> for configuring the body.
	/// </returns>
	public PhysicsBodyBuilder AddBody( float mass = default, Surface surface = default, string boneName = default )
	{
		var builder = new PhysicsBodyBuilder
		{
			Mass = mass,
			Surface = surface,
			BoneName = boneName,
			BindPose = Transform.Zero
		};
		_bodies.Add( builder );
		return builder;
	}

	private unsafe CPhysBodyDescArray CreatePhysBodyDesc()
	{
		if ( _bodies.Count == 0 )
			return default;

		var arr = CPhysBodyDescArray.Create( _bodies.Count, _joints.Count );
		int index = 0;
		foreach ( var body in _bodies )
		{
			var desc = arr.Get( index++ );

			foreach ( var hull in body.Hulls )
			{
				var simplify = hull.Simplify ?? new PhysicsBodyBuilder.HullSimplify
				{
					Method = PhysicsBodyBuilder.SimplifyMethod.None
				};

				fixed ( Vector3* pPoints = &hull.Points[0] )
				{
					desc.AddHull(
						(IntPtr)pPoints,
						hull.Points.Length,
						hull.Transform,
						simplify.AngleTolerance,
						simplify.DistanceTolerance,
						simplify.MaxFaces, simplify.MaxEdges, simplify.MaxVerts,
						(int)simplify.Method
					);
				}
			}

			foreach ( var shape in body.Spheres )
			{
				desc.AddSphere( shape.Sphere );
			}

			foreach ( var shape in body.Capsules )
			{
				desc.AddCapsule( shape.Capsule );
			}

			foreach ( var mesh in body.Meshes )
			{
				fixed ( Vector3* pVertices = mesh.Vertices )
				fixed ( uint* pIndices = mesh.Indices )
				fixed ( byte* pMaterials = mesh.Materials )
				{
					desc.AddMesh(
						(IntPtr)pVertices, (uint)mesh.Vertices.Length,
						(IntPtr)pIndices, (uint)mesh.Indices.Length,
						(IntPtr)pMaterials
					);
				}
			}

			desc.m_flMass = body.Mass;
			desc.SetBoneName( body.BoneName );
			desc.SetBindPose( body.BindPose );

			if ( body.Surface is not null )
			{
				desc.SetSurface( new StringToken( body.Surface.NameHash ) );
			}
		}

		index = 0;
		foreach ( var joint in _joints )
		{
			var dest = arr.GetJoint( index++ );
			var desc = joint.Desc;
			dest.m_nType = (ushort)desc.Type;
			dest.m_nBody1 = (ushort)desc.Body1;
			dest.m_nBody2 = (ushort)desc.Body2;
			dest.m_nFlags = desc.Flags;
			dest.m_bEnableCollision = desc.EnableCollision;
			dest.m_bEnableLinearLimit = desc.EnableLinearLimit;
			dest.m_bEnableLinearMotor = desc.EnableLinearMotor;
			dest.m_vLinearTargetVelocity = desc.LinearTargetVelocity;
			dest.m_flMaxForce = desc.MaxForce;
			dest.m_bEnableSwingLimit = desc.EnableSwingLimit;
			dest.m_bEnableTwistLimit = desc.EnableTwistLimit;
			dest.m_bEnableAngularMotor = desc.EnableAngularMotor;
			dest.m_vAngularTargetVelocity = desc.AngularTargetVelocity;
			dest.m_flMaxTorque = desc.MaxTorque;
			dest.m_flLinearFrequency = desc.LinearFrequency;
			dest.m_flLinearDampingRatio = desc.LinearDamping;
			dest.m_flAngularFrequency = desc.AngularFrequency;
			dest.m_flAngularDampingRatio = desc.AngularDamping;
			dest.m_flLinearStrength = desc.LinearStrength;
			dest.m_flAngularStrength = desc.AngularStrength;
			dest.m_Frame1 = desc.Frame1;
			dest.m_Frame2 = desc.Frame2;
			dest.SetLinearLimitMin( desc.LinearLimit.x );
			dest.SetLinearLimitMax( desc.LinearLimit.y );
			dest.SetSwingLimitMin( desc.SwingLimit.x.DegreeToRadian() );
			dest.SetSwingLimitMax( desc.SwingLimit.y.DegreeToRadian() );
			dest.SetTwistLimitMin( desc.TwistLimit.x.DegreeToRadian() );
			dest.SetTwistLimitMax( desc.TwistLimit.y.DegreeToRadian() );
		}

		return arr;
	}
}
