using System.Linq;

namespace CompilingTests;

/// <summary>
/// Diagnostics from generating initializer methods for fields added by a hotload.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AddedFieldInitializerTest
{
	[TestMethod]
	public async Task NewBaseTypeFromOtherAssembly_Warns()
	{
		var diagnostics = await BuildTwiceAsync(
			"public class Probe { }",
			"public class Probe : Sandbox.Component { }" );

		var warning = diagnostics.Single( x => x.Id == "SB400" );
		StringAssert.Contains( warning.GetMessage(), "Sandbox.Component" );
	}

	[TestMethod]
	public async Task NewBaseTypeFromSameAssembly_DoesNotWarn()
	{
		// Fields of base types from the same assembly get initializer methods instead

		var diagnostics = await BuildTwiceAsync(
			"public class Base { public int Value = 1; } public class Probe { }",
			"public class Base { public int Value = 1; } public class Probe : Base { }" );

		Assert.IsFalse( diagnostics.Any( x => x.Id == "SB400" ) );
	}

	[TestMethod]
	public async Task ExistingBaseTypeFromOtherAssembly_DoesNotWarn()
	{
		// New types have no existing instances, and the existing type's base didn't change

		var diagnostics = await BuildTwiceAsync(
			"public class Probe : Sandbox.Component { }",
			"public class Probe : Sandbox.Component { public int Added = 1; } public class Other : Sandbox.Component { }" );

		Assert.IsFalse( diagnostics.Any( x => x.Id == "SB400" ) );
	}

	/// <summary>
	/// Builds <paramref name="before"/> then <paramref name="after"/> as an incremental change, and returns
	/// the diagnostics from the second build, which must succeed.
	/// </summary>
	static async Task<Microsoft.CodeAnalysis.Diagnostic[]> BuildTwiceAsync( string before, string after, [System.Runtime.CompilerServices.CallerMemberName] string testName = "" )
	{
		var fileSystem = new MemoryFileSystem();
		fileSystem.WriteAllText( "/Probe.cs", before );

		using var group = new CompileGroup( testName );
		var compiler = group.GetOrCreateCompiler( "added-field-initializer" );
		compiler.AddSourceLocation( fileSystem );
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );

		fileSystem.WriteAllText( "/Probe.cs", after );
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );

		return compiler.Diagnostics.ToArray();
	}
}
