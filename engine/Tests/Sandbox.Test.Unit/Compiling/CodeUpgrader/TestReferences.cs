using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodeUpgraderTests;

/// <summary>
/// References for analyzer and fixer tests. Uses the .NET reference assemblies embedded in Sandbox.Compiling
/// instead of letting the testing library download a NuGet package into a temp folder that CI runners share.
/// </summary>
static class TestReferences
{
	public static ReferenceAssemblies None { get; } = new( "net10.0" );

	public static void AddTo( SolutionState state )
	{
		state.AdditionalReferences.AddRange( Sandbox.FrameworkReferences.All.Values );
		state.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.Internal.GlobalGameNamespace ).Assembly.Location ) );
		state.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.ConCmdAttribute ).Assembly.Location ) );
		state.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( NetFlags ).Assembly.Location ) );
	}
}
