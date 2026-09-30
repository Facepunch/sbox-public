using System;
using System.Collections.Generic;
using System.Linq;


namespace Sandbox;

public partial class VertexBuffer
{
	/// <summary>
	/// Add a vertex using this position and everything else from Default
	/// </summary>
	public void Add( Vector3 pos )
	{
		var v = Default;
		v.Position = pos;
		Add( v );
	}

	/// <summary>
	/// Add a vertex using this position and UV, and everything else from Default
	/// </summary>
	public void Add( Vector3 pos, Vector2 uv )
	{
		var v = Default;
		v.Position = pos;
		v.TexCoord0.x = uv.x;
		v.TexCoord0.y = uv.y;

		Add( v );
	}

	/// <summary>
	/// Add a triangle to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public void AddTriangle( Vertex a, Vertex b, Vertex c )
	{
		Add( a );
		Add( b );
		Add( c );

		if ( Indexed )
		{
			AddTriangleIndex( 3, 2, 1 );
		}
	}

	/// <summary>
	/// Add a quad to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public void AddQuad( Rect rect )
	{
		var pos = rect.Position;
		var size = rect.Size;

		AddQuad( pos, new Vector2( pos.x + size.x, pos.y ), pos + size, new Vector2( pos.x, pos.y + size.y ) );
	}

	/// <summary>
	/// Add a quad to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public void AddQuad( Vertex a, Vertex b, Vertex c, Vertex d )
	{
		if ( Indexed )
		{
			Add( a );
			Add( b );
			Add( c );
			Add( d );

			AddTriangleIndex( 4, 3, 2 );
			AddTriangleIndex( 2, 1, 4 );
		}
		else
		{
			Add( a );
			Add( b );
			Add( c );

			Add( c );
			Add( d );
			Add( a );
		}
	}

	/// <summary>
	/// Add a quad to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public void AddQuad( Vector3 a, Vector3 b, Vector3 c, Vector3 d )
	{
		if ( Indexed )
		{
			Add( a, new Vector2( 0, 0 ) );
			Add( b, new Vector2( 1, 0 ) );
			Add( c, new Vector2( 1, 1 ) );
			Add( d, new Vector2( 0, 1 ) );

			AddTriangleIndex( 4, 3, 2 );
			AddTriangleIndex( 2, 1, 4 );
		}
		else
		{
			Add( a, new Vector2( 0, 0 ) );
			Add( b, new Vector2( 1, 0 ) );
			Add( c, new Vector2( 1, 1 ) );

			Add( c, new Vector2( 1, 1 ) );
			Add( d, new Vector2( 0, 1 ) );
			Add( a, new Vector2( 0, 0 ) );
		}
	}

	/// <summary>
	/// Add a quad to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public void AddQuad( Ray origin, Vector3 width, Vector3 height )
	{
		Default.Normal = origin.Forward;
		Default.Tangent = new Vector4( width.Normal, 1 );

		AddQuad( origin.Position - width - height,
			origin.Position + width - height,
			origin.Position + width + height,
			origin.Position - width + height );
	}

	/// <summary>
	/// Add a cube to the vertex buffer. Will include indices if they're enabled.
	/// </summary>
	public void AddCube( Vector3 center, Vector3 size, Rotation rot, Color32 color = default )
	{
		var oldColor = Default.Color;
		Default.Color = color;

		var f = rot.Forward * size.x * 0.5f;
		var l = rot.Left * size.y * 0.5f;
		var u = rot.Up * size.z * 0.5f;

		AddQuad( new Ray( center + f, f.Normal ), l, u );
		AddQuad( new Ray( center - f, -f.Normal ), l, -u );

		AddQuad( new Ray( center + l, l.Normal ), -f, u );
		AddQuad( new Ray( center - l, -l.Normal ), f, u );

		AddQuad( new Ray( center + u, u.Normal ), f, l );
		AddQuad( new Ray( center - u, -u.Normal ), f, -l );

		Default.Color = oldColor;
	}
}

/// <summary>
/// The extension-method home these helpers had when they lived in the base library. Compiled
/// games call through here; new code uses the <see cref="VertexBuffer"/> members directly.
/// </summary>
[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
public static class SandboxBaseExtensions
{
	public static void Add( this VertexBuffer self, Vector3 pos ) => self.Add( pos );
	public static void Add( this VertexBuffer self, Vector3 pos, Vector2 uv ) => self.Add( pos, uv );
	public static void AddTriangle( this VertexBuffer self, Vertex a, Vertex b, Vertex c ) => self.AddTriangle( a, b, c );
	public static void AddQuad( this VertexBuffer self, Rect rect ) => self.AddQuad( rect );
	public static void AddQuad( this VertexBuffer self, Vertex a, Vertex b, Vertex c, Vertex d ) => self.AddQuad( a, b, c, d );
	public static void AddQuad( this VertexBuffer self, Vector3 a, Vector3 b, Vector3 c, Vector3 d ) => self.AddQuad( a, b, c, d );
	public static void AddQuad( this VertexBuffer self, Ray origin, Vector3 width, Vector3 height ) => self.AddQuad( origin, width, height );
	public static void AddCube( this VertexBuffer self, Vector3 center, Vector3 size, Rotation rot, Color32 color = default ) => self.AddCube( center, size, rot, color );
}
