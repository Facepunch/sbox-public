using System;

namespace Services;

[TestClass]
public class PackageTypeTest
{
	[TestMethod]
	public async Task Load()
	{
		await Sandbox.Services.PackageType.LoadAsync();

		var all = Sandbox.Services.PackageType.All;

		Assert.IsNotNull( all );
		Assert.IsTrue( all.Count > 0 );

		foreach ( var t in all )
		{
			Console.WriteLine( $"{t.Name} - {t.Title} - {t.Count}/{t.TotalCount}" );
			Assert.IsFalse( string.IsNullOrEmpty( t.Name ) );
		}
	}

	[TestMethod]
	public async Task GetByName()
	{
		await Sandbox.Services.PackageType.LoadAsync();

		var first = Sandbox.Services.PackageType.All.FirstOrDefault();
		Assert.IsNotNull( first );

		var fetched = Sandbox.Services.PackageType.Get( first.Name );
		Assert.AreSame( first, fetched );
	}

	[TestMethod]
	public async Task CaseInsensitive()
	{
		await Sandbox.Services.PackageType.LoadAsync();

		var first = Sandbox.Services.PackageType.All.FirstOrDefault();
		Assert.IsNotNull( first );

		var upper = Sandbox.Services.PackageType.Get( first.Name.ToUpperInvariant() );
		Assert.AreSame( first, upper );
	}

	[TestMethod]
	public async Task NotFound()
	{
		await Sandbox.Services.PackageType.LoadAsync();

		var t = Sandbox.Services.PackageType.Get( "this-type-definitely-does-not-exist-xyz123" );
		Assert.IsNull( t );
	}

	[TestMethod]
	[DataRow( null )]
	[DataRow( "" )]
	public void EmptyName( string name )
	{
		var t = Sandbox.Services.PackageType.Get( name );
		Assert.IsNull( t );
	}
}
