namespace Sandbox;

/// <summary>
/// Used for sbox-dev editor
/// </summary>
public class EditorAppSystem : AppSystem
{
	const ulong EditorAppId = 2129370;

	public override void Init()
	{
		Application.AppId = EditorAppId;

		LoadSteamDll();

		base.Init();

		// Error as early as possible if invalid project
		if ( !CheckProject() )
			return;

		CreateMenu();
		CreateGame();
		CreateEditor();

		var createInfo = new AppSystemCreateInfo()
		{
			WindowTitle = "s&box editor",
			Flags = AppSystemFlags.IsGameApp | AppSystemFlags.IsEditor
		};

		if ( Utility.CommandLine.HasSwitch( "-test" ) )
			createInfo.Flags |= AppSystemFlags.IsUnitTest;

		InitGame( createInfo );
	}


	/// <summary>
	/// Checks if a valid -project parameter was passed
	/// </summary>
	protected bool CheckProject()
	{
		// -test special case
		if ( !Utility.CommandLine.HasSwitch( "-project" ) && Utility.CommandLine.HasSwitch( "-test" ) )
			return true;

		var path = Utility.CommandLine.GetSwitch( "-project", "" ).TrimQuoted();

		Project project = new() { ConfigFilePath = path };
		if ( project.LoadMinimal() )
			return true;

		NativeEngine.EngineGlobal.Plat_MessageBox( "Couldn't open project", $"Couldn't open project file: {path}" );
		return false;
	}
}
