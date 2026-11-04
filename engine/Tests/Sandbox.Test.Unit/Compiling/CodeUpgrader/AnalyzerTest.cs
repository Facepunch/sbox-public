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
			// The engine targets .NET 10, so test code that calls into it needs the matching reference set
			ReferenceAssemblies = new ReferenceAssemblies( "net10.0", new PackageIdentity( "Microsoft.NETCore.App.Ref", "10.0.11" ), System.IO.Path.Combine( "ref", "net10.0" ) ),
			TestCode = code,
		};

		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.Internal.GlobalGameNamespace ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( Sandbox.ConCmdAttribute ).Assembly.Location ) );
		test.TestState.AdditionalReferences.Add( MetadataReference.CreateFromFile( typeof( NetFlags ).Assembly.Location ) );

		if ( globalConfig is not null )
		{
			test.TestState.AnalyzerConfigFiles.Add( ("/.globalconfig", $"is_global = true\n{globalConfig}\n") );
		}

		await test.RunAsync();
	}
}
