namespace Editor.AnimGraph;

internal sealed class FloatChannels : Widget
{
	private sealed class Row
	{
		public string Channel;
		public Widget Widget;
		public LineEdit Name;
	}

	private readonly SkeletonDocument _document;
	private readonly LineEdit _search;
	private readonly ScrollArea _scroll;
	private readonly Widget _channels;
	private readonly Label _empty;
	private readonly Label _problem;
	private readonly List<Row> _rows = [];
	private Row _draft;

	public Action<string> Committed { get; set; }

	public string SearchText
	{
		get => _search.Text;
		set => _search.Text = value;
	}

	public FloatChannels( Widget parent, SkeletonDocument document ) : base( parent )
	{
		_document = document;
		Layout = Layout.Column();
		Layout.Spacing = 4;
		Layout.Alignment = TextFlag.Top;
		VerticalSizeMode = SizeMode.CanGrow;

		var bar = Layout.AddRow();
		bar.Spacing = 4;
		_search = bar.Add( new LineEdit( this ) { PlaceholderText = "Filter channels...", ClearButtonEnabled = true, MinimumWidth = 0 }, 1 );
		_search.TextChanged += _ => Filter();
		bar.Add( SectionHeader.CreateButton( this, "add", Theme.Green, "Add a channel", AddChannel, !ReadOnly ) );

		_scroll = Layout.Add( new ScrollArea( this ) { FixedHeight = 160, HorizontalScrollbarMode = ScrollbarMode.Off } );
		_scroll.Canvas = _channels = new Widget( _scroll ) { Layout = Layout.Column(), VerticalSizeMode = SizeMode.CanGrow };
		_channels.Layout.Spacing = 2;
		foreach ( var channel in document.FloatChannelSets.SelectMany( set => set.Channels ).Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.OrdinalIgnoreCase ) )
			AddRow( channel );
		_empty = _channels.Layout.Add( new Label( "No float channels. Use + to add one.", _channels ) { Color = Theme.TextControl.WithAlpha( 0.6f ), WordWrap = true, Margin = 8 } );
		_problem = Layout.Add( new Label( this ) { Color = Theme.Red, WordWrap = true, Visible = false } );
		Filter();
	}

	private Row AddRow( string channel )
	{
		var row = new Row { Channel = channel, Widget = new Widget( _channels ) { Layout = Layout.Row(), FixedHeight = Theme.RowHeight } };
		row.Widget.Layout.Spacing = 4;
		row.Name = row.Widget.Layout.Add( new LineEdit( channel ?? "", row.Widget ) { PlaceholderText = "Channel name", MinimumWidth = 0, ReadOnly = ReadOnly }, 1 );
		row.Name.EditingFinished += () => CommitRow( row );
		row.Widget.Layout.Add( SectionHeader.CreateButton( row.Widget, "close", Theme.TextControl, "Remove this channel and its mask weights", () => RemoveRow( row ), !ReadOnly ) );
		_channels.Layout.Add( row.Widget );
		_rows.Add( row );
		return row;
	}

	private void AddChannel()
	{
		if ( ReadOnly )
			return;

		_search.Text = "";
		_draft ??= AddRow( null );
		Filter();
		_draft.Name.Focus();
		_scroll.MakeVisible( _draft.Widget );
	}

	private void CommitRow( Row row )
	{
		if ( ReadOnly || !_rows.Contains( row ) )
			return;

		var name = row.Name.Text.Trim();
		var problem = string.IsNullOrWhiteSpace( name ) ? "Enter a channel name."
			: _rows.Any( other => other != row && string.Equals( other.Channel, name, StringComparison.OrdinalIgnoreCase ) ) ? "A channel with this name already exists."
			: BoneMaskWeights.GetBones( _document.SourceSkeleton ).Any( bone => string.Equals( bone.Name, name, StringComparison.OrdinalIgnoreCase ) ) ? "A channel cannot use a bone's name."
			: row.Channel is not null && _document.Masks.Any( mask => mask.FloatChannelWeights.Any( weight => !string.Equals( weight.Channel, row.Channel, StringComparison.OrdinalIgnoreCase ) && string.Equals( weight.Channel, name, StringComparison.OrdinalIgnoreCase ) ) ) ? "A mask already has an override with this channel name." : "";
		_problem.Text = problem;
		_problem.Visible = problem.Length > 0;
		if ( problem.Length > 0 || name == row.Channel )
			return;

		var adding = row.Channel is null;
		if ( adding )
		{
			var set = _document.FloatChannelSets.FirstOrDefault( set => string.Equals( set.Name, "MorphChannel", StringComparison.OrdinalIgnoreCase ) );
			if ( set is null )
			{
				set = new SkeletonDocument.FloatChannelSet { Name = "MorphChannel" };
				_document.FloatChannelSets.Add( set );
			}
			set.Channels.Add( name );
			_draft = null;
		}
		else
		{
			foreach ( var set in _document.FloatChannelSets )
			{
				for ( var i = 0; i < set.Channels.Count; i++ )
				{
					if ( string.Equals( set.Channels[i], row.Channel, StringComparison.OrdinalIgnoreCase ) )
						set.Channels[i] = name;
				}
			}
			foreach ( var weight in _document.Masks.SelectMany( mask => mask.FloatChannelWeights ).Where( weight => string.Equals( weight.Channel, row.Channel, StringComparison.OrdinalIgnoreCase ) ) )
				weight.Channel = name;
		}

		row.Channel = name;
		row.Name.Text = name;
		Filter();
		Committed?.Invoke( adding ? "Add Float Channel" : "Rename Float Channel" );
	}

	private void RemoveRow( Row row )
	{
		if ( ReadOnly || !_rows.Remove( row ) )
			return;

		if ( row == _draft )
			_draft = null;
		if ( row.Channel is not null )
		{
			foreach ( var set in _document.FloatChannelSets.ToArray() )
			{
				if ( set.Channels.RemoveAll( name => string.Equals( name, row.Channel, StringComparison.OrdinalIgnoreCase ) ) > 0 && set.Channels.Count == 0 )
					_document.FloatChannelSets.Remove( set );
			}
			foreach ( var mask in _document.Masks )
				mask.FloatChannelWeights.RemoveAll( weight => string.Equals( weight.Channel, row.Channel, StringComparison.OrdinalIgnoreCase ) );
		}
		row.Widget.Destroy();
		_problem.Visible = false;
		Filter();
		if ( row.Channel is not null )
			Committed?.Invoke( "Remove Float Channel" );
	}

	private void Filter()
	{
		var any = false;
		foreach ( var row in _rows )
		{
			var visible = row.Channel is null || string.IsNullOrWhiteSpace( _search.Text ) || row.Channel.Contains( _search.Text, StringComparison.OrdinalIgnoreCase );
			row.Widget.Visible = visible;
			any |= visible;
		}
		_empty.Text = _rows.Count == 0 ? "No float channels. Use + to add one." : "No channels match the current filter.";
		_empty.Visible = !any;
	}
}
