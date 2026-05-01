using Editor.Wizards;
using Editor.Utility;

namespace Editor;

file class ProjectTitleButton : Widget
{
	Project Project { get; init; }

	private const int HorizontalPadding = 8;
	private const int LogoSize = 32;
	private const int LogoShrink = 4;
	private const int LogoTextSpacing = 8;

	Pixmap _placeholderIcon;

	public ProjectTitleButton( Project project )
	{
		Project = project;
		FixedHeight = 32;

		Layout = Layout.Row();
		Layout.Margin = new( 8, 0 );
		Layout.Spacing = 0;
		Layout.Alignment = TextFlag.LeftCenter;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrush( Theme.ButtonBackground.Lighten( 0.25f ) );
			Paint.DrawRect( LocalRect );
		}

		var contentRect = LocalRect.Shrink( HorizontalPadding, 0 );
		var package = Project.Package;

		// Calculate logo rect on the left side
		var logoRect = contentRect;
		logoRect.Width = contentRect.Height;
		logoRect = logoRect.Shrink( LogoShrink );

		// Draw project logo
		if ( package?.Thumb != null )
		{
			Paint.SetPen( Color.White );
			Paint.Draw( logoRect, package.Thumb, borderRadius: 4 );
		}
		else
		{
			_placeholderIcon ??= PlaceholderIcon.Generate( Project.Config.Title, 64 );

			Paint.BilinearFiltering = true;
			Paint.SetBrush( Color.White );
			Paint.Draw( logoRect, _placeholderIcon, borderRadius: 4 );
			Paint.BilinearFiltering = false;
		}

		// Adjust text area to not overlap with logo
		var textRect = contentRect;
		textRect.Left = logoRect.Right + LogoTextSpacing;

		Paint.ClearBrush();
		Paint.SetPen( Theme.Text );
		Paint.DrawText( textRect, Project.Config.Title, TextFlag.LeftCenter );
	}

	protected override Vector2 SizeHint() => CalculateSize();

	private Vector2 CalculateSize()
	{
		Paint.SetDefaultFont();
		var textSize = Paint.MeasureText( new Rect( 0, 0, 256, 32 ), Project.Config.Title, TextFlag.LeftCenter );
		var width = HorizontalPadding + LogoSize + LogoTextSpacing + textSize.Width + HorizontalPadding;

		return new Vector2( width, 32 );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		var menu = new Menu( this );
		menu.AddOption( $"Open in {CodeEditor.Title}", "integration_instructions", CodeEditor.OpenSolution );
		menu.AddOption( "Open in Explorer", "folder", () => EditorUtility.OpenFolder( Project.GetRootPath() ) );
		menu.AddSeparator();
		menu.AddOption( "Publish..", "backup", () => PublishWizard.Open( Project ) );
		menu.AddOption( "Export..", "save_alt", () => StandaloneWizard.Open( Project ) );

		menu.OpenNextTo( this, WidgetAnchor.BottomEnd );
	}

	[Event( "editor.titlebar.buttons.build" )]
	public static void OnBuildTitleBarButtons( TitleBarButtons titleBarButtons )
	{
		titleBarButtons.Add( new ProjectTitleButton( Project.Current ) );
		titleBarButtons.AddButton( "settings", OpenProjectSettings );
	}

	private static void OpenProjectSettings()
	{
		ProjectSettingsWindow.OpenForProject( Project.Current );
	}
}
