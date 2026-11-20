using Sandbox.UI;
namespace Sandbox;

public partial class SpawnlistFooter : Panel
{
	protected override int BuildHash() => HashCode.Combine( CanCreate() );

	bool CanCreate()
	{
		return true;
	}

	void CreateSpawnlist()
	{
		SpawnlistData.PromptCreate( this, onCreated: () => Ancestors.OfType<SpawnlistsPage>().FirstOrDefault()?.RefreshList() );
	}

	void Refresh()
	{
		Ancestors.OfType<SpawnlistsPage>().FirstOrDefault()?.RefreshList();
	}
}

