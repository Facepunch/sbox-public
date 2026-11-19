using Sandbox.Internal;
using System;
using System.Reflection;
using System.Reflection.Emit;

namespace ScriptingTests;

/// <summary>
/// Script lookup preserves name precedence and observes catalog changes when reusing its snapshot.
/// </summary>
[TestClass]
public class ScriptTypeLookupTests
{
	/// <summary>
	/// Exact names beat earlier simple matches, while top-level names beat earlier nested matches.
	/// Qualified nested names retain both CLR and C# spellings without accepting unrelated suffixes.
	/// </summary>
	[TestMethod]
	public void NamePrecedenceSurvivesSinglePassLookup()
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "LookupPrecedence" ), AssemblyBuilderAccess.RunAndCollect );
		var module = assembly.DefineDynamicModule( "Types" );
		var outer = module.DefineType( "LookupFixture.Outer", TypeAttributes.Public );
		var nestedInput = outer.DefineNestedType( "Input", TypeAttributes.NestedPublic ).CreateType();
		var nestedPick = outer.DefineNestedType( "Pick", TypeAttributes.NestedPublic ).CreateType();
		var middle = outer.DefineNestedType( "Middle", TypeAttributes.NestedPublic );
		var deep = middle.DefineNestedType( "Deep", TypeAttributes.NestedPublic ).CreateType();
		middle.CreateType();
		outer.CreateType();
		var topInput = module.DefineType( "LookupFixture.Input", TypeAttributes.Public ).CreateType();
		module.DefineType( "LookupFixture.Pick", TypeAttributes.Public ).CreateType();
		var exactPick = module.DefineType( "Pick", TypeAttributes.Public ).CreateType();
		var library = new TypeLibrary();
		try
		{
			var resolver = new TypeLibraryScriptResolver( library ) { IncomingAssemblies = [assembly] };
			Assert.AreSame( topInput, resolver.ResolveType( "Input" ) );
			Assert.AreSame( exactPick, resolver.ResolveType( "Pick" ) );
			Assert.AreSame( nestedInput, resolver.ResolveType( "LookupFixture.Outer.Input" ) );
			Assert.AreSame( nestedInput, resolver.ResolveType( "LookupFixture.Outer+Input" ) );
			Assert.AreSame( nestedPick, resolver.ResolveType( "Outer.Pick" ) );
			Assert.AreSame( deep, resolver.ResolveType( "LookupFixture.Outer.Middle.Deep" ) );
			Assert.AreSame( deep, resolver.ResolveType( "LookupFixture.Outer+Middle+Deep" ) );
			Assert.IsNull( resolver.ResolveType( "LookupFixture.Outer.Middle.deep" ) );
			Assert.IsNull( resolver.ResolveType( "Missing.Outer.Pick" ) );
			Assert.IsNull( resolver.ResolveType( "Input.Pick" ) );
		}
		finally
		{
			library.Dispose();
		}
	}

	/// <summary>
	/// The same resolver sees newly registered, removed, restored and incoming replacement types.
	/// No cached catalog snapshot can keep a removed type available to scripts.
	/// </summary>
	[TestMethod]
	public void CatalogSnapshotTracksRegistrationAndIncomingReplacement()
	{
		var original = EmitType( "LookupCatalog" );
		var replacement = EmitType( "LookupCatalog" );
		var library = new TypeLibrary();
		try
		{
			var resolver = new TypeLibraryScriptResolver( library );
			Assert.IsNull( resolver.ResolveType( "CatalogProbe" ) );
			library.AddAssembly( original.Assembly, true );
			Assert.AreSame( original, resolver.ResolveType( "CatalogProbe" ) );
			Assert.AreSame( original, resolver.ResolveType( "LookupFixture.CatalogProbe" ) );
			resolver.IncomingAssemblies = [replacement.Assembly];
			Assert.AreSame( replacement, resolver.ResolveType( "CatalogProbe" ) );
			Assert.AreSame( replacement, resolver.ResolveType( "LookupFixture.CatalogProbe" ) );
			resolver.IncomingAssemblies = [];
			Assert.AreSame( original, resolver.ResolveType( "CatalogProbe" ) );
			library.RemoveAssembly( original.Assembly );
			Assert.IsNull( resolver.ResolveType( "CatalogProbe" ) );
			Assert.IsNull( resolver.ResolveType( "LookupFixture.CatalogProbe" ) );
			library.AddAssembly( replacement.Assembly, true );
			Assert.AreSame( replacement, resolver.ResolveType( "CatalogProbe" ) );
		}
		finally
		{
			library.Dispose();
		}
	}

	/// <summary>
	/// Catalog alias fallback preserves case-insensitive matching without per-candidate closures.
	/// Exact-full-name mode continues to accept aliases but reject simple names.
	/// </summary>
	[TestMethod, TestCategory( Allocations.Category )]
	public void NameMatchingDoesNotAllocate()
	{
		var type = EmitType( "LookupAliases", aliases: true );
		var library = new TypeLibrary();
		try
		{
			library.AddAssembly( type.Assembly, true );
			var description = library.GetType( type );
			Assert.IsTrue( description.IsNamed( "AlTeRnAtE", exactFullName: true ) );
			Assert.IsFalse( description.IsNamed( "CatalogProbe", exactFullName: true ) );
			var resolver = new TypeLibraryScriptResolver( library );
			Assert.AreSame( type, resolver.ResolveType( "ALTERNATE" ) );
			Assert.AreSame( type.GetNestedType( "Nested" ), resolver.ResolveType( "ALTERNATE.Nested" ) );
			Assert.IsNull( resolver.ResolveType( "Missing.ALTERNATE.Nested" ) );
			description.Aliases[0] = "Renamed";
			Assert.IsNull( resolver.ResolveType( "ALTERNATE" ) );
			Assert.AreSame( type, resolver.ResolveType( "Renamed" ) );
			Assert.AreSame( type.GetNestedType( "Nested" ), resolver.ResolveType( "Renamed.Nested" ) );
			description.Aliases[0] = "Alternate";
			int matches = 0;
			Allocations.AssertSettles( "Catalog name matching", () =>
			{
				matches = (description.IsNamed( "CatalogProbe" ) ? 1 : 0)
					+ (description.IsNamed( "lookupfixture.catalogprobe", exactFullName: true ) ? 1 : 0)
					+ (description.IsNamed( "alternate", exactFullName: true ) ? 1 : 0)
					+ (description.IsNamed( "Missing" ) ? 1 : 0);
			} );
			Assert.AreEqual( 3, matches );
		}
		finally
		{
			library.Dispose();
		}
	}

	private static Type EmitType( string assemblyName, bool aliases = false )
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( assemblyName ), AssemblyBuilderAccess.RunAndCollect );
		var type = assembly.DefineDynamicModule( "Types" ).DefineType( "LookupFixture.CatalogProbe", TypeAttributes.Public );
		if ( aliases )
		{
			type.SetCustomAttribute( new CustomAttributeBuilder( typeof( AliasAttribute ).GetConstructor( [typeof( string[] )] ),
				[new[] { "Alternate" }] ) );
			type.DefineNestedType( "Nested", TypeAttributes.NestedPublic ).CreateType();
		}
		return type.CreateType();
	}
}
