namespace Editor;

/// <summary>
/// One row per agent showing whether the s&amp;box skill is installed, with install, update and remove buttons.
/// </summary>
internal class AgentSkillRows : Widget
{
	readonly Label message;
	readonly Widget rows;

	public AgentSkillRows( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 8;

		rows = Layout.Add( new Widget( this ) );
		rows.Layout = Layout.Column();
		rows.Layout.Spacing = 8;

		message = Layout.Add( new Label( this ) { WordWrap = true, Color = Theme.TextControl.WithAlpha( 0.6f ) } );

		Refresh();
	}

	void Refresh()
	{
		rows.Layout.Clear( true );

		if ( AgentSkill.Status() is not { } targets )
		{
			rows.Layout.Add( new Label( "Couldn't run sbox-cli. See the console for details." ) { WordWrap = true } );
			return;
		}

		foreach ( var target in targets )
		{
			var row = rows.Layout.AddRow();
			row.Spacing = 8;

			var text = row.Add( new Widget( rows ), 1 );
			text.Layout = Layout.Column();
			text.Layout.Add( new Label( target.Detected ? target.Agents : $"{target.Agents} (not found on this computer)" ) { Color = Theme.Text } );
			text.Layout.Add( new Label( $"{Describe( target.State )} · {target.Path}" ) { WordWrap = true, Color = Theme.TextControl.WithAlpha( 0.5f ) } );

			if ( target.State is "missing" or "outdated" )
			{
				row.Add( new Button.Primary( target.State == "missing" ? "Install" : "Update", "download", rows ) { Clicked = () => Apply( AgentSkill.Install( target.Name ) ) } );
			}

			if ( target.State is "current" or "outdated" )
			{
				row.Add( new Button( "Remove", "delete", rows )
				{
					Clicked = () =>
					{
						// Removing it is a clear answer, so don't offer it again at startup.
						AgentSkill.NeverAsk = true;
						Apply( AgentSkill.Uninstall( target.Name ) );
					}
				} );
			}
		}
	}

	static string Describe( string state ) => state switch
	{
		"current" => "Installed",
		"outdated" => "An older version is installed",
		"foreign" => "Another skill named sbox is installed here, so s&box leaves it alone",
		_ => "Not installed"
	};

	void Apply( AgentSkill.Result result )
	{
		message.Text = result?.Message ?? "Couldn't run sbox-cli. See the console for details.";
		Refresh();
	}
}
