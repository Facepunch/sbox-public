using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CompilingTests.CodeAccess;

[TestClass]
[DoNotParallelize]
public class SymbolSignatureTest
{
	[TestMethod]
	public void FormatsNestedGenericsConstructorsOperatorsAndByRefParameters()
	{
		var tree = CSharpSyntaxTree.ParseText( """
			namespace Fixtures;

			public class Outer<T>
			{
				public class Inner<U>
				{
					public Inner( ref T value ) { }

					public V Convert<V>( in T input, out U output, ref V value )
					{
						output = default;
						return value;
					}

					public U this[int index]
					{
						get => default;
						set { }
					}

					public static Inner<U> operator +( Inner<U> left, Inner<U> right ) => left;
				}
			}
			""" );
		var compilation = CSharpCompilation.Create(
			"signature-fixture",
			[tree],
			FrameworkReferences.All.Values,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ).WithAllowUnsafe( true ) );
		var errors = compilation.GetDiagnostics().Where( x => x.Severity == DiagnosticSeverity.Error ).ToArray();
		Assert.AreEqual( 0, errors.Length, string.Join( "\n", errors.Select( x => x.ToString() ) ) );

		var inner = compilation.GetTypeByMetadataName( "Fixtures.Outer`1+Inner`1" );
		Assert.IsNotNull( inner );
		Assert.AreEqual( "Fixtures.Outer`1/Inner`1", SymbolSignature.Type( inner ) );

		var constructor = inner.InstanceConstructors.Single( x => x.Parameters.Length == 1 );
		Assert.AreEqual( "Fixtures.Outer`1/Inner`1..ctor( T& )", SymbolSignature.Method( constructor ) );

		var convert = inner.GetMembers( "Convert" ).OfType<IMethodSymbol>().Single();
		Assert.AreEqual( "Fixtures.Outer`1/Inner`1.Convert<V>( T&, U&, V& )", SymbolSignature.Method( convert ) );

		var indexer = inner.GetMembers().OfType<IPropertySymbol>().Single( x => x.IsIndexer );
		Assert.AreEqual( "Fixtures.Outer`1/Inner`1.get_Item( System.Int32 )", SymbolSignature.Method( indexer.GetMethod ) );
		Assert.AreEqual( "Fixtures.Outer`1/Inner`1.set_Item( System.Int32, U )", SymbolSignature.Method( indexer.SetMethod ) );

		var addition = inner.GetMembers().OfType<IMethodSymbol>().Single( x => x.MethodKind == MethodKind.UserDefinedOperator );
		Assert.AreEqual(
			"Fixtures.Outer`1/Inner`1.op_Addition( Fixtures.Outer`1/Inner`1<U>, Fixtures.Outer`1/Inner`1<U> )",
			SymbolSignature.Method( addition ) );
	}
}
