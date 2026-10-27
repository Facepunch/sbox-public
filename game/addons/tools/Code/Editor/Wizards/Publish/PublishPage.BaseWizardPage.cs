namespace Editor.Wizards;

internal class PublishWizardPage : BaseWizardPage
{
	/// <summary>
	/// This should replace SharedBag as a way to pass data between pages
	/// </summary>
	public PublishConfig PublishConfig { get; set; }

	public Project Project { get; set; }

	/// <summary>
	/// What's being published, for the words on the page - "game", "map", "model"
	/// </summary>
	protected string TypeName => Sandbox.Services.PackageType.Get( Project.Config.Type )?.Title?.ToLowerInvariant() ?? Project.Config.Type;
}
