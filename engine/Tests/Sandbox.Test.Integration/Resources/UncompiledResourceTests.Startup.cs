using Sandbox.Tasks;

namespace ResourceTests;

public partial class UncompiledResourceTests
{
	/// <summary>
	/// Startup scans honor runtime extensions and filters, isolate failures, and do not repeat successful loads.
	/// </summary>
	[TestMethod]
	public void StartupLoadsDeclaredSourceTypes()
	{
		files.WriteAllText( "a.rawtest", "fail" );
		files.WriteAllText( "b.rawtest", "loaded" );
		files.WriteAllText( "ignored.rawtest_c", "compiled counterpart" );
		AsyncPump.RunSynchronously( () => ResourceLoader.LoadAllGameResourceAsync( files, includeExtension: extension => extension != ".rawtest" ) );
		Assert.IsNull( ResourceLibrary.Get<TestSource>( "b.rawtest" ) );

		AsyncPump.RunSynchronously( () => ResourceLoader.LoadAllGameResourceAsync( files ) );
		var resource = ResourceLibrary.Get<TestSource>( "b.rawtest" );
		Assert.IsNotNull( resource );
		Assert.IsNull( ResourceLibrary.Get<TestSource>( "a.rawtest" ) );
		Assert.IsNull( ResourceLibrary.Get<TestSource>( "ignored.rawtest" ) );

		files.WriteAllText( "a.rawtest", "repaired" );
		ResourceLoader.LoadAllGameResource( files );
		Assert.AreEqual( "repaired", ResourceLibrary.Get<TestSource>( "a.rawtest" ).Text );
		Assert.AreEqual( 1, resource.Loads );
	}
}
