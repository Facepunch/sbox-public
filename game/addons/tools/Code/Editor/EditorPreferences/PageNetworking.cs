namespace Editor.Preferences;

internal class PageNetworking : Widget
{
	public PageNetworking( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 32;

		{
			Layout.Add( new Label.Subtitle( "Networking" ) );

			Layout.Add( new InformationBox( "<p>These are options related to opening a new game instance (for testing networking in the editor)</p>" ) );

			Layout.AddSpacingCell( 8 );

			Layout.Add( new Label( "Game Instance" ) );
			{
				var sheet = new ControlSheet();

				sheet.AddProperty( () => EditorPreferences.WindowedLocalInstances );
				sheet.AddProperty( () => EditorPreferences.NewInstanceCommandLineArgs );
				Layout.Add( sheet );
			}

			Layout.Add( new Label( "In-Process Clients" ) );
			{
				var sheet = new ControlSheet();

				sheet.AddProperty( () => EditorPreferences.InProcessClients );
				Layout.Add( sheet );
			}

			Layout.Add( new Label( "Dedicated Server" ) );
			{
				var sheet = new ControlSheet();

				sheet.AddProperty( () => EditorPreferences.DedicatedServerCommandLineArgs );
				Layout.Add( sheet );
			}


			Layout.AddStretchCell();
		}
	}
}
