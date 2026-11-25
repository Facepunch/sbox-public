namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoDraw
	{
		public void Position( string name, Rotation? axisRotation = null, float squareSize = 3, bool surfaceCenter = false )
		{
			using var scope = PushScope( name, Vector3.Zero, axisRotation ?? Rotation.Identity );
			using var scaler = GizmoControls.PushFixedScaleScope();
			if ( Settings.GlobalSpace && axisRotation is null )
				Transform = Transform.WithRotation( Rotation.Identity );
			IgnoreDepth = true;
			Color = Colors.Up;
			Arrow( "up", Vector3.Up );
			Color = Colors.Left;
			Arrow( "left", Vector3.Left );
			Color = Colors.Forward;
			Arrow( "forward", Vector3.Forward );
			if ( squareSize <= 0 ) return;

			using var squares = PushScope( "squares" );
			Hitbox.DepthBias *= 0.8f;
			Color = Color.White.WithAlpha( 1.1f );
			if ( surfaceCenter )
				DragCenter( "drag-camera", 2 );
			else
				DragSquare( "drag-camera", squareSize, Transform.RotationToLocal( Camera.Rotation ), DrawPositionCenter );

			DrawAxisSquares( squareSize );
		}

		void DrawAxisSquares( float size )
		{
			var offset = size * 3;
			using ( PushScope() )
			{
				Transform = Transform.ToWorld( new Transform( (Vector3.Up + Vector3.Left) * offset ) );
				Color = Colors.Up;
				DragSquare( "left-up", size, Rotation.LookAt( Vector3.Backward, Vector3.Up ) );
			}
			using ( PushScope() )
			{
				Transform = Transform.ToWorld( new Transform( (Vector3.Forward + Vector3.Left) * offset ) );
				Color = Colors.Left;
				DragSquare( "forward-left", size, Rotation.LookAt( Vector3.Up, Vector3.Forward ) );
			}
			using ( PushScope() )
			{
				Transform = Transform.ToWorld( new Transform( (Vector3.Forward + Vector3.Up) * offset ) );
				Color = Colors.Forward;
				DragSquare( "forward-up", size, Rotation.LookAt( Vector3.Left, Vector3.Down ) );
			}
		}

		static void DrawPositionCenter()
		{
			Draw.LineThickness = 2;
			Draw.LineCircle( 0, IsHovered ? 0.6f : 0.5f, 8 );
		}

		public void Arrow( string name, Vector3 axis, float length = 24, float girth = 6, float axisOffset = 2, float cullAngle = 10, float snapSize = 0, string head = "cone" )
		{
			if ( !GizmoControls.IsAxisVisible( axis, cullAngle ) ) return;
			using var scope = PushScope( name, axis * axisOffset, Rotation.LookAt( axis, Vector3.Up ) );
			girth *= 0.5f;
			Hitbox.BBox( new BBox( new Vector3( 0, -girth, -girth ), new Vector3( length, girth, girth ) ) );
			if ( !IsHovered ) Color = Color.Darken( 0.33f );
			LineThickness = girth;
			if ( snapSize > 0 ) length = length.SnapToGrid( snapSize );
			const float headLength = 4;
			Line( 0, Vector3.Forward * (length - headLength) );
			if ( head == "cone" )
				SolidCone( Vector3.Forward * (length - headLength), Vector3.Forward * headLength, headLength * 0.33f );
			else if ( head == "box" )
				SolidBox( BBox.FromPositionAndSize( Vector3.Forward * (length - headLength), headLength * 0.5f ) );
		}

		public void DragCenter( string name, float size )
		{
			using var scope = PushScope( name );
			Hitbox.BBox( new BBox( new Vector3( -size ), new Vector3( size ) ) );
			if ( !IsHovered ) Color = Color.Darken( 0.33f );
			var drawSize = IsHovered ? size : size * 0.85f;
			SolidBox( new BBox( new Vector3( -drawSize ), new Vector3( drawSize ) ) );
		}

		public void DragBox( string name, Vector3 size, Rotation rotation )
		{
			using var scope = PushScope( name, new Transform( 0, rotation ) );
			var box = new BBox( -size * 0.5f, size * 0.5f );
			Hitbox.BBox( box );
			if ( !IsHovered ) Color = Color.Darken( 0.33f ).Darken( 0.03f );
			SolidBox( box );
		}

		public void DragSquare( string name, Vector2 size, Rotation rotation, Action drawHandle = null )
		{
			using var scope = PushScope( name, new Transform( 0, rotation ) );
			var box = new BBox( new Vector3( -0.01f, -size.x, -size.y ), new Vector3( 0.01f, size.x, size.y ) );
			Hitbox.BBox( box );
			if ( !IsHovered ) Color = Color.Darken( 0.33f );
			if ( drawHandle is not null )
				drawHandle();
			else
			{
				LineThickness = 2;
				LineBBox( box );
			}
		}
	}
}
