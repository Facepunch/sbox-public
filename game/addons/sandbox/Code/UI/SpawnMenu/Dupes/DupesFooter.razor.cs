using Sandbox.UI;
namespace Sandbox;

public partial class DupesFooter : Panel
{
	protected override int BuildHash() => HashCode.Combine( CanSaveDupe() );

	bool CanSaveDupe()
	{
		var mode = Player.FindLocalToolMode<DuplicatorTool>();
		if ( mode is null ) return false;

		// toolgun isn't out, not in duplicator mode
		if ( !mode.Active ) return false;

		if ( string.IsNullOrWhiteSpace( mode.CopiedJson ) ) return false;

		return true;
	}

	void MakeSave()
	{
		var mode = Player.FindLocalToolMode<DuplicatorTool>();
		if ( mode is null ) return;

		mode.Save();
	}
}

