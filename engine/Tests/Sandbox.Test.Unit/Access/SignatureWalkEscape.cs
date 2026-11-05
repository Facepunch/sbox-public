using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;
using System.IO;
using System.Linq;

namespace AccessTests;

// System.IO.FileInfo and System.Runtime.InteropServices.Marshal stand in for any type access control must refuse.
[TestClass]
public class SignatureWalkEscapeTest
{
	static AccessControlResult Verify( string source )
	{
		var tree = CSharpSyntaxTree.ParseText( source );

		var corePath = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var refs = new[]
		{
			typeof( object ).Assembly.Location,
			Path.Combine( corePath, "System.Runtime.dll" ),
			Path.Combine( corePath, "System.Collections.dll" ),
			Path.Combine( corePath, "System.Numerics.Vectors.dll" ),
			typeof( Color ).Assembly.Location,
			typeof( System.Text.Json.JsonSerializer ).Assembly.Location,
		}.Distinct().Select( x => MetadataReference.CreateFromFile( x ) ).ToArray();

		var compilation = CSharpCompilation.Create( "package.signaturewalktest", new[] { tree }, refs,
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

	static void AssertRejected( AccessControlResult result, string expected )
	{
		Assert.IsFalse( result.Success, "Assembly must not pass access control" );
		Assert.IsTrue( result.Errors.Any( x => x.Contains( expected ) ),
			$"Expected a {expected} whitelist error, got:\n" + string.Join( "\n", result.Errors ) );
	}

	static void AssertPasses( AccessControlResult result )
	{
		Assert.IsTrue( result.Success, "Legitimate code must pass:\n" + string.Join( "\n", result.Errors ) );
	}

	// the property seeds InAttribute, which used to short circuit the walk of the in parameter's type.
	[TestMethod]
	public void InParameter_After_Seeded_Modifier_Is_Rejected()
	{
		var result = Verify( """
			public interface ICarrier
			{
				ref readonly int Seed { get; }
				void Hidden( in System.IO.FileInfo file );
			}
			""" );

		AssertRejected( result, "System.IO.FileInfo" );
	}

	// fields whose signature mentions a generic parameter used to be skipped entirely.
	[TestMethod]
	public void Generic_Field_Hiding_Type_Is_Rejected()
	{
		var result = Verify( """
			public class Carrier<T>
			{
				public System.Collections.Generic.Dictionary<T, System.IO.FileInfo> Values;
			}
			""" );

		AssertRejected( result, "System.IO.FileInfo" );
	}

	// Array and byref element types used to unwrap to the open generic, skipping its arguments.
	[DataTestMethod]
	[DataRow( "public class Carrier { public static System.Collections.Generic.List<System.IO.FileInfo>[] Values; }" )]
	[DataRow( "public class Carrier { public static void M( ref System.Collections.Generic.List<System.IO.FileInfo> values ) { } }" )]
	public void Array_Or_ByRef_Of_Generic_Hiding_Type_Is_Rejected( string source )
	{
		AssertRejected( Verify( source ), "System.IO.FileInfo" );
	}

	// an external field operand only had its value type checked, never the type that declares it.
	[TestMethod]
	public void Field_On_Forbidden_Type_Is_Rejected()
	{
		var result = Verify( """
			public static class Reader
			{
				public static int Read() => System.Runtime.InteropServices.Marshal.SystemDefaultCharSize;
			}
			""" );

		AssertRejected( result, "System.Runtime.InteropServices.Marshal" );
	}

	// the runtime and compiler honour these by name, so a self-defined copy must not count as our own type.
	[DataTestMethod]
	[DataRow( "UnsafeAccessorAttribute", "public sealed class UnsafeAccessorAttribute : System.Attribute { }" )]
	[DataRow( "UnsafeAccessorKind", "public enum UnsafeAccessorKind { Method }" )]
	[DataRow( "UnsafeAccessorTypeAttribute", "public sealed class UnsafeAccessorTypeAttribute : System.Attribute { }" )]
	[DataRow( "ModuleInitializerAttribute", "public sealed class ModuleInitializerAttribute : System.Attribute { }" )]
	public void SelfDefined_Runtime_Honoured_Type_Is_Rejected( string name, string definition )
	{
		var result = Verify( $$"""
			namespace System.Runtime.CompilerServices
			{
				{{definition}}
			}
			""" );

		AssertRejected( result, name );
	}

	[TestMethod]
	public void Generic_Fields_Pass()
	{
		var result = Verify( """
			public class Holder<T>
			{
				public T Value;
				public T[] Values;
				public System.Collections.Generic.List<T> Items = new();
				public System.Collections.Generic.Dictionary<string, T> ByName = new();
				public static System.Collections.Generic.List<int>[] Buckets;
			}
			""" );

		AssertPasses( result );
	}

	[TestMethod]
	public void In_Parameters_Of_Allowed_Types_Pass()
	{
		var result = Verify( """
			public interface ICarrier
			{
				ref readonly int Seed { get; }
				void Allowed( in Vector3 position, in Color color );
			}
			""" );

		AssertPasses( result );
	}

	[TestMethod]
	public void Public_Fields_Of_Allowed_Types_Pass()
	{
		var result = Verify( """
			public static class Fields
			{
				public static float Use()
				{
					var color = Color.White;
					color.r = 0.5f;

					var zero = Vector3.Zero;

					System.Numerics.Vector3 numerics = default;
					numerics.X = 1;

					(int A, string B) tuple = (1, "a");

					var separator = System.IO.Path.DirectorySeparatorChar;
					var empty = string.Empty;

					return color.r + zero.x + numerics.X + tuple.A + separator + empty.Length;
				}
			}
			""" );

		AssertPasses( result );
	}

	const string CarrierAttribute = """
		[System.AttributeUsage( System.AttributeTargets.All, AllowMultiple = true )]
		public sealed class CarrierAttribute : System.Attribute
		{
			public CarrierAttribute() { }
			public CarrierAttribute( System.Type type ) { }
			public CarrierAttribute( params System.Type[] types ) { }
			public CarrierAttribute( object value ) { }
			public CarrierAttribute( object[] values ) { }
			public System.Type TypeProperty { get; set; }
			public System.Type TypeField;
		}
		""";

	// attribute argument values were never walked, so typeof() could smuggle any type past the check.
	[DataTestMethod]
	[DataRow( "[Carrier( typeof( System.IO.FileInfo ) )] public sealed class Holder { }" )]
	[DataRow( "[Carrier( typeof( int ), typeof( System.IO.FileInfo ) )] public sealed class Holder { }" )]
	[DataRow( "[Carrier( new System.Type[] { typeof( System.IO.FileInfo ) } )] public sealed class Holder { }" )]
	[DataRow( "[Carrier( (object)typeof( System.IO.FileInfo ) )] public sealed class Holder { }" )]
	[DataRow( "[Carrier( new object[] { 1, typeof( System.IO.FileInfo ) } )] public sealed class Holder { }" )]
	[DataRow( "[Carrier( TypeProperty = typeof( System.IO.FileInfo ) )] public sealed class Holder { }" )]
	[DataRow( "[Carrier( TypeField = typeof( System.IO.FileInfo ) )] public sealed class Holder { }" )]
	[DataRow( "[Carrier( typeof( System.Collections.Generic.List<System.IO.FileInfo> ) )] public sealed class Holder { }" )]
	[DataRow( "public static class Holder { public static void M( [Carrier( typeof( System.IO.FileInfo ) )] int value ) { } }" )]
	[DataRow( "public static class Holder { [return: Carrier( typeof( System.IO.FileInfo ) )] public static int M() => 0; }" )]
	[DataRow( "public class Holder { [Carrier( typeof( System.IO.FileInfo ) )] public event System.Action Changed; }" )]
	public void Attribute_Argument_Type_Is_Rejected( string source )
	{
		AssertRejected( Verify( source + CarrierAttribute ), "System.IO.FileInfo" );
	}

	[TestMethod]
	public void Attribute_Arguments_Of_Allowed_Types_Pass()
	{
		var result = Verify( """
			public enum Mode { A, B }

			[Carrier( typeof( Holder ) )]
			[Carrier( typeof( Color ), typeof( System.Collections.Generic.List<int> ) )]
			[Carrier( TypeProperty = typeof( Vector3 ), TypeField = typeof( string ) )]
			[Carrier( (object)typeof( Holder ) )]
			[Carrier( new object[] { 1, "a", typeof( int ) } )]
			public sealed class Holder
			{
				[System.Text.Json.Serialization.JsonConverter( typeof( System.Text.Json.Serialization.JsonStringEnumConverter ) )]
				public Mode Mode { get; set; }

				public static void M( [Carrier( typeof( Holder ) )] int value ) { }
			}
			""" + CarrierAttribute );

		AssertPasses( result );
	}
}
