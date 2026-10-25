using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;

namespace SystemTests;

[TestClass]
public class AssemblyOrdererTest
{
	static readonly MetadataReference[] CoreReferences =
	[
		MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
		MetadataReference.CreateFromFile( Path.Combine( Path.GetDirectoryName( typeof( object ).Assembly.Location ), "System.Runtime.dll" ) ),
	];

	static byte[] Compile( string name, string source, params byte[][] references )
	{
		var refs = CoreReferences.Concat( references.Select( x => MetadataReference.CreateFromImage( x ) ) );
		var compilation = CSharpCompilation.Create( name, [CSharpSyntaxTree.ParseText( source )], refs,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

		using var ms = new MemoryStream();
		var emit = compilation.Emit( ms );
		Assert.IsTrue( emit.Success, string.Join( "\n", emit.Diagnostics ) );
		return ms.ToArray();
	}

	/// <summary>
	/// A game's dll sorts ahead of the libraries it bundles alphabetically, but has to load after them.
	/// </summary>
	[TestMethod]
	public void LibrariesComeBeforeTheGameThatReferencesThem()
	{
		var water = Compile( "package.redsnail.watertool2", "public class WaterVolume { }" );
		var grass = Compile( "package.redsnail.grasstool", "public class GrassPainter : WaterVolume { }", water );
		var game = Compile( "package.hiltownrp.darkrp2", "public class Weather : GrassPainter { }", grass, water );

		var orderer = new AssemblyOrderer();
		orderer.Add( "package.hiltownrp.darkrp2.dll", game );
		orderer.Add( "package.redsnail.grasstool.dll", grass );
		orderer.Add( "package.redsnail.watertool2.dll", water );

		CollectionAssert.AreEqual(
			new[] { "package.redsnail.watertool2.dll", "package.redsnail.grasstool.dll", "package.hiltownrp.darkrp2.dll" },
			orderer.GetDependencyOrdered().Select( x => x.Identifier ).ToArray() );
	}

	[TestMethod]
	public void UnrelatedAssembliesKeepTheirOrder()
	{
		var orderer = new AssemblyOrderer();
		orderer.Add( "package.org.b.dll", Compile( "package.org.b", "public class B { }" ) );
		orderer.Add( "package.org.a.dll", Compile( "package.org.a", "public class A { }" ) );

		CollectionAssert.AreEqual(
			new[] { "package.org.b.dll", "package.org.a.dll" },
			orderer.GetDependencyOrdered().Select( x => x.Identifier ).ToArray() );
	}
}
