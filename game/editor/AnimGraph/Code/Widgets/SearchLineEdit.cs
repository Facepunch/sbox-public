namespace Editor.AnimGraph;

internal sealed class SearchLineEdit : LineEdit
{
	public Action<KeyEvent> KeyPressed { get; set; }

	public SearchLineEdit( Widget parent ) : base( parent ) { }

	protected override void OnKeyPress( KeyEvent e )
	{
		// Let the results list handle navigation keys before the text input does.
		KeyPressed?.Invoke( e );

		if ( !e.Accepted )
			base.OnKeyPress( e );
	}
}
