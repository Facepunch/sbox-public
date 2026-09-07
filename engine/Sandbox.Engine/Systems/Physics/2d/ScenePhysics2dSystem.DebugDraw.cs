using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NativeEngine;

namespace Sandbox;

sealed partial class ScenePhysics2dSystem
{
	[ThreadStatic]
	static DebugOverlaySystem _currentOverlay;

	[StructLayout( LayoutKind.Sequential )]
	unsafe struct ManagedDebugDraw
	{
		public nint DrawPolygonFcn;
		public nint DrawSolidPolygonFcn;
		public nint DrawCircleFcn;
		public nint DrawSolidCircleFcn;
		public nint DrawSolidCapsuleFcn;
		public nint DrawLineFcn;
		public nint DrawTransformFcn;
		public nint DrawPointFcn;
		public nint DrawStringFcn;

		public b2AABB drawingBounds;

		public float forceScale;
		public float jointScale;

		public byte drawContacts;
		public byte drawAnchorA;
		public byte drawShapes;
		public byte drawJoints;
		public byte drawJointExtras;
		public byte drawBounds;
		public byte drawMass;
		public byte drawBodyNames;
		public byte drawGraphColors;
		public byte drawContactFeatures;
		public byte drawContactNormals;
		public byte drawContactForces;
		public byte drawFrictionForces;
		public byte drawIslands;

		public nint context;
	}

	static ManagedDebugDraw _debugDraw;
	static bool _debugDrawInitialized;

	static unsafe void EnsureDebugDrawInitialized()
	{
		if ( _debugDrawInitialized ) return;

		_debugDraw = new ManagedDebugDraw();
		_debugDraw.drawingBounds = new b2AABB
		{
			lowerBound = new b2Vec2 { x = float.MinValue, y = float.MinValue },
			upperBound = new b2Vec2 { x = float.MaxValue, y = float.MaxValue }
		};

		_debugDraw.DrawPolygonFcn = (nint)(delegate* unmanaged[Cdecl]< b2Vec2*, int, int, nint, void >)&OnDrawPolygon;
		_debugDraw.DrawSolidPolygonFcn = (nint)(delegate* unmanaged[Cdecl]< b2Transform, b2Vec2*, int, float, int, nint, void >)&OnDrawSolidPolygon;
		_debugDraw.DrawCircleFcn = (nint)(delegate* unmanaged[Cdecl]< b2Vec2, float, int, nint, void >)&OnDrawCircle;
		_debugDraw.DrawSolidCircleFcn = (nint)(delegate* unmanaged[Cdecl]< b2Transform, float, int, nint, void >)&OnDrawSolidCircle;
		_debugDraw.DrawSolidCapsuleFcn = (nint)(delegate* unmanaged[Cdecl]< b2Vec2, b2Vec2, float, int, nint, void >)&OnDrawSolidCapsule;
		_debugDraw.DrawLineFcn = (nint)(delegate* unmanaged[Cdecl]< b2Vec2, b2Vec2, int, nint, void >)&OnDrawLine;
		_debugDraw.DrawTransformFcn = (nint)(delegate* unmanaged[Cdecl]< b2Transform, nint, void >)&OnDrawTransform;
		_debugDraw.DrawPointFcn = (nint)(delegate* unmanaged[Cdecl]< b2Vec2, float, int, nint, void >)&OnDrawPoint;
		_debugDraw.DrawStringFcn = (nint)(delegate* unmanaged[Cdecl]< b2Vec2, nint, int, nint, void >)&OnDrawString;

		_debugDrawInitialized = true;
	}

	static void UpdateDebugDrawFlags()
	{
		byte shapes = (byte)ConVarSystem.GetInt( "physics_debug_draw", 0, true );
		byte mass = (byte)ConVarSystem.GetInt( "physics_debug_draw_mass", 0, true );
		byte contacts = (byte)ConVarSystem.GetInt( "physics_debug_draw_contacts", 0, true );
		byte joints = (byte)ConVarSystem.GetInt( "physics_debug_draw_joints", 0, true );
		byte bounds = (byte)ConVarSystem.GetInt( "physics_debug_draw_bounds", 0, true );

		_debugDraw.drawShapes = shapes;
		_debugDraw.drawMass = mass;
		_debugDraw.drawContacts = contacts;
		_debugDraw.drawContactFeatures = contacts;
		_debugDraw.drawContactNormals = contacts;
		_debugDraw.drawContactForces = contacts;
		_debugDraw.drawFrictionForces = contacts;
		_debugDraw.drawJoints = joints;
		_debugDraw.drawJointExtras = joints;
		_debugDraw.drawBounds = bounds;
		_debugDraw.drawIslands = bounds;
	}

	unsafe void DrawDebug()
	{
		if ( !Scene.Is2D )
			return;

		if ( ConVarSystem.GetInt( "physics_debug_draw", 0, true ) != 1 )
			return;

		var overlay = Scene.GetSystem<DebugOverlaySystem>();
		if ( overlay is null )
			return;

		var world = World;
		if ( !world.IsValid() )
			return;

		_currentOverlay = overlay;

		EnsureDebugDrawInitialized();
		UpdateDebugDrawFlags();
		fixed ( ManagedDebugDraw* ptr = &_debugDraw )
		{
			Box2d.b2World_Draw( world.WorldId, (nint)ptr );
		}

		_currentOverlay = null;
	}

	static Color HexToColor( int hex )
	{
		float r = ((hex >> 16) & 0xFF) / 255f;
		float g = ((hex >> 8) & 0xFF) / 255f;
		float b = (hex & 0xFF) / 255f;
		return new Color( r, g, b );
	}


	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static unsafe void OnDrawPolygon( b2Vec2* vertices, int vertexCount, int color, nint context )
	{
		var overlay = _currentOverlay;
		if ( overlay is null ) return;

		try
		{
			var c = HexToColor( color );
			for ( int i = 0; i < vertexCount; i++ )
			{
				int next = (i + 1) % vertexCount;
				overlay.Line( (Vector3)vertices[i], (Vector3)vertices[next], c, overlay: true );
			}
		}
		catch ( Exception e )
		{
			OnDrawFailed( e );
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void OnDrawCircle( b2Vec2 center, float radius, int color, nint context )
	{
		var overlay = _currentOverlay;
		if ( overlay is null ) return;

		try
		{
			DrawCircleLines( overlay, center, radius, HexToColor( color ) );
		}
		catch ( Exception e )
		{
			OnDrawFailed( e );
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static unsafe void OnDrawSolidPolygon( b2Transform transform, b2Vec2* vertices, int vertexCount, float radius, int color, nint context )
	{
		var overlay = _currentOverlay;
		if ( overlay is null ) return;

		try
		{
			var c = HexToColor( color );
			for ( int i = 0; i < vertexCount; i++ )
			{
				int next = (i + 1) % vertexCount;
				Vector3 a = TransformPoint( transform, vertices[i] );
				Vector3 b = TransformPoint( transform, vertices[next] );
				overlay.Line( a, b, c, overlay: true );
			}
		}
		catch ( Exception e )
		{
			OnDrawFailed( e );
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void OnDrawSolidCircle( b2Transform transform, float radius, int color, nint context )
	{
		var overlay = _currentOverlay;
		if ( overlay is null ) return;

		try
		{
			Vector3 center = transform.p;
			var c = HexToColor( color );

			DrawCircleLines( overlay, center, radius, c );

			var direction = new Vector3( transform.q.c, transform.q.s, 0 );
			overlay.Line( center, center + direction * radius, c, overlay: true );
		}
		catch ( Exception e )
		{
			OnDrawFailed( e );
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void OnDrawSolidCapsule( b2Vec2 p1, b2Vec2 p2, float radius, int color, nint context )
	{
		var overlay = _currentOverlay;
		if ( overlay is null ) return;

		try
		{
			Vector3 c1 = p1;
			Vector3 c2 = p2;
			var c = HexToColor( color );

			var diff = c2 - c1;
			var dir = diff.IsNearZeroLength ? Vector3.Up : diff.Normal;
			var perp = new Vector3( -dir.y, dir.x, 0 );

			overlay.Line( c1 + perp * radius, c2 + perp * radius, c, overlay: true );
			overlay.Line( c1 - perp * radius, c2 - perp * radius, c, overlay: true );

			const int halfSegments = 12;
			DrawArc( overlay, c1, perp, -dir, radius, halfSegments, c );
			DrawArc( overlay, c2, perp, dir, radius, halfSegments, c );
		}
		catch ( Exception e )
		{
			OnDrawFailed( e );
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void OnDrawLine( b2Vec2 p1, b2Vec2 p2, int color, nint context )
	{
		var overlay = _currentOverlay;
		if ( overlay is null ) return;

		try
		{
			overlay.Line( (Vector3)p1, (Vector3)p2, HexToColor( color ), overlay: true );
		}
		catch ( Exception e )
		{
			OnDrawFailed( e );
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void OnDrawPoint( b2Vec2 p, float size, int color, nint context )
	{
		var overlay = _currentOverlay;
		if ( overlay is null ) return;

		try
		{
			Vector3 pos = p;
			var c = HexToColor( color );
			var half = size * 0.5f;

			overlay.Line( pos - new Vector3( half, 0, 0 ), pos + new Vector3( half, 0, 0 ), c, overlay: true );
			overlay.Line( pos - new Vector3( 0, half, 0 ), pos + new Vector3( 0, half, 0 ), c, overlay: true );
		}
		catch ( Exception e )
		{
			OnDrawFailed( e );
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void OnDrawTransform( b2Transform transform, nint context )
	{
		var overlay = _currentOverlay;
		if ( overlay is null ) return;

		try
		{
			Vector3 pos = transform.p;
			const float axisLength = 5.0f;

			var xAxis = new Vector3( transform.q.c, transform.q.s, 0 );
			var yAxis = new Vector3( -transform.q.s, transform.q.c, 0 );

			overlay.Line( pos, pos + xAxis * axisLength, Color.Red, overlay: true );
			overlay.Line( pos, pos + yAxis * axisLength, Color.Green, overlay: true );
		}
		catch ( Exception e )
		{
			OnDrawFailed( e );
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static unsafe void OnDrawString( b2Vec2 p, nint str, int color, nint context )
	{
		var overlay = _currentOverlay;
		if ( overlay is null ) return;

		try
		{
			var text = Marshal.PtrToStringAnsi( str );
			if ( string.IsNullOrEmpty( text ) ) return;

			var c = HexToColor( color );
			var scope = new TextRendering.Scope( text, c, 10, "Lucida Console", 400 )
			{
				Outline = new TextRendering.Outline { Color = Color.Black, Size = 1, Enabled = true },
				FilterMode = Rendering.FilterMode.Point,
				FontSmooth = UI.FontSmooth.Never,
			};

			overlay.ScreenText( p, scope );
		}
		catch ( Exception e )
		{
			OnDrawFailed( e );
		}
	}

	/// <summary>
	/// Clears the overlay so the rest of this draw pass bails out instead of throwing per primitive.
	/// </summary>
	static void OnDrawFailed( Exception e )
	{
		_currentOverlay = null;
		Log.Warning( e, $"Error in 2D physics debug draw: {e.Message}" );
	}

	static b2Vec2 TransformPoint( b2Transform tx, b2Vec2 p )
	{
		return new b2Vec2
		{
			x = tx.p.x + tx.q.c * p.x - tx.q.s * p.y,
			y = tx.p.y + tx.q.s * p.x + tx.q.c * p.y
		};
	}

	static void DrawCircleLines( DebugOverlaySystem overlay, Vector3 center, float radius, Color color, int segments = 24 )
	{
		for ( int i = 0; i < segments; i++ )
		{
			float a0 = (i / (float)segments) * MathF.Tau;
			float a1 = ((i + 1) / (float)segments) * MathF.Tau;

			var p0 = center + new Vector3( MathF.Cos( a0 ), MathF.Sin( a0 ), 0 ) * radius;
			var p1 = center + new Vector3( MathF.Cos( a1 ), MathF.Sin( a1 ), 0 ) * radius;
			overlay.Line( p0, p1, color, overlay: true );
		}
	}

	static void DrawArc( DebugOverlaySystem overlay, Vector3 center, Vector3 right, Vector3 forward, float radius, int segments, Color color )
	{
		for ( int i = 0; i < segments; i++ )
		{
			float a0 = (i / (float)segments) * MathF.PI;
			float a1 = ((i + 1) / (float)segments) * MathF.PI;

			var p0 = center + (right * MathF.Cos( a0 ) + forward * MathF.Sin( a0 )) * radius;
			var p1 = center + (right * MathF.Cos( a1 ) + forward * MathF.Sin( a1 )) * radius;
			overlay.Line( p0, p1, color, overlay: true );
		}
	}
}
