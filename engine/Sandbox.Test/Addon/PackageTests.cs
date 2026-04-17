namespace Addon
{
	[TestClass]
	public class PackageQueryTests
	{
		[TestMethod]
		public async Task PackageFindAsync()
		{
			var result = await Package.FindAsync( "type:game", 200, 0 );

			Assert.IsNotNull( result.Packages );
			Assert.IsTrue( result.Packages.Length > 0 );
		}
	}
}
