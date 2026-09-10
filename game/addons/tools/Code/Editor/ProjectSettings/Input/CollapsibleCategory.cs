namespace Editor;

internal partial class CollapsibleCategory : Widget
{
	CollapsibleHeader Header { get; set; }

	internal Widget Container;

	string _stateCookieName;
	internal string StateCookieName
	{
		get => _stateCookieName;
		set
		{
			if ( _stateCookieName == value ) return;
			_stateCookieName = value;

			var state = EditorCookie.Get( _stateCookieName, Header.IsExpanded );
			SetState( state );
		}
	}

	public void SetState( bool s )
	{
		Header.IsExpanded = s;

		using var su = SuspendUpdates.For( this );

		if ( !s ) Container.Hide();
		else Container.Show();

		if ( !string.IsNullOrEmpty( StateCookieName ) )
		{
			EditorCookie.Set( StateCookieName, s );
		}
	}

	internal Color Color
	{
		set
		{
			Header.Color = value;
		}
	}

	internal void Refresh()
	{
		if ( !Header.IsExpanded ) Container.Hide();
		else Container.Show();
	}

	public CollapsibleCategory( Widget parent = null, string categoryName = "My Category", string icon = null ) : base( parent )
	{
		Layout = Layout.Column();

		Header = Layout.Add( new CollapsibleHeader( this ) );
		Header.Icon = icon;
		Header.Title = categoryName;
		Header.Color = Theme.Blue;
		Header.IsCollapsable = true;
		Header.IsExpanded = true;
		Header.BuildUI();

		Container = new Widget( null );
		Container.Layout = Layout.Column();
		Container.Layout.Margin = new( 4, 4 );

		Layout.Add( Container );

		Refresh();
	}

	private class CollapsibleHeader : InspectorHeader
	{
		private CollapsibleCategory Owner { get; set; }

		public CollapsibleHeader( CollapsibleCategory owner )
		{
			Owner = owner;
		}

		protected override void BuildRightIcons( Layout layout ) { }

		protected override void OnExpandChanged()
		{
			Owner?.SetState( IsExpanded );
		}
	}
}

