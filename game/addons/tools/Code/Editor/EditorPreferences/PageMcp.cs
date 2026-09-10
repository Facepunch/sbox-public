using Editor.Mcp;

namespace Editor.Preferences;

internal class PageMcp : Widget
{
	Label statusLabel;
	Button copyUrl;
	Button copyCommand;

	public PageMcp( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 32;

		{
			Layout.Add( new Label.Subtitle( "MCP Server" ) );

			Layout.Add( new InformationBox( "<p>The editor can serve the Model Context Protocol, which lets AI agents " +
				"like Claude Code see and drive the project you have open. The server only accepts connections from this computer.</p>" ) );

			Layout.AddSpacingCell( 8 );

			{
				var sheet = new ControlSheet();

				sheet.AddProperty( () => EditorPreferences.McpServerEnabled );
				sheet.AddProperty( () => EditorPreferences.McpServerPort );

				Layout.Add( sheet );
			}

			Layout.AddSpacingCell( 16 );

			statusLabel = new Label( this );
			Layout.Add( statusLabel );

			Layout.AddSpacingCell( 8 );

			{
				var row = Layout.AddRow();
				row.Spacing = 4;

				copyUrl = new Button( "Copy Url", "content_copy" )
				{
					Clicked = () => EditorUtility.Clipboard.Copy( McpServer.Url )
				};

				copyCommand = new Button( "Copy Claude Code Command", "content_copy" )
				{
					Clicked = () => EditorUtility.Clipboard.Copy( $"claude mcp add --transport http sbox {McpServer.Url}" )
				};

				row.Add( copyUrl );
				row.Add( copyCommand );
				row.AddStretchCell();
			}

			Layout.AddStretchCell();

			UpdateStatus();
		}
	}

	[EditorEvent.Frame]
	public void UpdateStatus()
	{
		var text = McpServer.IsRunning ? $"🟢 Running at {McpServer.Url}" : "🔴 Not running";

		if ( statusLabel.Text != text )
		{
			statusLabel.Text = text;
		}

		copyUrl.Enabled = McpServer.IsRunning;
		copyCommand.Enabled = McpServer.IsRunning;
	}
}
