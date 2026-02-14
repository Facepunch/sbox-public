
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

	public override void OnBegin( SelectionTool tool )
	{
		_startBox = tool.CalculateSelectionBounds();
		_deltaBox = default;
		_box = _startBox;
	}

	protected override void OnUpdate( SelectionTool tool )
	{
		var size = _startBox.Size;
		if ( size.x.AlmostEqual( 0.0f ) ) return;
		if ( size.y.AlmostEqual( 0.0f ) ) return;
		if ( size.z.AlmostEqual( 0.0f ) ) return;

		var meshTool = tool.Manager?.CurrentTool as MeshTool;
		Vector3? snapTarget = null;

		if ( meshTool?.VertexSnappingEnabled == true && Gizmo.IsLeftMouseDown )
		{
			var gizmoSize = 0.5f * Gizmo.Settings.GizmoScale * Application.DpiScale;

			var closestVertex = tool.MeshTrace.GetClosestVertex( 8 );
			if ( closestVertex.IsValid() )
			{
				var cameraDistance = Gizmo.Camera.Position.Distance( closestVertex.PositionWorld );
				var scaledGizmo = gizmoSize * (cameraDistance / 50.0f).Clamp( 0.1f, 4.0f );

				snapTarget = closestVertex.PositionWorld;

				using ( Gizmo.Scope( "VertexSnapTarget" ) )
				{
					Gizmo.Draw.IgnoreDepth = true;
					Gizmo.Draw.Color = Color.Green;
					Gizmo.Draw.Sprite( snapTarget.Value, 8, null, false );

					Gizmo.Transform = new Transform( snapTarget.Value, Rotation.LookAt( Gizmo.LocalCameraTransform.Rotation.Backward ) );
					Gizmo.Draw.LineThickness = 2;
					Gizmo.Draw.LineCircle( 0, Vector3.Forward, scaledGizmo );
				}
			}
			else
			{
				var nearbyVertex = tool.MeshTrace.GetClosestVertex( 50 );
				if ( nearbyVertex.IsValid() )
				{
					var cameraDistance = Gizmo.Camera.Position.Distance( nearbyVertex.PositionWorld );
					var scaledGizmo = gizmoSize * (cameraDistance / 50.0f).Clamp( 0.1f, 4.0f );
					var distance = Vector3.DistanceBetween( nearbyVertex.PositionWorld, tool.Pivot );

					if ( distance > 5f )
					{
						using ( Gizmo.Scope( "VertexNearby" ) )
						{
							Gizmo.Draw.IgnoreDepth = true;

							Gizmo.Draw.Color = Color.Red;
							Gizmo.Transform = new Transform( nearbyVertex.PositionWorld, Rotation.LookAt( Gizmo.LocalCameraTransform.Rotation.Backward ) );
							Gizmo.Draw.LineThickness = 2;
							Gizmo.Draw.LineCircle( 0, Vector3.Forward, scaledGizmo );
						}
					}
				}
			}
		}

		using ( Gizmo.Scope( "box" ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.CanInteract = CanUseGizmo;

			if ( Gizmo.Control.BoundingBox( "resize", _box, out var outBox ) )
			{
				var moveMins = outBox.Mins - _box.Mins;
				var moveMaxs = outBox.Maxs - _box.Maxs;

				_deltaBox.Maxs += moveMaxs;
				_deltaBox.Mins += moveMins;

				_box = Snap( _startBox, _deltaBox );

				if ( snapTarget.HasValue )
				{
					var target = snapTarget.Value;
					var threshold = 0.001f;

					if ( MathF.Abs( moveMins.x ) > threshold ) _box.Mins.x = target.x;
					if ( MathF.Abs( moveMins.y ) > threshold ) _box.Mins.y = target.y;
					if ( MathF.Abs( moveMins.z ) > threshold ) _box.Mins.z = target.z;

					if ( MathF.Abs( moveMaxs.x ) > threshold ) _box.Maxs.x = target.x;
					if ( MathF.Abs( moveMaxs.y ) > threshold ) _box.Maxs.y = target.y;
					if ( MathF.Abs( moveMaxs.z ) > threshold ) _box.Maxs.z = target.z;

					_deltaBox.Mins = _box.Mins - _startBox.Mins;
					_deltaBox.Maxs = _box.Maxs - _startBox.Maxs;
				}

				tool.StartDrag();

				ResizeBBox( tool, _startBox, _box, Rotation.Identity );

				tool.UpdateDrag();

				tool.Pivot = tool.CalculateSelectionOrigin();
			}
		}
	}

	static BBox Snap( BBox startBox, BBox movement )
	{
		var mins = startBox.Mins + movement.Mins;
		var maxs = startBox.Maxs + movement.Maxs;

		var snap = Gizmo.Settings.SnapToGrid != Gizmo.IsCtrlPressed;

		if ( snap )
		{
			mins = Gizmo.Snap( mins, movement.Mins );
			maxs = Gizmo.Snap( maxs, movement.Maxs );
		}

		var spacing = 1.0f;

		mins.x = MathF.Min( mins.x, startBox.Maxs.x - spacing );
		mins.y = MathF.Min( mins.y, startBox.Maxs.y - spacing );
		mins.z = MathF.Min( mins.z, startBox.Maxs.z - spacing );

		maxs.x = MathF.Max( maxs.x, startBox.Mins.x + spacing );
		maxs.y = MathF.Max( maxs.y, startBox.Mins.y + spacing );
		maxs.z = MathF.Max( maxs.z, startBox.Mins.z + spacing );

		return new BBox( mins, maxs );
	}

	static void ResizeBBox( SelectionTool tool, BBox prevBox, BBox newBox, Rotation basis )
	{
		var prevSize = prevBox.Size;

		var scale = newBox.Size / prevSize;
		var dMin = newBox.Mins - prevBox.Mins;
		var dMax = newBox.Maxs - prevBox.Maxs;

		var origin = prevBox.Center;

		if ( MathF.Abs( dMax.x ) > MathF.Abs( dMin.x ) ) origin.x = prevBox.Mins.x;
		else if ( MathF.Abs( dMin.x ) > MathF.Abs( dMax.x ) ) origin.x = prevBox.Maxs.x;

		if ( MathF.Abs( dMax.y ) > MathF.Abs( dMin.y ) ) origin.y = prevBox.Mins.y;
		else if ( MathF.Abs( dMin.y ) > MathF.Abs( dMax.y ) ) origin.y = prevBox.Maxs.y;

		if ( MathF.Abs( dMax.z ) > MathF.Abs( dMin.z ) ) origin.z = prevBox.Mins.z;
		else if ( MathF.Abs( dMin.z ) > MathF.Abs( dMax.z ) ) origin.z = prevBox.Maxs.z;

		tool.Resize( origin, basis, scale );
	}
}
