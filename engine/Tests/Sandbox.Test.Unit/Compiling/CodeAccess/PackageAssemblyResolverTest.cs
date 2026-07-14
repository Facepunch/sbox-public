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
		resolver.AddAssembly( dependencyBytes );

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

	static byte[] Compile( string assemblyName, string source, params MetadataReference[] additionalReferences )
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
