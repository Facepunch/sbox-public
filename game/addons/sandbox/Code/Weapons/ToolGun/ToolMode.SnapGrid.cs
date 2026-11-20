public abstract partial class ToolMode
{
	/// <summary>
	/// When true (default), a SnapGrid overlay is shown on the hovered object surface.
	/// Override to return false to opt out.
	/// </summary>
	public virtual bool UseSnapGrid => false;

	/// <summary>
	/// Label for the snap hint, shown whenever this tool uses the snap grid.
	/// </summary>
	public virtual string SnapAction => UseSnapGrid && !AbsorbMouseInput ? "#tool.hint.snap" : null;

	/// <summary>
	/// The active snap grid, updated every frame in <see cref="OnControl"/>.
	/// Null when snap grid is disabled or the tool is inactive.
	/// </summary>
	protected SnapGrid SnapGrid { get; private set; }

	/// <summary>
	/// While snapping, the unsnapped aim driven by the mouse. The grid is traced from this,
	/// and the real camera is steered onto the resulting snap point.
	/// </summary>
	private Angles? _snapFreeAim;

	/// <summary>
	/// True when the snap grid was updated from a valid surface this frame.
	/// </summary>
	private bool _hasSnapTarget;
	private SelectionPoint _snapSelection;
	private SelectionPoint _lockedSnapPlane;
	private bool _snapHeld;
	private bool _snapAimDirty;
	private Vector3 _lockedAimLocal;

	private void DisableSnapGrid()
	{
		SnapGrid?.Destroy();
		SnapGrid = null;
		_snapHeld = false;
		_lockedSnapPlane = default;
		_snapFreeAim = null;
		_hasSnapTarget = false;
	}

	/// <summary>
	/// While hovering an object, hold alt key to keep the camera aim snapped to the grid
	/// </summary>
	public virtual void OnCameraMove( Player player, ref Angles angles )
	{
		// Skip when the mode is absorbing mouse input for its own purposes (e.g. Weld rotation stage).
		if ( AbsorbMouseInput || SnapGrid == null || !Input.Down( "snap" ) )
		{
			_snapFreeAim = null;
			return;
		}

		var currentAngles = player.Controller.EyeAngles;

		_snapAimDirty |= MathF.Abs( angles.pitch ) > 0.00001f || MathF.Abs( angles.yaw ) > 0.00001f;
		var freeAim = (_snapFreeAim ?? currentAngles) + angles;
		freeAim.pitch = freeAim.pitch.Clamp( -89f, 89f );
		_snapFreeAim = freeAim;

		// Resolve this input's target before steering, rather than using the previous frame's point.
		UpdateSnapTarget();

		var desiredAngles = _hasSnapTarget
			? Rotation.LookAt( SnapGrid.LastSnapWorldPos - player.EyeTransform.Position ).Angles()
			: freeAim;

		angles = desiredAngles - currentAngles;
	}

	/// <summary>
	/// The ray the snap grid is traced from, free aim while snapping, otherwise the eyes.
	/// </summary>
	private SelectionPoint TraceSnapGrid()
	{
		var player = Toolgun?.Owner;
		if ( !player.IsValid() ) return default;

		if ( _snapFreeAim is not { } freeAim )
			return TraceFromRay( player.EyeTransform.ForwardRay, 4096, player.GameObject );

		var ray = new Ray( player.EyeTransform.Position, freeAim.Forward );
		return TraceFromRay( ray, 4096, player.GameObject );
	}

	/// <summary>
	/// Override to control which objects show the snap grid. Excludes player geometry by default.
	/// </summary>
	protected virtual bool ShouldDisplaySnapGrid( GameObject go )
	{
		return !go.Tags.Has( "player" );
	}

	public virtual void OnControl()
	{
		UpdateSnapTarget();
		DispatchActions();
	}

	private void UpdateSnapTarget()
	{
		_hasSnapTarget = false;
		if ( !UseSnapGrid || AbsorbMouseInput ) return;

		var held = Input.Down( "snap" );
		if ( !held )
		{
			_snapHeld = false;
			_lockedSnapPlane = default;
		}
		else if ( !_snapHeld )
		{
			// Capture once per press, before mouse input can select another surface.
			_snapHeld = true;
			_lockedSnapPlane = _snapSelection.IsValid() ? _snapSelection : TraceSnapGrid();
			if ( _lockedSnapPlane.IsValid() ) _lockedAimLocal = _lockedSnapPlane.LocalTransform.Position;
		}

		var preview = held ? _lockedSnapPlane : TraceSnapGrid();
		if ( held && preview.IsValid() )
		{
			var player = Toolgun?.Owner;
			if ( !player.IsValid() ) return;
			var plane = _lockedSnapPlane.WorldTransform();
			var direction = _snapFreeAim?.Forward ?? player.EyeTransform.Forward;
			var denominator = Vector3.Dot( direction, plane.Rotation.Forward );
			if ( _snapAimDirty && MathF.Abs( denominator ) > 0.0001f )
			{
				var distance = Vector3.Dot( plane.Position - player.EyeTransform.Position, plane.Rotation.Forward ) / denominator;
				if ( distance >= 0 && distance <= 4096 )
					_lockedAimLocal = preview.GameObject.WorldTransform.PointToLocal( player.EyeTransform.Position + direction * distance );
			}
			_snapAimDirty = false;
			var local = preview.LocalTransform;
			local.Position = _lockedAimLocal;
			preview.LocalTransform = local;
		}
		_hasSnapTarget = preview.IsValid() && ShouldDisplaySnapGrid( preview.GameObject );
		if ( _hasSnapTarget )
		{
			SnapGrid ??= new SnapGrid();
			SnapGrid.Update( Scene.SceneWorld, preview.GameObject, preview.WorldPosition(), preview.WorldTransform().Rotation.Forward );
			if ( !SnapGrid.HasValidSnap )
			{
				_hasSnapTarget = false;
				return;
			}
			var snapped = preview.WorldTransform();
			snapped.Position = SnapGrid.LastSnapWorldPos;
			snapped.Rotation = Rotation.LookAt( SnapGrid.LastSnapWorldNormal );
			preview.LocalTransform = preview.GameObject.WorldTransform.ToLocal( snapped );
			_snapSelection = preview;
		}
		else
		{
			SnapGrid?.Hide();
			if ( !held ) _snapSelection = default;
		}
	}
}
