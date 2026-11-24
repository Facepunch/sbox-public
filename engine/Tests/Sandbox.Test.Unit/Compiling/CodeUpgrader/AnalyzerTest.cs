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
	public async Task TestWithMarkup( string code, string globalConfig = null )
	{
		var test = new CSharpAnalyzerTest<T, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
		{
			ReferenceAssemblies = TestReferences.None,
			TestCode = code,
		};

		TestReferences.AddTo( test.TestState );

		if ( globalConfig is not null )
		{
			test.TestState.AnalyzerConfigFiles.Add( ("/.globalconfig", $"is_global = true\n{globalConfig}\n") );
		}

		await test.RunAsync();
	}
}
