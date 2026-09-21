using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Text;

namespace CompilingTests;

[TestClass, DoNotParallelize]
public class TypeDescriptionGeneration
{
	[TestMethod]
	[DataRow( "class Example { }" )]
	[DataRow( "struct Example { }" )]
	[DataRow( "readonly ref struct Example { }" )]
	[DataRow( "record Example;" )]
	[DataRow( "record struct Example;" )]
	[DataRow( "interface Example { }" )]
	[DataRow( "enum Example { Value }" )]
	[DataRow( "delegate void Example();" )]
	public void EveryTypeDeclarationGetsItsSummary( string declaration )
	{
		var compilation = Generate( $"using Sandbox;\n/// <summary>A documented type.</summary>\npublic {declaration}" );
		AssertDescription( compilation, "Example", "A documented type." );
	}

	[TestMethod]
	[DataRow( "class" )]
	[DataRow( "struct" )]
	[DataRow( "record" )]
	[DataRow( "record struct" )]
	[DataRow( "interface" )]
	public void PartialTypesGetOneDescriptionAcrossFiles( string kind )
	{
		var first = $"using Sandbox; public partial {kind} Example {{ }}";
		var second = $"using Sandbox;\n/// <summary>The other part has the summary.</summary>\npublic partial {kind} Example {{ }}";
		foreach ( var sources in new[] { new[] { first, second }, new[] { second, first } } )
		{
			AssertDescription( Generate( sources ), "Example", "The other part has the summary." );
		}
	}

	[TestMethod]
	public void ExplicitDescriptionsArePreservedAndUndocumentedTypesStayEmpty()
	{
		var compilation = Generate( """
			using Sandbox;
			/// <summary>Generated summary.</summary>
			[Description("Explicit description.")]
			public record struct Example;
			public struct Empty { }
			public class Outer
			{
				/// <summary>A nested generic type.</summary>
				public struct Inner<T> { }
			}
			""" );
		AssertDescription( compilation, "Example", "Explicit description." );
		AssertDescription( compilation, "Outer+Inner`1", "A nested generic type." );
		Assert.AreEqual( 0, Descriptions( compilation, "Empty" ).Length );
	}

	static AttributeData[] Descriptions( CSharpCompilation compilation, string name ) =>
		compilation.GetTypeByMetadataName( name ).GetAttributes()
			.Where( x => x.AttributeClass.Name == "DescriptionAttribute" ).ToArray();

	static void AssertDescription( CSharpCompilation compilation, string name, string expected )
	{
		var attributes = Descriptions( compilation, name );
		Assert.AreEqual( 1, attributes.Length );
		Assert.AreEqual( expected, attributes[0].ConstructorArguments[0].Value );
	}

	static CSharpCompilation Generate( params string[] sources )
	{
		var corePath = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var references = new[]
		{
			MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
			MetadataReference.CreateFromFile( Path.Combine( corePath, "System.Runtime.dll" ) ),
			MetadataReference.CreateFromFile( typeof( DescriptionAttribute ).Assembly.Location )
		};
		var trees = sources.Select( ( text, index ) => CSharpSyntaxTree.ParseText( text,
			path: $"Type{index}.cs", encoding: Encoding.UTF8 ) );
		var compilation = CSharpCompilation.Create( "description.tests", trees, references,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
		var processor = new Sandbox.Generator.Processor { AddonName = "description.tests" };
		processor.Run( compilation );
		using var stream = new MemoryStream();
		var result = processor.Compilation.Emit( stream );
		Assert.IsTrue( result.Success, string.Join( "\n", result.Diagnostics ) );
		return processor.Compilation;
	}
}
