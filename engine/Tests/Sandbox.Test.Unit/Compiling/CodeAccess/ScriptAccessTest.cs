using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;

namespace CompilingTests.CodeAccess;

[TestClass]
[DoNotParallelize]
public class ScriptAccessTest
{
	[TestMethod]
	public void FluentInvocationPassesTheAccessGate()
	{
		var source = """
			using Sandbox;
			public static class Example
			{
				public static bool Run(Script script, Painter writer, Painter reader) =>
					script.With("writer", writer).With("reader", reader).With("amount", 10).Run(1000);
			}
			""";

		var result = CodeAccessGate.Verify( source );
		Assert.IsFalse( result.EmitFailed, result.ToString() );
		Assert.AreEqual( 0, result.Denied.Count, result.ToString() );

		// The public Script API must not leak Breen types into addon IL either.
		var compilation = CSharpCompilation.Create( "package.scriptarguments",
			[CSharpSyntaxTree.ParseText( source )], CodeAccessGate.BuildReferences(),
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
		using var stream = new MemoryStream();
		var emitted = compilation.Emit( stream );
		Assert.IsTrue( emitted.Success, string.Join( "\n", emitted.Diagnostics ) );
		stream.Position = 0;
		using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly( stream );
		Assert.IsFalse( assembly.MainModule.AssemblyReferences.Any( x => x.Name == "Breen" ), "Addon IL must not reference Breen." );
	}

	[TestMethod]
	public void DirectBreenRuntimeAccessFailsTheAccessGate()
	{
		var breen = MetadataReference.CreateFromFile( typeof( Breen.Script ).Assembly.Location );
		var result = CodeAccessGate.Verify(
			"public static class Example { public static bool Run(Breen.Script script) => script.Run(1000).Success; }",
			extraReferences: [breen] );

		Assert.IsFalse( result.EmitFailed, result.ToString() );
		Assert.IsTrue( result.Denied.Any( x => x.Contains( "Breen" ) ), result.ToString() );
	}

	[TestMethod]
	public void BreenIsNotWhitelisted()
	{
		var rules = AccessRules.Instance;
		Assert.IsFalse( rules.WholesaleAssembly( "Breen" ) );
		Assert.IsFalse( rules.IsAllowed( "Breen/Breen.IScriptInputs" ) );
		Assert.IsFalse( rules.IsAllowed( "Breen/Breen.ScriptInputs" ) );
		Assert.IsFalse( rules.IsAllowed( "Breen/Breen.ScriptInputs`2" ) );
		Assert.IsFalse( rules.IsAllowed( "Breen/Breen.ScriptSystem" ) );
		Assert.IsFalse( rules.IsAllowed( "Breen/Breen.Script" ) );
		Assert.IsFalse( rules.IsAllowed( "Breen/Breen.ScriptInvocation`1" ) );
		Assert.IsFalse( rules.IsAllowed( "Breen/Breen.DefaultResolver" ) );
	}
}
