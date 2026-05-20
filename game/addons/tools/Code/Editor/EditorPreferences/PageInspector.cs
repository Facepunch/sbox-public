namespace Editor.Preferences;

internal class PageInspector : Widget
{
	public PageInspector( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 32;

		Layout.Add( new Label.Subtitle( "Inspector" ) );

		var sheet = new ControlSheet();

		sheet.AddProperty( () => EditorPreferences.SelectFullValuesOnFocus );

		Layout.Add( sheet );
		Layout.AddStretchCell();
	}
}
