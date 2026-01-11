
namespace Sandbox;

/// <summary>
/// Extensions for Surfaces
/// </summary>
public static partial class SandboxBaseExtensions
{
	/// <summary>
	/// Add a vertex using this position and everything else from Default
	/// </summary>
	public static void Add( this VertexBuffer self, Vector3 pos )
	{
		var v = self.Default;
		v.Position = pos;
		self.Add( v );
	}

	/// <summary>
	/// Add a vertex using this position and UV, and everything else from Default
	/// </summary>
	public static void Add( this VertexBuffer self, Vector3 pos, Vector2 uv )
	{
		var v = self.Default;
		v.Position = pos;
		v.TexCoord0.x = uv.x;
		v.TexCoord0.y = uv.y;

		self.Add( v );
	}

	/// <summary>
	/// Add a triangle to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public static void AddTriangle( this VertexBuffer self, Vertex a, Vertex b, Vertex c )
	{
		self.Add( a );
		self.Add( b );
		self.Add( c );

		if ( self.Indexed )
		{
			self.AddTriangleIndex( 3, 2, 1 );
		}
	}

	/// <summary>
	/// Add a quad to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public static void AddQuad( this VertexBuffer self, Rect rect )
	{
		var pos = rect.Position;
		var size = rect.Size;

		AddQuad( self, pos, new Vector2( pos.x + size.x, pos.y ), pos + size, new Vector2( pos.x, pos.y + size.y ) );
	}

	/// <summary>
	/// Add a quad to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public static void AddQuad( this VertexBuffer self, Vertex a, Vertex b, Vertex c, Vertex d )
	{
		if ( self.Indexed )
		{
			self.Add( a );
			self.Add( b );
			self.Add( c );
			self.Add( d );

			self.AddTriangleIndex( 4, 3, 2 );
			self.AddTriangleIndex( 2, 1, 4 );
		}
		else
		{
			self.Add( a );
			self.Add( b );
			self.Add( c );

			self.Add( c );
			self.Add( d );
			self.Add( a );
		}
	}

	/// <summary>
	/// Add a quad to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public static void AddQuad( this VertexBuffer self, Vector3 a, Vector3 b, Vector3 c, Vector3 d )
	{
		if ( self.Indexed )
		{
			self.Add( a, new Vector2( 0, 0 ) );
			self.Add( b, new Vector2( 1, 0 ) );
			self.Add( c, new Vector2( 1, 1 ) );
			self.Add( d, new Vector2( 0, 1 ) );

			self.AddTriangleIndex( 4, 3, 2 );
			self.AddTriangleIndex( 2, 1, 4 );
		}
		else
		{
			self.Add( a, new Vector2( 0, 0 ) );
			self.Add( b, new Vector2( 1, 0 ) );
			self.Add( c, new Vector2( 1, 1 ) );

			self.Add( c, new Vector2( 1, 1 ) );
			self.Add( d, new Vector2( 0, 1 ) );
			self.Add( a, new Vector2( 0, 0 ) );
		}
	}

	/// <summary>
	/// Add a quad to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public static void AddQuad( this VertexBuffer self, Ray origin, Vector3 width, Vector3 height )
	{
		self.Default.Normal = origin.Forward;
		self.Default.Tangent = new Vector4( width.Normal, 1 );

		AddQuad( self, origin.Position - width - height,
			origin.Position + width - height,
			origin.Position + width + height,
			origin.Position - width + height );
	}

	/// <summary>
	/// Add a cube to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public static void AddCube( this VertexBuffer self, Vector3 center, Vector3 size, Rotation rot, Color32 color = default )
	{
		var oldColor = self.Default.Color;
		self.Default.Color = color;

		var f = rot.Forward * size.x * 0.5f;
		var l = rot.Left * size.y * 0.5f;
		var u = rot.Up * size.z * 0.5f;

		AddQuad( self, new Ray( center + f, f.Normal ), l, u );
		AddQuad( self, new Ray( center - f, -f.Normal ), l, -u );

		AddQuad( self, new Ray( center + l, l.Normal ), -f, u );
		AddQuad( self, new Ray( center - l, -l.Normal ), f, u );

		AddQuad( self, new Ray( center + u, u.Normal ), f, l );
		AddQuad( self, new Ray( center - u, -u.Normal ), f, -l );

		self.Default.Color = oldColor;
	}
}
