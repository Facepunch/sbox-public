using NativeEngine;
using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox
{
	/// <summary>
	/// Provides ability to generate <see cref="Model"/>s at runtime.
	/// A static instance of this class is available at <see cref="Model.Builder"/>
	/// </summary>
	public sealed partial class ModelBuilder
	{
		private readonly List<Mesh> meshes = new();
		private readonly List<Vector3> vertices = new();
		private readonly List<int> indices = new();
		private readonly List<byte> triangleMaterials = new();
		private readonly List<BoxDesc> boxes = new();
		private readonly List<SphereDesc> spheres = new();
		private readonly List<CapsuleDesc> capsules = new();
		private readonly List<HullDesc> hulls = new();
		private readonly List<MeshDesc> meshShapes = new();
		private readonly List<int> lods = new();
		private readonly List<ulong> bodyGroups = new();
		private readonly List<MeshGroupDesc> meshGroups = new();
		private readonly List<BoneDesc> bones = new();
		private readonly StringBuilder names = new();
		private int startTraceVertex;
		private int startTraceIndex;
		private int numTraceVertices;
		private int numTraceIndices;
		private string modelName;

		private float mass = 1000;
		private string surfaceProperty = "";
		private readonly float[] lodSwitchDistance = Enumerable.Range( 0, 8 )
			.Select( i => i * 50.0f )
			.ToArray();

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

		private struct BoneDesc
		{
			public int nameOffset;
			public int nameLength;
			public int parentNameOffset;
			public int parentNameLength;
			public Vector3 position;
			public Rotation rotation;
			public float radius;
			public bool attachment;
		}

		private struct MeshGroupDesc
		{
			public int nameOffset;
			public int nameLength;
		}

		/// <summary>
		/// Total mass of the physics body (Default is 1000)
		/// </summary>
		public ModelBuilder WithMass( float mass )
		{
			this.mass = mass;

			return this;
		}

		/// <summary>
		/// Surface property to use for collision
		/// </summary>
		public ModelBuilder WithSurface( string name )
		{
			surfaceProperty = name;

			return this;
		}

		/// <summary>
		/// LOD switch distance increment for each Level of Detail (LOD) level. (Default is 50)
		/// </summary>
		public ModelBuilder WithLodDistance( int lod, float distance )
		{
			if ( lod >= 8 )
				throw new ArgumentException( "Max LOD count is 8" );

			lodSwitchDistance[lod] = distance;

			return this;
		}

		/// <summary>
		/// Add box collision shape.
		/// </summary>
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
		/// Add sphere collision shape.
		/// </summary>
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
		/// Add capsule collision shape.
		/// </summary>
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
		/// Add a CONVEX hull collision shape.
		/// </summary>
		public ModelBuilder AddCollisionHull( List<Vector3> vertices, Vector3? center = default, Rotation? rotation = default )
		{
			return AddCollisionHull( CollectionsMarshal.AsSpan( vertices ), center, rotation );
		}

		/// <summary>
		/// Add a CONVEX hull collision shape.
		/// </summary>
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
		/// Add a CONCAVE mesh collision shape. (This shape can NOT be physically simulated)
		/// </summary>
		public ModelBuilder AddCollisionMesh( List<Vector3> vertices, List<int> indices )
		{
			return AddCollisionMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ) );
		}

		/// <summary>
		/// Add a CONCAVE mesh collision shape. (This shape can NOT be physically simulated)
		/// </summary>
		public ModelBuilder AddCollisionMesh( List<Vector3> vertices, List<int> indices, List<byte> materials )
		{
			return AddCollisionMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ), CollectionsMarshal.AsSpan( materials ) );
		}

		/// <summary>
		/// Add a CONCAVE mesh collision shape. (This shape can NOT be physically simulated)
		/// </summary>
		public ModelBuilder AddCollisionMesh( Span<Vector3> vertices, Span<int> indices )
		{
			return AddCollisionMesh( vertices, indices, null );
		}

		/// <summary>
		/// Add a CONCAVE mesh collision shape. (This shape can NOT be physically simulated)
		/// </summary>
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
		/// Add trace vertices for tracing against mesh
		/// </summary>
		public ModelBuilder AddTraceMesh( List<Vector3> vertices, List<int> indices )
		{
			return AddTraceMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ) );
		}

		/// <summary>
		/// Add trace vertices for tracing against mesh
		/// </summary>
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

			var startVertex = this.vertices.Count;
			var startIndex = this.indices.Count;

			startTraceVertex = startVertex;
			startTraceIndex = startIndex;
			numTraceVertices = vertices.Length;
			numTraceIndices = indices.Length;

			this.vertices.AddRange( vertices );
			this.indices.AddRange( indices );

			return this;
		}

		/// <summary>
		/// Add a mesh.
		/// </summary>
		public ModelBuilder AddMesh( Mesh mesh )
		{
			AddMesh( mesh, 255, ulong.MaxValue );
			return this;
		}

		/// <summary>
		/// Add a bunch of meshes.
		/// </summary>
		public ModelBuilder AddMeshes( Mesh[] meshes )
		{
			AddMeshes( meshes, 255, ulong.MaxValue );
			return this;
		}

		/// <summary>
		/// Add a mesh to a Level of Detail (LOD) group.
		/// </summary>
		public ModelBuilder AddMesh( Mesh mesh, int lod )
		{
			if ( lod < 0 ) lod = 0;
			var lodMask = 1 << lod;

			AddMesh( mesh, lodMask, ulong.MaxValue );
			return this;
		}

		/// <summary>
		/// Add a bunch of meshes to a Level of Detail (LOD) group.
		/// </summary>
		public ModelBuilder AddMeshes( Mesh[] meshes, int lod )
		{
			if ( lod < 0 ) lod = 0;
			var lodMask = 1 << lod;

			AddMeshes( meshes, lodMask, ulong.MaxValue );
			return this;
		}

		/// <summary>
		/// Add a mesh to a body group choice.
		/// </summary>
		public ModelBuilder AddMesh( Mesh mesh, string groupName, int choiceIndex )
		{
			return AddMesh( mesh, 0, groupName, choiceIndex );
		}

		/// <summary>
		/// Add a mesh to a Level of Detail (LOD) and a body group choice.
		/// </summary>
		public ModelBuilder AddMesh( Mesh mesh, int lod, string groupName, int choiceIndex )
		{
			var groupIndex = EnsureGroupChoice( groupName, choiceIndex );
			var groupMask = 1UL << groupIndex;

			if ( lod < 0 ) lod = 0;
			var lodMask = 1 << lod;

			return AddMesh( mesh, lodMask, groupMask );
		}

		private ModelBuilder AddMesh( Mesh mesh, int lodMask, ulong bodyGroupMask )
		{
			if ( mesh == null || !mesh.IsValid )
				return this;

			if ( !mesh.HasVertexBuffer )
				throw new ArgumentException( "Mesh has invalid vertex buffer" );

			meshes.Add( mesh );
			lods.Add( lodMask );
			bodyGroups.Add( bodyGroupMask );

			return this;
		}

		private ModelBuilder AddMeshes( Mesh[] meshes, int lodMask, ulong bodyGroupMask )
		{
			if ( meshes == null || meshes.Length == 0 )
				return this;

			int numMeshes = 0;
			foreach ( var mesh in meshes )
			{
				if ( mesh == null || !mesh.IsValid )
					continue;

				if ( !mesh.HasVertexBuffer )
					throw new ArgumentException( "Mesh has invalid vertex buffer" );

				this.meshes.Add( mesh );
				numMeshes++;
			}

			if ( numMeshes == 0 )
				return this;

			lods.AddRange( Enumerable.Repeat( lodMask, numMeshes ) );
			bodyGroups.AddRange( Enumerable.Repeat( bodyGroupMask, numMeshes ) );

			return this;
		}

		private readonly Dictionary<(string, int), int> _groupChoiceToIndex = new();
		private readonly HashSet<string> _groupNames = new( StringComparer.Ordinal );
		private ulong _defaultMeshGroupMask;

		private ulong DefaultMeshGroupMask => _groupNames.Count > 0 ? _defaultMeshGroupMask : ulong.MaxValue;

		private int EnsureGroupChoice( string name, int choice )
		{
			if ( string.IsNullOrWhiteSpace( name ) ) throw new ArgumentException( null, nameof( name ) );
			ArgumentOutOfRangeException.ThrowIfNegative( choice );

			var key = (name, choice);
			if ( _groupChoiceToIndex.TryGetValue( key, out var index ) ) return index;

			if ( meshGroups.Count >= 64 )
				throw new InvalidOperationException( "Total bodygroup choices exceed 64 bits." );

			var groupName = $"{name}_@{choice}";
			var nameOffset = names.Length;
			index = meshGroups.Count;
			meshGroups.Add( new MeshGroupDesc { nameLength = groupName.Length, nameOffset = nameOffset } );
			names.Append( groupName );
			_groupChoiceToIndex[key] = index;

			if ( _groupNames.Add( name ) )
			{
				_defaultMeshGroupMask |= (1UL << index);
			}

			return index;
		}

		/// <summary>
		/// A bone definition for use with <see cref="ModelBuilder"/>.
		/// </summary>
		/// <param name="Name">Name of the bone.</param>
		/// <param name="ParentName">Name of the parent bone.</param>
		/// <param name="Position">Position of the bone, relative to its parent.</param>
		/// <param name="Rotation">Rotation of the bone, relative to its parent.</param>
		public readonly record struct Bone( string Name, string ParentName, Vector3 Position, Rotation Rotation );

		/// <summary>
		/// Add a bone to the skeleton via a <see cref="Bone"/> struct.
		/// </summary>
		public void AddBone( Bone bone )
		{
			AddBone( bone.Name, bone.Position, bone.Rotation, bone.ParentName );
		}

		/// <summary>
		/// Add multiple bones to the skeleton.
		/// </summary>
		public void AddBones( Bone[] bones )
		{
			if ( bones == null )
				return;

			foreach ( var bone in bones )
				AddBone( bone.Name, bone.Position, bone.Rotation, bone.ParentName );
		}

		/// <summary>
		/// Add a bone to the skeleton.
		/// </summary>
		public ModelBuilder AddBone( string name, Vector3 position, Rotation rotation, string parentName = null )
		{
			return AddBone( name, position, rotation, parentName, false );
		}

		/// <summary>
		/// Add an attachment to the skeleton.
		/// </summary>
		public ModelBuilder AddAttachment( string name, Vector3 position, Rotation rotation, string parentName = null )
		{
			return AddBone( name, position, rotation, parentName, true );
		}

		internal ModelBuilder AddBone( string name, Vector3 position, Rotation rotation, string parentName, bool attachment )
		{
			var nameOffset = -1;
			if ( !string.IsNullOrWhiteSpace( name ) )
			{
				nameOffset = names.Length;
				names.Append( name );
			}

			var parentNameOffset = -1;
			if ( !string.IsNullOrWhiteSpace( parentName ) )
			{
				parentNameOffset = names.Length;
				names.Append( parentName );
			}

			bones.Add( new BoneDesc
			{
				nameOffset = nameOffset,
				nameLength = name != null ? name.Length : 0,
				parentNameOffset = parentNameOffset,
				parentNameLength = parentName != null ? parentName.Length : 0,
				position = position,
				rotation = rotation,
				radius = -1,
				attachment = attachment
			} );

			return this;
		}

		/// <summary>
		/// Provide a name to identify the model by
		/// </summary>
		/// <param name="name">Desired model name</param>
		public ModelBuilder WithName( string name )
		{
			modelName = name;
			return this;
		}

		List<int> _surfaces = [];

		public ModelBuilder AddSurface( Surface surface )
		{
			surface ??= Surface.FindByName( "default" );
			_surfaces.Add( surface.Index );
			return this;
		}

		/// <summary>
		/// Finish creation of the model.
		/// </summary>
		public unsafe Model Create()
		{
			var renderMeshes = meshes
				.Where( x => x != null && x.IsValid )
				.Select( x => x.native )
				.ToArray();

			var vertices_span = CollectionsMarshal.AsSpan( vertices );
			var indices_span = CollectionsMarshal.AsSpan( indices );
			var materials_span = CollectionsMarshal.AsSpan( triangleMaterials );
			var spheres_span = CollectionsMarshal.AsSpan( spheres );
			var capsules_span = CollectionsMarshal.AsSpan( capsules );
			var boxes_span = CollectionsMarshal.AsSpan( boxes );
			var hulls_span = CollectionsMarshal.AsSpan( hulls );
			var meshes_span = CollectionsMarshal.AsSpan( meshShapes );
			var lods_span = CollectionsMarshal.AsSpan( lods );
			var bodygroups_span = CollectionsMarshal.AsSpan( bodyGroups );
			var meshgroups_span = CollectionsMarshal.AsSpan( meshGroups );
			var bones_span = CollectionsMarshal.AsSpan( bones );
			var surfaces_span = CollectionsMarshal.AsSpan( _surfaces );

			fixed ( IMesh* meshes_ptr = renderMeshes )
			fixed ( Vector3* vertices_ptr = vertices_span )
			fixed ( int* indices_ptr = indices_span )
			fixed ( byte* materials_ptr = materials_span )
			fixed ( SphereDesc* spheres_ptr = spheres_span )
			fixed ( CapsuleDesc* capsule_ptr = capsules_span )
			fixed ( BoxDesc* boxes_ptr = boxes_span )
			fixed ( HullDesc* hulls_ptr = hulls_span )
			fixed ( MeshDesc* meshShapes_ptr = meshes_span )
			fixed ( int* lods_ptr = lods_span )
			fixed ( ulong* bodygroups_ptr = bodygroups_span )
			fixed ( MeshGroupDesc* meshgroups_ptr = meshgroups_span )
			fixed ( BoneDesc* bones_ptr = bones_span )
			fixed ( float* pLodSwitchDistance = &lodSwitchDistance[0] )
			fixed ( int* pSurfaces = surfaces_span )
			{
				var anim = CreateAnimationGroup();
				var bodies = CreatePhysBodyDesc();
				var materialGroups = CreateMaterialGroups();

				var model = MeshGlue.CreateModel(
					anim,
					bodies,
					materialGroups,
					mass,
					surfaceProperty,
					(IntPtr)pLodSwitchDistance,
					(IntPtr)meshes_ptr, renderMeshes.Length,
					(IntPtr)lods_ptr,
					(IntPtr)bodygroups_ptr,
					(IntPtr)meshgroups_ptr, meshGroups.Count,
					(IntPtr)vertices_ptr, vertices.Count,
					(IntPtr)indices_ptr, indices.Count,
					(IntPtr)materials_ptr,
					(IntPtr)pSurfaces, _surfaces.Count,
					(IntPtr)spheres_ptr, spheres.Count,
					(IntPtr)capsule_ptr, capsules.Count,
					(IntPtr)boxes_ptr, boxes.Count,
					(IntPtr)hulls_ptr, hulls.Count,
					(IntPtr)meshShapes_ptr, meshShapes.Count,
					(IntPtr)bones_ptr, bones.Count,
					names.Length > 0 ? names.ToString() : null,
					startTraceVertex, startTraceIndex,
					numTraceVertices, numTraceIndices,
					DefaultMeshGroupMask );

				if ( anim.IsValid ) anim.DeleteThis();
				if ( bodies.IsValid ) bodies.DeleteThis();
				if ( materialGroups.IsValid ) materialGroups.DeleteThis();

				return Model.FromNative( model, true, modelName );
			}
		}
	}
}
