using Sandbox.Helpers;

namespace Editor.MeshEditor;

/// <summary>
/// The point transforms happen around.
/// <br/>
/// Until it's installed by hand it just follows the selection origin. Once installed it's a world
/// position that moves when the selection is translated, and stays put when it's rotated or scaled.
/// </summary>
public sealed class SelectionPivot( SelectionTool tool )
{
	/// <summary>
	/// An installed pivot. Null follows the selection origin.
	/// </summary>
	public readonly record struct State( Vector3? Position, int Index );

	State _state;

	State? _dragState;
	Vector3 _dragPosition;
	UndoSystem.Entry _dragEntry;

	/// <summary>
	/// Where the pivot is in the world.
	/// </summary>
	public Vector3 Position { get; private set; }

	/// <summary>
	/// True when the pivot has been installed by hand, otherwise it follows the selection origin.
	/// </summary>
	public bool IsInstalled => _state.Position.HasValue;

	/// <summary>
	/// Snapshot to pass to <see cref="AddUndo"/>.
	/// </summary>
	public State Current => _state;

	/// <summary>
	/// Keep an uninstalled pivot on the selection origin.
	/// </summary>
	public void Update()
	{
		Position = _state.Position ?? tool.CalculateSelectionOrigin();
	}

	/// <summary>
	/// Put the pivot at a world position, without an undo entry.
	/// </summary>
	public void Install( Vector3 position, int index = 0 )
	{
		_state = new State( position, index );

		Update();
	}

	/// <summary>
	/// Put the pivot back on the selection origin, without an undo entry.
	/// </summary>
	public void Reset() => Apply( default );

	/// <summary>
	/// Move an installed pivot along with the selection.
	/// </summary>
	public void Translate( Vector3 delta )
	{
		if ( !IsInstalled ) return;

		Install( Position + delta, _state.Index );
	}

	/// <summary>
	/// Offset an installed pivot from where it was when the drag started. The selection is
	/// transformed from its own drag baseline the same way, so this keeps them in step.
	/// </summary>
	public void Drag( Vector3 delta )
	{
		if ( !IsInstalled ) return;

		Install( _dragPosition + delta, _state.Index );
	}

	/// <summary>
	/// Remember where the pivot was, and which undo entry we were on, for <see cref="EndDrag"/>.
	/// </summary>
	public void BeginDrag()
	{
		_dragState = _state;
		_dragPosition = Position;
		_dragEntry = LatestEntry;
	}

	/// <summary>
	/// Fold the pivot move into the undo entry the drag just pushed, so a single undo puts the
	/// selection and the pivot back together.
	/// </summary>
	public void EndDrag()
	{
		if ( _dragState is not { } before ) return;

		_dragState = null;

		var entry = LatestEntry;

		// The drag didn't change anything, so there's no entry of ours to fold into.
		if ( entry is null || entry == _dragEntry ) return;
		if ( before == _state ) return;

		var move = new Move( this, before, _state );

		entry.Undo += move.Undo;
		entry.Redo += move.Redo;
	}

	/// <summary>
	/// Add an undo entry for a pivot move that has already happened.
	/// </summary>
	public void AddUndo( State before, string title )
	{
		if ( before == _state ) return;

		var move = new Move( this, before, _state );

		SceneEditorSession.Active.AddUndo( title, move.Undo, move.Redo );
	}

	public void Previous() => Step( -1, "Cycle Pivot Previous" );

	public void Next() => Step( 1, "Cycle Pivot Next" );

	public void Zero() => SetCommand( default, 0, "Set Pivot To Origin" );

	public void Center()
	{
		var box = tool.CalculateSelectionBounds();
		if ( box.Size.Length <= 0 ) return;

		SetCommand( box.Center, 0, "Center Pivot" );
	}

	public void Clear()
	{
		var before = _state;

		Reset();
		AddUndo( before, "Clear Pivot" );
	}

	static UndoSystem.Entry LatestEntry
	{
		get
		{
			var undo = SceneEditorSession.Active?.UndoSystem;
			return undo is not null && undo.Back.TryPeek( out var entry ) ? entry : null;
		}
	}

	void SetCommand( Vector3 position, int index, string title )
	{
		var before = _state;

		Install( position, index );
		AddUndo( before, title );

		tool.OnPivotChanged();
	}

	void Apply( State state )
	{
		_state = state;

		Update();

		tool.OnPivotChanged();
	}

	void Step( int direction, string title )
	{
		var box = tool.CalculateSelectionBounds();
		if ( box.Size.Length <= 0 ) return;

		var positions = GetPositions( box );
		var index = (_state.Index + direction + positions.Count) % positions.Count;

		SetCommand( positions[index], index, title );
	}

	/// <summary>
	/// The positions we cycle through - the centre of the selection, its eight corners,
	/// then the centre of its top and bottom faces.
	/// </summary>
	static IReadOnlyList<Vector3> GetPositions( BBox box )
	{
		var mins = box.Mins;
		var maxs = box.Maxs;
		var center = box.Center;

		return
		[
			center,

			new Vector3( mins.x, mins.y, mins.z ),
			new Vector3( maxs.x, mins.y, mins.z ),
			new Vector3( mins.x, maxs.y, mins.z ),
			new Vector3( maxs.x, maxs.y, mins.z ),

			new Vector3( mins.x, mins.y, maxs.z ),
			new Vector3( maxs.x, mins.y, maxs.z ),
			new Vector3( mins.x, maxs.y, maxs.z ),
			new Vector3( maxs.x, maxs.y, maxs.z ),

			new Vector3( center.x, center.y, mins.z ),
			new Vector3( center.x, center.y, maxs.z ),
		];
	}

	/// <summary>
	/// A pivot move sitting in the undo stack. Named methods, so a code hotload can rematch them.
	/// </summary>
	sealed class Move( SelectionPivot pivot, State before, State after )
	{
		public void Undo() => pivot.Apply( before );

		public void Redo() => pivot.Apply( after );
	}
}
