using System.Collections.Concurrent;
using System.Collections.Generic;
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

	[TestMethod]
	public void GetAllFromManyThreadsAgreesAndSettlesOnOneList()
	{
		ThreadSafe.MarkMainThread();
		var resource = new PrefabFile();
		resource.RegisterWeakResourceId( "getalltest_threads.prefab" );
		var resources = Game.Resources;
		resources.Register( resource );
		try
		{
			for ( int round = 0; round < 20; round++ )
			{
				resources.OnHotload();
				var lists = new ConcurrentBag<IEnumerable<PrefabFile>>();
				Parallel.For( 0, 64, _ => lists.Add( resources.GetAll<PrefabFile>() ) );
				Assert.IsTrue( lists.All( x => x.Count( r => r == resource ) == 1 ) );

				var settled = resources.GetAll<PrefabFile>();
				Assert.AreSame( settled, resources.GetAll<PrefabFile>() );
			}
		}
		finally
		{
			resources.Unregister( resource );
		}
	}
}
