using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using System.Collections.Generic;

namespace Sandbox.Test;

[TestClass]
public class ScriptArguments
{
	[TestMethod]
	public void FluentInvocationPassesTheAddonAssemblyScanner()
	{
		var result = Verify( """
			using Sandbox;
			public static class Example
			{
				public static bool Run(Script script, Painter writer, Painter reader) =>
					script.With("writer", writer).With("reader", reader).With("amount", 10).Run(1000);
			}
			""" );
		Assert.IsTrue( result.Success, string.Join( "\n", result.Errors ) );
	}

	[TestMethod]
	public void DirectBreenRuntimeAccessFailsTheAddonAssemblyScanner()
	{
		var result = Verify( "public static class Example { public static bool Run(Breen.Script script) => script.Run(1000).Success; }", referenceBreen: true );
		Assert.IsFalse( result.Success );
		Assert.IsTrue( result.WhitelistErrors.Any( x => x.Name.Contains( "Breen" ) ), string.Join( "\n", result.Errors ) );
	}

	static AccessControlResult Verify( string source, bool referenceBreen = false )
	{
		var corePath = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var references = new List<MetadataReference>
		{
			MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
			MetadataReference.CreateFromFile( Path.Combine( corePath, "System.Runtime.dll" ) ),
			MetadataReference.CreateFromFile( typeof( Script ).Assembly.Location )
		};
		if ( referenceBreen ) references.Add( MetadataReference.CreateFromFile( typeof( Breen.Script ).Assembly.Location ) );
		var compilation = CSharpCompilation.Create( "package.scriptarguments",
			[CSharpSyntaxTree.ParseText( source )], references,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
		using var stream = new MemoryStream();
		var emitted = compilation.Emit( stream );
		Assert.IsTrue( emitted.Success, string.Join( "\n", emitted.Diagnostics ) );
		stream.Position = 0;
		if ( !referenceBreen )
		{
			using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly( stream );
			Assert.IsFalse( assembly.MainModule.AssemblyReferences.Any( x => x.Name == "Breen" ), "Addon IL must not reference Breen." );
			stream.Position = 0;
		}
		using var access = new AccessControl();
		using var metadata = referenceBreen ? access.TrustUnsafe( File.ReadAllBytes( typeof( Breen.Script ).Assembly.Location ) ) : null;
		var result = access.VerifyAssembly( stream, out var trusted );
		trusted?.Dispose();
		return result;
	}

	[TestMethod]
	public void BreenIsNotWhitelisted()
	{
		var rules = new AccessRules();
		Assert.IsFalse( rules.AssemblyWhitelist.Contains( "Breen" ) );
		Assert.IsFalse( rules.IsInWhitelist( "Breen/Breen.IScriptInputs" ) );
		Assert.IsFalse( rules.IsInWhitelist( "Breen/Breen.ScriptInputs" ) );
		Assert.IsFalse( rules.IsInWhitelist( "Breen/Breen.ScriptInputs`2" ) );
		Assert.IsFalse( rules.IsInWhitelist( "Breen/Breen.ScriptSystem" ) );
		Assert.IsFalse( rules.IsInWhitelist( "Breen/Breen.Script" ) );
		Assert.IsFalse( rules.IsInWhitelist( "Breen/Breen.ScriptInvocation`1" ) );
		Assert.IsFalse( rules.IsInWhitelist( "Breen/Breen.DefaultResolver" ) );
	}
}
