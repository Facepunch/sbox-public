namespace Editor.DooEditor;

public class DooVariableControlWidget : StringControlWidget
{
	public DooVariableControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Margin = new Margin( 2, 0, 0, 0 );
		Layout.Add( new IconButton( "electric_bolt" ) { OnClick = OpenMenu, ToolTip = "Variable", Background = Theme.Green.WithAlpha( 0.2f ), Foreground = Theme.Green, IconSize = 15, FixedSize = Theme.RowHeight - 4 } );
		Layout.Add( LineEdit, 1 );
	}

	protected override void DoLayout()
	{
		// nothing
	}

	void OpenMenu()
	{
		var editor = GetAncestor<DooEditorWidget>();

		var menu = new ContextMenu( this );

		if ( editor.ArgumentHints != null )
		{
			foreach ( var arg in editor.ArgumentHints )
			{
				menu.AddOption( $"{arg.Name} ({arg.Hint.Name})", "", () => { SerializedProperty.SetValue( arg.Name ); } );
			}
		}

		foreach ( var arg in editor.GetArguments() )
		{
			if ( editor.ArgumentHints?.Any( x => x.Name == arg ) == true )
				continue;

			menu.AddOption( $"{arg}", "", () => { SerializedProperty.SetValue( arg ); } );
		}

		menu.OpenNextTo( this, WidgetAnchor.BottomStart );
	}
}
