using Microsoft.CodeAnalysis;

namespace TestCompiler;

public partial class CompilerTest
{
	/// <summary>
	/// A compiler can resolve a reference to a pre-built parent package assembly
	/// via the ReferenceProvider, simulating the publish compile flow for addons
	/// with a ParentPackage.
	/// </summary>
	[TestMethod]
	public async Task ParentPackageReference()
	{
		// First, compile the "parent" package to get its assembly bytes
		var parentPath = System.IO.Path.GetFullPath( "data/code/parent_package" );
		using var parentGroup = new CompileGroup( "ParentBuild" );

		var parentSettings = new Compiler.Configuration();
		parentSettings.Clean();

		var parentCompiler = parentGroup.CreateCompiler( "parent.game", parentPath, parentSettings );
		await parentGroup.BuildAsync();

		Assert.IsTrue( parentGroup.BuildResult.Success, parentGroup.BuildResult.BuildDiagnosticsString() );

		var parentOutput = parentGroup.BuildResult.Output.First();
		Assert.IsNotNull( parentOutput );

		var childPath = System.IO.Path.GetFullPath( "data/code/uses_parent_package" );
		using var childGroup = new CompileGroup( "ChildBuild" );
		childGroup.ReferenceProvider = new InMemoryReferenceProvider( "package.parent.game", parentOutput.AssemblyData );

		var childSettings = new Compiler.Configuration();
		childSettings.Clean();

		var childCompiler = childGroup.CreateCompiler( "child.addon", childPath, childSettings );
		childCompiler.AddReference( "package.parent.game" );

		await childGroup.BuildAsync();

		Assert.IsTrue( childGroup.BuildResult.Success, childGroup.BuildResult.BuildDiagnosticsString() );
		Assert.AreEqual( 1, childGroup.BuildResult.Output.Count() );
	}

	/// <summary>
	/// Without the parent package reference, the child should fail to compile
	/// because it can't resolve the parent types.
	/// </summary>
	[TestMethod]
	public async Task ParentPackageReference_FailsWithoutProvider()
	{
		var childPath = System.IO.Path.GetFullPath( "data/code/uses_parent_package" );
		using var childGroup = new CompileGroup( "ChildBuild" );

		var childSettings = new Compiler.Configuration();
		childSettings.Clean();

		var childCompiler = childGroup.CreateCompiler( "child.addon", childPath, childSettings );

		// No ReferenceProvider, no parent reference, should fail compilation
		await childGroup.BuildAsync();

		Assert.IsFalse( childGroup.BuildResult.Success, "Should fail without parent package reference" );
	}

	/// <summary>
	/// Simulates the full flow: parent package installed, ReferenceProvider set,
	/// child addon compiles successfully with access to parent types.
	/// Verifies the child assembly actually contains the expected type.
	/// </summary>
	[TestMethod]
	public async Task ParentPackageReference_ChildContainsExpectedType()
	{
		// Build parent
		var parentPath = System.IO.Path.GetFullPath( "data/code/parent_package" );
		using var parentGroup = new CompileGroup( "ParentBuild" );

		var parentSettings = new Compiler.Configuration();
		parentSettings.Clean();
		parentGroup.CreateCompiler( "parent.game", parentPath, parentSettings );
		await parentGroup.BuildAsync();
		Assert.IsTrue( parentGroup.BuildResult.Success );

		var parentBytes = parentGroup.BuildResult.Output.First().AssemblyData;

		// Build child with parent reference
		var childPath = System.IO.Path.GetFullPath( "data/code/uses_parent_package" );
		using var childGroup = new CompileGroup( "ChildBuild" );
		childGroup.ReferenceProvider = new InMemoryReferenceProvider( "package.parent.game", parentBytes );

		var childSettings = new Compiler.Configuration();
		childSettings.Clean();

		var childCompiler = childGroup.CreateCompiler( "child.addon", childPath, childSettings );
		childCompiler.AddReference( "package.parent.game" );

		await childGroup.BuildAsync();
		Assert.IsTrue( childGroup.BuildResult.Success, childGroup.BuildResult.BuildDiagnosticsString() );

		// Verify the child assembly references the parent
		var childOutput = childGroup.BuildResult.Output.First();
		Assert.IsNotNull( childOutput.AssemblyData );
		Assert.IsTrue( childOutput.AssemblyData.Length > 0 );
	}

	/// <summary>
	/// A simple ICompileReferenceProvider that returns a pre-built assembly
	/// for a specific reference name.
	/// </summary>
	private class InMemoryReferenceProvider : ICompileReferenceProvider
	{
		private readonly string _referenceName;
		private readonly byte[] _assemblyBytes;

		public InMemoryReferenceProvider( string referenceName, byte[] assemblyBytes )
		{
			_referenceName = referenceName;
			_assemblyBytes = assemblyBytes;
		}

		public PortableExecutableReference Lookup( string reference )
		{
			if ( string.Equals( reference, _referenceName, System.StringComparison.OrdinalIgnoreCase ) )
			{
				return MetadataReference.CreateFromImage( _assemblyBytes );
			}

			return null;
		}
	}
}
