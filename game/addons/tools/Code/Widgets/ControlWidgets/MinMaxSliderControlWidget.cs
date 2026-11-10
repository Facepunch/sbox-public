namespace Editor;

/// <summary>
/// Edits any Vector2 marked with MinMaxSlider as a single row: minimum, two-handle slider, maximum.
/// </summary>
[CustomEditor( typeof( Vector2 ), WithAllAttributes = new[] { typeof( MinMaxSliderAttribute ) } )]
public sealed class MinMaxSliderControlWidget : ControlWidget
{
	/// <inheritdoc />
	public override bool SupportsMultiEdit => true;

	/// <summary>
	/// Uses the property's MinMaxSlider and Step attributes while retaining manual numeric entry.
	/// </summary>
	public MinMaxSliderControlWidget( SerializedProperty property ) : base( property )
	{
		property.TryGetAsObject( out var values );

		if ( values is null )
		{
			Log.Warning( $"Error when trying to get {property} as object" );
			return;
		}

		var range = property.GetAttributes().OfType<RangeAttribute>().FirstOrDefault();
		var step = property.GetAttributes().OfType<StepAttribute>().FirstOrDefault()?.Step ?? 0;
		var bounds = new Vector2( range?.Min ?? 0, range?.Max ?? 1 );

		Layout = Layout.Row();
		Layout.Spacing = 0;
		FixedHeight = Theme.RowHeight;

		// The parent paints one continuous background for the entries and slider.
		var start = Layout.Add( new FloatControlWidget( values.GetProperty( "x" ) ) { Label = null, ToolTip = "Minimum", PaintBackground = false, Cursor = CursorShape.Arrow } );
		Layout.Add( new IntervalSlider( property, bounds, step ) );
		var end = Layout.Add( new FloatControlWidget( values.GetProperty( "y" ) ) { Label = null, ToolTip = "Maximum", PaintBackground = false, Cursor = CursorShape.Arrow } );

		// Match the numeric entry width used by FloatControlWidget's slider.
		start.FixedWidth = end.FixedWidth = SliderDrawing.ValueWidth;
		start.MakeRanged( bounds, step, range?.Clamped ?? false, slider: false );
		end.MakeRanged( bounds, step, range?.Clamped ?? false, slider: false );
	}

	/// <summary>
	/// Dragging a handle adjusts one endpoint; dragging between the handles moves the whole interval.
	/// </summary>
	sealed class IntervalSlider : ControlWidget
	{
		readonly Vector2 bounds;
		readonly float step;
		bool dragging;
		bool dragStart;
		bool dragRange;
		float dragOrigin;
		Vector2 dragValue;

		/// <inheritdoc />
		public override bool SupportsMultiEdit => true;

		/// <summary>
		/// Creates the slider using the same serialized property as the numeric fields, including undo tracking.
		/// </summary>
		public IntervalSlider( SerializedProperty property, Vector2 bounds, float step ) : base( property )
		{
			// Repaint thumb hover when the pointer moves without a button held.
			MouseTracking = true;

			this.bounds = bounds;
			this.step = step;
			PaintBackground = false;
			FixedHeight = Theme.RowHeight;
			MinimumWidth = Theme.RowHeight * 2;
			HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
			Cursor = CursorShape.Arrow;
		}

		float HandlePosition( float value ) => SliderDrawing.HandleWidth * 0.5f
			+ MathX.LerpInverse( value, bounds.x, bounds.y, true ) * (Width - SliderDrawing.HandleWidth);

		/// <inheritdoc />
		protected override void PaintControl()
		{
			if ( SerializedProperty.IsMultipleDifferentValues )
			{
				Paint.SetPen( Theme.MultipleValues );
				Paint.DrawText( LocalRect, "Multiple Values", TextFlag.Center );
				return;
			}

			var value = SerializedProperty.GetValue<Vector2>();
			var start = HandlePosition( value.x );
			var end = HandlePosition( value.y );
			SliderDrawing.DrawTrack( LocalRect, start, end, Theme.Green, IsControlDisabled );
			SliderDrawing.DrawHandle( this, LocalRect, start );
			SliderDrawing.DrawHandle( this, LocalRect, end );
		}

		/// <inheritdoc />
		protected override void OnValueChanged() => Update();

		/// <inheritdoc />
		protected override void OnMouseEnter()
		{
			base.OnMouseEnter();
			Update();
		}

		/// <inheritdoc />
		protected override void OnMouseLeave()
		{
			base.OnMouseLeave();
			Update();
		}

		void MoveSelection( float position )
		{
			if ( IsControlDisabled || Width <= SliderDrawing.HandleWidth )
				return;

			var value = SerializedProperty.GetValue<Vector2>();
			if ( dragRange )
			{
				var delta = (position - dragOrigin) / (Width - SliderDrawing.HandleWidth) * (bounds.y - bounds.x);
				if ( step > 0 )
					delta = delta.SnapToGrid( step );

				// Clamp the translation, not the endpoints, so the interval keeps its width.
				// Manually entered out-of-bounds values must not jump when dragging begins.
				delta = delta.Clamp( MathF.Min( 0, bounds.x - dragValue.x ), MathF.Max( 0, bounds.y - dragValue.y ) );
				value = dragValue + new Vector2( delta, delta );
			}
			else
			{
				var fraction = ((position - SliderDrawing.HandleWidth * 0.5f) / (Width - SliderDrawing.HandleWidth)).Clamp( 0, 1 );
				var distance = MathX.LerpTo( bounds.x, bounds.y, fraction );
				if ( step > 0 )
					distance = distance.SnapToGrid( step );

				distance = distance.Clamp( bounds.x, bounds.y );

				if ( dragStart )
				{
					value.x = MathF.Min( distance, value.y );
				}
				else
				{
					value.y = MathF.Max( distance, value.x );
				}
			}

			SerializedProperty.SetValue( value );
			SignalValuesChanged();
			Update();
		}

		/// <inheritdoc />
		protected override void OnMousePress( MouseEvent e )
		{
			// Mixed selections retain independent numeric editing; dragging requires a common interval.
			if ( !e.LeftMouseButton || IsControlDisabled || SerializedProperty.IsMultipleDifferentValues )
				return;

			var value = SerializedProperty.GetValue<Vector2>();
			var start = HandlePosition( value.x );
			var end = HandlePosition( value.y );
			dragOrigin = e.LocalPosition.x;
			dragValue = value;
			dragRange = dragOrigin > start + SliderDrawing.HandleWidth * 0.5f && dragOrigin < end - SliderDrawing.HandleWidth * 0.5f;
			dragStart = dragOrigin < (start + end) * 0.5f;
			dragging = true;
			e.Accepted = true;
			PropertyStartEdit();
			MoveSelection( e.LocalPosition.x );
		}

		/// <inheritdoc />
		protected override void OnMouseMove( MouseEvent e )
		{
			Update();

			if ( !dragging || !e.ButtonState.Contains( MouseButtons.Left ) )
				return;

			e.Accepted = true;
			MoveSelection( e.LocalPosition.x );
		}

		/// <inheritdoc />
		protected override void OnMouseReleased( MouseEvent e )
		{
			if ( !e.LeftMouseButton || !dragging )
				return;

			dragging = false;
			e.Accepted = true;
			PropertyFinishEdit();
		}
	}
}
