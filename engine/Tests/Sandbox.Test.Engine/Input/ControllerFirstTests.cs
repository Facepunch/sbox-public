using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class ControllerFirstTests
{
	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 3 )]
	public void FirstMatchesFirstOrDefaultWithoutAllocating( int count ) => WithControllers( count, () =>
	{
		Assert.AreSame( Controller.All.FirstOrDefault(), Controller.First );

		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ )
			_ = Controller.First;

		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );
	} );

	[TestMethod]
	public void FirstFollowsRemovalsAndReadds() => WithControllers( 3, () =>
	{
		Controller.All.Remove( Controller.All.First() );
		Assert.AreSame( Controller.All.FirstOrDefault(), Controller.First );

		Controller.All.Add( NewController() );
		Assert.AreSame( Controller.All.FirstOrDefault(), Controller.First );

		Controller.All.Clear();
		Assert.IsNull( Controller.First );
	} );

	static Controller NewController() => (Controller)RuntimeHelpers.GetUninitializedObject( typeof( Controller ) );

	static void WithControllers( int count, Action test )
	{
		var saved = Controller.All.ToArray();
		try
		{
			Controller.All.Clear();
			for ( int i = 0; i < count; i++ )
				Controller.All.Add( NewController() );

			test();
		}
		finally
		{
			Controller.All.Clear();
			foreach ( var controller in saved )
				Controller.All.Add( controller );
		}
	}
}
