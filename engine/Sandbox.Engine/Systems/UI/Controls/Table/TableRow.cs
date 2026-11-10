namespace Sandbox.UI;

/// <summary>
/// A <see cref="Table"/>'s head - its row of headings, staying put over the body.
/// </summary>
[Library( "thead" )]
public class TableHead : Panel
{
	public TableHead()
	{
		ElementName = "thead";
	}
}

/// <summary>
/// A <see cref="Table"/>'s body - its rows, scrolling under the head.
/// </summary>
[Library( "tbody" )]
public class TableBody : Panel
{
	public TableBody()
	{
		ElementName = "tbody";
	}
}

/// <summary>
/// A <see cref="Table"/>'s foot - totals or a summary, staying put under the body.
/// </summary>
[Library( "tfoot" )]
public class TableFoot : Panel
{
	public TableFoot()
	{
		ElementName = "tfoot";
	}
}

/// <summary>
/// A row of cells, on its <see cref="Table"/>'s columns. Takes the <c>selected</c> and <c>disabled</c> classes.
/// </summary>
[Library( "tr" )]
public class TableRow : Panel
{
	public TableRow()
	{
		ElementName = "tr";
	}

	internal Table Table => Ancestors.OfType<Table>().FirstOrDefault();
}
