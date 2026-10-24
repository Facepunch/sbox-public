namespace Editor.GraphicsItems;

public partial class EditableCurve
{
	/// <summary>
	/// Moves the <see cref="Handle"/>s being dragged in one <see cref="CurveEditor"/> as a group:
	/// they all move by the same offset from where they started, and stop together as soon as one
	/// of them reaches the edge of its chart, so the shape of the selection is kept. Each handle
	/// corrects only itself as the view moves it, and the curves are updated once per frame from
	/// <see cref="Flush"/> rather than once per handle.
	/// </summary>
	internal class HandleDrag
	{
		readonly Dictionary<Handle, Vector2> _startPositions = new();
		readonly List<Handle> _dragged = new();
		readonly HashSet<EditableCurve> _dirty = new();

		Handle _pressed;
		bool _applying;
		Vector2 _minOffset;
		Vector2 _maxOffset;

		/// <summary>
		/// True if this handle is part of the drag in progress
		/// </summary>
		public bool IsDragging( Handle handle )
		{
			if ( !_pressed.IsValid() ) return false;
			if ( _dragged.Count > 0 ) return _dragged.Contains( handle );

			return handle == _pressed || handle.Selected;
		}

		/// <summary>
		/// A handle has been pressed. The selection isn't updated until after this, so remember
		/// where every handle started and work out which ones are dragged on the first move.
		/// </summary>
		public void Begin( Handle pressed )
		{
			End();

			if ( pressed.GraphicsView is not { } view ) return;

			foreach ( var curve in view.Items.OfType<EditableCurve>() )
			{
				foreach ( var handle in curve.Handles )
				{
					if ( !handle.IsValid() ) continue;

					_startPositions[handle] = handle.Position;
				}
			}

			if ( _startPositions.ContainsKey( pressed ) )
			{
				_pressed = pressed;
			}
		}

		public void End()
		{
			Flush();

			_startPositions.Clear();
			_dragged.Clear();

			_pressed = null;
			_applying = false;
		}

		/// <summary>
		/// A handle has been moved. Returns false if it isn't part of the drag, in which case it
		/// should clamp itself to its chart. Dragged handles are corrected right away, because the
		/// view sets their positions from where the drag started on every mouse move.
		/// </summary>
		public bool Moved( Handle handle )
		{
			if ( !_pressed.IsValid() ) return false;
			if ( _applying ) return true;

			if ( _dragged.Count == 0 )
			{
				Resolve( handle );
			}

			if ( !_dragged.Contains( handle ) ) return false;
			if ( !_startPositions.TryGetValue( handle, out var start ) ) return false;

			var offset = handle.Position - start;

			if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) && _startPositions.TryGetValue( _pressed, out var pressedStart ) )
			{
				offset = _pressed.SnapToGridPosition( pressedStart + offset ) - pressedStart;
			}

			var position = start + Vector2.Clamp( offset, _minOffset, _maxOffset );

			if ( handle.Position != position )
			{
				_applying = true;

				try
				{
					handle.Position = position;
				}
				finally
				{
					_applying = false;
				}
			}

			handle.UpdateValueFromPosition();
			_dirty.Add( handle.EditableCurve );

			return true;
		}

		void Resolve( Handle moved )
		{
			_minOffset = new Vector2( float.NegativeInfinity );
			_maxOffset = new Vector2( float.PositiveInfinity );

			foreach ( var (handle, start) in _startPositions )
			{
				if ( !handle.IsValid() ) continue;
				if ( handle != moved && handle != _pressed && !handle.Selected ) continue;

				_dragged.Add( handle );

				_minOffset = Vector2.Max( _minOffset, -start );
				_maxOffset = Vector2.Min( _maxOffset, handle.EditableCurve.Size - start );
			}
		}

		/// <summary>
		/// Update the curves whose handles were moved since the last flush
		/// </summary>
		public void Flush()
		{
			if ( _dirty.Count == 0 ) return;

			foreach ( var curve in _dirty )
			{
				if ( !curve.IsValid() ) continue;

				curve.OnHandleMoved();
			}

			_dirty.Clear();
		}
	}
}
