using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TestCompiler;

[TestClass]
public class IncrementalProcessorTest
{
	[TestMethod]
	public async Task IncrementalCompilation_ReuseTrees()
	{
		var memoryFs = new MemoryFileSystem();
		memoryFs.WriteAllText( "/Test.cs", "public class Test : Sandbox.Component { [Sandbox.Sync] public int SyncTest { get; set; } }" );
		memoryFs.WriteAllText( "/Test2.cs", "public class Test2 : Sandbox.Component { [Sandbox.Sync] public int SyncTest2 { get; set; } }" );

		using var group = new CompileGroup( "Test" );
		var config = new Compiler.Configuration();
		config.Clean();

		var compiler = group.GetOrCreateCompiler( "test" );
		compiler.AddSourceLocation( memoryFs );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		// cache trees to compare later
		var prevCompilation = compiler.incrementalState.Compilation;

		memoryFs.WriteAllText( "/Test2.cs", "public class Test2 : Sandbox.Component { [Sandbox.Sync] public int SyncTest2 { private get; set; } }" );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		Assert.IsTrue( group.BuildResult.Success, "Build should succeed after modifying the file." );

		// test tree reuse, only Test2 should be new
		var newTrees = compiler.incrementalState.Compilation.SyntaxTrees.Where( x => !prevCompilation.SyntaxTrees.Contains( x ) ).ToArray();
		Assert.IsTrue( newTrees.Any( x => x.FilePath.Contains( "Test2" ) ), "Test2 has changed" );
		Assert.IsFalse( newTrees.Any( x => x.FilePath.Contains( "Test1" ) ), "Test1 has not changed" );

		// make sure it's been processed
		var changed = newTrees.First( x => x.FilePath.Contains( "Test2" ) );
		Assert.IsTrue( changed.GetText().ToString().Contains( "__sync_GetValue" ) );
	}

	[TestMethod]
	public async Task IncrementalCompilation_ModifiedTrees()
	{
		var memoryFs = new MemoryFileSystem();
		memoryFs.WriteAllText( "/Test.cs", "public class Test : Sandbox.Component { [Sandbox.Sync] public int SyncTest { get; set; } }" );
		memoryFs.WriteAllText( "/Test2.cs", "public class Test2 : Sandbox.Component { [Sandbox.Sync] public int SyncTest { get; set; } }" );

		using var group = new CompileGroup( "Test" );
		var config = new Compiler.Configuration();
		config.Clean();

		var compiler = group.GetOrCreateCompiler( "test" );
		compiler.AddSourceLocation( memoryFs );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		SyntaxTree[] trees = [CSharpSyntaxTree.ParseText( "public class Test2 : Sandbox.Component { [Sandbox.Sync] public int SyncTest2 { get; set; } }", null, "/Test2.cs" )];

		// test ReplaceSyntaxTrees' modifiedSyntaxTrees output directly
		compiler.ReplaceSyntaxTrees( compiler.incrementalState.Compilation, trees, out var modifiedSyntaxTrees );
		Assert.AreEqual( modifiedSyntaxTrees.Count(), 1 );
	}
}
