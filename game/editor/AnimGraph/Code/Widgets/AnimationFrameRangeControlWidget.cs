namespace Editor.AnimGraph;

[CustomEditor( typeof( Vector2 ), NamedEditor = "animgraph.frame-range" )]
sealed class AnimationFrameRangeControlWidget : ControlWidget
{
	private readonly ClipDocument _document;
	private MinMaxSliderControlWidget _slider;
	private IconButton _reset;
	private Label _summary;
	private int _frameCount = -1;

	public AnimationFrameRangeControlWidget( SerializedProperty property ) : base( property )
	{
		_document = property.Parent.Targets.OfType<ClipDocument>().First();
		PaintBackground = false;
		Layout = Layout.Column();
		Layout.Spacing = 4;
	}

	protected override int ValueHash => HashCode.Combine( base.ValueHash, _document.SourceFilename, _document.Take, _document.SourceFrameCount );

	protected override void OnValueChanged()
	{
		base.OnValueChanged();
		var frames = _document.SourceFrameCount;
		if ( _frameCount != frames )
		{
			_frameCount = frames;
			Layout.Clear( true );

			var row = Layout.AddRow();
			row.Spacing = 4;
			var property = SerializedProperty.GetCustomizable();
			property.AddAttribute( new RangeAttribute( 0, Math.Max( frames - 1, 0 ), clamped: true ) );
			property.AddAttribute( new StepAttribute( 1 ) );
			_slider = row.Add( new MinMaxSliderControlWidget( property ), 1 );
			_slider.ToolTip = "Drag either handle to trim. Drag between the handles to move the range. Type exact frame numbers at either end.";
			_reset = row.Add( new IconButton( "restart_alt" )
			{
				FixedSize = Theme.RowHeight,
				ToolTip = "Use the full take",
				OnClick = ResetRange
			} );
			_summary = Layout.Add( new Label( this ) { Color = Theme.TextLight, WordWrap = true } );
		}

		var range = _document.FrameRange;
		var valid = frames > 0 && range.x >= 0 && range.y < frames && range.x <= range.y;
		_slider.Enabled = !IsControlDisabled && frames > 1;
		_reset.Enabled = !IsControlDisabled && (_document.StartFrame.HasValue || _document.EndFrame.HasValue);
		_summary.Color = valid ? Theme.TextLight : Theme.Yellow;
		_summary.Text = !valid ? $"Range is outside this take's {frames:N0} frames. Adjust the range or reset to the full take."
			: _document.StartFrame is null && _document.EndFrame is null ? $"Full take - {frames:N0} {(frames == 1 ? "frame" : "frames")}"
			: $"{range.y - range.x + 1:N0} of {frames:N0} frames selected";
	}

	private void ResetRange()
	{
		if ( IsControlDisabled )
			return;

		PropertyStartEdit();
		_document.StartFrame = null;
		_document.EndFrame = null;
		SerializedProperty.Parent.NoteChanged( SerializedProperty );
		PropertyFinishEdit();
		OnValueChanged();
	}
}
