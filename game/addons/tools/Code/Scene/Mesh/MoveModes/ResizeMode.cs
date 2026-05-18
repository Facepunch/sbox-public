
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
	private BBox _deltaBox;
	private BBox _box;
	private Rotation _basis;

	public override void OnBegin( SelectionTool tool )
	{
		_basis = tool.CalculateSelectionBasis();
		_startBox = tool.GlobalSpace ? tool.CalculateSelectionBounds() : tool.CalculateLocalBounds();
		_deltaBox = default;
		_box = _startBox;
	}

	protected override void OnUpdate( SelectionTool tool )
	{
		var snapTarget = FindVertexSnapTarget( tool );

		using ( Gizmo.Scope( "box", new Transform( Vector3.Zero, _basis ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.CanInteract = CanUseGizmo;

			if ( !Gizmo.Control.BoundingBox( "resize", _box, out var outBox, out _, out var resizeAxis ) )
				return;

			_deltaBox.Mins += outBox.Mins - _box.Mins;
			_deltaBox.Maxs += outBox.Maxs - _box.Maxs;

			_box = Snap( _startBox, _deltaBox );

			if ( snapTarget.HasValue )
				ApplyVertexSnap( ref _box, resizeAxis, _basis.Inverse * snapTarget.Value );

			tool.StartDrag();
			ResizeBBox( tool, _startBox, _box, _basis );
			tool.UpdateDrag();
			tool.Pivot = tool.CalculateSelectionOrigin();
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

	static BBox Snap( BBox startBox, BBox movement )
	{
		var mins = startBox.Mins + movement.Mins;
		var maxs = startBox.Maxs + movement.Maxs;

		if ( Gizmo.Settings.SnapToGrid != Gizmo.IsCtrlPressed )
		{
			mins = Gizmo.Snap( mins, movement.Mins );
			maxs = Gizmo.Snap( maxs, movement.Maxs );
		}

		var result = default( BBox );
		result.Mins = mins;
		result.Maxs = maxs;
		return result;
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

	static int FaceAxis( Vector3 axis ) => axis.x != 0 ? 0 : axis.y != 0 ? 1 : 2;
	static bool IsMaxsFace( Vector3 axis ) => axis[FaceAxis( axis )] > 0;
}
