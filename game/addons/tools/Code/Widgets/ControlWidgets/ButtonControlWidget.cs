namespace Editor;

[CustomEditor( typeof( void ), ForMethod = true, WithAllAttributes = new[] { typeof( ButtonAttribute ) } )]
public class ButtonControlWidget : ControlWidget
{
	public override bool IsWideMode => false;
	public override bool IncludeLabel => false;

	public override bool SupportsMultiEdit => true;

	public ButtonControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.AddSpacingCell( 147f );
		Layout.Margin = 2f;

		var props = new List<SerializedProperty>();
		if ( property.IsMultipleValues )
		{
			props = property.MultipleProperties.ToList();
		}
		else
		{
			props.Add( property );
		}

		if ( property.TryGetAttribute<ButtonAttribute>( out var attr ) )
		{
			var name = string.IsNullOrEmpty( attr.Title ) ? property.DisplayName : attr.Title;
			var button = new Button( name, string.IsNullOrEmpty( attr.Icon ) ? null : attr.Icon, this );

			if ( Tint != Color.White )
				button.Tint = Tint;

			button.Clicked += () =>
			{
				var session = SceneEditorSession.Resolve( property.GetContainingGameObject() ) ?? SceneEditorSession.Active;
				using ( session.Scene.Push() )
				{
					foreach ( var prop in props )
					{
						prop.Invoke();
					}
				}
			};
			button.MouseRightClick += () =>
			{
				var m = new Menu();
				m.AddOption( "Jump to code", "code", action: () => CodeEditor.OpenFile( property.SourceFile, property.SourceLine ) );
				m.OpenAtCursor();
			};
			button.ToolTip = property.Description;
			button.FixedHeight = Theme.RowHeight;
			button.HorizontalSizeMode = SizeMode.CanGrow;
			Layout.Add( button );
		}

		Layout.AddStretchCell();
	}

	protected override void OnPaint()
	{
		// nothing
	}

}
