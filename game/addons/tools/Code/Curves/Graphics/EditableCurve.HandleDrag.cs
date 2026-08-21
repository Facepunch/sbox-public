namespace Editor.GraphicsItems;

public partial class EditableCurve
{
	/// <summary>
	/// Tracks the <see cref="Handle"/>s being dragged by the mouse. They all move by the same
	/// offset, and stop together as soon as one of them reaches the edge of its chart, so the
	/// shape of the selection is kept.
	/// </summary>
	private static class HandleDrag
	{
		private static readonly Dictionary<Handle, Vector2> StartPositions = new();
		private static readonly List<Handle> Dragged = new();
		private static readonly List<EditableCurve> DraggedCurves = new();

		private static bool _started;
		private static bool _applying;
		private static Vector2 _minOffset;
		private static Vector2 _maxOffset;

		/// <summary>
		/// True while we're moving the dragged handles ourselves
		/// </summary>
		public static bool IsApplying => _applying;

		/// <summary>
		/// A handle has been pressed. The selection isn't updated until after this, so remember
		/// where every handle started and work out which ones are dragged on the first move.
		/// </summary>
		public static void Begin( Handle pressed )
		{
			End();

			if ( pressed.GraphicsView is not { } view ) return;

			foreach ( var curve in view.Items.OfType<EditableCurve>() )
			{
				foreach ( var handle in curve.Handles )
				{
					if ( !handle.IsValid() ) continue;

					StartPositions[handle] = handle.Position;
				}
			}

			_started = StartPositions.ContainsKey( pressed );
		}

		public static void End()
		{
			StartPositions.Clear();
			Dragged.Clear();
			DraggedCurves.Clear();

			_started = false;
			_applying = false;
		}

		/// <summary>
		/// A handle has been moved. Returns false if it isn't being dragged, in which case it
		/// should clamp itself to its chart.
		/// </summary>
		public static bool Moved( Handle handle )
		{
			if ( !_started ) return false;
			if ( _applying ) return true;

			if ( Dragged.Count == 0 )
			{
				Resolve( handle );
			}

			if ( !Dragged.Contains( handle ) ) return false;
			if ( !StartPositions.TryGetValue( handle, out var start ) ) return false;

			// Every dragged handle gets the same offset, so we can read it from this one
			Apply( Vector2.Clamp( handle.Position - start, _minOffset, _maxOffset ) );

			return true;
		}

		private static void Resolve( Handle moved )
		{
			_minOffset = new Vector2( float.NegativeInfinity );
			_maxOffset = new Vector2( float.PositiveInfinity );

			foreach ( var (handle, start) in StartPositions )
			{
				if ( !handle.IsValid() ) continue;
				if ( handle != moved && !handle.Selected ) continue;

				Dragged.Add( handle );

				if ( !DraggedCurves.Contains( handle.EditableCurve ) )
				{
					DraggedCurves.Add( handle.EditableCurve );
				}

				_minOffset = Vector2.Max( _minOffset, -start );
				_maxOffset = Vector2.Min( _maxOffset, handle.EditableCurve.Size - start );
			}
		}

		private static void Apply( Vector2 offset )
		{
			_applying = true;

			try
			{
				foreach ( var handle in Dragged )
				{
					if ( !handle.IsValid() ) continue;

					handle.Position = StartPositions[handle] + offset;
					handle.UpdateValueFromPosition();
				}
			}
			finally
			{
				_applying = false;
			}

			foreach ( var curve in DraggedCurves )
			{
				curve.OnHandleMoved();
			}
		}
	}
}
