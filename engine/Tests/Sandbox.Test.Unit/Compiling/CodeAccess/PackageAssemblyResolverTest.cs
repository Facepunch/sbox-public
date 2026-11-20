using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Sandbox;

namespace CompilingTests.CodeAccess;

[TestClass]
[DoNotParallelize]
public class PackageAssemblyResolverTest
{
	[TestMethod]
	public void InMemoryPackageDependency_ResolvesForHotload()
	{
		var dependencyBytes = Compile( "package.resolver.dependency", "public sealed class StreamedDependency { }" );
		var dependencyReference = MetadataReference.CreateFromImage( dependencyBytes );
		var consumerBytes = Compile( "package.resolver.consumer", "public sealed class Consumer { public StreamedDependency Value; }", dependencyReference );

		using var resolver = new PackageAssemblyResolver();
		resolver.AddAssembly( dependencyBytes, "package.resolver.dependency" );

		using var stream = new MemoryStream( consumerBytes );
		using var consumer = AssemblyDefinition.ReadAssembly( stream,
			new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true, AssemblyResolver = resolver } );
		var fieldType = consumer.MainModule.GetType( "Consumer" ).Fields.Single().FieldType.Resolve();

		Assert.IsNotNull( fieldType );
		Assert.AreEqual( "StreamedDependency", fieldType.Name );
		Assert.AreEqual( "package.resolver.dependency", fieldType.Module.Assembly.Name.Name );
	}

	[DataTestMethod]
	[DataRow( "package.actual", "package.expected" )]
	[DataRow( "not-a-package", "not-a-package" )]
	public void StreamedAssembly_RequiresExpectedPackageIdentity( string assemblyName, string expectedName )
	{
		var bytes = Compile( assemblyName, "public sealed class Marker { }" );
		using var resolver = new PackageAssemblyResolver();

		Assert.ThrowsException<InvalidDataException>( () => resolver.AddAssembly( bytes, expectedName ) );
	}

	[TestMethod]
	public void MalformedStream_IsRejected()
	{
		using var loader = new PackageLoader( "MalformedAssemblyTest", typeof( PackageAssemblyResolverTest ).Assembly );
		using var enroller = loader.CreateEnroller( "test" );
		using var stream = new MemoryStream( [0, 1, 2, 3] );

		Assert.IsFalse( enroller.LoadAssemblyFromStream( "package.bad", stream ) );
	}

	[TestMethod]
	public void StreamedAssembly_ReplacesSameVersionAndEvictsOlderVersions()
	{
		var version1 = Compile( "package.resolver.versioned", "[assembly: System.Reflection.AssemblyVersion( \"1.0.0.0\" )] public sealed class V1 { }" );
		var version2 = Compile( "package.resolver.versioned", "[assembly: System.Reflection.AssemblyVersion( \"2.0.0.0\" )] public sealed class V2 { }" );
		var version1Name = new AssemblyNameReference( "package.resolver.versioned", new System.Version( 1, 0, 0, 0 ) );

		using var resolver = new PackageAssemblyResolver();
		resolver.AddAssembly( version1, "package.resolver.versioned" );
		var original = resolver.Resolve( version1Name );

		resolver.AddAssembly( version1, "package.resolver.versioned" );
		var replacement = resolver.Resolve( version1Name );
		Assert.AreNotSame( original, replacement );

		resolver.AddAssembly( version2, "package.resolver.versioned" );
		var newest = resolver.Resolve( version1Name );
		Assert.AreEqual( new System.Version( 2, 0, 0, 0 ), newest.Name.Version );
	}

	/// <summary>
	/// A live callback keeps its captured value when two package builds use the same version,
	/// including when fast hotload has updated the bytes sent to clients in the meantime.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FullHotloadUsesTheImageThatCreatedTheCallback( bool fastHotloadFirst )
	{
		const string name = "package.resolver.callbackimages";
		const string source = """
			public static class Menu
			{
				public static System.Action SelectionChanged;
				public static string Selected;
				public static void Subscribe<T>( string tab )
				{
					void SaveSelection() => Selected = tab;
					SelectionChanged += SaveSelection;
				}
			}
			""";

		using var oldLoader = new PackageLoader( "OutgoingImageTest", GetType().Assembly ) { ToolsMode = true };
		using var oldPackages = oldLoader.CreateEnroller( "outgoing" );
		using var originalStream = new MemoryStream( Compile( name, source ) );
		Assert.IsTrue( oldPackages.LoadAssemblyFromStream( name, originalStream ) );
		var original = oldPackages.GetLoadedAssemblies().Single();
		var originalMenu = original.Assembly.GetType( "Menu" );
		originalMenu.GetMethod( "Subscribe" ).MakeGenericMethod( typeof( int ) ).Invoke( null, ["props"] );

		if ( fastHotloadFirst )
		{
			using var platform = new ILHotload( "ImageTest" );
			if ( !platform.IsSupported || System.Diagnostics.Debugger.IsAttached )
			{
				Assert.Inconclusive( "The IL fast-hotload path is unavailable in this run." );
			}

			var fastSource = "[assembly: Sandbox.SupportsILHotload( \"0.0.0.0\" )]\n" + source;
			var reference = MetadataReference.CreateFromFile( typeof( SupportsILHotloadAttribute ).Assembly.Location );
			using var fastStream = new MemoryStream( Compile( name, fastSource, reference ) );
			Assert.IsTrue( oldPackages.LoadAssemblyFromStream( name, fastStream ) );
			Assert.IsTrue( original.FastHotload );
			using var oldImage = PackageManager.AssemblyResolver.OpenAssemblyImage( original.Assembly );
			Assert.AreEqual( original.Assembly.ManifestModule.ModuleVersionId, oldImage.MainModule.Mvid );
			Assert.AreNotEqual( original.ModifiedAssembly.ManifestModule.ModuleVersionId, oldImage.MainModule.Mvid );
		}

		// An added member moves the compiler's number for Subscribe. Both sides need their own method bodies to match it.
		var replacementSource = source.Replace( "public static void Subscribe", "public static void ExtraMember() { } public static void Subscribe" );
		using var newLoader = new PackageLoader( "IncomingImageTest", GetType().Assembly ) { ToolsMode = true };
		using var newPackages = newLoader.CreateEnroller( "incoming" );
		using var newStream = new MemoryStream( Compile( name, replacementSource ) );
		Assert.IsTrue( newPackages.LoadAssemblyFromStream( name, newStream ) );
		var replacement = newPackages.GetLoadedAssemblies().Single().Assembly;
		Assert.AreEqual( original.Assembly.FullName, replacement.FullName );
		Assert.AreNotEqual( original.Assembly.ManifestModule.ModuleVersionId, replacement.ManifestModule.ModuleVersionId );

		// Walk only the fixture's statics in the managed test process.
		var hotload = new Hotload { AssemblyResolver = PackageManager.AssemblyResolver };
		hotload.ReplacingAssembly( original.Assembly, replacement );
		var result = hotload.UpdateReferences();
		Assert.IsTrue( result.Success, string.Join( "\n", result.Errors ) );
		var menu = replacement.GetType( "Menu" );
		((System.Action)menu.GetField( "SelectionChanged" ).GetValue( null ))();
		Assert.AreEqual( "props", menu.GetField( "Selected" ).GetValue( null ) );
	}

	[TestMethod]
	public void PackageCallback_ResolvesOnceThenUsesCache()
	{
		var bytes = Compile( "package.resolver.callback", "public sealed class CallbackDependency { }" );
		var calls = 0;
		using var resolver = new PackageAssemblyResolver
		{
			PackageBytes = name =>
			{
				calls++;
				return name == "package.resolver.callback" ? bytes : null;
			}
		};
		var name = new AssemblyNameReference( "package.resolver.callback", new System.Version( 0, 0, 0, 0 ) );

		var first = resolver.Resolve( name );
		var cached = resolver.Resolve( name, new ReaderParameters() );

		Assert.AreSame( first, cached );
		Assert.AreEqual( 1, calls );
		Assert.ThrowsException<System.Exception>( () => resolver.Resolve(
			new AssemblyNameReference( "package.resolver.missing", new System.Version( 1, 0, 0, 0 ) ) ) );
	}

	[TestMethod]
	public void FrameworkAssembly_ResolvesFromDiskAndCaches()
	{
		using var resolver = new PackageAssemblyResolver();
		var name = AssemblyNameReference.Parse( typeof( System.Text.Json.JsonSerializer ).Assembly.FullName );

		var first = resolver.Resolve( name, new ReaderParameters() );
		var cached = resolver.Resolve( name );

		Assert.AreSame( first, cached );
		Assert.ThrowsException<System.Exception>( () => resolver.Resolve(
			new AssemblyNameReference( "Sandbox.DefinitelyMissing", new System.Version( 1, 0, 0, 0 ) ) ) );
	}

	[TestMethod]
	public void Dispose_IsIdempotent()
	{
		var resolver = new PackageAssemblyResolver();
		resolver.Dispose();
		resolver.Dispose();
	}

	internal static byte[] Compile( string assemblyName, string source, params MetadataReference[] additionalReferences )
	{
		var references = new List<MetadataReference>( FrameworkReferences.All.Values );
		references.AddRange( additionalReferences );

		var compilation = CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText( source )],
			references,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

		using var stream = new MemoryStream();
		var result = compilation.Emit( stream );
		Assert.IsTrue( result.Success, string.Join( "\n", result.Diagnostics ) );
		return stream.ToArray();
	}
}
