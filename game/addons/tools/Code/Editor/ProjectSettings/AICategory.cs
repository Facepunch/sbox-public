using Sandbox.DataModel;

namespace Editor.ProjectSettingPages;

/// <summary>
/// Shared project preferences for AI tooling.
/// </summary>
[Title( "AI" ), Icon( "smart_toy" )]
internal sealed class AICategory : ProjectSettingsWindow.Category
{
	ProjectAISettings settings;

	/// <summary>
	/// Edit a copy so preferences are only applied when saved.
	/// </summary>
	public override void OnInit( Project project )
	{
		settings = new ProjectAISettings
		{
			EnableMcpCommunication = project.Config.AI?.EnableMcpCommunication ?? true
		};

		var so = settings.GetSerialized();
		ListenForChanges( so );

		var sheet = new ControlSheet();
		sheet.AddObject( so );
		BodyLayout.Add( sheet );
	}

	/// <summary>
	/// Save the project preferences without sharing the editable copy.
	/// </summary>
	public override void OnSave()
	{
		Project.Config.AI = new ProjectAISettings
		{
			EnableMcpCommunication = settings.EnableMcpCommunication
		};

		base.OnSave();
	}
}
