namespace CompilingTests;

// archive references end up as {name}.dll paths in the launcher's assembly resolver.
[TestClass]
[DoNotParallelize]
public class ArchiveReferenceTest
{
	[DataTestMethod]
	[DataRow( "Sandbox./../../data/payload" )]
	[DataRow( "Sandbox.\\..\\..\\data\\payload" )]
	[DataRow( "Sandbox.C:/payload" )]
	[DataRow( "package./../payload" )]
	[DataRow( "System.Diagnostics.Process" )]
	public void Invalid_Archive_Reference_Is_Dropped( string reference )
	{
		using var group = new CompileGroup( "ArchiveReference" );
		using var compiler = group.GetOrCreateCompiler( "archive_reference" );

		var archive = new CodeArchive();
		archive.References.Add( "Sandbox.Engine" );
		archive.References.Add( reference );

		compiler.UpdateFromArchive( archive );

		Assert.IsFalse( archive.References.Contains( reference ), $"'{reference}' must not survive into the build" );
		Assert.IsTrue( archive.References.Contains( "Sandbox.Engine" ) );
	}

	[DataTestMethod]
	[DataRow( "Sandbox.Engine" )]
	[DataRow( "Sandbox.Menu" )]
	[DataRow( "Facepunch.ActionGraphs" )]
	[DataRow( "Microsoft.AspNetCore.Components" )]
	[DataRow( "package.facepunch.base_library" )]
	[DataRow( "package.some-org.some-ident" )]
	public void Valid_Archive_Reference_Is_Kept( string reference )
	{
		using var group = new CompileGroup( "ArchiveReference" );
		using var compiler = group.GetOrCreateCompiler( "archive_reference" );

		var archive = new CodeArchive();
		archive.References.Add( reference );

		compiler.UpdateFromArchive( archive );

		Assert.IsTrue( archive.References.Contains( reference ) );
	}

	[TestMethod]
	public void Project_Reference_With_Path_Is_Not_Permitted()
	{
		var config = new Compiler.Configuration();
		config.AssemblyReferences = new() { "Sandbox./../../data/payload", "Sandbox.Menu" };

		CollectionAssert.AreEquivalent( new[] { "Sandbox.Menu" }, config.DistinctAssemblyReferences.ToArray() );
	}
}
