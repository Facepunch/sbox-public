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
			TestCode = oldcode.ReplaceLineEndings( "\r\n" ),
			FixedCode = fixedcode.ReplaceLineEndings( "\r\n" ),
		};

		// Code fixers insert CRLF trivia. Keep fixtures and formatter settings consistent,
		// independent of the checkout's line endings and the test host's defaults.
		test.TestState.AnalyzerConfigFiles.Add( ("/.editorconfig", "root = true\n\n[*.cs]\nend_of_line = crlf\n") );

		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.Internal.GlobalGameNamespace ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.ConCmdAttribute ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( NetFlags ).Assembly.Location ) );

		await test.RunAsync();
	}
}
