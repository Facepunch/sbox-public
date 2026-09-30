namespace Editor.GraphicsItems;

public partial class EditableCurve
{
	/// <summary>
	/// Moves the <see cref="Handle"/>s being dragged in one <see cref="CurveEditor"/> as a group:
	/// they all stop as soon as one of them reaches the edge of its chart, so the shape of the
	/// selection is kept.
	/// </summary>
	internal class HandleDrag
	{
		readonly List<Handle> _dragged = new();
		readonly List<EditableCurve> _curves = new();

		Handle _pressed;
		bool _applying;
		bool _pending;

		/// <summary>
		/// True if this handle is part of the drag in progress
		/// </summary>
		public bool IsDragging( Handle handle ) => _pressed.IsValid() && (handle == _pressed || handle.Selected);

		public void Begin( Handle pressed ) => _pressed = pressed;

		public void End()
		{
			_pressed = null;
			_pending = false;
		}

		/// <summary>
		/// A handle has been moved. Returns false if it isn't part of the drag, in which case it
		/// should clamp itself to its chart. Dragged handles are corrected as a group instead,
		/// once per mouse event, from <see cref="ApplyPending"/>.
		/// </summary>
		public bool Moved( Handle handle )
		{
			if ( _applying ) return true;
			if ( !IsDragging( handle ) ) return false;

			_pending = true;
			return true;
		}

		/// <summary>
		/// The view has moved all the dragged handles for this mouse event. Push the group back
		/// so every handle is inside its chart, snap it to the grid if Ctrl is held, then update
		/// the keyframes.
		/// </summary>
		public void ApplyPending()
		{
			if ( !_pending ) return;
			_pending = false;

			if ( !_pressed.IsValid() )
			{
				End();
				return;
			}

			_dragged.Clear();
			_curves.Clear();

			foreach ( var curve in _pressed.GraphicsView.Items.OfType<EditableCurve>() )
			{
				foreach ( var handle in curve.Handles )
				{
					if ( !handle.IsValid() || !IsDragging( handle ) ) continue;

					_dragged.Add( handle );

					if ( !_curves.Contains( curve ) )
					{
						_curves.Add( curve );
					}
				}
			}

			// One correction for the whole group, so the selection keeps its shape. Snap by the
			// pressed handle, then push everyone back in by the largest overshoot on each side.
			var correction = Vector2.Zero;

			if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
			{
				correction = _pressed.SnapToGridPosition( _pressed.Position ) - _pressed.Position;
			}

			var pushIn = Vector2.Zero;
			var pushBack = Vector2.Zero;

			foreach ( var handle in _dragged )
			{
				var target = handle.Position + correction;
				pushIn = Vector2.Max( pushIn, -target );
				pushBack = Vector2.Max( pushBack, target - handle.EditableCurve.Size );
			}

			correction += pushIn - pushBack;

			if ( correction != Vector2.Zero )
			{
				_applying = true;

				try
				{
					foreach ( var handle in _dragged )
					{
						handle.Position += correction;
					}
				}
				finally
				{
					_applying = false;
				}
			}

			foreach ( var handle in _dragged )
			{
				handle.UpdateValueFromPosition();
			}

			foreach ( var curve in _curves )
			{
				curve.OnHandleMoved();
			}
		}
	}
}
