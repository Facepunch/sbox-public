using System.Runtime.InteropServices;

namespace Sandbox
{
	public partial class VertexBuffer
	{
		/// <summary>
		/// List of all vertices in this buffer.
		/// </summary>
		internal List<Vertex> Vertex = new List<Vertex>( 32 );

		/// <summary>
		/// All indices associated with the vertices of this buffer
		/// </summary>
		internal List<ushort> Index = new List<ushort>( 32 );

		/// <summary>
		/// Whether this vertex buffer has any indexes. This is set by <see cref="Init"/>.
		/// </summary>
		public bool Indexed { get; private set; }

		public Vertex Default;

		/// <summary>
		/// Clear all vertices and indices, and resets <see cref="Default"/>.
		/// </summary>
		public virtual void Clear()
		{
			Vertex.Clear();
			Index.Clear();
			Default = default;
		}

		/// <summary>
		/// Clear the buffer and set whether it will have indices.
		/// </summary>
		/// <param name="useIndexBuffer">Whether this buffer will have indices. Affects <see cref="Indexed"/>.</param>
		public virtual void Init( bool useIndexBuffer )
		{
			Indexed = useIndexBuffer;
			Clear();
		}

		/// <summary>
		/// Add a vertex
		/// </summary>
		public void Add( Vertex v )
		{
			Vertex.Add( v );
		}

		/// <summary>
		/// Add an index. This is relative to the top of the vertex buffer. So 0 is Vertex.Count., 1 is Vertex.Count -1
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown when <see cref="Indexed"/> is false.</exception>
		public void AddIndex( int i )
		{
			AddRawIndex( Vertex.Count - i );
		}

		/// <summary>
		/// Add a triangle by indices. This is relative to the top of the vertex buffer. So 0 is Vertex.Count.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown when <see cref="Indexed"/> is false.</exception>
		public void AddTriangleIndex( int a, int b, int c )
		{
			AddIndex( a );
			AddIndex( b );
			AddIndex( c );
		}

		/// <summary>
		/// Add an index. This is NOT relative to the top of the vertex buffer.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown when <see cref="Indexed"/> is false.</exception>
		public void AddRawIndex( int i )
		{
			if ( !Indexed ) throw new InvalidOperationException( "Vertex buffer is not indexed!" );

			Index.Add( (ushort)(i) );
		}

		/// <summary>
		/// Draw this mesh using Material
		/// </summary>
		public unsafe void Draw( Material material, RenderAttributes attributes = null )
		{
			Graphics.AssertRenderBlock();

			if ( Indexed )
			{
				var icount = Index.Count;
				var vcount = Vertex.Count;
				if ( icount < 3 ) return;

				var vspan = CollectionsMarshal.AsSpan<Vertex>( Vertex );
				var ispan = CollectionsMarshal.AsSpan<ushort>( Index );
				Graphics.Draw( vspan, vcount, ispan, icount, material, attributes );
			}
			else
			{
				var vcount = Vertex.Count;
				if ( vcount < 3 ) return;

				var vspan = CollectionsMarshal.AsSpan<Vertex>( Vertex );
				Graphics.Draw( vspan, vcount, material, attributes );
			}
		}
	}
}
