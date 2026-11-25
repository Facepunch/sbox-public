using Borealis.Backend;

namespace BorealisGpuTests;

/// <summary>
/// The resident cull's per-frame state lets go of the views it named: kept in its pool, last frame's assignments held a closed
/// game's views, and so their visibility resources (three views' worth, ~430 MiB) at the menu, where nothing culls.
/// </summary>
[TestClass]
public class ResidentCullStateTests
{
	[TestMethod]
	public void LastFramesAssignmentsLetGoOfTheirViews()
	{
		var culls = new ResidentCullState();
		culls.BeginFrame( 1, null );
		var view = new object();
		var assignment = culls.Add( view, null, 1 );
		Assert.IsNotNull( assignment );
		assignment.Visibility = new BackendDevice.VisibilityView();
		Assert.AreSame( view, assignment.View );

		culls.BeginFrame( 2, null );
		Assert.IsNull( assignment.View, "the view is let go of" );
		Assert.IsNull( assignment.Visibility, "and its visibility" );
		Assert.IsNull( culls.Take( view, null, 2 ), "nor is it found in the new frame" );
	}
}
