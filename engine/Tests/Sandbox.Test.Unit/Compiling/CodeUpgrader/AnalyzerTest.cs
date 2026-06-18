using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Sandbox.CodeUpgrader;

namespace CodeUpgraderTests;

public class AnalyzerTest<T> : IAnalyzerTest where T : DiagnosticAnalyzer, new()
{
	/// <summary>
	/// This code should trigger the diagnostic.
	/// </summary>
	public async Task TestWithMarkup( string code )
	{
		var test = new CSharpAnalyzerTest<T, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
		{
			ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
			TestCode = code,
		};

		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.Internal.GlobalGameNamespace ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.ConCmdAttribute ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( NetFlags ).Assembly.Location ) );

		await test.RunAsync();
	}
}
