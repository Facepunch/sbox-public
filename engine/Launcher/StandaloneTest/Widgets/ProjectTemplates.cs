using Sandbox.DataModel;

namespace Editor;

internal class ProjectTemplates : Widget
{
	public ProjectTemplatesListView ListView { get; set; }

	public ProjectTemplates( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 8;

		ListView = new ProjectTemplatesListView( this );
		ListView.SetSizeMode( SizeMode.Default, SizeMode.Default );
		ListView.Layout = Layout.Row();

		Layout.Add( ListView, 1 );
	}
}

internal class ProjectTemplatesListView : ListView
{
	/// <summary>
	/// The current template, used by the addon creator.
	/// </summary>
	public ProjectTemplate ChosenTemplate { get; set; }

	/// <summary>
	/// Relative to the game directory.
	/// </summary>
	const string TemplatesDirectory = "/templates";

	List<ProjectTemplate> Templates = new();

	public ProjectTemplatesListView( Widget parent ) : base( parent )
	{
		ItemSelected = OnItemClicked;
		ItemSize = new Vector2( 0, 48 );
		ItemSpacing = 4;

		FindLocalTemplates();

		var orderedTemplates = Templates.OrderBy( x => x.Order ).ToList();
		SetItems( orderedTemplates );

		ChosenTemplate = orderedTemplates.FirstOrDefault();

		if ( ChosenTemplate is not null )
		{
			SelectItem( ChosenTemplate );
		}
	}

	public void OnItemClicked( object value )
	{
		if ( value is ProjectTemplate pt )
		{
			ChosenTemplate = pt;
		}
	}

	protected void FindLocalTemplates()
	{
		if ( !FileSystem.Root.DirectoryExists( TemplatesDirectory ) )
			return;

		var directories = FileSystem.Root.FindDirectory( TemplatesDirectory );

		foreach ( var directory in directories )
		{
			var templateRoot = $"{TemplatesDirectory}/{directory}";
			var addonPath = $"{templateRoot}/$ident.sbproj";

			if ( !FileSystem.Root.FileExists( addonPath ) )
				continue;

			var addon = Json.Deserialize<ProjectConfig>( FileSystem.Root.ReadAllText( addonPath ) );
			if ( addon == null )
				continue;

			if ( addon.Type == "library" )
				continue;

			Templates.Add( new ProjectTemplate( addon, templateRoot ) );
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );

		// Draws the list items
		base.OnPaint();
	}

	protected override void PaintItem( VirtualWidget v )
	{
		var item = v.Object;
		var rect = v.Rect;

		if ( item is not ProjectTemplate template )
			return;

		var r = rect;
		var fg = Theme.Text;

		if ( Paint.HasSelected )
			fg = Theme.Text;

		Paint.Antialiasing = true;
		Paint.ClearPen();

		Paint.SetBrush( Theme.ButtonBackground.WithAlpha( 0.1f ) );

		if ( Paint.HasSelected )
			Paint.SetBrush( Theme.SelectedBackground );

		Paint.DrawRect( r, 4.0f );

		if ( Paint.HasMouseOver )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Text.WithAlpha( 0.05f ) );
			Paint.DrawRect( r, 4.0f );
		}

		Paint.Antialiasing = false;

		r = r.Shrink( 8.0f );
		Paint.SetPen( fg.WithAlpha( 0.7f ) );

		var iconRect = r.Align( rect.Height - 16.0f, TextFlag.LeftCenter );
		Paint.DrawIcon( iconRect, template.Icon, 24.0f );

		Paint.SetDefaultFont();
		Paint.SetPen( fg );
		r = r.Shrink( rect.Height - 8.0f, 0 );

		var x = Paint.DrawText( r, template.Title, TextFlag.LeftTop );
		r.Top += x.Height + 4.0f;

		// Middle bit
		{
			if ( Paint.HasSelected )
				Paint.SetPen( Theme.Text.WithAlpha( 1.0f ) );
			else
				Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );

			r.Right = rect.Width;

			x = Paint.DrawText( r, template.Description, TextFlag.LeftTop );
			r.Left = x.Right + 4;
		}
	}
}
