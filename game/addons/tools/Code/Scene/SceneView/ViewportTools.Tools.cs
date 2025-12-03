namespace Editor;

partial class ViewportTools
{
	private void BuildToolbarLeft( Layout layout )
	{
		var button = layout.Add( new ToolsModeButton() );
		button.FixedHeight = Theme.ControlHeight;
	}
}
internal class ToolsModeButton : Button
{
	private IGrouping<string, (TypeDescription Type, EditorToolAttribute Attribute)>[] _toolGroups;

	public ToolsModeButton() : base( null )
	{
		SetStyles( $"padding-left: 32px; padding-right: 32px; font-family: '{Theme.DefaultFont}'; padding-top: 6px; padding-bottom: 6px;" );
		FixedWidth = 210;
		FixedHeight = Theme.RowHeight + 8;

		InitializeToolGroups();
		UpdateButtonText();

		Clicked = Click;
	}

	private void InitializeToolGroups()
	{
		_toolGroups = EditorTypeLibrary.GetTypesWithAttribute<EditorToolAttribute>()
			.GroupBy( x => string.IsNullOrEmpty( x.Type.Group ) ? "Tools" : x.Type.Group )
			.OrderByDescending( x => x.Key )
			.ToArray();
	}

	private void UpdateButtonText()
	{
		foreach ( var group in _toolGroups )
		{
			foreach ( var type in group.OrderBy( x => x.Type.Name ) )
			{
				if ( EditorToolManager.CurrentModeName != type.Type.Name )
					continue;

				Text = type.Type.Title;
				Icon = type.Type.Icon;
				return;
			}
		}
	}

	private void Click()
	{
		var menu = new ContextMenu();

		for ( int i = 0; i < _toolGroups.Length; i++ )
		{
			var group = _toolGroups[i];

			// No visible tools in this group - skip
			if ( !group.Any( x => !x.Type.GetAttribute<EditorToolAttribute>().Hidden ) )
				continue;

			menu.AddHeading( group.Key );

			foreach ( var type in group.OrderBy( x => x.Type.Order ) )
			{
				var attr = type.Type.GetAttribute<EditorToolAttribute>();
				if ( attr.Hidden )
					continue;

				var option = new Option();
				option.Text = type.Type.Title;
				option.ShortcutName = attr.Shortcut;
				option.Icon = type.Type.Icon;
				option.Triggered = () =>
				{
					EditorToolManager.CurrentModeName = type.Type.Name;
					UpdateButtonText();
				};

				menu.AddOption( option );
			}
		}

		menu.OpenAt( ScreenRect.BottomLeft, false );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		UpdateButtonText();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		var fg = Theme.Text;

		Paint.SetDefaultFont();
		Paint.SetPen( fg.WithAlphaMultiplied( Paint.HasMouseOver ? 1.0f : 0.9f ) );
		Paint.DrawIcon( LocalRect.Shrink( 8, 0, 0, 0 ), Icon, 14, TextFlag.LeftCenter );
		Paint.DrawText( LocalRect.Shrink( 32, 0, 0, 0 ), Text, TextFlag.LeftCenter );

		Paint.DrawIcon( LocalRect.Shrink( 4, 0 ), "arrow_drop_down", 18, TextFlag.RightCenter );
	}
}
