namespace Editor;

/// <summary>
/// A generic sliding hierarchical selector widget.
/// Build a tree of <see cref="AdvancedDropdownItem"/> and hand it to this widget.
/// </summary>
public partial class AdvancedDropdownWidget : Widget
{
	/// <summary>
	/// Called when a leaf item is selected. Receives the item's <see cref="AdvancedDropdownItem.Value"/>.
	/// </summary>
	public Action<object> OnSelect { get; set; }

	/// <summary>
	/// Called after selection to allow the host to close/cleanup.
	/// </summary>
	public Action OnFinished { get; set; }

	/// <summary>
	/// Placeholder text shown in the search bar.
	/// </summary>
	public string SearchPlaceholderText
	{
		get => _searchPlaceholder;
		set
		{
			_searchPlaceholder = value;
			if ( Search is not null ) Search.PlaceholderText = value;
		}
	}
	string _searchPlaceholder = "Search";

	/// <summary>
	/// Title shown in the root panel header.
	/// </summary>
	public string RootTitle { get; set; } = "Select";

	/// <summary>
	/// Fixed size of the content area (below the search bar).
	/// </summary>
	public Vector2 ContentSize { get; set; } = new( 300, 400 );

	/// <summary>
	/// The root of the item tree. Populated by <see cref="OnBuildItems"/> or set directly.
	/// </summary>
	public AdvancedDropdownItem RootItem { get; set; } = new();

	/// <summary>
	/// Called to (re)build the item tree. Receives <see cref="RootItem"/> after it has been cleared.
	/// </summary>
	public Action<AdvancedDropdownItem> OnBuildItems { get; set; }

	/// <summary>
	/// Optional custom search scorer. Receives an item and the search words, returns a score (0 = no match).
	/// If null, the default scorer matches against Title and Description.
	/// </summary>
	public Func<AdvancedDropdownItem, string[], int> SearchScorer { get; set; }

	/// <summary>
	/// Optional filter widget placed next to the search bar (e.g. a settings button).
	/// </summary>
	public Widget FilterWidget
	{
		get => _filterWidget;
		set
		{
			if ( _filterWidget is not null && _headLayout is not null )
			{
				_filterWidget.Destroy();
			}

			_filterWidget = value;

			if ( value is not null && _headLayout is not null )
			{
				_headLayout.Add( value );
			}
		}
	}
	Widget _filterWidget;
	Layout _headLayout;

	/// <summary>
	/// For subclasses that have a text input inside the panel (e.g. a name field) -
	/// set to true to prevent Left arrow key from popping the panel.
	/// </summary>
	protected bool IsTextInputActive { get; set; }

	List<AdvancedDropdownPanel> Panels { get; set; } = new();
	int CurrentPanelId { get; set; } = 0;
	protected Widget Main { get; set; }
	string searchString;

	/// <summary>
	/// Whether the user is currently searching.
	/// </summary>
	protected bool IsSearching => !string.IsNullOrWhiteSpace( searchString );

	internal LineEdit Search { get; init; }

	public AdvancedDropdownWidget( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();

		var head = Layout.Row();
		head.Margin = 6;
		Layout.Add( head );

		Main = new Widget( this );
		Main.Layout = Layout.Row();
		Main.Layout.Enabled = false;
		Main.FixedSize = ContentSize;
		Layout.Add( Main, 1 );

		DeleteOnClose = true;

		Search = new LineEdit( this );
		Search.Layout = Layout.Row();
		Search.Layout.AddStretchCell( 1 );
		Search.MinimumHeight = 22;
		Search.PlaceholderText = SearchPlaceholderText;
		Search.TextEdited += ( t ) =>
		{
			searchString = t;
			Rebuild();
		};

		var clearButton = Search.Layout.Add( new ToolButton( string.Empty, "clear", this ) );
		clearButton.MouseLeftPress = () =>
		{
			Search.Text = searchString = string.Empty;
			Rebuild();
		};

		head.Add( Search );

		_headLayout = head;

		if ( FilterWidget is not null )
		{
			head.Add( FilterWidget );
		}

		Search.Focus();
	}

	/// <summary>
	/// Rebuild the item tree and reset to the root panel.
	/// </summary>
	public void Rebuild()
	{
		RootItem.Clear();
		OnBuildItems?.Invoke( RootItem );
		ResetToRoot();
	}

	/// <summary>
	/// Push a new panel onto the stack (navigate deeper into a category).
	/// </summary>
	protected void PushPanel( AdvancedDropdownPanel panel )
	{
		CurrentPanelId++;

		if ( Panels.Count > CurrentPanelId && Panels.ElementAt( CurrentPanelId ) is var existingObj )
			existingObj.Destroy();

		Panels.Insert( CurrentPanelId, panel );
		Main.Layout.Add( panel, 1 );

		if ( !panel.IsManual )
		{
			BuildPanel( panel );
		}

		AnimateSelection( true, Panels[CurrentPanelId - 1], panel );
		panel.Focus();
	}

	/// <summary>
	/// Pop the current panel (navigate back).
	/// </summary>
	public void PopPanel()
	{
		if ( CurrentPanelId == 0 ) return;

		var currentPanel = Panels[CurrentPanelId];
		CurrentPanelId--;

		AnimateSelection( false, currentPanel, Panels[CurrentPanelId] );
		Panels[CurrentPanelId].Focus();
	}

	void AnimateSelection( bool forward, AdvancedDropdownPanel prev, AdvancedDropdownPanel selection )
	{
		const string easing = "ease-out";
		const float speed = 0.2f;

		var distance = Width;

		var prevFrom = prev.Position.x;
		var prevTo = forward ? prev.Position.x - distance : prev.Position.x + distance;

		var selectionFrom = forward ? selection.Position.x + distance : selection.Position.x;
		var selectionTo = forward ? selection.Position.x : selection.Position.x + distance;

		var func = ( AdvancedDropdownPanel a, float x ) =>
		{
			a.Position = a.Position.WithX( x );
			OnMoved();
		};

		Animate.Add( prev, speed, prevFrom, prevTo, x => func( prev, x ), easing );
		Animate.Add( selection, speed, selectionFrom, selectionTo, x => func( selection, x ), easing );
	}

	void ResetToRoot()
	{
		Main.Layout.Clear( true );
		Panels.Clear();

		var panel = new AdvancedDropdownPanel( Main, this, RootTitle );
		CurrentPanelId = 0;

		BuildPanel( panel );

		Panels.Add( panel );
		Main.Layout.Add( panel );
	}

	/// <summary>
	/// Build a panel's content from the item tree. Override to customize.
	/// </summary>
	protected virtual void BuildPanel( AdvancedDropdownPanel panel )
	{
		panel.ClearEntries();
		panel.ItemList.Add( panel.CategoryHeader );

		var items = panel.SourceItem?.Children ?? RootItem.Children;

		if ( !string.IsNullOrWhiteSpace( searchString ) )
		{
			BuildSearchResults( panel );
			return;
		}

		foreach ( var item in items )
		{
			if ( item.HasChildren )
			{
				panel.AddEntry( new CategoryEntry( panel )
				{
					Category = item.Title,
					MouseClick = () =>
					{
						var sub = new AdvancedDropdownPanel( Main, this, item.Title ) { SourceItem = item };
						PushPanel( sub );
					}
				} );
			}
			else
			{
				panel.AddEntry( new ItemEntry( panel, item )
				{
					MouseClick = () =>
					{
						OnSelect?.Invoke( item.Value );
						OnFinished?.Invoke();
					}
				} );
			}
		}

		panel.AddStretchCell();
	}

	void BuildSearchResults( AdvancedDropdownPanel panel )
	{
		var searchWords = searchString.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		var scorer = SearchScorer ?? DefaultSearchScore;

		var results = RootItem.GetAllLeaves()
			.Select( x => new { Item = x, Score = scorer( x, searchWords ) } )
			.Where( x => x.Score > 0 )
			.OrderByDescending( x => x.Score )
			.Select( x => x.Item );

		foreach ( var item in results )
		{
			panel.AddEntry( new ItemEntry( panel, item )
			{
				MouseClick = () =>
				{
					OnSelect?.Invoke( item.Value );
					OnFinished?.Invoke();
				}
			} );
		}

		OnBuildSearchResults( panel, searchString );

		panel.AddStretchCell();
	}

	/// <summary>
	/// Called after search results are populated. Override to add extra entries (e.g. "New Component" button).
	/// </summary>
	protected virtual void OnBuildSearchResults( AdvancedDropdownPanel panel, string searchText )
	{
	}

	static int DefaultSearchScore( AdvancedDropdownItem item, string[] parts )
	{
		var score = 0;
		var t = (item.Title ?? "").Replace( " ", "" );
		var d = (item.Description ?? "").Replace( " ", "" );

		foreach ( var w in parts )
		{
			if ( t.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 10;
			if ( d.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 1;
		}

		return score;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetBrushAndPen( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		if ( e.Key == KeyCode.Down )
		{
			var panel = Panels[CurrentPanelId];
			if ( panel.ItemList.FirstOrDefault().IsValid() )
			{
				panel.Focus();
				panel.PostKeyEvent( KeyCode.Down );
				e.Accepted = true;
			}
		}
	}

	/// <summary>
	/// A single sliding panel with header, scroll area, and item list.
	/// </summary>
	public partial class AdvancedDropdownPanel : Widget
	{
		internal string Title { get; init; }
		internal Widget CategoryHeader { get; init; }
		ScrollArea Scroller { get; init; }
		internal AdvancedDropdownWidget Owner { get; set; }

		/// <summary>
		/// The item whose children this panel displays. Null for root panel.
		/// </summary>
		internal AdvancedDropdownItem SourceItem { get; set; }

		internal List<Widget> ItemList { get; private set; } = new();
		internal int CurrentItemId { get; private set; } = 0;
		internal Widget CurrentItem { get; private set; }

		internal bool IsManual { get; set; }

		internal AdvancedDropdownPanel( Widget parent, AdvancedDropdownWidget owner, string title = null ) : base( parent )
		{
			Owner = owner;
			Title = title;
			FixedSize = parent.ContentRect.Size;

			Layout = Layout.Column();

			CategoryHeader = new Widget( this );
			CategoryHeader.FixedHeight = Theme.RowHeight;
			CategoryHeader.OnPaintOverride = PaintHeader;
			CategoryHeader.MouseClick = Owner.PopPanel;
			Layout.Add( CategoryHeader );

			Scroller = new ScrollArea( this );
			Scroller.Layout = Layout.Column();
			Scroller.FocusMode = FocusMode.None;
			Layout.Add( Scroller, 1 );

			Scroller.Canvas = new Widget( Scroller );
			Scroller.Canvas.Layout = Layout.Column();
			Scroller.Canvas.OnPaintOverride = () =>
			{
				Paint.ClearPen();
				Paint.SetBrush( Theme.WidgetBackground );
				Paint.DrawRect( Scroller.Canvas.LocalRect );
				return true;
			};
		}

		protected bool SelectMoveRow( int delta )
		{
			var panel = Owner.Panels[Owner.CurrentPanelId];
			if ( delta == 1 && panel.ItemList.Count - 1 > panel.CurrentItemId )
			{
				panel.CurrentItem = panel.ItemList[++panel.CurrentItemId];
				panel.Update();

				if ( panel.CurrentItem.IsValid() )
					Scroller.MakeVisible( panel.CurrentItem );

				return true;
			}
			else if ( delta == -1 )
			{
				if ( panel.CurrentItemId > 0 )
				{
					panel.CurrentItem = panel.ItemList[--panel.CurrentItemId];
					panel.Update();

					if ( panel.CurrentItem.IsValid() )
						Scroller.MakeVisible( panel.CurrentItem );

					return true;
				}
				else
				{
					panel.Owner.Search.Focus();
					panel.CurrentItem = null;
					panel.Update();
					return true;
				}
			}

			return false;
		}

		protected bool Enter()
		{
			var panel = Owner.Panels[Owner.CurrentPanelId];
			if ( panel.ItemList[panel.CurrentItemId] is Widget entry )
			{
				entry.MouseClick?.Invoke();
				return true;
			}

			return false;
		}

		protected override void OnKeyRelease( KeyEvent e )
		{
			if ( e.Key == KeyCode.Down )
			{
				e.Accepted = true;
				SelectMoveRow( 1 );
				return;
			}

			if ( e.Key == KeyCode.Up )
			{
				e.Accepted = true;
				SelectMoveRow( -1 );
				return;
			}

			if ( e.Key == KeyCode.Left && !Owner.IsTextInputActive )
			{
				e.Accepted = true;
				Owner.PopPanel();
				return;
			}

			if ( (e.Key == KeyCode.Return || e.Key == KeyCode.Right) && Enter() )
			{
				e.Accepted = true;
				return;
			}
		}

		internal bool PaintHeader()
		{
			var c = CategoryHeader;
			var selected = c.IsUnderMouse || CurrentItem == c;

			Paint.ClearPen();
			Paint.SetBrush( selected ? Theme.ControlBackground : Theme.WidgetBackground.WithAlpha( selected ? 0.7f : 0.4f ) );
			Paint.DrawRect( c.LocalRect );

			var r = c.LocalRect.Shrink( 12, 2 );
			Paint.SetPen( Theme.TextControl );

			if ( Owner.CurrentPanelId > 0 )
			{
				Paint.DrawIcon( r, "arrow_back", 14, TextFlag.LeftCenter );
			}

			var headerTitle = Title ?? Owner.RootTitle;
			Paint.SetDefaultFont( 8, 600 );
			Paint.DrawText( r, headerTitle, TextFlag.Center );

			return true;
		}

		/// <summary>
		/// Add an entry widget to this panel.
		/// </summary>
		internal Widget AddEntry( Widget entry )
		{
			var layoutWidget = Scroller.Canvas.Layout.Add( entry );
			ItemList.Add( entry );

			if ( entry is ItemEntry e ) e.Panel = this;
			if ( entry is CategoryEntry c ) c.Panel = this;

			return layoutWidget;
		}

		internal void AddStretchCell()
		{
			Scroller.Canvas.Layout.AddStretchCell( 1 );
			Update();
		}

		internal void ClearEntries()
		{
			Scroller.Canvas.Layout.Clear( true );
			ItemList.Clear();
		}

		protected override void OnPaint()
		{
			Paint.Antialiasing = true;
			Paint.SetBrushAndPen( Theme.ControlBackground );
			Paint.DrawRect( LocalRect.Shrink( 0 ), 3 );
		}
	}

	/// <summary>
	/// A leaf item entry widget.
	/// </summary>
	public class ItemEntry : Widget
	{
		public string Text { get; set; } = "Item";
		public string Icon { get; set; }
		public bool IsSelected { get; set; }

		internal AdvancedDropdownPanel Panel { get; set; }

		/// <summary>
		/// The source item for this entry.
		/// </summary>
		public AdvancedDropdownItem Item { get; init; }

		public ItemEntry( Widget parent, AdvancedDropdownItem item = null ) : base( parent )
		{
			FixedHeight = 24;
			Item = item;

			if ( item is not null )
			{
				Text = item.Title;
				Icon = item.Icon;
				ToolTip = item.Tooltip;
			}
		}

		protected override void OnPaint()
		{
			var r = LocalRect.Shrink( 12, 2 );
			var selected = IsUnderMouse || Panel?.CurrentItem == this;
			var opacity = selected ? 1.0f : 0.7f;

			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground );

			if ( selected )
			{
				Paint.SetBrush( Theme.ControlBackground );
			}

			Paint.DrawRect( LocalRect );

			var iconRect = new Rect( r.Position, r.Height ).Shrink( 2 );

			if ( Item?.PaintIcon is not null )
			{
				Item.PaintIcon( iconRect, opacity );
			}
			else
			{
				var icon = !string.IsNullOrEmpty( Icon ) ? Icon : "circle";
				Paint.SetPen( Theme.Green.WithAlpha( opacity ) );
				Paint.DrawIcon( iconRect, icon, r.Height, TextFlag.Center );
			}

			r.Left += r.Height + 6;

			Paint.SetDefaultFont( 8 );
			Paint.SetPen( Theme.TextControl.WithAlpha( selected ? 1.0f : 0.5f ) );
			Paint.DrawText( r, Text, TextFlag.LeftCenter );
		}
	}

	/// <summary>
	/// A category entry widget with a forward arrow.
	/// </summary>
	public class CategoryEntry : Widget
	{
		public string Category { get; set; }

		internal AdvancedDropdownPanel Panel { get; set; }

		public CategoryEntry( Widget parent ) : base( parent )
		{
			FixedHeight = 24;
		}

		protected override void OnPaint()
		{
			var selected = IsUnderMouse || Panel?.CurrentItem == this;

			Paint.ClearPen();
			Paint.SetBrush( Theme.WidgetBackground );

			if ( selected )
			{
				Paint.SetBrush( Theme.ControlBackground );
			}

			Paint.DrawRect( LocalRect );

			var r = LocalRect.Shrink( 12, 2 );

			Paint.SetPen( Theme.TextControl.WithAlpha( selected ? 1.0f : 0.5f ) );
			Paint.SetDefaultFont( 8 );
			Paint.DrawText( r, Category, TextFlag.LeftCenter );
			Paint.DrawIcon( r, "arrow_forward", 14, TextFlag.RightCenter );
		}
	}
}
