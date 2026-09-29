using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;
using System.IO;
using System.Linq;

namespace AccessTests;

// Regression test for the IgnoresAccessChecksTo sandbox escape: the runtime honours a self-defined
// IgnoresAccessChecksToAttribute, so a package could apply one naming Sandbox.Engine and call its private methods.
[TestClass]
[DoNotParallelize]
public class IgnoresAccessChecksEscapeTest
{
	const string AttributeDefinition = """
		namespace System.Runtime.CompilerServices
		{
			[System.AttributeUsage( System.AttributeTargets.Assembly, AllowMultiple = true )]
			public class IgnoresAccessChecksToAttribute : System.Attribute
			{
				public IgnoresAccessChecksToAttribute( string assemblyName ) { }
			}
		}
		""";

	static AccessControlResult Verify( string source )
	{
		var tree = CSharpSyntaxTree.ParseText( source );

		var corePath = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var refs = new[]
		{
			MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
			MetadataReference.CreateFromFile( Path.Combine( corePath, "System.Runtime.dll" ) ),
		};

		var compilation = CSharpCompilation.Create( "package.ignoresaccesstest", new[] { tree }, refs,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

		using var ms = new MemoryStream();
		var emit = compilation.Emit( ms );
		Assert.IsTrue( emit.Success, string.Join( "\n", emit.Diagnostics ) );
		ms.Position = 0;

		using var ac = new AccessControl();
		var result = ac.VerifyAssembly( ms, out var trusted );
		trusted?.Dispose();
		return result;
	}

	[TestMethod]
	public void Applied_Attribute_Is_Rejected()
	{
		var result = Verify( $$"""
			[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo( "Sandbox.Engine" )]
			{{AttributeDefinition}}
			""" );

		Assert.IsFalse( result.Success, "IgnoresAccessChecksTo must not pass access control" );
		Assert.IsTrue( result.Errors.Any( x => x.Contains( "IgnoresAccessChecksTo" ) ),
			"Expected an IgnoresAccessChecksTo whitelist error, got:\n" + string.Join( "\n", result.Errors ) );
	}

	[TestMethod]
	public void Defined_Attribute_Is_Rejected()
	{
		var result = Verify( AttributeDefinition );

		Assert.IsFalse( result.Success, "Defining IgnoresAccessChecksToAttribute must not pass access control" );
	}

	// Same shape under another name, so the rejections above are the attribute name and not the attribute.
	[TestMethod]
	public void Other_Attribute_Is_Allowed()
	{
		var result = Verify( """
			[assembly: System.Runtime.CompilerServices.SomethingElse( "Sandbox.Engine" )]
			namespace System.Runtime.CompilerServices
			{
				[System.AttributeUsage( System.AttributeTargets.Assembly )]
				public class SomethingElseAttribute : System.Attribute
				{
					public SomethingElseAttribute( string assemblyName ) { }
				}
			}
			""" );

		Assert.IsTrue( result.Success, "Control assembly must pass:\n" + string.Join( "\n", result.Errors ) );
	}
}
