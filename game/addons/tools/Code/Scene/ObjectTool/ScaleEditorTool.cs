namespace Editor;

/// <summary>
/// Scale selected GameObjects.<br/> <br/>
/// <b>Ctrl</b> - toggle snap to grid<br/>
/// <b>Shift</b> - scale all 3 axis
/// </summary>
[Title( "Scale" )]
[Icon( "zoom_out_map" )]
[Alias( "tools.scale-tool" )]
[Group( "1" )]
[Order( 2 )]
public class ScaleEditorTool : EditorTool
{
	readonly Dictionary<GameObject, (Vector3 StartScale, Vector3 StartSize, Vector3 StartPosition)> startState = [];
	Vector3 scaleDelta;
	Vector3 handlePosition;
	Rotation handleRotation;
	Vector3 startBoundsSize;
	IDisposable undoScope;

	public override void OnUpdate()
	{
		var nonSceneGos = Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Sandbox.Scene ) );
		if ( !nonSceneGos.Any() ) return;

		var positions = nonSceneGos.Select( x => x.WorldPosition ).ToArray();
		var centroid = positions.Aggregate( Vector3.Zero, ( sum, p ) => sum + p ) / positions.Length;

		if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
		{
			startState.Clear();
			scaleDelta = default;
			handlePosition = centroid;
			handleRotation = nonSceneGos.FirstOrDefault()?.WorldRotation ?? Rotation.Identity;
			startBoundsSize = BBox.FromPoints( positions ).Size;
			undoScope?.Dispose();
			undoScope = null;
		}

		// do not update the handle position while dragging
		var origin = Gizmo.Pressed.Any ? handlePosition : centroid;
		var scaleAsGroup = nonSceneGos.Count() > 1 && Gizmo.Settings.GlobalSpace;
		var groupSize = MathF.Max( startBoundsSize.x, MathF.Max( startBoundsSize.y, startBoundsSize.z ) );

		using ( Gizmo.Scope( "Tool", new Transform( origin ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Scale( "scale", Vector3.Zero, out var delta, handleRotation ) )
			{
				scaleDelta += delta / 0.01f;

				if ( startState.Count == 0 )
				{
					undoScope ??= SceneEditorSession.Active.UndoScope( "Transform Object(s)" ).WithGameObjectChanges( nonSceneGos, GameObjectUndoFlags.All ).Push();

					foreach ( var go in nonSceneGos )
					{
						go.DispatchPreEdited( nameof( GameObject.LocalScale ) );
						if ( scaleAsGroup ) go.DispatchPreEdited( nameof( GameObject.LocalPosition ) );
						go.BreakProceduralBone();
						startState[go] = (go.WorldScale, go.GetBounds().Size, go.WorldPosition);
					}
				}

				foreach ( var (go, (startScale, startSize, startPosition)) in startState )
				{
					if ( !go.IsValid() ) continue;

					var newSize = scaleAsGroup ? groupSize : MathF.Max( startSize.x, MathF.Max( startSize.y, startSize.z ) );
					if ( newSize < 0.001f ) newSize = 1.0f;

					var snap = Gizmo.Snap( scaleDelta, scaleDelta ) * 2.0f;

					var scaleFactor = new Vector3(
						1.0f + snap.x / newSize,
						1.0f + snap.y / newSize,
						1.0f + snap.z / newSize
					);

					go.WorldScale = startScale * scaleFactor;

					if ( scaleAsGroup )
					{
						go.WorldPosition = origin + (startPosition - origin) * scaleFactor;
					}

					go.DispatchEdited( nameof( GameObject.LocalScale ) );
					if ( scaleAsGroup ) go.DispatchEdited( nameof( GameObject.LocalPosition ) );
				}
			}
		}
	}

	[Shortcut( "tools.scale-tool", "r", typeof( SceneViewWidget ) )]
	public static void ActivateSubTool()
	{
		if ( !(EditorToolManager.CurrentModeName == nameof( ObjectEditorTool ) || EditorToolManager.CurrentModeName == "object") ) return;
		EditorToolManager.SetSubTool( nameof( ScaleEditorTool ) );
	}
}
