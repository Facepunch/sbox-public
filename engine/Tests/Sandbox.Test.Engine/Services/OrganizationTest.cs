using System;

namespace ServicesTests;

[TestClass]
[TestCategory( "LiveBackend" )]
public class OrganizationTest
{
	[TestMethod]
	public async Task Basic()
	{
		var org = await Sandbox.Services.Organization.Get( "facepunch" );

		Assert.IsNotNull( org );
		Assert.AreEqual( "facepunch", org.Ident );
		Assert.IsFalse( string.IsNullOrEmpty( org.Title ) );
		Assert.IsTrue( org.PackageCount > 0 );
		Assert.IsTrue( org.MemberCount > 0 );
		Assert.IsNotNull( org.Members );
		Assert.AreEqual( org.MemberCount, org.Members.Length );

		Console.WriteLine( $"{org.Ident} - {org.Title} - {org.PackageCount} packages, {org.MemberCount} members" );

		foreach ( var member in org.Members )
		{
			Console.WriteLine( $"  {member.Id} - {member.Name}" );
		}
	}

	[TestMethod]
	public async Task Cached()
	{
		var first = await Sandbox.Services.Organization.Get( "facepunch" );
		var second = await Sandbox.Services.Organization.Get( "facepunch" );

		Assert.IsNotNull( first );
		Assert.AreSame( first, second );
	}

	[TestMethod]
	public async Task CaseInsensitive()
	{
		var lower = await Sandbox.Services.Organization.Get( "facepunch" );
		var upper = await Sandbox.Services.Organization.Get( "FACEPUNCH" );

		Assert.IsNotNull( lower );
		Assert.AreSame( lower, upper );
	}

	[TestMethod]
	public async Task NotFound()
	{
		var org = await Sandbox.Services.Organization.Get( "this-org-definitely-does-not-exist-xyz123" );
		Assert.IsNull( org );
	}

	[TestMethod]
	[DataRow( null )]
	[DataRow( "" )]
	public async Task EmptyIdent( string ident )
	{
		var org = await Sandbox.Services.Organization.Get( ident );
		Assert.IsNull( org );
	}
}
