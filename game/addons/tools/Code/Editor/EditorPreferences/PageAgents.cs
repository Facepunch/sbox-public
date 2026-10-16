namespace Editor.Preferences;

/// <summary>
/// Install or remove the s&amp;box skill for the coding agents on this computer.
/// </summary>
internal class PageAgents : Widget
{
	public PageAgents( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 32;
		Layout.Spacing = 8;

		Layout.Add( new Label.Subtitle( "AI Agents" ) );

		Layout.Add( new InformationBox( "<p>The s&amp;box skill teaches coding agents like Claude Code and Codex to work with s&amp;box projects and drive the editor.</p>" +
			"<p>It's installed for your user, not into your projects, and reads its instructions from this s&amp;box installation, so it stays current as s&amp;box updates.</p>" ) );

		Layout.Add( new AgentSkillRows( this ) );

		Layout.AddStretchCell();
	}
}
