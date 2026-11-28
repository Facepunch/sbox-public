public abstract partial class ToolMode
{
	/// <summary>
	/// Shows a grid on the hovered surface and lets the player snap tool actions to it.
	/// </summary>
	public virtual bool UseSnapGrid => false;

	/// <summary>
	/// Label for the snap hint, shown whenever this tool uses the snap grid.
	/// </summary>
	public virtual string SnapAction => UseSnapGrid && !AbsorbMouseInput ? "#tool.hint.snap" : null;

	/// <summary>
	/// The surface grid, updated before the tool handles input.
	/// </summary>
	protected SnapGrid SnapGrid { get; private set; }

	/// <summary>
	/// The highlighted surface point while the player holds snap.
	/// </summary>
	protected SelectionPoint SnappedSelection => _snapHeld ? _snapSelection : default;

	private SelectionPoint _snapSelection;
	private SelectionPoint _lockedSnapPlane;
	private bool _snapHeld;

	private void DisableSnapGrid()
	{
		SnapGrid?.Destroy();
		SnapGrid = null;
		_snapHeld = false;
		_lockedSnapPlane = default;
		_snapSelection = default;
	}

	/// <summary>
	/// While snapping, steers the view onto the grid corner nearest to where the mouse is heading.
	/// </summary>
	public virtual void OnCameraMove( Player player, ref Angles angles )
	{
		var snapped = SnappedSelection;
		if ( !snapped.IsValid() ) return;

		var eye = player.EyeTransform;
		var current = eye.Rotation.Angles();
		var predicted = (Rotation.From( angles ) * eye.Rotation).Angles();

		var surface = snapped.WorldTransform();
		var hit = TraceFromRay( new Ray( eye.Position, Rotation.From( predicted ).Forward ), ToolTraceDistance, player.GameObject );
		var aim = hit.IsValid() ? hit.WorldPosition() : surface.Position;

		var corner = SnapGrid.GetSnappedPoint( aim );
		const float tolerance = 0.01f;
		if ( corner.Distance( SnapGrid.LastLatticePoint ) < tolerance ) corner = surface.Position;
		var target = Rotation.LookAt( corner - eye.Position ).Angles();
		angles = (target - current).Normal;
	}

	/// <summary>
	/// Override to control which objects show the snap grid. Excludes player geometry by default.
	/// </summary>
	protected virtual bool ShouldDisplaySnapGrid( GameObject go )
	{
		return !go.Tags.Has( "player" );
	}

	/// <summary>
	/// Handles the active tool's input after its snap target has been updated.
	/// </summary>
	public virtual void OnControl()
	{
		DispatchActions();
	}

	internal void UpdateSnapTarget()
	{
		if ( !UseSnapGrid || AbsorbMouseInput )
		{
			_snapHeld = false;
			_snapSelection = default;
			SnapGrid?.Hide();
			return;
		}

		var player = Toolgun?.Owner;
		if ( !player.IsValid() )
		{
			DisableSnapGrid();
			return;
		}

		var ray = AimRay;
		var held = Input.Down( "snap" );
		if ( held && !_snapHeld )
		{
			// Keep the highlighted face selected while the player aims across its grid.
			_lockedSnapPlane = _snapSelection.IsValid() ? _snapSelection : TraceFromRay( ray, ToolTraceDistance, player.GameObject );
		}

		_snapHeld = held;
		_snapSelection = default;

		var preview = held ? _lockedSnapPlane : TraceFromRay( ray, ToolTraceDistance, player.GameObject );
		if ( !preview.IsValid() || !ShouldDisplaySnapGrid( preview.GameObject ) )
		{
			SnapGrid?.Hide();
			return;
		}

		var surface = preview.WorldTransform();
		var plane = new Plane( surface.Position, surface.Forward );
		var aimPoint = surface.Position;
		if ( held && !plane.TryTrace( ray, out aimPoint, twosided: true, maxDistance: ToolTraceDistance ) )
		{
			SnapGrid?.Hide();
			return;
		}

		SnapGrid ??= new SnapGrid();
		SnapGrid.Update( Scene.SceneWorld, preview.GameObject, aimPoint, surface.Forward );
		if ( !SnapGrid.HasValidSnap ) return;

		surface.Position = SnapGrid.LastSnapWorldPos;
		surface.Rotation = Rotation.LookAt( SnapGrid.LastSnapWorldNormal );
		preview.LocalTransform = preview.GameObject.WorldTransform.ToLocal( surface );
		_snapSelection = preview;
	}
}
