namespace Editor.AnimGraph;

/// <summary>
/// The selected event's own fields. Time is here as well as on the lane so an event can be put on
/// an exact frame, which dragging can't promise.
/// </summary>
internal sealed class ClipEventProperties : Widget
{
	/// <summary>Called once an edit is finished and actually changed something.</summary>
	public Action Edited { get; set; }

	public Action Deleted { get; set; }
	public int FrameCount { get; set; }

	private FloatControlWidget _start;
	private FloatControlWidget _length;

	public ClipEventProperties( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void Show( ClipDocument.Event item, string eventClass, string hint )
	{
		using var suspend = SuspendUpdates.For( this );
		Layout.Clear( true );
		_start = null;
		_length = null;

		if ( item is null )
		{
			Layout.Add( new Label( hint ?? "", this ) { Enabled = false, WordWrap = true } );
			return;
		}

		var serialized = EditorTypeLibrary.GetSerializedObject( item );
		var before = item.Json.ToJsonString();

		// Only once the edit is finished, otherwise every keystroke recompiles the clip. It fires
		// on focus loss too, so clicking through the fields without typing anything isn't allowed
		// to rewrite the clip and recompile it
		var editing = false;
		void Commit()
		{
			var after = item.Json.ToJsonString();

			if ( after == before )
				return;

			before = after;
			UpdateRanges( item );
			Edited?.Invoke();
		}
		serialized.OnPropertyStartEdit += _ => editing = true;
		serialized.OnPropertyFinishEdit += _ =>
		{
			editing = false;
			Commit();
		};
		serialized.OnPropertyChanged += _ =>
		{
			if ( !editing )
				Commit();
		};

		var header = Layout.AddRow();
		header.Add( new Label( $"{ClipEventTypes.Title( eventClass )} Event", this ) { Color = Theme.TextControl.WithAlpha( 0.6f ) }, 1 );
		header.Add( SectionHeader.CreateButton( this, "delete", Theme.Red, "Delete this event", () => Deleted?.Invoke() ) );

		if ( ClipEventTypes.PayloadProperty( eventClass ) is { } payload )
			Add( Layout, serialized, payload );

		var timing = Layout.AddRow();
		timing.Spacing = 4;
		_start = Add( timing, serialized, nameof( ClipDocument.Event.StartFrame ), 1 ) as FloatControlWidget;
		_length = Add( timing, serialized, nameof( ClipDocument.Event.DurationFrames ), 1 ) as FloatControlWidget;
		UpdateRanges( item );
		Layout.AddStretchCell();
	}

	private void UpdateRanges( ClipDocument.Event item )
	{
		var last = MathF.Max( FrameCount - 1, 0 );
		_start?.MakeRanged( new Vector2( 0, MathF.Max( last - item.DurationFrames, 0 ) ), 0, true, false );
		_length?.MakeRanged( new Vector2( 0, MathF.Max( last - item.StartFrame, 0 ) ), 0, true, false );
	}

	private ControlWidget Add( Layout layout, SerializedObject serialized, string name, int stretch = 0 )
	{
		if ( !serialized.TryGetProperty( name, out var property ) )
			return null;

		var field = new Widget( this ) { Layout = Layout.Column(), HorizontalSizeMode = SizeMode.Flexible };
		field.Layout.Spacing = 2;
		field.Layout.Add( new Label( property.DisplayName, field ) );
		var control = field.Layout.Add( ControlWidget.Create( property ) );
		control.MinimumWidth = 0;
		layout.Add( field, stretch );
		return control;
	}
}
