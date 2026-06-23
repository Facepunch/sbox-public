namespace Editor.MeshEditor;

/// <summary>
/// Resize everything in the selection using box resize handles.
/// </summary>
[Title( "Resize" )]
[Icon( "device_hub" )]
[Alias( "mesh.resize.mode" )]
[Order( 4 )]
public sealed class ResizeMode : MoveMode
{
	private BBox _startBox;
	private BBox _box;
	private Rotation _basis;

	private Vector3 _activeResizeAxis;
	private Vector3 _resizeTextPosition;
	private float _resizeDistance;
	private bool _isResizing;

	public override void OnBegin( SelectionTool tool )
	{
		_basis = tool.CalculateSelectionBasis();
		_startBox = tool.GlobalSpace ? tool.CalculateSelectionBounds() : tool.CalculateLocalBounds();
		_box = _startBox;

		_activeResizeAxis = default;
		_resizeTextPosition = default;
		_resizeDistance = 0.0f;
		_isResizing = false;
	}

	protected override void OnUpdate( SelectionTool tool )
	{
		if ( !Gizmo.IsLeftMouseDown )
		{
			_isResizing = false;
		}

		var snapTarget = FindVertexSnapTarget( tool );

		using ( Gizmo.Scope( "box", new Transform( Vector3.Zero, _basis ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.CanInteract = CanUseGizmo;

			if ( Gizmo.Control.BoundingBox( "resize", _box, out var outBox, out _, out var resizeAxis ) )
			{
				_box = outBox;
				_activeResizeAxis = resizeAxis;
				_isResizing = true;

				if ( snapTarget.HasValue )
					ApplyVertexSnap( ref _box, resizeAxis, _basis.Inverse * snapTarget.Value );

				UpdateResizeMeasurement( resizeAxis );

				tool.StartDrag();
				ResizeBBox( tool, _startBox, _box, _basis );
				tool.UpdateDrag();
				tool.Pivot = tool.CalculateSelectionOrigin();
			}
		}

		if ( _isResizing && Gizmo.IsLeftMouseDown )
		{
			UpdateResizeMeasurement( _activeResizeAxis );
			DrawResizeText( _resizeTextPosition, _resizeDistance );
		}
	}

	static Vector3? FindVertexSnapTarget( SelectionTool tool )
	{
		var meshTool = tool.Manager?.CurrentTool as MeshTool;
		if ( meshTool?.VertexSnappingEnabled != true || !Gizmo.IsLeftMouseDown )
			return null;

		var gizmoSize = 0.5f * Gizmo.Settings.GizmoScale * Application.DpiScale;
		var closestVertex = tool.MeshTrace.GetClosestVertex( 8 );

		if ( closestVertex.IsValid() )
		{
			DrawVertexIndicator( "VertexSnapTarget", closestVertex.PositionWorld, gizmoSize, Color.Green, drawSprite: true );
			return closestVertex.PositionWorld;
		}

		var nearbyVertex = tool.MeshTrace.GetClosestVertex( 50 );
		if ( nearbyVertex.IsValid() && Vector3.DistanceBetween( nearbyVertex.PositionWorld, tool.Pivot ) > 5f )
			DrawVertexIndicator( "VertexNearby", nearbyVertex.PositionWorld, gizmoSize, Color.Red );

		return null;
	}

	static void DrawVertexIndicator( string name, Vector3 position, float gizmoSize, Color color, bool drawSprite = false )
	{
		var cameraDistance = Gizmo.Camera.Position.Distance( position );
		var scaledGizmo = gizmoSize * (cameraDistance / 50.0f).Clamp( 0.1f, 4.0f );

		using ( Gizmo.Scope( name ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = color;

			if ( drawSprite )
				Gizmo.Draw.Sprite( position, 8, null, false );

			Gizmo.Transform = new Transform( position, Rotation.LookAt( Gizmo.LocalCameraTransform.Rotation.Backward ) );
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.LineCircle( 0, Vector3.Forward, scaledGizmo );
		}
	}

	static void ApplyVertexSnap( ref BBox box, Vector3 axis, Vector3 target )
	{
		var i = FaceAxis( axis );

		if ( IsMaxsFace( axis ) ) box.Maxs[i] = target[i];
		else box.Mins[i] = target[i];
	}

	static void ResizeBBox( SelectionTool tool, BBox prevBox, BBox newBox, Rotation basis )
	{
		var prevSize = prevBox.Size;
		var newSize = newBox.Size;
		var dMin = newBox.Mins - prevBox.Mins;
		var dMax = newBox.Maxs - prevBox.Maxs;

		var scale = Vector3.One;
		var origin = prevBox.Center;

		for ( var i = 0; i < 3; i++ )
		{
			if ( !prevSize[i].AlmostEqual( 0.0f ) ) scale[i] = newSize[i] / prevSize[i];
			if ( MathF.Abs( dMax[i] ) > MathF.Abs( dMin[i] ) ) origin[i] = prevBox.Mins[i];
			else if ( MathF.Abs( dMin[i] ) > MathF.Abs( dMax[i] ) ) origin[i] = prevBox.Maxs[i];
		}

		tool.Resize( basis * origin, basis, scale );
	}

	private void UpdateResizeMeasurement( Vector3 resizeAxis )
	{
		var startLocal = GetResizeFaceCenter( _startBox, resizeAxis );
		var endLocal = GetResizeFaceCenter( _box, resizeAxis );

		var handleWorld = _basis * endLocal;
		var outwardWorld = GetResizeFaceOutward( resizeAxis );

		_resizeDistance = startLocal.Distance( endLocal );

		var cameraDistance = Gizmo.Camera.Position.Distance( handleWorld );
		var worldOffset = 10.0f * Gizmo.Settings.GizmoScale * (cameraDistance / 50.0f).Clamp( 0.5f, 4.0f );

		_resizeTextPosition = handleWorld + outwardWorld * worldOffset;
	}

	static Vector3 GetResizeFaceCenter( BBox box, Vector3 axis )
	{
		var i = FaceAxis( axis );
		var point = box.Center;

		point[i] = IsMaxsFace( axis ) ? box.Maxs[i] : box.Mins[i];

		return point;
	}

	private Vector3 GetResizeFaceOutward( Vector3 axis )
	{
		var i = FaceAxis( axis );
		var outward = Vector3.Zero;

		outward[i] = IsMaxsFace( axis ) ? 1.0f : -1.0f;

		return (_basis * outward).Normal;
	}

	private void DrawResizeText( Vector3 position, float distance )
	{
		using ( Gizmo.Scope( "ResizeText" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			var textSize = 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;

			var cameraDistance = Gizmo.Camera.Position.Distance( position );
			var scaledTextSize = textSize * (cameraDistance / 50.0f).Clamp( 0.5f, 1.0f );

			var textScope = new TextRendering.Scope
			{
				Text = $"{distance:0.##}",
				TextColor = Color.White,
				FontSize = scaledTextSize,
				FontName = "Roboto Mono",
				FontWeight = 600,
				LineHeight = 1,
				Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
			};

			Gizmo.Draw.ScreenText( textScope, position, new Vector2( 0, -scaledTextSize * 0.5f ) );
		}
	}

	static int FaceAxis( Vector3 axis ) => axis.x != 0 ? 0 : axis.y != 0 ? 1 : 2;
	static bool IsMaxsFace( Vector3 axis ) => axis[FaceAxis( axis )] > 0;
}
