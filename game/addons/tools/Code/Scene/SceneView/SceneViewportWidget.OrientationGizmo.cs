namespace Editor;

public partial class SceneViewportWidget
{
	// World directions for the six handles, ordered X+, X-, Y+, Y-, Z+, Z-
	private static readonly Vector3[] GizmoAxes =
	{
		Vector3.Forward, Vector3.Backward, // X+, X-
		Vector3.Left, Vector3.Right,       // Y+, Y-
		Vector3.Up, Vector3.Down,          // Z+, Z-
	};

	private static readonly string[] GizmoAxisLabels = { "X", "X", "Y", "Y", "Z", "Z" };

	private static Color GizmoAxisColor( int axis ) => (axis / 2) switch
	{
		0 => Color.FromBytes( 226, 84, 84 ),  // X - red
		1 => Color.FromBytes( 130, 200, 80 ), // Y - green
		_ => Color.FromBytes( 74, 140, 226 ), // Z - blue
	};

	private bool _gizmoHovered;
	private int _gizmoHoveredAxis = -1;
	private bool _gizmoDragging;
	private bool _gizmoMouseDown;
	private bool _gizmoLeftWasDown;
	private Vector2 _gizmoPressPos;
	private Vector2 _gizmoLockCursor;

	private float GizmoRadius => 30f * Renderer.DpiScale;
	private float GizmoBallRadius => 7f * Renderer.DpiScale;

	private Vector2 GizmoCenter
	{
		get
		{
			var inset = GizmoRadius + GizmoBallRadius + 20f * Renderer.DpiScale;
			return new Vector2( Renderer.Size.x * Renderer.DpiScale - inset, inset );
		}
	}

	private Vector2 GizmoAxisScreenDir( Vector3 worldDir, out float depth )
	{
		var local = _activeCamera.WorldRotation.Inverse * worldDir;
		depth = local.x;
		return new Vector2( -local.y, -local.z );
	}

	private Vector3 ComputeOrbitPivot()
	{
		var pos = _activeCamera.WorldPosition;
		var fwd = _activeCamera.WorldRotation.Forward;

		var plane = new Plane( Vector3.Up, 0f );
		if ( plane.TryTrace( new Ray( pos, fwd ), out var hit, twosided: true, maxDistance: 500f ) )
			return hit;

		return (pos + fwd * 500f).WithZ( 0f );
	}

	private bool UpdateOrientationGizmo( bool hasMouseFocus )
	{
		if ( !_activeCamera.IsValid() )
			return false;

		var center = GizmoCenter;
		var radius = GizmoRadius;
		var ballRadius = GizmoBallRadius;

		// Pick the front-most handle under the cursor, but not while dragging
		_gizmoHoveredAxis = -1;
		var pickDist = ballRadius * 1.6f;
		var bestDepth = float.MaxValue;
		if ( !_gizmoDragging )
		{
			for ( int i = 0; i < GizmoAxes.Length; i++ )
			{
				var pos = center + GizmoAxisScreenDir( GizmoAxes[i], out var depth ) * radius;
				var d = Vector2.DistanceBetween( MousePosition, pos );
				if ( d <= pickDist && depth < bestDepth )
				{
					bestDepth = depth;
					_gizmoHoveredAxis = i;
				}
			}
		}

		var overBody = Vector2.DistanceBetween( MousePosition, center ) <= radius + ballRadius;
		var interacting = _gizmoMouseDown || _gizmoDragging;
		var appActive = IsActiveWindow || (Overlay.IsValid() && Overlay.IsActiveWindow);
		_gizmoHovered = appActive && (interacting || overBody || _gizmoHoveredAxis >= 0);

		var leftDown = Application.MouseButtons.HasFlag( MouseButtons.Left );

		// Begin interaction only on a direct click on the gizmo, so a scene drag that happens to pass over doesn't take over
		if ( leftDown && !_gizmoLeftWasDown && !_gizmoMouseDown && _gizmoHovered )
		{
			_gizmoMouseDown = true;
			_gizmoDragging = false;
			_gizmoPressPos = MousePosition;
			_gizmoLockCursor = Application.UnscaledCursorPosition;
		}

		_gizmoLeftWasDown = leftDown;

		if ( _gizmoMouseDown )
		{
			if ( leftDown )
			{
				if ( !_gizmoDragging && Vector2.DistanceBetween( MousePosition, _gizmoPressPos ) > 4f * Renderer.DpiScale )
					_gizmoDragging = true;

				if ( _gizmoDragging )
				{
					OrbitAroundPivot();

					// Lock the mouse cursor in place
					Renderer.Cursor = CursorShape.Blank;
					if ( Overlay.IsValid() )
						Overlay.Cursor = CursorShape.Blank;
					Application.UnscaledCursorPosition = _gizmoLockCursor;
				}
			}
			else
			{
				// Released without dragging over a handle -> snap to that axis view.
				if ( !_gizmoDragging && _gizmoHoveredAxis >= 0 )
					SnapToAxis( _gizmoHoveredAxis );

				// Restore the cursor to where the drag began (over the gizmo) so hover resumes, and make it visible
				if ( _gizmoDragging )
				{
					Application.UnscaledCursorPosition = _gizmoLockCursor;
					Renderer.Cursor = CursorShape.None;
					if ( Overlay.IsValid() )
						Overlay.Cursor = CursorShape.None;
				}

				_gizmoMouseDown = false;
				_gizmoDragging = false;
			}
		}

		return _gizmoHovered || _gizmoMouseDown || _gizmoDragging;
	}

	private void OrbitAroundPivot()
	{
		EnterPerspectiveView();

		var delta = Application.CursorDelta * 0.1f;
		GizmoInstance.OrbitCameraAroundPivot( _activeCamera, delta, ref cameraOrbitDistance );

		State.CameraRotation = _activeCamera.WorldRotation;
		State.CameraPosition = _activeCamera.WorldPosition;

		Renderer.Focus();
	}

	private void SnapToAxis( int axis )
	{
		var axisDir = GizmoAxes[axis];
		var gizmoPivot = ComputeOrbitPivot();

		// If we're already looking down this axis, toggle between ortho/perspective
		var alreadyAligned = Vector3.Dot( State.CameraRotation.Forward, -axisDir ) > 0.999f;
		var currentlyOrtho = _gizmoOrthoActive || State.Is2D;
		if ( alreadyAligned && currentlyOrtho )
		{
			EnterPerspectiveView();
			return;
		}

		var distance = (State.CameraPosition - gizmoPivot).Length;
		if ( distance < 1f )
			distance = 400f;

		// Ensure 2D views always have X+ right
		Vector3 up;
		if ( axisDir.z > 0.5f ) up = Vector3.Left;
		else if ( axisDir.z < -0.5f ) up = Vector3.Right;
		else up = Vector3.Up;

		EnterOrthoView( axisDir, up, gizmoPivot, distance );
	}

	internal void PaintOrientationGizmo()
	{
		if ( !_activeCamera.IsValid() )
			return;

		const float radius = 30f;
		const float ballRadius = 7f;
		var inset = radius + ballRadius + 20f;
		var rendererOffset = Renderer.ScreenPosition - ScreenPosition;
		var center = rendererOffset + new Vector2( Renderer.Width - inset, inset );

		Paint.Antialiasing = true;

		if ( _gizmoHovered )
		{
			var bg = radius + ballRadius + 4f;
			Paint.ClearPen();
			Paint.SetBrush( Color.Black.WithAlpha( 0.1f ) );
			Paint.DrawCircle( center, new Vector2( bg * 2f ) );
		}

		Span<int> order = stackalloc int[GizmoAxes.Length];
		Span<float> depths = stackalloc float[GizmoAxes.Length];
		Span<Vector2> positions = stackalloc Vector2[GizmoAxes.Length];

		for ( int i = 0; i < GizmoAxes.Length; i++ )
		{
			positions[i] = center + GizmoAxisScreenDir( GizmoAxes[i], out var depth ) * radius;
			depths[i] = depth;
			order[i] = i;
		}

		for ( int i = 1; i < order.Length; i++ )
		{
			var key = order[i];
			int j = i - 1;
			while ( j >= 0 && depths[order[j]] < depths[key] )
			{
				order[j + 1] = order[j];
				j--;
			}
			order[j + 1] = key;
		}

		foreach ( var i in order )
		{
			var positive = (i % 2) == 0;
			var color = GizmoAxisColor( i );
			var pos = positions[i];

			// Fade handles that are further back
			var facing = depths[i].Remap( 1f, -1f, 0.5f, 1f, true );
			var hovered = _gizmoHoveredAxis == i;

			// Positive axes get a solid line, negative axes a thinner dashed line. Hovered axes are highlighted.
			var lineColor = (hovered ? Color.Lerp( color, Color.White, 0.5f ) : color)
				.WithAlpha( facing * (positive ? (hovered ? 1f : 0.8f) : (hovered ? 0.9f : 0.45f)) );
			Paint.SetPen( lineColor, positive ? (hovered ? 2.5f : 2f) : (hovered ? 2f : 1.5f), positive ? PenStyle.Solid : PenStyle.Dash );
			Paint.DrawLine( center, pos );

			if ( positive )
			{
				Paint.SetBrush( color.WithAlpha( facing ) );
				if ( hovered )
					Paint.SetPen( Color.White, 1.5f );
				else
					Paint.ClearPen();

				Paint.DrawCircle( pos, new Vector2( ballRadius * 2f ) );
			}
			else
			{
				// Hollow ball for negative axes.
				Paint.SetBrush( Color.Black.WithAlpha( facing * 0.5f ) );
				Paint.SetPen( (hovered ? Color.White : color).WithAlpha( facing ), 1.5f );
				Paint.DrawCircle( pos, new Vector2( ballRadius * 2f ) );
			}
		}

		// X/Y/Z labels for the positive axes
		Paint.SetFont( "Roboto", 8, 200 );
		Paint.SetPen( Color.Black );
		foreach ( var i in order )
		{
			if ( (i % 2) != 0 )
				continue;

			Paint.DrawText( new Rect( positions[i] - new Vector2( ballRadius ), ballRadius * 2f ), GizmoAxisLabels[i], TextFlag.Center );
		}
	}
}
