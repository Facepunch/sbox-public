namespace Editor;

internal class ListHeader : Widget
{
	/// <summary>
	/// The displayed columns in the header
	/// </summary>
	public string[] Columns { get; set; } = new string[] { "Name", "Type" };

	/// <summary>
	/// The name of the currently selected column for sorting
	/// </summary>
	public string SelectedColumn => Columns[SortIndex];

	/// <summary>
	/// The index of the column we're currently sorting by
	/// </summary>
	public int SortIndex { get; set; } = 0;

	/// <summary>
	/// Returns true if the current column is in ascending order.
	/// </summary>
	public bool SortAscending { get; set; } = true;

	/// <summary>
	/// Fires whenever a column has been clicked, providing the column name and whether it's in ascending order or not.
	/// </summary>
	public Action<string, bool> OnColumnSelect { get; set; }

	/// <summary>
	/// Fires whenever a a column has been resized, meaning your list should probably update its columns.
	/// </summary>
	public Action OnColumnResize { get; set; }

	private HashSet<string> ShownColumns { get; set; } = new();
	private List<Label> Labels = new();
	private Splitter Splitter;

	public ListHeader( Widget parent, string[] cols ) : base( parent )
	{
		FixedHeight = Theme.RowHeight;
		Columns = cols;
		ShownColumns = Columns.ToHashSet();

		Splitter = new Splitter( this );
		Splitter.FixedHeight = FixedHeight;
		Splitter.IsHorizontal = true;
		Splitter.HorizontalSizeMode = SizeMode.Flexible;
		Splitter.HandleWidth = 1;

		BuildSplitter();
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		Splitter.Width = Width;
	}

	void BuildSplitter()
	{
		Splitter.DestroyChildren();
		Labels.Clear();

		int i = 0;
		foreach ( var col in Columns )
		{
			var lbl = new Label( col );
			lbl.ContentMargins = new Sandbox.UI.Margin( 8, 0, 4, 0 );
			lbl.Cursor = CursorShape.Finger;
			lbl.OnPaintOverride += () =>
			{
				var bgColor = Paint.HasMouseOver ? Theme.Highlight : Theme.SurfaceBackground;
				var rect = lbl.LocalRect;
				rect.Top += 1;
				Paint.SetBrushAndPen( bgColor );
				Paint.DrawRect( rect );

				if ( Columns[SortIndex] == lbl.Text )
				{
					rect.Top -= 1;
					var icon = SortAscending ? "keyboard_arrow_up" : "keyboard_arrow_down";
					Paint.SetPen( Theme.Text.WithAlpha( 0.4f ) );
					Paint.DrawIcon( rect, icon, 14, TextFlag.RightCenter );
				}
				return false;
			};
			lbl.MouseClick += () =>
			{
				SelectColumn( lbl.Text );
			};
			lbl.MouseRightClick += CreateContextMenu;
			if ( i % 2 == 0 )
			{
				// Only every other needs since they update neighbors and we want no overlap
				lbl.Moved += () => OnColumnResize?.Invoke();
			}
			Labels.Add( lbl );
			Splitter.AddWidget( lbl );
			Splitter.SetStretch( i, 1 );
			Splitter.SetCollapsible( i, false );
			i++;
		}
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.SetBrushAndPen( Theme.SidebarBackground );
		Paint.DrawRect( LocalRect );
	}

	/// <summary>
	/// Set the visibility of a column by name
	/// </summary>
	public void SetColumnVisible( string col, bool visible )
	{
		if ( !Columns.Contains( col ) )
			return;
		if ( visible )
			ShownColumns.Add( col );
		else
			ShownColumns.Remove( col );
		foreach ( var lbl in Labels )
		{
			lbl.Visible = ShownColumns.Contains( lbl.Text );
		}
	}

	/// <summary>
	/// Get the width of a column by name, returns null if not found or hidden
	/// </summary>
	public float GetColumnWidth( string col )
	{
		if ( !ShownColumns.Contains( col ) )
			return -1;

		var lbl = Labels.FirstOrDefault( x => x.Text == col );
		if ( lbl == null )
			return -1;
		return lbl.Width;
	}

	/// <summary>
	/// Checks if a column is currently visible/enabled
	/// </summary>
	public bool IsColumnVisible( string col )
	{
		return ShownColumns.Contains( col );
	}

	private void CreateContextMenu()
	{
		var m = new ContextMenu( this );
		foreach ( var col in Columns )
		{
			var option = m.AddOption( col );
			option.Checkable = true;
			option.Checked = ShownColumns.Contains( col );
			if ( col == "Name" )
			{
				option.Enabled = false;
			}
			else
			{
				// Only add toggle action to enabled columns
				option.Toggled += ( val ) =>
				{
					if ( !val )
					{
						if ( ShownColumns.Count > 1 )
							ShownColumns.Remove( col );
					}
					else
					{
						ShownColumns.Add( col );
					}
					foreach ( var lbl in Labels )
					{
						lbl.Visible = ShownColumns.Contains( lbl.Text );
					}
				};
			}
		}

		m.OpenAtCursor();
	}

	private void SelectColumn( string col )
	{
		var index = Array.IndexOf( Columns, col );
		if ( SortIndex == index )
		{
			SortAscending = !SortAscending;
		}
		else
		{
			SortIndex = index;
			SortAscending = true;
		}
		OnColumnSelect?.Invoke( SelectedColumn, SortAscending );
	}
}
