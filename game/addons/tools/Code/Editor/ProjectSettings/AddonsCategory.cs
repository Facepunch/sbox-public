namespace Editor.ProjectSettingPages;

/// <summary>
/// Workshop addons made for this game - whether players can load them, and the kinds there are
/// </summary>
[Title( "Addons" ), Icon( "extension" )]
internal sealed class AddonsCategory : ProjectSettingsWindow.Category
{
	/// <summary>
	/// Players can load Workshop addons made for this game. Turning this off hides the Addons menu and skips loading them.
	/// </summary>
	public bool AllowAddons { get; set; } = true;

	/// <summary>
	/// The kinds of addon this game has, like Weapons or Tools. Addons pick theirs when they're published, and players
	/// filter by them in the Addons menu.
	/// </summary>
	public List<string> AddonTypes { get; set; }

	public override void OnInit( Project project )
	{
		AllowAddons = Project.Config.GetMetaOrDefault( "AllowAddons", true );
		AddonTypes = Project.Config.GetMetaOrDefault( ProjectPublisher.AddonTypesMetaKey, new List<string>() );

		var so = this.GetSerialized();
		var sheet = new ControlSheet();
		sheet.AddRow( so.GetProperty( nameof( AllowAddons ) ) );
		sheet.AddRow( so.GetProperty( nameof( AddonTypes ) ) );

		BodyLayout.Add( sheet );
		ListenForChanges( so );
	}

	public override void OnSave()
	{
		Project.Config.SetMeta( "AllowAddons", AllowAddons ? null : false );

		var types = AddonTypes?.Select( x => x?.Trim() ).Where( x => !string.IsNullOrEmpty( x ) ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
		Project.Config.SetMeta( ProjectPublisher.AddonTypesMetaKey, types is { Length: > 0 } ? types : null );

		base.OnSave();
	}
}
