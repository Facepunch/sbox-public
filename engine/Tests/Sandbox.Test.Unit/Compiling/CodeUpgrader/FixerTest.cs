using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Sandbox.CodeUpgrader;

namespace CodeUpgraderTests;

public class FixerTest<T, TFix> : IFixerTest where T : DiagnosticAnalyzer, new() where TFix : CodeFixProvider, new()
{
	public async Task Test( string oldcode, string fixedcode )
	{
		var test = new CSharpCodeFixTest<T, TFix, DefaultVerifier>
		{
			ReferenceAssemblies = ReferenceAssemblies.NetStandard.NetStandard20,
			TestCode = oldcode,
			FixedCode = fixedcode,
		};

		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.Internal.GlobalGameNamespace ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.ConCmdAttribute ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( NetFlags ).Assembly.Location ) );

		await test.RunAsync();
	}
}
