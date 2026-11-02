namespace Editor.AnimGraph;

internal sealed class AnimationInspectorSection : InspectorHeader
{
	private readonly Widget _body;
	private readonly string _cookie;

	public AnimationInspectorSection( string title, string icon, Widget body, string cookie )
	{
		_body = body;
		_cookie = cookie;
		Title = title;
		Icon = icon;
		Color = Theme.Blue;
		IsExpanded = EditorCookie.Get( _cookie, true );
		BuildUI();
		_body.Visible = IsExpanded;
	}

	protected override void BuildRightIcons( Layout layout ) { }

	protected override void OnExpandChanged()
	{
		using var suspend = SuspendUpdates.For( Parent );
		_body.Visible = IsExpanded;
		UpdateGeometry();
		Parent?.UpdateGeometry();
		EditorCookie.Set( _cookie, IsExpanded );
	}
}
