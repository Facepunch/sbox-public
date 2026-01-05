namespace Editor.MeshEditor;

/// <summary>
/// Set the location of the gizmo for the current selection.
/// <br/><b>Space (Hold)</b> - Snap pivot to scene/vertices
/// </summary>
[Title( "Pivot Tool" )]
[Icon( "adjust" )]
[Alias( "mesh.pivot.mode" )]
[Order( 3 )]
public sealed class PivotMode : MoveMode
{
	private Vector3 _pivot;
	private Rotation _basis;

	public override bool AllowSceneSelection => !Application.IsKeyDown( KeyCode.Space );

	public override void OnBegin( SelectionTool tool )
	{
		_pivot = tool.Pivot;
		_basis = tool.CalculateSelectionBasis();
	}

	protected override void OnUpdate( SelectionTool tool )
	{
		if ( Application.IsKeyDown( KeyCode.Space ) && Gizmo.HasMouseFocus )
		{
			RunPicker( tool );
			return;
		}

		var origin = tool.Pivot;

		using ( Gizmo.Scope( "Tool", new Transform( origin ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Position( "position", Vector3.Zero, out var delta, _basis ) )
			{
				_pivot += delta;
				tool.Pivot = Gizmo.Snap( _pivot * _basis.Inverse, delta * _basis.Inverse ) * _basis;
			}
		}
	}

	private void RunPicker( SelectionTool tool )
	{
		var tr = tool.Scene.Trace
			.Ray( Gizmo.CurrentRay, Gizmo.RayDepth )
			.UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
			.UsePhysicsWorld( false )
			.Run();

		Vector3 targetPosition = default;
		bool hasTarget = false;
		bool snappedToVertex = false;

		if ( tr.Hit && tr.Component is MeshComponent meshComponent )
		{
			var mesh = meshComponent.Mesh;
			if ( mesh != null )
			{
				targetPosition = tr.HitPosition;
				hasTarget = true;

				var face = mesh.TriangleToFace( tr.Triangle );
				var bestDist = float.MaxValue;
				Vector3? bestVert = null;

				foreach ( var vHandle in mesh.GetFaceVertices( face ) )
				{
					var vPosLocal = mesh.GetVertexPosition( vHandle );
					var vPosWorld = meshComponent.WorldTransform.PointToWorld( vPosLocal );
					var screenDist = Gizmo.Camera.ToScreen( vPosWorld ).Distance( Gizmo.Camera.ToScreen( targetPosition ) );

					if ( screenDist < bestDist )
					{
						bestDist = screenDist;
						bestVert = vPosWorld;
					}
				}

				if ( bestVert.HasValue )
				{
					Gizmo.Draw.IgnoreDepth = true;
					var screenDist = Gizmo.Camera.ToScreen( bestVert.Value ).Distance( Gizmo.Camera.ToScreen( targetPosition ) );
					var isSnapRange = screenDist < 20.0f;

					var color = isSnapRange ? Theme.Green : Theme.Red;
					Gizmo.Draw.Color = color.WithAlpha( screenDist.Remap( 0, 100, 1.0f, 0.0f, true ) );
					Gizmo.Draw.SolidSphere( bestVert.Value, 4 );

					if ( isSnapRange )
					{
						targetPosition = bestVert.Value;
						snappedToVertex = true;
					}
				}
			}
		}

		if ( !hasTarget )
		{
			var plane = new Plane( new Vector3( 0, 0, tool.Pivot.z ), Vector3.Up );
			if ( plane.TryTrace( Gizmo.CurrentRay, out var dist ) )
			{
				targetPosition = Gizmo.CurrentRay.Project( dist.Length );
				hasTarget = true;
			}
		}

		if ( !hasTarget ) return;

		if ( !snappedToVertex && (Gizmo.Settings.SnapToGrid || Gizmo.IsCtrlPressed) )
		{
			targetPosition = Gizmo.Snap( targetPosition, Vector3.One );
		}

		using ( Gizmo.Scope( "Pivot Pick" ) )
		{
			Gizmo.Transform = new Transform( targetPosition );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Theme.Yellow;
			Gizmo.Draw.SolidSphere( Vector3.Zero, 2 );

			Gizmo.Draw.Color = Color.White.WithAlpha( 0.8f );
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Up * 16 );
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Forward * 16 );
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Left * 16 );

			Gizmo.Transform = new Transform( tool.Pivot );
			Gizmo.Draw.Color = Gizmo.Colors.Up;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Up * 16 );
			Gizmo.Draw.Color = Gizmo.Colors.Forward;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Forward * 16 );
			Gizmo.Draw.Color = Gizmo.Colors.Left;
			Gizmo.Draw.Line( Vector3.Zero, Vector3.Left * 16 );

			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.SolidSphere( Vector3.Zero, 1.5f );
		}

		if ( Gizmo.IsLeftMouseDown )
		{
			tool.Pivot = targetPosition;
		}
	}
}
