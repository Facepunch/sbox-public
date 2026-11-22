using System.Linq;

namespace ResourceTests;

[TestClass]
[DoNotParallelize]
public class ResourceGetAllTests
{
	[TestMethod]
	public void GetAllFollowsRegistrationAndReusesTheListUntilItChanges()
	{
		ThreadSafe.MarkMainThread();
		var first = new PrefabFile();
		first.RegisterWeakResourceId( "getalltest_first.prefab" );
		var second = new PrefabFile();
		second.RegisterWeakResourceId( "getalltest_second.prefab" );

		Game.Resources.Register( first );
		try
		{
			var before = ResourceLibrary.GetAll<PrefabFile>();
			Assert.IsTrue( before.Contains( first ) );
			Assert.IsFalse( before.Contains( second ) );
			Assert.AreSame( before, ResourceLibrary.GetAll<PrefabFile>(), "unchanged index built the list again" );

			Game.Resources.Register( second );
			var after = ResourceLibrary.GetAll<PrefabFile>();
			Assert.IsTrue( after.Contains( first ) && after.Contains( second ) );
			Assert.IsFalse( before.Contains( second ), "a list already handed out changed" );

			Game.Resources.Unregister( second );
			Assert.IsFalse( ResourceLibrary.GetAll<PrefabFile>().Contains( second ) );
			Assert.AreEqual( 1, ResourceLibrary.GetAll<PrefabFile>().Count( x => x == first ) );
		}
		finally
		{
			Game.Resources.Unregister( first );
			Game.Resources.Unregister( second );
		}

		Assert.IsFalse( ResourceLibrary.GetAll<PrefabFile>().Contains( first ) );
	}
}
