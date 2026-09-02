namespace Sandbox.PanelGallery;

/// <summary>
/// A little mock editor tool - title bar, toolbar, a list, a detail pane and a status bar - built
/// entirely out of panels. The same panel runs in an editor window and in a game scene.
/// </summary>
public class ToolWindow : Panel
{
	readonly string Heading;
	readonly List<string> Items;

	Panel ListPanel;
	Panel DetailPanel;
	Sandbox.UI.Label Status;

	string selected;

	public ToolWindow( string heading, IEnumerable<string> items )
	{
		Heading = heading;
		Items = items.ToList();

		StyleSheet.Load( "/styles/gallery.scss" );

		BuildTitleBar();
		BuildToolBar();
		BuildBody();
		BuildStatusBar();

		Select( Items.FirstOrDefault() );
	}

	void BuildTitleBar()
	{
		var bar = Add.Panel( "titlebar" );
		bar.Add.Label( Heading );
		bar.Add.Panel( "spacer" );
		bar.Add.Label( "panel ui" );
	}

	void BuildToolBar()
	{
		var bar = Add.Panel( "toolbar" );

		AddButton( bar, "Add", () => AddItem( $"New Item {Items.Count + 1}" ) );
		AddButton( bar, "Remove", () => RemoveItem( selected ) );
		AddButton( bar, "Reverse", Reverse );
	}

	void BuildBody()
	{
		var body = Add.Panel( "body" );

		ListPanel = body.Add.Panel( "list" );
		DetailPanel = body.Add.Panel( "detail" );

		RebuildList();
	}

	void BuildStatusBar()
	{
		var bar = Add.Panel( "statusbar" );
		Status = bar.Add.Label( "" );
	}

	static void AddButton( Panel parent, string text, Action onClick )
	{
		var button = parent.Add.Label( text, "button" );
		button.AddEventListener( "onclick", onClick );
	}

	void RebuildList()
	{
		ListPanel.DeleteChildren( true );

		foreach ( var item in Items )
		{
			var row = ListPanel.Add.Label( item, "row" );
			row.SetClass( "selected", item == selected );
			row.AddEventListener( "onclick", () => Select( item ) );
		}
	}

	void Select( string item )
	{
		selected = item;

		RebuildList();
		RebuildDetail();
	}

	void RebuildDetail()
	{
		DetailPanel.DeleteChildren( true );

		if ( selected is null )
		{
			DetailPanel.Add.Label( "Nothing selected", "sub" );
			UpdateStatus();
			return;
		}

		DetailPanel.Add.Label( selected, "heading" );
		DetailPanel.Add.Label( $"{Heading} entry", "sub" );

		AddField( "Name", selected );
		AddField( "Index", Items.IndexOf( selected ).ToString() );
		AddField( "Length", $"{selected.Length} characters" );
		AddField( "Uppercase", selected.ToUpperInvariant() );

		UpdateStatus();
	}

	void AddField( string name, string value )
	{
		var field = DetailPanel.Add.Panel( "field" );
		field.Add.Label( name, "name" );
		field.Add.Label( value, "value" );
	}

	void UpdateStatus()
	{
		Status.Text = $"{Items.Count} items - {selected ?? "none"} selected";
	}

	void AddItem( string item )
	{
		Items.Add( item );
		Select( item );
	}

	void RemoveItem( string item )
	{
		if ( item is null )
			return;

		Items.Remove( item );
		Select( Items.FirstOrDefault() );
	}

	void Reverse()
	{
		Items.Reverse();
		RebuildList();
	}
}
