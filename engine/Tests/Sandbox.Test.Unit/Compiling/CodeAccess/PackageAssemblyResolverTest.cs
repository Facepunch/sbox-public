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
