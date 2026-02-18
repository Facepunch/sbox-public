namespace Editor;

internal class ProjectSettingsHeader : Widget
{
	public Project Project { get; set; }

	public ProjectSettingsHeader( Widget parent, Project project ) : base( parent )
	{
		Project = project;
		MinimumSize = new Vector2( 250, 42 );

		Layout = Layout.Row();
		Layout.Alignment = TextFlag.Center;

		if ( Project.IsPublished && !Project.Config.IsStandaloneOnly )
		{
			var row = Layout.Row();
			row.AddStretchCell();
			row.Add( new Button( $"View Package", "launch" ) { Clicked = () => EditorUtility.OpenFolder( Project.EditUrl ) } );
			row.AddSpacingCell( 16 );

			Layout.Add( row );
		}
	}

	protected override void OnPaint()
	{
		if ( Project is null )
			return;

		Paint.Antialiasing = true;

		Package.TryGetCached( Project.Config.FullIdent, out var package );

		var inner = LocalRect.Shrink( 12, 6 );

		var icon = inner;
		icon.Width = inner.Height;

		//
		// Icon
		//
		if ( package?.Thumb != null )
		{
			Paint.SetPen( Theme.Text );
			Paint.Draw( icon, package?.Thumb, borderRadius: 4 );
		}
		else
		{
			Paint.SetPen( Theme.Text );
			Paint.DrawIcon( inner, "settings", 22, TextFlag.LeftCenter );
		}

		inner.Left = icon.Right + 12;

		Paint.SetHeadingFont( 9, 400 );
		Paint.DrawText( inner, Project.Config.Title, TextFlag.LeftTop );
		Paint.SetDefaultFont( 8, 400 );
		Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
		Paint.DrawText( inner, Project.Config.FullIdent, TextFlag.LeftBottom );
	}
}
