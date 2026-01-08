using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox
{
	/// <summary>
	/// Possible primitive types of a <see cref="Mesh"/>.
	/// </summary>
	public enum MeshPrimitiveType
	{
		Points,
		Lines,
		LineStrip,
		Triangles,
		TriangleStrip
	}

	/// <summary>
	/// A <a href="https://en.wikipedia.org/wiki/Polygon_mesh">mesh</a> is a basic version of a <see cref="Model"/>,
	/// containing a set of vertices and indices which make up faces that make up a shape.
	///
	/// <para>A set of meshes can be used to create a <see cref="Model"/> via the <see cref="ModelBuilder"/> class.</para>
	/// </summary>
	public partial class Mesh : IValid
	{
		internal IMesh native;
		internal long instanceId;

		public Mesh() : this( null, MeshPrimitiveType.Triangles )
		{

		}

		public Mesh( Material material, MeshPrimitiveType primType = MeshPrimitiveType.Triangles ) : this( "mesh", material, primType )
		{
		}

		public Mesh( string name, Material material, MeshPrimitiveType primType = MeshPrimitiveType.Triangles )
		{
			if ( string.IsNullOrWhiteSpace( name ) )
				name = "mesh";

			native = MeshGlue.CreateRenderMesh( material != null ? material.native : IntPtr.Zero, (int)MeshPrimTypeToRenderPrimType( primType ), name );

			if ( native.IsNull ) throw new Exception( "RenderMesh pointer cannot be null!" );

			instanceId = native.GetBindingPtr().ToInt64();
		}

		private Mesh( IMesh native, long instanceId )
		{
			if ( native.IsNull ) throw new Exception( "RenderMesh pointer cannot be null!" );

			this.native = native;
			this.instanceId = instanceId;
		}

		~Mesh()
		{
			var n = native;
			native = default;

			MainThread.Queue( () => n.DestroyStrongHandle() );
		}

		/// <inheritdoc cref="IValid.IsValid"/>
		public bool IsValid => native.IsValid && native.IsStrongHandleValid();

		/// <summary>
		/// Sets the primitive type for this mesh.
		/// </summary>
		public MeshPrimitiveType PrimitiveType
		{
			set => MeshGlue.SetMeshPrimType( native, (int)MeshPrimTypeToRenderPrimType( value ) );
		}

		/// <summary>
		/// Sets material for this mesh.
		/// </summary>
		public Material Material
		{
			set => MeshGlue.SetMeshMaterial( native, value != null ? value.native : IntPtr.Zero );
		}

		/// <summary>
		/// Sets AABB bounds for this mesh.
		/// </summary>
		public BBox Bounds
		{
			set
			{
				MeshGlue.SetMeshBounds( native, value.Mins, value.Maxs );
			}

			// TODO - get
		}

		/// <summary>
		/// Used to calculate texture size for texture streaming.
		/// </summary>
		public float UvDensity
		{
			set
			{
				MeshGlue.SetMeshUvDensity( native, value );
			}
		}

		/// <summary>
		/// Set how many vertices this mesh draws (if there's no index buffer)
		/// </summary>
		public void SetVertexRange( int start, int count )
		{
			MeshGlue.SetMeshVertexRange( native, start, count );
		}

		/// <summary>
		/// Set how many indices this mesh draws
		/// </summary>
		public void SetIndexRange( int start, int count )
		{
			MeshGlue.SetMeshIndexRange( native, start, count );
		}

		/// <summary>
		/// Create vertex and index buffers.
		/// </summary>
		/// <param name="vb">Input vertex buffer. If it is indexed (<see cref="VertexBuffer.Indexed"/>), then index buffer will also be created.</param>
		/// <param name="calculateBounds">Whether to recalculate bounds from the vertex buffer.</param>
		public void CreateBuffers( VertexBuffer vb, bool calculateBounds = true )
		{
			var vertices_span = CollectionsMarshal.AsSpan( vb.Vertex );
			CreateVertexBuffer( vb.Vertex.Count, vertices_span );

			if ( vb.Indexed )
			{
				// This sucks but probably temp
				var indices = new int[vb.Index.Count];
				for ( int i = 0; i < indices.Length; ++i )
				{
					indices[i] = vb.Index[i];
				}

				CreateIndexBuffer( vb.Index.Count, indices );
			}

			if ( calculateBounds )
			{
				var bounds = new BBox();
				foreach ( var v in vb.Vertex )
				{
					bounds = bounds.AddPoint( v.Position );
				}

				Bounds = bounds;
			}
		}

		private static RenderPrimitiveType MeshPrimTypeToRenderPrimType( MeshPrimitiveType primType )
		{
			RenderPrimitiveType renderPrimType;
			switch ( primType )
			{
				case MeshPrimitiveType.Points:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_POINTS;
					break;
				case MeshPrimitiveType.Lines:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_LINES;
					break;
				case MeshPrimitiveType.LineStrip:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_LINE_STRIP;
					break;
				case MeshPrimitiveType.Triangles:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_TRIANGLES;
					break;
				case MeshPrimitiveType.TriangleStrip:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP;
					break;
				default:
					renderPrimType = RenderPrimitiveType.RENDER_PRIM_TRIANGLES;
					break;
			}

			return renderPrimType;
		}

		/// <summary>
		/// Triangulate a polygon made up of points, returns triangle indices into the list of vertices.
		/// </summary>
		public static unsafe Span<int> TriangulatePolygon( Span<Vector3> vertices )
		{
			if ( vertices.Length < 3 )
				return default;

			var vertexCount = vertices.Length;
			var indexCount = (vertexCount - 2) * 3;
			var indices = new int[indexCount];

			fixed ( int* pIndices = indices )
			fixed ( Vector3* pVertices = vertices )
			{
				indexCount = MeshGlue.TriangulatePolygon( (IntPtr)pVertices, vertices.Length, (IntPtr)pIndices, indexCount );
				return indices.AsSpan( 0, indexCount );
			}
		}

		internal static void ClipPolygon( Span<Vector3> vertices, Vector3 a, Vector3 b, out Vector3[] outVertices )
		{
			unsafe
			{
				fixed ( Vector3* pVertices = vertices )
				{
					var arrVectors = CUtlVectorVector.Create( 0, 0 );
					MeshGlue.ClipPolygonLineSegment( (IntPtr)pVertices, vertices.Length, a, b, arrVectors );

					outVertices = new Vector3[arrVectors.Count()];
					for ( var i = 0; i < outVertices.Length; ++i )
						outVertices[i] = arrVectors.Element( i );

					arrVectors.DeleteThis();
				}
			}
		}
	}
}
