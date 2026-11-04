namespace Editor.AnimGraph;

/// <summary>
/// Every bone in the skeleton with its weight in the mask being edited, laid out as the hierarchy so
/// the shape of the rig is there to read. A mask only authors a few of these - the rest inherit,
/// and are shown greyed at the weight they end up with so the whole mask can be seen at once.
/// </summary>
internal sealed class BoneWeights : Widget
{
	private sealed class Row
	{
		public string Bone;
		public bool Included;
		public bool Alternate;
		public Widget Widget;
		public Label Name;
		public FloatControlWidget Control;
		public Widget Clear;

		[Range( 0, 1 )]
		public float Weight { get; set; }
	}

	private const string Hint = "<b>Bone Weights</b><p>Drag a bone's slider to weight it in the mask. Bones with no weight of their own inherit from the bone above them, so weighting one bone weights everything under it.</p>";

	private readonly Widget _canvas;
	private readonly Layout _list;
	private readonly LineEdit _search;
	private readonly IconButton _authored;
	private Label _empty;
	private readonly List<Row> _rows = [];

	private IReadOnlyList<(string Name, int ParentIndex)> _bones = [];
	private SkeletonDocument _document;
	private SkeletonDocument.BoneMask _mask;

	/// <summary>
	/// An edit, named so it can be undone. Only ever raised once an edit is finished.
	/// </summary>
	public Action<string> Committed { get; set; }

	/// <summary>
	/// Raised as a slider moves, so the preview can show the mask while it's being dragged.
	/// </summary>
	public Action Changed { get; set; }

	public string SearchText
	{
		get => _search.Text;
		set => _search.Text = value;
	}

	public bool AuthoredOnly
	{
		get => _authored.IsActive;
		set
		{
			_authored.IsActive = value;
			Filter();
		}
	}

	/// <summary>
	/// The inspector bounds the list's height and owns its width.
	/// </summary>
	protected override Vector2 MinimumSizeHint() => new( 0, 120 );

	public BoneWeights( Widget parent ) : base( parent )
	{
		Name = "BoneWeights";
		WindowTitle = "Bone Weights";
		SetWindowIcon( "account_tree" );

		Layout = Layout.Column();

		var bar = new Widget( this ) { FixedHeight = Theme.RowHeight + 8 };

		bar.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( bar.LocalRect );
			return true;
		};

		var top = bar.Layout = Layout.Row();
		top.Margin = new Sandbox.UI.Margin( 4, 4, 4, 4 );
		top.Alignment = TextFlag.LeftCenter;

		// Rigs run to hundreds of bones, so finding one by scrolling isn't realistic
		_search = top.Add( new LineEdit( bar ) { PlaceholderText = "⌕  Filter bones..", ClearButtonEnabled = true, FixedHeight = Theme.RowHeight, ToolTip = Hint }, 1 );
		_search.TextChanged += _ => Filter();
		_authored = top.Add( new IconButton( "edit_note", null, bar )
		{
			IsToggle = true,
			ToolTip = "Show only authored weights",
			Background = Color.Transparent,
			FixedSize = Theme.RowHeight
		} );
		_authored.OnToggled = _ => Filter();

		Layout.Add( bar );

		var scroll = Layout.Add( new ScrollArea( this ), 1 );
		scroll.VerticalScrollbarMode = ScrollbarMode.Auto;
		scroll.HorizontalScrollbarMode = ScrollbarMode.Off;
		scroll.Canvas = _canvas = new Widget( scroll )
		{
			Layout = Layout.Column(),
			HorizontalSizeMode = SizeMode.Flexible,
			VerticalSizeMode = SizeMode.CanGrow,
		};

		_list = _canvas.Layout;
		_list.Margin = 0;
		_list.Spacing = 0;
	}

	/// <summary>
	/// The compiled skeleton supplies the hierarchy; the document supplies the root and masks.
	/// </summary>
	public void SetSource( SkeletonDocument document )
	{
		_bones = BoneMaskWeights.GetBones( document?.SourceSkeleton );
		_document = document;

		Rebuild();
	}

	/// <summary>
	/// The mask being edited, or null when none is selected.
	/// </summary>
	public SkeletonDocument.BoneMask Mask
	{
		get => _mask;
		set
		{
			_mask = value;
			Refresh();
		}
	}

	/// <summary>
	/// Show the weights again after something else changed them, like an undo.
	/// </summary>
	public void Refresh()
	{
		// The mask may have been deleted out from under the rows
		if ( _document is not null && _mask is not null && !_document.Masks.Contains( _mask ) )
			_mask = null;

		var weights = BoneMaskWeights.Build( _bones, _mask );
		var editable = _mask is not null && !ReadOnly;

		foreach ( var row in _rows )
		{
			var authored = Authored( row.Bone );
			var weight = weights.GetValueOrDefault( row.Bone );

			row.Weight = weight;
			row.Control.ReadOnly = !editable || !row.Included;
			row.Control.HighlightColor = !row.Included ? Theme.TextControl.WithAlpha( 0.2f )
				: authored is null ? Theme.TextControl.WithAlpha( 0.35f ) : Theme.Green;
			row.Control.ToolTip = authored is null ? "Inherited weight. Edit to override, or clear an override to inherit again." : "Authored weight. Clear to inherit again.";

			row.Clear.Visible = authored is not null;
			row.Clear.Enabled = editable;
			row.Name.Color = Theme.TextControl.WithAlpha( !row.Included ? 0.35f : authored is null ? 0.7f : 0.95f );
		}

		Filter();
	}

	/// <summary>
	/// Hide the rows that don't match the search rather than rebuilding them, so a slider being
	/// dragged and the scroll position both survive typing.
	/// </summary>
	private void Filter()
	{
		using var suspend = SuspendUpdates.For( _canvas );

		var search = _search.Text;
		var alternate = false;
		var any = false;

		foreach ( var row in _rows )
		{
			var visible = (string.IsNullOrWhiteSpace( search ) || row.Bone.Contains( search, StringComparison.OrdinalIgnoreCase ))
				&& (!AuthoredOnly || Authored( row.Bone ) is not null);
			row.Widget.Visible = visible;

			if ( !visible )
				continue;

			any = true;
			// Striping goes by what's on screen, not by what was built
			row.Alternate = alternate;
			alternate = !alternate;
		}

		if ( _empty.IsValid() )
		{
			_empty.Text = _bones.Count == 0 ? "No compiled bones are available." : "No bones match the current filters.";
			_empty.Visible = !any;
		}
	}

	private void Rebuild()
	{
		// A hundred rows going in one at a time shows through as a flicker without this
		using var suspend = SuspendUpdates.For( _canvas );

		_rows.Clear();
		_list.Clear( true );

		// Bones the compiler trims away are still worth listing, greyed - a mask can't reach them,
		// and that's easier to see than to be told
		var included = BonesUnderRoot();

		foreach ( var bone in _bones )
		{
			_list.Add( BoneRow( bone, included is null || included.Contains( bone.Name ) ) );
		}

		_list.Add( _empty = new Label( _canvas ) { Color = Theme.TextControl.WithAlpha( 0.6f ), WordWrap = true, Margin = 8 } );
		_list.AddStretchCell();
		Refresh();
	}

	private Widget BoneRow( (string Name, int ParentIndex) bone, bool included )
	{
		var widget = new Widget( _canvas ) { FixedHeight = Theme.RowHeight + 2 };

		var entry = new Row { Bone = bone.Name, Included = included, Widget = widget, Alternate = _rows.Count % 2 == 1 };

		widget.OnPaintOverride = () =>
		{
			if ( !entry.Alternate )
				return true;

			Paint.ClearPen();
			Paint.SetBrush( Theme.WindowBackground.WithAlpha( 0.25f ) );
			Paint.DrawRect( widget.LocalRect );

			return true;
		};

		var row = widget.Layout = Layout.Row();
		row.Margin = new Sandbox.UI.Margin( 4, 0, 4, 0 );
		row.Spacing = 4;
		row.Alignment = TextFlag.LeftCenter;

		// Leave the name the remaining width; cap indentation so deep rigs fit a narrow inspector.
		var names = new Widget( widget ) { MinimumWidth = 0, Layout = Layout.Row() };
		names.Layout.Margin = new Sandbox.UI.Margin( Math.Min( Depth( bone ), 4 ) * 6, 0, 0, 0 );

		entry.Name = names.Layout.Add( new Label( bone.Name, names )
		{
			Alignment = TextFlag.LeftCenter,
			MinimumWidth = 0,
			HorizontalSizeMode = SizeMode.Flexible | SizeMode.Ignore,
			ToolTip = included ? bone.Name : "This bone isn't under the skeleton's root bone, so it isn't part of the skeleton"
		}, 1 );

		row.Add( names, 1 );

		var serialized = entry.GetSerialized();
		entry.Control = row.Add( new FloatControlWidget( serialized.GetProperty( nameof( entry.Weight ) ) )
		{
			Label = null,
			PaintBackground = false,
			MinimumWidth = 110,
			MaximumWidth = 150,
			ReadOnly = !included
		}, 1 );

		var editing = false;
		serialized.OnPropertyStartEdit += _ => editing = true;
		serialized.OnPropertyChanged += _ =>
		{
			SetWeight( entry.Bone, entry.Weight );
			Changed?.Invoke();
			if ( !editing )
				Committed?.Invoke( "Set Bone Weight" );
			Refresh();
		};
		serialized.OnPropertyFinishEdit += _ =>
		{
			editing = false;
			Committed?.Invoke( "Set Bone Weight" );
			Refresh();
		};

		// Only shown on bones with a weight of their own, so it stays out of the way of the rest
		var clear = new Widget( widget ) { FixedWidth = 20, Layout = Layout.Row() };

		entry.Clear = clear.Layout.Add( new IconButton( "backspace", () =>
		{
			ClearWeight( entry.Bone );

			Committed?.Invoke( "Unweight Bone" );
			Changed?.Invoke();
			Refresh();
		}, clear )
		{
			ToolTip = "Stop weighting this bone, and let it inherit again",
			IconSize = 12,
			FixedSize = 20,
			Background = Color.Transparent,
			Foreground = Theme.TextControl
		} );

		row.Add( clear );

		_rows.Add( entry );

		return widget;
	}

	private void SetWeight( string bone, float weight )
	{
		if ( _mask is null || ReadOnly )
			return;

		if ( Authored( bone ) is { } existing )
		{
			existing.Weight = weight;
			return;
		}

		_mask.Weights.Add( new SkeletonDocument.BoneWeight { Bone = bone, Weight = weight } );
	}

	private void ClearWeight( string bone )
	{
		if ( ReadOnly )
			return;

		if ( Authored( bone ) is { } existing )
			_mask.Weights.Remove( existing );
	}

	/// <summary>
	/// The weight this bone was given by hand, as opposed to the one it inherits.
	/// </summary>
	private SkeletonDocument.BoneWeight Authored( string bone )
	{
		return _mask?.Weights.FirstOrDefault( weight => string.Equals( weight.Bone, bone, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// How far down the hierarchy a bone is, which is how far its row is indented.
	/// </summary>
	private int Depth( (string Name, int ParentIndex) bone )
	{
		var depth = 0;

		for ( var parent = bone.ParentIndex; parent >= 0; parent = _bones[parent].ParentIndex )
		{
			depth++;
		}

		return depth;
	}

	/// <summary>
	/// The bones the skeleton actually compiles to - a root bone trims the rig to the part under it.
	/// </summary>
	private HashSet<string> BonesUnderRoot()
	{
		if ( string.IsNullOrWhiteSpace( _document?.RootBone ) )
			return null;

		var root = Enumerable.Range( 0, _bones.Count ).FirstOrDefault( index => string.Equals( _bones[index].Name, _document.RootBone, StringComparison.OrdinalIgnoreCase ), -1 );
		if ( root < 0 )
			return null;

		var included = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var queue = new Queue<int>();
		queue.Enqueue( root );

		while ( queue.Count > 0 )
		{
			var bone = queue.Dequeue();
			included.Add( _bones[bone].Name );

			foreach ( var child in Enumerable.Range( 0, _bones.Count ).Where( index => _bones[index].ParentIndex == bone ) )
			{
				queue.Enqueue( child );
			}
		}

		return included;
	}
}
