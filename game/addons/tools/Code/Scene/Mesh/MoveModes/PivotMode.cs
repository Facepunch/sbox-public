namespace Editor.MeshEditor;

/// <summary>
/// Set the location of the gizmo for the current selection.
/// <br/><b>Space (Hold)</b> - Snap pivot to scene/vertices
/// </summary>
[Title( "Pivot Tool" )]
[Icon( "meshtools/move_modes/pivot_tool.png" )]
[Alias( "mesh.pivot.mode" )]
[Order( 3 )]
public sealed class PivotMode : MoveMode
{
	/// <summary>
	/// How close to a vertex the cursor has to be, on screen, before we snap to it.
	/// </summary>
	private const float SnapScreenDistance = 20.0f;

	private Vector3 _unsnappedPivot;
	private Vector3 _moveDelta;
	private Rotation _basis;
	private SelectionPivot.State? _dragStart;

	private static bool IsPicking => Application.IsKeyDown( KeyCode.Space );

	public override bool AllowSceneSelection => !IsPicking;

	public override void OnBegin( SelectionTool tool )
	{
		_unsnappedPivot = tool.Pivot.Position;
		_moveDelta = default;
		_basis = tool.CalculateSelectionBasis();
	}

	protected override void OnUpdate( SelectionTool tool )
	{
		EndDrag( tool );

		if ( IsPicking && Gizmo.HasMouseFocus )
		{
			UpdatePicker( tool );
			return;
		}

		UpdateGizmo( tool );
	}

	/// <summary>
	/// Install the pivot, remembering where it was so the whole drag undoes in one step.
	/// </summary>
	private void Install( SelectionTool tool, Vector3 position )
	{
		_dragStart ??= tool.Pivot.Current;

		tool.Pivot.Install( position );
	}

	private void EndDrag( SelectionTool tool )
	{
		if ( !_dragStart.HasValue ) return;
		if ( Gizmo.Pressed.Any || Gizmo.IsLeftMouseDown ) return;

		tool.Pivot.AddUndo( _dragStart.Value, "Move Pivot" );

		_dragStart = null;
	}

	private void UpdateGizmo( SelectionTool tool )
	{
		var origin = tool.Pivot.Position;

		// Make sure our unsnapped pivot is set to the snapped pivot when not dragging.
		if ( !Gizmo.Pressed.Any )
		{
			_unsnappedPivot = origin;
			_moveDelta = default;
		}

		using ( Gizmo.Scope( "Tool", new Transform( origin ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( !Gizmo.Control.Position( "position", Vector3.Zero, out var delta, _basis ) )
				return;

			_moveDelta += delta;

			var target = (_unsnappedPivot + _moveDelta) * _basis.Inverse;
			Install( tool, Gizmo.Snap( target, _moveDelta * _basis.Inverse ) * _basis );
		}
	}

	private void UpdatePicker( SelectionTool tool )
	{
		var target = TraceTarget( tool );
		if ( !target.HasValue ) return;

		DrawPicker( tool, target.Value );

		if ( Gizmo.IsLeftMouseDown )
		{
			Install( tool, target.Value );
		}
	}

	/// <summary>
	/// Where the pivot would land - the nearest vertex of whatever is under the cursor,
	/// otherwise a plane at the pivot's height.
	/// </summary>
	private static Vector3? TraceTarget( SelectionTool tool )
	{
		var trace = tool.Scene.Trace
			.Ray( Gizmo.CurrentRay, Gizmo.RayDepth )
			.UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
			.UsePhysicsWorld( false )
			.Run();

		if ( trace.Hit && trace.Component is MeshComponent component && component.Mesh is not null )
		{
			var position = trace.HitPosition;
			var vertex = FindNearestVertex( component, trace.Triangle, position );

			if ( !vertex.HasValue )
				return SnapToGrid( position );

			var distance = ScreenDistance( vertex.Value, position );
			DrawVertexSnap( vertex.Value, distance );

			return distance < SnapScreenDistance ? vertex.Value : SnapToGrid( position );
		}

		var plane = new Plane( new Vector3( 0, 0, tool.Pivot.Position.z ), Vector3.Up );

		return plane.TryTrace( Gizmo.CurrentRay, out var hit )
			? SnapToGrid( Gizmo.CurrentRay.Project( hit.Length ) )
			: null;
	}

	/// <summary>
	/// The vertex of the traced face that is closest to the cursor on screen.
	/// </summary>
	private static Vector3? FindNearestVertex( MeshComponent component, int triangle, Vector3 position )
	{
		var mesh = component.Mesh;
		var face = mesh.TriangleToFace( triangle );

		Vector3? nearest = null;
		var nearestDistance = float.MaxValue;

		foreach ( var handle in mesh.GetFaceVertices( face ) )
		{
			var world = component.WorldTransform.PointToWorld( mesh.GetVertexPosition( handle ) );
			var distance = ScreenDistance( world, position );

			if ( distance >= nearestDistance )
				continue;

			nearestDistance = distance;
			nearest = world;
		}

		return nearest;
	}

	private static Vector3 SnapToGrid( Vector3 position )
	{
		return Gizmo.Settings.SnapToGrid || Gizmo.IsCtrlPressed
			? Gizmo.Snap( position, Vector3.One )
			: position;
	}

	private static float ScreenDistance( Vector3 a, Vector3 b )
	{
		return Gizmo.Camera.ToScreen( a ).Distance( Gizmo.Camera.ToScreen( b ) );
	}

	/// <summary>
	/// Keep gizmo geometry the same size on screen however far away it is.
	/// </summary>
	private static float ScreenSize( Vector3 position, float scale )
	{
		return scale * Gizmo.Camera.Position.Distance( position ) / 1000.0f;
	}

	private static void DrawVertexSnap( Vector3 vertex, float screenDistance )
	{
		var color = screenDistance < SnapScreenDistance ? Theme.Green : Theme.Red;

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = color.WithAlpha( screenDistance.Remap( 0, 100, 1.0f, 0.0f, true ) );
		Gizmo.Draw.SolidSphere( vertex, ScreenSize( vertex, 8.0f ) );
	}

	private static void DrawPicker( SelectionTool tool, Vector3 target )
	{
		using ( Gizmo.Scope( "Pivot Pick" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			Gizmo.Transform = new Transform( target );
			Gizmo.Draw.Color = Theme.Yellow;
			Gizmo.Draw.SolidSphere( Vector3.Zero, ScreenSize( target, 5.0f ) );

			Gizmo.Draw.Color = Color.White.WithAlpha( 0.8f );
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Up * 16 );
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Forward * 16 );
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Left * 16 );

			Gizmo.Transform = new Transform( tool.Pivot.Position );
			Gizmo.Draw.Color = Gizmo.Colors.Up;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Up * 16 );
			Gizmo.Draw.Color = Gizmo.Colors.Forward;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Forward * 16 );
			Gizmo.Draw.Color = Gizmo.Colors.Left;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Left * 16 );

			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.SolidSphere( Vector3.Zero, ScreenSize( tool.Pivot.Position, 5.0f ) );
		}
	}
}
