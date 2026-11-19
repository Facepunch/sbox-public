namespace Sandbox.UI;

/// <summary>
/// A cell - in the next column along, or across <see cref="ColSpan"/> of them, aligned as its column is unless it
/// says otherwise.
/// </summary>
[Library( "td" )]
public class TableCell : Panel
{
	/// <summary>
	/// How many columns it covers, as an HTML cell's <c>colspan</c>.
	/// </summary>
	public int ColSpan { get; set; } = 1;

	/// <summary>
	/// Left, center or right - its column's alignment if not set.
	/// </summary>
	public string Align { get; set; }

	/// <summary>
	/// The first column it's in
	/// </summary>
	internal int Column { get; private set; }

	int laidOutFor = -1;
	int laidOutAt = -1;

	public TableCell()
	{
		ElementName = "td";
	}

	public override void SetProperty( string name, string value )
	{
		switch ( name.ToLowerInvariant() )
		{
			case "colspan": ColSpan = int.TryParse( value, out var span ) ? span : 1; return;
			case "align": Align = value; return;
		}

		base.SetProperty( name, value );
	}

	public override void Tick()
	{
		base.Tick();
		Layout();
	}

	/// <summary>
	/// Its width from the columns it covers - which it works out from the cells before it in its row
	/// </summary>
	void Layout()
	{
		if ( Parent is not TableRow row || row.Table is not { } table ) return;

		var columns = table.ColumnLayout;
		// By index - it's every frame for every cell, and walking Children allocates
		var first = 0;
		for ( var i = 0; i < row.ChildrenCount; i++ )
		{
			var child = row.GetChild( i );
			if ( child == this ) break;
			if ( child is TableCell cell ) first += Math.Max( 1, cell.ColSpan );
		}

		Column = first;

		var key = HashCode.Combine( first, ColSpan, Align );
		if ( laidOutFor == table.LayoutVersion && laidOutAt == key ) return;

		laidOutFor = table.LayoutVersion;
		laidOutAt = key;

		var covered = columns.Skip( first ).Take( Math.Max( 1, ColSpan ) ).ToArray();
		if ( covered.Length == 0 ) return;

		// Fractions share what's left over the fixed ones; a cell over fixed columns is as wide as them all
		var grow = covered.Sum( x => x.Grow );
		var fixedWidth = covered.Where( x => x.Width is { Unit: LengthUnit.Pixels } ).Sum( x => x.Width.Value.Value );
		var percent = covered.Where( x => x.Width is { Unit: LengthUnit.Percentage } ).Sum( x => x.Width.Value.Value );

		// Every sized column gives way when they come to more than the row - dragged wide, or the table narrowed - but
		// none past its minimum (nor a narrow one's own width), so the last of them never falls off the row's end.
		// An auto one is as wide as what's in it.
		var sized = grow > 0 || fixedWidth > 0 || percent > 0;
		var minimum = covered.Sum( x => x.Min );
		if ( grow <= 0 && percent <= 0 ) minimum = Math.Min( minimum, fixedWidth );

		Style.FlexGrow = grow;
		Style.FlexShrink = sized ? 1 : 0;
		Style.FlexBasis = grow > 0 ? Length.Pixels( fixedWidth ) : null;
		Style.MinWidth = sized ? Length.Pixels( minimum ) : null;
		Style.Width = grow > 0 ? null : percent > 0 ? Length.Percent( percent ) : fixedWidth > 0 ? Length.Pixels( fixedWidth ) : null;

		var align = Align ?? covered[0].Align;
		SetClass( "center", align == "center" );
		SetClass( "right", align == "right" );
	}
}

/// <summary>
/// A heading cell. Given a <see cref="Sort"/> key, pressing it sorts its <see cref="Table"/> by it, and it shows which
/// way when it's the one sorted by. In a resizable table, its edge can be dragged.
/// </summary>
[Library( "th" )]
public class TableHeader : TableCell
{
	/// <summary>
	/// What pressing it sorts by - null for a plain heading.
	/// </summary>
	public string Sort { get; set; }

	/// <summary>
	/// Sort highest first when first pressed, as you'd want for a count or a score. <c>sortfirst="descending"</c>.
	/// </summary>
	public bool DescendingFirst { get; set; }

	/// <summary>
	/// Its edge can be dragged to resize its column, in a resizable table.
	/// </summary>
	public bool Resizable { get; set; } = true;

	Label arrow;
	TableResizeHandle handle;

	public TableHeader()
	{
		ElementName = "th";

		AddEventListener( "onclick", () =>
		{
			if ( Sort is not null ) Table.Of( this )?.Sort( this );
		} );
	}

	public override void SetProperty( string name, string value )
	{
		switch ( name.ToLowerInvariant() )
		{
			case "sort": Sort = string.IsNullOrEmpty( value ) ? null : value; return;
			case "sortfirst": DescendingFirst = string.Equals( value, "descending", StringComparison.OrdinalIgnoreCase ); return;
			case "resizable": Resizable = !string.Equals( value, "false", StringComparison.OrdinalIgnoreCase ); return;
		}

		base.SetProperty( name, value );
	}

	public override void Tick()
	{
		base.Tick();

		var table = Table.Of( this );
		var sorted = Sort is not null && table?.SortColumn == Sort;

		SetClass( "sortable", Sort is not null );
		SetClass( "sorted", sorted );

		// Which way, after its text
		if ( sorted && !arrow.IsValid() )
		{
			arrow = AddChild<Label>( "arrow" );
		}
		else if ( !sorted && arrow.IsValid() )
		{
			arrow.Delete();
			arrow = null;
		}

		if ( arrow.IsValid() ) arrow.Text = table.SortDescending ? "▾" : "▴";

		// A handle on its edge, in a resizable table - not after the last column, which has nothing the other side
		var resizable = table is not null && table.Resizable && Resizable && ColSpan == 1 && Column + 1 < table.ColumnLayout.Length;
		SetClass( "resizable", resizable );

		if ( resizable && !handle.IsValid() )
		{
			handle = AddChild<TableResizeHandle>();
		}
		else if ( !resizable && handle.IsValid() )
		{
			handle.Delete();
			handle = null;
		}
	}
}

/// <summary>
/// The edge after a heading - dragged, it moves the edge between its column and the next; double-clicked, puts them
/// back to their widths.
/// </summary>
public class TableResizeHandle : Panel
{
	float startMouse;
	float startLeft;
	float startRight;

	public TableResizeHandle()
	{
		AddClass( "resize-handle" );
	}

	TableHeader Header => Parent as TableHeader;

	Table Table => Table.Of( this );

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		e.StopPropagation();

		if ( Table is not { } table || Header is not { } header ) return;

		startMouse = table.MousePosition.x * table.ScaleFromScreen;
		startLeft = table.ColumnWidth( header.Column );
		startRight = table.ColumnWidth( header.Column + 1 );
		SetClass( "dragging", true );
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( !HasActive || !HasClass( "dragging" ) || Table is not { } table || Header is not { } header ) return;

		table.ResizeColumn( header.Column, startLeft, startRight, table.MousePosition.x * table.ScaleFromScreen - startMouse );
		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( !HasClass( "dragging" ) ) return;

		SetClass( "dragging", false );
		Table?.SaveWidths();
	}

	// Not a press on the heading it's in - that'd sort by it
	protected override void OnClick( MousePanelEvent e )
	{
		e.StopPropagation();
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		e.StopPropagation();

		if ( Header is { } header ) Table?.ResetColumn( header.Column );
	}
}
