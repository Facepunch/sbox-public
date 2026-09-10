using System.Runtime.InteropServices;

namespace Sandbox
{
	public sealed partial class ModelBuilder
	{
		private readonly List<Vector3> vertices = new();
		private readonly List<int> indices = new();
		private readonly List<byte> triangleMaterials = new();
		private readonly List<BoxDesc> boxes = new();
		private readonly List<SphereDesc> spheres = new();
		private readonly List<CapsuleDesc> capsules = new();
		private readonly List<HullDesc> hulls = new();
		private readonly List<MeshDesc> meshShapes = new();
		private readonly List<Vector3> traceVertices = [];
		private readonly List<int> traceIndices = [];

		private struct BoxDesc
		{
			public Transform transform;
			public Vector3 extents;
		}

		private struct SphereDesc
		{
			public Vector3 center;
			public float radius;
		}

		private struct CapsuleDesc
		{
			public Vector3 center0;
			public Vector3 center1;
			public float radius;
		}

		private struct HullDesc
		{
			public Transform transform;
			public int startVertex;
			public int numVertex;
		}

		private struct MeshDesc
		{
			public int startVertex;
			public int numVertex;
			public int startIndex;
			public int numIndex;
			public int startMaterial;
		}

		/// <summary>
		/// Adds a box collision shape.
		/// </summary>
		/// <param name="extents">The distance from the center to each side of the box.</param>
		/// <param name="center">The center of the box.</param>
		/// <param name="rotation">The rotation of the box.</param>
		public ModelBuilder AddCollisionBox( Vector3 extents, Vector3? center = default, Rotation? rotation = default )
		{
			boxes.Add( new()
			{
				extents = extents,
				transform = new Transform( center ?? Vector3.Zero, rotation ?? Rotation.Identity )
			} );

			return this;
		}

		/// <summary>
		/// Adds a sphere collision shape.
		/// </summary>
		/// <param name="radius">The sphere radius.</param>
		/// <param name="center">The sphere center.</param>
		public ModelBuilder AddCollisionSphere( float radius, Vector3 center = default )
		{
			spheres.Add( new()
			{
				center = center,
				radius = radius
			} );

			return this;
		}

		/// <summary>
		/// Adds a capsule collision shape.
		/// </summary>
		/// <param name="center0">The center of the first rounded end.</param>
		/// <param name="center1">The center of the second rounded end.</param>
		/// <param name="radius">The capsule radius.</param>
		public ModelBuilder AddCollisionCapsule( Vector3 center0, Vector3 center1, float radius )
		{
			capsules.Add( new()
			{
				center0 = center0,
				center1 = center1,
				radius = radius
			} );

			return this;
		}

		/// <summary>
		/// Adds a convex hull collision shape.
		/// </summary>
		/// <param name="vertices">The points used to create the hull. Empty collections are ignored.</param>
		/// <param name="center">The local position of the hull.</param>
		/// <param name="rotation">The local rotation of the hull.</param>
		public ModelBuilder AddCollisionHull( List<Vector3> vertices, Vector3? center = default, Rotation? rotation = default )
		{
			return AddCollisionHull( CollectionsMarshal.AsSpan( vertices ), center, rotation );
		}

		/// <summary>
		/// Adds a convex hull collision shape.
		/// </summary>
		/// <param name="vertices">The points used to create the hull. Empty spans are ignored.</param>
		/// <param name="center">The local position of the hull.</param>
		/// <param name="rotation">The local rotation of the hull.</param>
		public ModelBuilder AddCollisionHull( Span<Vector3> vertices, Vector3? center = default, Rotation? rotation = default )
		{
			if ( vertices.IsEmpty ) return this;

			var startVertex = this.vertices.Count;

			hulls.Add( new()
			{
				startVertex = startVertex,
				numVertex = vertices.Length,
				transform = new Transform( center ?? Vector3.Zero, rotation ?? Rotation.Identity )
			} );

			this.vertices.AddRange( vertices );

			return this;
		}

		/// <summary>
		/// Adds a concave triangle mesh collision shape.
		/// Mesh collision cannot be physically simulated.
		/// </summary>
		/// <param name="vertices">The mesh vertices.</param>
		/// <param name="indices">Triangle indices. Fewer than three vertices or indices are ignored.</param>
		/// <exception cref="ArgumentException">Indices do not form triangles.</exception>
		/// <exception cref="ArgumentOutOfRangeException">An index refers to a vertex outside <paramref name="vertices"/>.</exception>
		public ModelBuilder AddCollisionMesh( List<Vector3> vertices, List<int> indices )
		{
			return AddCollisionMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ) );
		}

		/// <summary>
		/// Adds a concave triangle mesh collision shape.
		/// Mesh collision cannot be physically simulated.
		/// </summary>
		/// <param name="vertices">The mesh vertices.</param>
		/// <param name="indices">Triangle indices. Fewer than three vertices or indices are ignored.</param>
		/// <param name="materials">An optional material index for each triangle.</param>
		/// <exception cref="ArgumentException">Indices do not form triangles, or the material count does not match the triangle count.</exception>
		/// <exception cref="ArgumentOutOfRangeException">An index refers to a vertex outside <paramref name="vertices"/>.</exception>
		public ModelBuilder AddCollisionMesh( List<Vector3> vertices, List<int> indices, List<byte> materials )
		{
			return AddCollisionMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ), CollectionsMarshal.AsSpan( materials ) );
		}

		/// <summary>
		/// Adds a concave triangle mesh collision shape.
		/// Mesh collision cannot be physically simulated.
		/// </summary>
		/// <param name="vertices">The mesh vertices.</param>
		/// <param name="indices">Triangle indices. Fewer than three vertices or indices are ignored.</param>
		/// <exception cref="ArgumentException">Indices do not form triangles.</exception>
		/// <exception cref="ArgumentOutOfRangeException">An index refers to a vertex outside <paramref name="vertices"/>.</exception>
		public ModelBuilder AddCollisionMesh( Span<Vector3> vertices, Span<int> indices )
		{
			return AddCollisionMesh( vertices, indices, null );
		}

		/// <summary>
		/// Adds a concave triangle mesh collision shape.
		/// Mesh collision cannot be physically simulated.
		/// </summary>
		/// <param name="vertices">The mesh vertices.</param>
		/// <param name="indices">Triangle indices. Fewer than three vertices or indices are ignored.</param>
		/// <param name="materials">An optional material index for each triangle.</param>
		/// <exception cref="ArgumentException">Indices do not form triangles, or the material count does not match the triangle count.</exception>
		/// <exception cref="ArgumentOutOfRangeException">An index refers to a vertex outside <paramref name="vertices"/>.</exception>
		public ModelBuilder AddCollisionMesh( Span<Vector3> vertices, Span<int> indices, Span<byte> materials )
		{
			if ( vertices.Length < 3 )
				return this;

			if ( indices.Length < 3 )
				return this;

			if ( (indices.Length % 3) != 0 )
				throw new ArgumentException( "Indices length must be a multiple of 3", nameof( indices ) );

			var triangleCount = indices.Length / 3;

			// Materials are optional, but if provided must match triangle count
			if ( materials.Length != 0 && materials.Length != triangleCount )
			{
				throw new ArgumentException(
					$"Materials length ({materials.Length}) must match triangle count ({triangleCount}) when provided",
					nameof( materials )
				);
			}

			// Validate indices are in range, creating collision mesh is expensive anyway so no harm in being safe.
			int numVertices = vertices.Length;
			foreach ( var i in indices )
			{
				if ( i < 0 || i >= numVertices )
					throw new ArgumentOutOfRangeException( nameof( indices ), $"Tried to access out of range vertex {i}, range is 0-{numVertices - 1}" );
			}

			var startVertex = this.vertices.Count;
			var startIndex = this.indices.Count;
			var startMaterial = this.triangleMaterials.Count;

			meshShapes.Add( new()
			{
				startVertex = startVertex,
				numVertex = vertices.Length,
				startIndex = startIndex,
				numIndex = indices.Length,
				startMaterial = materials.Length != 0 ? startMaterial : -1,
			} );

			this.vertices.AddRange( vertices );
			this.indices.AddRange( indices );

			if ( materials.Length != 0 )
				triangleMaterials.AddRange( materials );

			return this;
		}

		/// <summary>
		/// Adds geometry used by traces and ray queries.
		/// Multiple calls append additional trace geometry.
		/// </summary>
		/// <param name="vertices">The trace mesh vertices.</param>
		/// <param name="indices">Triangle indices. Fewer than three vertices or indices are ignored.</param>
		/// <exception cref="ArgumentOutOfRangeException">An index refers to a vertex outside <paramref name="vertices"/>.</exception>
		public ModelBuilder AddTraceMesh( List<Vector3> vertices, List<int> indices )
		{
			return AddTraceMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ) );
		}

		/// <summary>
		/// Adds geometry used by traces and ray queries.
		/// Multiple calls append additional trace geometry.
		/// </summary>
		/// <param name="vertices">The trace mesh vertices.</param>
		/// <param name="indices">Triangle indices. Fewer than three vertices or indices are ignored.</param>
		/// <exception cref="ArgumentOutOfRangeException">An index refers to a vertex outside <paramref name="vertices"/>.</exception>
		public ModelBuilder AddTraceMesh( Span<Vector3> vertices, Span<int> indices )
		{
			if ( vertices.Length < 3 )
				return this;

			if ( indices.Length < 3 )
				return this;

			// Validate indices are in range
			int numVertices = vertices.Length;
			foreach ( var i in indices )
			{
				if ( i < 0 || i >= numVertices )
					throw new ArgumentOutOfRangeException( nameof( indices ), $"Tried to access out of range vertex {i}, range is 0-{numVertices - 1}" );
			}

			var vertexOffset = traceVertices.Count;
			traceVertices.AddRange( vertices );

			foreach ( var index in indices )
			{
				traceIndices.Add( vertexOffset + index );
			}

			return this;
		}

		private readonly List<int> _surfaces = [];

		private unsafe void ApplyTraceMesh( NativeEngine.ModelBuilder modelBuilder )
		{
			if ( traceVertices.Count == 0 || traceIndices.Count == 0 )
				return;

			var vertexSpan = CollectionsMarshal.AsSpan( traceVertices );
			var indexSpan = CollectionsMarshal.AsSpan( traceIndices );
			fixed ( Vector3* vertexPointer = vertexSpan )
			fixed ( int* indexPointer = indexSpan )
			{
				modelBuilder.SetTraceMesh( (IntPtr)vertexPointer, vertexSpan.Length, (IntPtr)indexPointer, indexSpan.Length );
			}
		}

		/// <summary>
		/// Adds a surface that can be referenced by collision mesh material indices.
		/// </summary>
		/// <param name="surface">The surface to add. Null uses the default surface.</param>
		public ModelBuilder AddSurface( Surface surface )
		{
			surface ??= Surface.FindByName( "default" );
			_surfaces.Add( surface.Index );
			return this;
		}

		private unsafe CPhysBodyDescArray CreateLegacyPhysicsBodies()
		{
			if ( spheres.Count == 0 && capsules.Count == 0 && boxes.Count == 0 && hulls.Count == 0 && meshShapes.Count == 0 )
				return default;

			var result = CPhysBodyDescArray.Create( 1, 0 );

			try
			{
				var body = result.Get( 0 );

				foreach ( var sphere in spheres )
				{
					body.AddSphere( new Sphere( sphere.center, sphere.radius ) );
				}

				foreach ( var capsule in capsules )
				{
					body.AddCapsule( new Capsule( capsule.center0, capsule.center1, capsule.radius ) );
				}

				foreach ( var box in boxes )
				{
					body.AddBox( box.extents, box.transform );
				}

				var vertexSpan = CollectionsMarshal.AsSpan( vertices );
				foreach ( var hull in hulls )
				{
					var hullVertices = vertexSpan.Slice( hull.startVertex, hull.numVertex );
					fixed ( Vector3* vertexPointer = hullVertices )
					{
						body.AddHull( (IntPtr)vertexPointer, hullVertices.Length, hull.transform );
					}
				}

				var indexSpan = CollectionsMarshal.AsSpan( indices );
				var materialSpan = CollectionsMarshal.AsSpan( triangleMaterials );
				foreach ( var mesh in meshShapes )
				{
					var meshVertices = vertexSpan.Slice( mesh.startVertex, mesh.numVertex );
					var meshIndices = indexSpan.Slice( mesh.startIndex, mesh.numIndex );

					fixed ( Vector3* vertexPointer = meshVertices )
					fixed ( int* indexPointer = meshIndices )
					{
						if ( mesh.startMaterial < 0 )
						{
							body.AddMesh( (IntPtr)vertexPointer, (uint)meshVertices.Length, (IntPtr)indexPointer, (uint)meshIndices.Length, IntPtr.Zero );
							continue;
						}

						var meshMaterials = materialSpan.Slice( mesh.startMaterial, mesh.numIndex / 3 );
						fixed ( byte* materialPointer = meshMaterials )
						{
							body.AddMesh( (IntPtr)vertexPointer, (uint)meshVertices.Length, (IntPtr)indexPointer, (uint)meshIndices.Length, (IntPtr)materialPointer );
						}
					}
				}

				body.m_flMass = mass;
				body.SetSurface( surfaceProperty );

				foreach ( var surface in _surfaces )
				{
					result.AddSurface( surface );
				}

				return result;
			}
			catch
			{
				result.DeleteThis();
				throw;
			}
		}

	}
}
