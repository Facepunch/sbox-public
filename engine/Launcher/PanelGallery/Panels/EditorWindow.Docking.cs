namespace Sandbox.PanelGallery;

public partial class EditorWindow
{
	DockHost dockHost;
	PanelDockWindows dockWindows;
	string defaultLayout;
	string savedLayout;
	readonly List<RootPanel> floatingRoots = new();

	void ConfigureFloatingWindow( RootPanel root )
	{
		root.AddClass( "mock-editor-surface" );
		root.AddClass( "editor-window" );
		root.StyleSheet.Load( "/styles/gallery.scss" );
		floatingRoots.Add( root );
		ApplyFloatingTheme( root );

		// A floated tool no longer bubbles keys through EditorWindow.
		var host = root.Children.OfType<DockHost>().Single();
		var shortcuts = root.AddChild( new FloatingShortcuts( this ) );
		host.Parent = shortcuts;
	}

	void ApplyFloatingTheme( RootPanel root )
	{
		root.SetClass( LightModeClass, lightMode );
		root.SetClass( "style-light", lightMode );
	}

	void ShowDock( string id )
	{
		var host = dockHost.Find( id ) is not null ? dockHost : floatingRoots
			.Where( x => x.IsValid )
			.SelectMany( x => x.Descendants.OfType<DockHost>() )
			.FirstOrDefault( x => x.Find( id ) is not null );
		if ( host is null ) return;
		if ( !host.IsOpen( id ) ) host.Dock( id );
		host.Activate( id );
		PanelWindow.FromPanel( host )?.Focus();
	}

	sealed class FloatingShortcuts : Panel
	{
		readonly EditorWindow editor;

		internal FloatingShortcuts( EditorWindow editor )
		{
			this.editor = editor;
			Style.Width = Length.Percent( 100 );
			Style.Height = Length.Percent( 100 );
		}

		public override void OnButtonEvent( ButtonEvent e )
		{
			if ( e.Pressed && editor.IsValid && editor.HandleShortcut( e ) )
			{
				e.StopPropagation = true;
				return;
			}
			base.OnButtonEvent( e );
		}
	}

	public override void OnDeleted()
	{
		dockWindows?.Dispose();
		floatingRoots.Clear();
		base.OnDeleted();
	}
}
