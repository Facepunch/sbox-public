namespace Sandbox.UI;

// A table, written as the web's - lowercase tags, so razor treats them as elements, not a component's fragments:
//
//     <table columns="1fr 200px 80px" align="left left right" sortcolumn=@sortBy sortdescending=@desc onsort=@Sort>
//         <thead>
//             <tr>
//                 <th sort="name">Name</th>
//                 <th sort="map">Map</th>
//                 <th sort="ping" sortfirst="descending">Ping</th>
//             </tr>
//         </thead>
//         <tbody>
//             @foreach ( var x in rows )
//             {
//                 <tr class="@(x == picked ? "selected" : "")" onclick=@( () => picked = x )>
//                     <td>@x.Name</td> ...
//                 </tr>
//             }
//         </tbody>
//     </table>
//
// columns   - widths as a CSS grid's: 1fr, 200px, 25%, auto, minmax( 120px, 1fr ). Every row's cells line up on them.
//             Each includes 6px padding either side of its content.
// align     - left, center or right per column. A cell's own align wins.
// onsort    - a heading with a sort key was pressed; the owner sorts its rows by the TableSort and passes the result
//             back as sortcolumn / sortdescending.
// resizable - "true" lets headings' edges be dragged; cookie="name" keeps the widths. <th resizable="false"> opts out.
// colspan   - on a cell, as the web's.
//
// Rows take the selected and disabled classes. Stripes and column lines are the stylesheet's, as on the web:
// tbody > tr:nth-child( even ) and td + td, th + th { border-left }.

/// <summary>
/// A table, written like an HTML one: <c>table</c>, <c>thead</c>, <c>tbody</c>, <c>tfoot</c>, <c>tr</c>, <c>th</c>
/// and <c>td</c>. Its columns are laid out up front from <see cref="Columns"/>, and every row's cells line up on them.
/// </summary>
[Library( "table" )]
[StyleSheet.Inline( "table", Styles )]
public partial class Table : Panel
{
	/// <summary>
	/// The columns' widths, as a CSS grid's: <c>1fr</c> (a share of what's left), <c>120px</c>, <c>25%</c>,
	/// <c>auto</c> (as wide as its content) or <c>minmax( 120px, 1fr )</c> (with a minimum width).
	/// </summary>
	public string Columns { get; set; }

	/// <summary>
	/// Each column's alignment, in step with <see cref="Columns"/> - left, center or right. A cell's own wins.
	/// </summary>
	public string Align { get; set; }

	/// <summary>
	/// The sort key the rows are in, shown on its heading.
	/// </summary>
	public string SortColumn { get; set; }

	/// <summary>
	/// Whether the rows are sorted highest first.
	/// </summary>
	public bool SortDescending { get; set; }

	/// <summary>
	/// Columns can be dragged wider or narrower by the edges between their headings.
	/// </summary>
	public bool Resizable { get; set; }

	/// <summary>
	/// Keeps the columns' dragged widths for next time, under this name.
	/// </summary>
	public string Cookie
	{
		get => cookie;
		set
		{
			if ( cookie == value ) return;
			cookie = value;
			LoadWidths();
		}
	}

	string cookie;

	/// <summary>
	/// The narrowest a column goes without a <c>minmax</c> saying otherwise.
	/// </summary>
	public const float MinimumWidth = 48;

	public Table()
	{
		ElementName = "table";
	}

	public override void SetProperty( string name, string value )
	{
		switch ( name.ToLowerInvariant() )
		{
			case "columns": Columns = value; return;
			case "align": Align = value; return;
			case "sortcolumn": SortColumn = value; return;
			case "sortdescending": SortDescending = bool.TryParse( value, out var b ) && b; return;
			case "resizable": Resizable = !string.Equals( value, "false", StringComparison.OrdinalIgnoreCase ); return;
			case "cookie": Cookie = value; return;
		}

		base.SetProperty( name, value );
	}

	internal readonly record struct Column( Length? Width, float Grow, string Align, float Min = MinimumWidth );

	string parsedColumns;
	string parsedAlign;
	int parsedResizes = -1;
	Column[] given = [];
	Column[] columns = [];

	/// <summary>
	/// Widths dragged to, by column - over what <see cref="Columns"/> gives them
	/// </summary>
	readonly Dictionary<int, float> resized = new();
	int resizes;

	/// <summary>
	/// The columns, worked out from <see cref="Columns"/> and <see cref="Align"/>, and any dragged to another width -
	/// again only when they change
	/// </summary>
	internal Column[] ColumnLayout
	{
		get
		{
			if ( parsedColumns == Columns && parsedAlign == Align && parsedResizes == resizes ) return columns;

			parsedColumns = Columns;
			parsedAlign = Align;
			parsedResizes = resizes;
			LayoutVersion++;

			var widths = Tokens( Columns );
			var aligns = (Align ?? "").Split( ' ', StringSplitOptions.RemoveEmptyEntries );

			given = widths.Select( ( w, i ) => Parse( w, i < aligns.Length ? aligns[i] : "left" ) ).ToArray();
			columns = given.Select( ( x, i ) => resized.TryGetValue( i, out var w ) ? x with { Width = Length.Pixels( Math.Max( w, x.Min ) ), Grow = 0 } : x ).ToArray();
			return columns;
		}
	}

	/// <summary>
	/// Goes up when the columns change, so cells lay themselves out again
	/// </summary>
	internal int LayoutVersion { get; private set; }

	/// <summary>
	/// The edge between a column and the next dragged along by <paramref name="delta"/> from where it was, the two of
	/// them <paramref name="left"/> and <paramref name="right"/> wide then. A <c>1fr</c> column either side takes up
	/// or gives up the room by itself; between two fixed ones, one grows as the other shrinks.
	/// </summary>
	internal void ResizeColumn( int column, float left, float right, float delta )
	{
		var layout = ColumnLayout;
		if ( column < 0 || column + 1 >= layout.Length ) return;

		// Neither past its minimum
		var shrinkLeft = given[column].Min - left;
		delta = Math.Clamp( delta, shrinkLeft, Math.Max( shrinkLeft, right - given[column + 1].Min ) );

		var leftShares = given[column].Grow > 0;
		var rightShares = given[column + 1].Grow > 0;

		if ( !leftShares || rightShares ) resized[column] = left + delta;
		if ( !rightShares ) resized[column + 1] = right - delta;

		resizes++;
	}

	/// <summary>
	/// The two columns either side of an edge back to the widths <see cref="Columns"/> gives them
	/// </summary>
	internal void ResetColumn( int column )
	{
		resized.Remove( column );
		resized.Remove( column + 1 );
		resizes++;
		SaveWidths();
	}

	/// <summary>
	/// How wide a column is now, from its heading - for a drag to start from
	/// </summary>
	internal float ColumnWidth( int column )
	{
		var cell = Descendants.OfType<TableHeader>().FirstOrDefault( x => x.Column == column && x.ColSpan == 1 );
		return cell is null ? 0 : cell.Box.Rect.Width * ScaleFromScreen;
	}

	internal void SaveWidths()
	{
		if ( string.IsNullOrEmpty( cookie ) ) return;

		Game.Cookies?.SetString( $"table.{cookie}", string.Join( ",", resized.Select( x => $"{x.Key}:{x.Value.ToString( System.Globalization.CultureInfo.InvariantCulture )}" ) ) );
	}

	void LoadWidths()
	{
		resized.Clear();
		resizes++;

		if ( string.IsNullOrEmpty( cookie ) ) return;

		foreach ( var entry in (Game.Cookies?.GetString( $"table.{cookie}" ) ?? "").Split( ',', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var parts = entry.Split( ':' );
			if ( parts.Length == 2 && int.TryParse( parts[0], out var column ) && float.TryParse( parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var width ) )
				resized[column] = width;
		}
	}

	static Column Parse( string width, string align )
	{
		static float Number( string text ) => float.TryParse( text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n ) ? n : 0;

		// minmax( 120px, 1fr ) - the second as any other, at least the first
		if ( width.StartsWith( "minmax(" ) && width.EndsWith( ")" ) && width[7..^1].Split( ',' ) is [var min, var size] )
		{
			var minimum = min.Trim();
			return Parse( size.Trim(), align ) with { Min = Number( minimum.EndsWith( "px" ) ? minimum[..^2] : minimum ) };
		}

		if ( width.EndsWith( "fr" ) ) return new( null, Number( width[..^2] ), align );
		if ( width.EndsWith( "%" ) ) return new( Length.Percent( Number( width[..^1] ) ), 0, align );
		if ( width.EndsWith( "px" ) ) return new( Length.Pixels( Number( width[..^2] ) ), 0, align );

		// auto - as wide as what's in it
		return new( null, 0, align );
	}

	/// <summary>
	/// The columns' widths, split on the spaces between them - not those inside a <c>minmax( ... )</c>
	/// </summary>
	static List<string> Tokens( string text )
	{
		var tokens = new List<string>();
		var current = new System.Text.StringBuilder();
		var depth = 0;

		foreach ( var c in text ?? "" )
		{
			if ( c == '(' ) depth++;
			if ( c == ')' ) depth--;

			if ( c == ' ' && depth == 0 )
			{
				if ( current.Length > 0 ) tokens.Add( current.ToString() );
				current.Clear();
				continue;
			}

			if ( c != ' ' ) current.Append( c );
		}

		if ( current.Length > 0 ) tokens.Add( current.ToString() );
		return tokens;
	}

	public override void Tick()
	{
		base.Tick();
		AlignEnds();
	}

	/// <summary>
	/// The head's and foot's rows in at each side as far as the body's - past its scrollbar, whatever that takes up -
	/// so their cells share out the same width and sit over the rows' columns
	/// </summary>
	void AlignEnds()
	{
		var row = Children.OfType<TableBody>().FirstOrDefault()?.Children.OfType<TableRow>().FirstOrDefault();
		if ( row is null || row.Box.Rect.Width <= 0 ) return;

		foreach ( var end in Children.Where( x => x is TableHead or TableFoot ) )
		{
			foreach ( var tr in end.Children.OfType<TableRow>() )
			{
				var left = (row.Box.RectInner.Left - tr.Box.Rect.Left) * ScaleFromScreen;
				var right = (tr.Box.Rect.Right - row.Box.RectInner.Right) * ScaleFromScreen;

				// Only when it's moved - setting it lays the table out again
				if ( MathF.Abs( (tr.Style.PaddingLeft?.Value ?? -1) - left ) > 0.1f ) tr.Style.PaddingLeft = Length.Pixels( left );
				if ( MathF.Abs( (tr.Style.PaddingRight?.Value ?? -1) - right ) > 0.1f ) tr.Style.PaddingRight = Length.Pixels( right );
			}
		}
	}

	/// <summary>
	/// A heading pressed - the same one again turns it round, another starts its own way
	/// </summary>
	internal void Sort( TableHeader header )
	{
		var descending = header.Sort == SortColumn ? !SortDescending : header.DescendingFirst;
		CreateEvent( "onsort", new TableSort( header.Sort, descending ) );
	}
}

/// <summary>
/// What a table's <c>onsort</c> event carries - the pressed heading's sort key, and whether to sort highest first.
/// </summary>
public readonly record struct TableSort( string Column, bool Descending );
