namespace Editor.AnimGraph;

internal sealed class FloatChannelWeights : Widget
{
	private readonly SkeletonDocument _document;
	private readonly SkeletonDocument.BoneMask _mask;
	private readonly LineEdit _search;
	private readonly Widget _channels;
	private readonly SerializedObject _serialized;
	private readonly List<(string Channel, Widget Row, FloatControlWidget Control, IconButton Clear, Label Name, bool Declared)> _rows = [];
	private Label _empty;
	private bool _editing;

	public Action<string> Committed { get; set; }

	public string SearchText
	{
		get => _search.Text;
		set => _search.Text = value;
	}

	public FloatChannelWeights( Widget parent, SkeletonDocument document, SkeletonDocument.BoneMask mask ) : base( parent )
	{
		_document = document;
		_mask = mask;
		Layout = Layout.Column();
		Layout.Spacing = 4;
		Layout.Alignment = TextFlag.Top;
		VerticalSizeMode = SizeMode.CanGrow;
		_serialized = mask.GetSerialized();
		_serialized.OnPropertyStartEdit += _ => _editing = true;
		_serialized.OnPropertyFinishEdit += _ =>
		{
			_editing = false;
			Committed?.Invoke( "Set Float Channel Weight" );
			Refresh();
		};
		_serialized.OnPropertyChanged += _ =>
		{
			if ( !_editing )
				Committed?.Invoke( "Set Float Channel Weight" );
			Refresh();
		};

		var row = Layout.AddRow();
		row.Margin = new Sandbox.UI.Margin( 4, 0, 4, 0 );
		row.Spacing = 4;
		row.Add( new Label( "Default Weight", this ) { Alignment = TextFlag.LeftCenter }, 1 );
		row.Add( new FloatControlWidget( _serialized.GetProperty( nameof( mask.DefaultFloatChannelWeight ) ) )
		{
			Label = null,
			PaintBackground = false,
			MinimumWidth = 110,
			MaximumWidth = 150,
			ToolTip = "Weight for channels without an override in this mask."
		}, 1 );
		row.Add( new Widget( this ) { FixedWidth = 20 } );
		_search = Layout.Add( new LineEdit( this ) { PlaceholderText = "Filter channel weights...", ClearButtonEnabled = true, MinimumWidth = 0 } );
		_search.TextChanged += _ => Refresh();
		var scroll = Layout.Add( new ScrollArea( this ) { FixedHeight = 160, HorizontalScrollbarMode = ScrollbarMode.Off } );
		scroll.Canvas = _channels = new Widget( scroll ) { Layout = Layout.Column(), VerticalSizeMode = SizeMode.CanGrow };
		_channels.Layout.Spacing = 0;
		Rebuild();
	}

	private SkeletonDocument.FloatChannelWeight Override( string channel ) => _mask.FloatChannelWeights.FirstOrDefault( weight => string.Equals( weight.Channel, channel, StringComparison.OrdinalIgnoreCase ) );

	public void Rebuild()
	{
		using var suspend = SuspendUpdates.For( _channels );
		_rows.Clear();
		_channels.Layout.Clear( true );
		var declared = _document.FloatChannelSets.SelectMany( set => set.Channels ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		foreach ( var channel in declared.Concat( _mask.FloatChannelWeights.Select( weight => weight.Channel ) ).Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.OrdinalIgnoreCase ) )
		{
			var row = new Widget( _channels ) { Layout = Layout.Row(), FixedHeight = Theme.RowHeight + 2 };
			row.Layout.Margin = new Sandbox.UI.Margin( 4, 0, 4, 0 );
			row.Layout.Spacing = 4;
			var name = row.Layout.Add( new Label( channel, row )
			{
				MinimumWidth = 0,
				Alignment = TextFlag.LeftCenter,
				Color = declared.Contains( channel ) ? Theme.TextControl : Theme.Yellow,
				ToolTip = declared.Contains( channel ) ? channel : "This override names a channel that is not in any channel set."
			}, 1 );
			var property = EditorTypeLibrary.CreateProperty<float>( channel,
				() => Override( channel )?.Weight ?? _mask.DefaultFloatChannelWeight,
				value =>
				{
					if ( ReadOnly )
						return;
					var weight = Override( channel );
					if ( weight is null )
						_mask.FloatChannelWeights.Add( new SkeletonDocument.FloatChannelWeight { Channel = channel, Weight = value } );
					else
						weight.Weight = value;
				}, [new RangeAttribute( 0, 1 )], _serialized );
			var control = row.Layout.Add( new FloatControlWidget( property ) { Label = null, MinimumWidth = 110, MaximumWidth = 150, PaintBackground = false }, 1 );
			var clearSlot = row.Layout.Add( new Widget( row ) { FixedWidth = 20, Layout = Layout.Row() } );
			var clear = clearSlot.Layout.Add( SectionHeader.CreateButton( clearSlot, "backspace", Theme.TextControl, "Clear this override and use the default weight", () =>
			{
				if ( ReadOnly )
					return;
				_mask.FloatChannelWeights.RemoveAll( weight => string.Equals( weight.Channel, channel, StringComparison.OrdinalIgnoreCase ) );
				Committed?.Invoke( "Clear Float Channel Weight" );
				if ( declared.Contains( channel ) )
					Refresh();
				else
					Rebuild();
			}, !ReadOnly ) );
			clear.Background = Color.Transparent;
			clear.IconSize = 12;
			clear.FixedSize = 20;
			_channels.Layout.Add( row );
			_rows.Add( (channel, row, control, clear, name, declared.Contains( channel )) );
		}
		_empty = _channels.Layout.Add( new Label( _channels ) { Color = Theme.TextControl.WithAlpha( 0.6f ), WordWrap = true, Margin = 8 } );
		Refresh();
	}

	private void Refresh()
	{
		var any = false;
		foreach ( var row in _rows )
		{
			var authored = Override( row.Channel ) is not null;
			var visible = string.IsNullOrWhiteSpace( _search.Text ) || row.Channel.Contains( _search.Text, StringComparison.OrdinalIgnoreCase );
			row.Row.Visible = visible;
			row.Control.ReadOnly = ReadOnly;
			row.Control.HighlightColor = authored ? Theme.Green : Theme.TextControl.WithAlpha( 0.35f );
			row.Control.ToolTip = authored ? "Authored weight. Clear to use the default weight." : "Default weight. Edit to override this channel.";
			row.Clear.Visible = authored;
			row.Clear.Enabled = !ReadOnly && authored;
			row.Name.Color = row.Declared ? Theme.TextControl.WithAlpha( authored ? 0.95f : 0.7f ) : Theme.Yellow;
			any |= visible;
		}
		_empty.Text = _rows.Count == 0 ? "This skeleton has no float channels." : "No channels match the current filter.";
		_empty.Visible = !any;
	}
}
