using System.Linq;

namespace CompilingTests.CodeAccess;

/// <summary>
/// Escapes the runtime IL verifier closed before it was retired, held against the source analyzer.
/// </summary>
[TestClass]
public class CodeAccessSignatureTest
{
	static void AssertDenied( string source, string expected )
	{
		var result = CodeAccessGate.Verify( source );
		Assert.IsFalse( result.EmitFailed, result.ToString() );
		Assert.IsTrue( result.Denied.Any( x => x.Contains( expected ) ), $"expected '{expected}' to be denied: {result}" );
	}

	static void AssertAllowed( string source )
	{
		var result = CodeAccessGate.Verify( source );
		Assert.IsFalse( result.EmitFailed, result.ToString() );
		Assert.AreEqual( 0, result.Denied.Count, result.ToString() );
	}

	const string IgnoresAccessChecksTo = """
		namespace System.Runtime.CompilerServices
		{
			[System.AttributeUsage( System.AttributeTargets.Assembly, AllowMultiple = true )]
			public class IgnoresAccessChecksToAttribute : System.Attribute
			{
				public IgnoresAccessChecksToAttribute( string assemblyName ) { }
			}
		}
		""";

	[TestMethod]
	public void IgnoresAccessChecksTo_Applied_IsDenied()
	{
		AssertDenied( $$"""
			[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo( "Sandbox.Engine" )]
			{{IgnoresAccessChecksTo}}
			""", "IgnoresAccessChecksToAttribute" );
	}

	[TestMethod]
	public void IgnoresAccessChecksTo_Defined_IsDenied()
	{
		AssertDenied( IgnoresAccessChecksTo, "IgnoresAccessChecksToAttribute" );
	}

	[TestMethod]
	public void SameShapeAttribute_IsAllowed()
	{
		AssertAllowed( """
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
	}

	// The runtime and compiler honour these by name, so a self-defined copy must not count as our own type.
	[DataTestMethod]
	[DataRow( "UnsafeAccessorAttribute", "public sealed class UnsafeAccessorAttribute : System.Attribute { }" )]
	[DataRow( "UnsafeAccessorKind", "public enum UnsafeAccessorKind { Method }" )]
	[DataRow( "UnsafeAccessorTypeAttribute", "public sealed class UnsafeAccessorTypeAttribute : System.Attribute { }" )]
	[DataRow( "ModuleInitializerAttribute", "public sealed class ModuleInitializerAttribute : System.Attribute { }" )]
	[DataRow( "InterceptsLocationAttribute", "public sealed class InterceptsLocationAttribute : System.Attribute { public InterceptsLocationAttribute( int version, string data ) { } }" )]
	public void SelfDefined_RuntimeHonouredType_IsDenied( string name, string definition )
	{
		AssertDenied( $$"""
			namespace System.Runtime.CompilerServices
			{
				{{definition}}
			}
			""", name );
	}

	[TestMethod]
	public void SelfDefined_ModuleInitializer_Applied_IsDenied()
	{
		AssertDenied( """
			namespace System.Runtime.CompilerServices
			{
				public sealed class ModuleInitializerAttribute : System.Attribute { }
			}

			static class Init
			{
				[System.Runtime.CompilerServices.ModuleInitializer]
				internal static void Run() { }
			}
			""", "ModuleInitializerAttribute" );
	}

	[TestMethod]
	public void InParameter_OfDeniedType_IsDenied()
	{
		AssertDenied( """
			public interface ICarrier
			{
				ref readonly int Seed { get; }
				void Hidden( in System.IO.FileInfo file );
			}
			""", "System.IO.FileInfo" );
	}

	[TestMethod]
	public void GenericField_HidingDeniedType_IsDenied()
	{
		AssertDenied( """
			public class Carrier<T>
			{
				public System.Collections.Generic.Dictionary<T, System.IO.FileInfo> Values;
			}
			""", "System.IO.FileInfo" );
	}

	[DataTestMethod]
	[DataRow( "public class Carrier { public static System.Collections.Generic.List<System.IO.FileInfo>[] Values; }" )]
	[DataRow( "public class Carrier { public static void M( ref System.Collections.Generic.List<System.IO.FileInfo> values ) { } }" )]
	public void ArrayOrByRef_OfGenericHidingDeniedType_IsDenied( string source )
	{
		AssertDenied( source, "System.IO.FileInfo" );
	}

	[TestMethod]
	public void Field_OnDeniedType_IsDenied()
	{
		AssertDenied( """
			public static class Reader
			{
				public static int Read() => System.Runtime.InteropServices.Marshal.SystemDefaultCharSize;
			}
			""", "System.Runtime.InteropServices.Marshal" );
	}

	[TestMethod]
	public void GenericFields_AreAllowed()
	{
		AssertAllowed( """
			public class Holder<T>
			{
				public T Value;
				public T[] Values;
				public System.Collections.Generic.List<T> Items = new();
				public System.Collections.Generic.Dictionary<string, T> ByName = new();
				public static System.Collections.Generic.List<int>[] Buckets;
			}
			""" );
	}

	[TestMethod]
	public void InParameters_OfAllowedTypes_AreAllowed()
	{
		AssertAllowed( """
			public interface ICarrier
			{
				ref readonly int Seed { get; }
				void Allowed( in Vector3 position, in Color color );
			}
			""" );
	}

	[TestMethod]
	public void PublicFields_OfAllowedTypes_AreAllowed()
	{
		AssertAllowed( """
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
	public void AttributeArgument_OfDeniedType_IsDenied( string source )
	{
		AssertDenied( source + CarrierAttribute, "System.IO.FileInfo" );
	}

	[TestMethod]
	public void AttributeArguments_OfAllowedTypes_AreAllowed()
	{
		AssertAllowed( """
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
	}

	[TestMethod]
	public void PointerSpanFactory_IsDenied()
	{
		var result = CodeAccessGate.Verify( "public static unsafe class C { public static int L( byte* t ) => System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpanFromNullTerminated( t ).Length; }", allowUnsafe: true );
		Assert.IsTrue( result.Denied.Any( x => x.Contains( "MemoryMarshal.CreateReadOnlySpanFromNullTerminated" ) ), result.ToString() );
	}

	[TestMethod]
	public void SpanCollectionExpression_IsAllowed()
	{
		AssertAllowed( "public static class C { public static int L( int a, int b ) { System.ReadOnlySpan<int> v = [a, b]; return v.Length; } }" );
	}
	[DataTestMethod]
	[DataRow( "public class Probe : Sandbox.RenderAttributes { public System.Action Capture() => base.Finalize; }" )]
	[DataRow( "public class Probe : System.WeakReference { public Probe() : base( new object() ) { } public System.Action Capture() => base.Finalize; }" )]
	[DataRow( "public class Middle : Sandbox.RenderAttributes { ~Middle() { } } public class Probe : Middle { public System.Action Capture() => base.Finalize; }" )]
	public void FinalizerDelegate_IsDenied( string source )
	{
		AssertDenied( source, "Finalize" );
	}

	[TestMethod]
	public void Destructor_IsAllowed()
	{
		AssertAllowed( "public class Probe { public int Count; ~Probe() { Count = 0; } }" );
	}

	[TestMethod]
	public void LocalFunction_TypeArgument_IsDenied()
	{
		AssertDenied( """
			public static class Probe
			{
				public static object Run()
				{
					static object Make<T>() => System.Activator.CreateInstance<T>();
					return Make<System.IO.FileInfo>();
				}
			}
			""", "System.IO.FileInfo" );
	}

	[TestMethod]
	public void EnclosingGenericTypeArgument_IsDenied()
	{
		AssertDenied( """
			public class Outer<T> { public class Inner { } }

			public static class Probe
			{
				public static object Run() => new Outer<System.IO.FileInfo>.Inner();
			}
			""", "System.IO.FileInfo" );
	}

	[TestMethod]
	public void ExplicitLayout_ShortOverload_IsDenied()
	{
		AssertDenied( "[System.Runtime.InteropServices.StructLayout( (short)2, Size = 16 )] public struct ExplicitBuffer { }", "StructLayout" );
	}

	[TestMethod]
	public void ExtensionCall_MatchesStaticSignature()
	{
		AssertAllowed( """
			using System.Threading.Tasks;

			public static class Probe
			{
				public static Task<int> Run( Task<Task<int>> task ) => task.Unwrap();
			}
			""" );
	}

	[TestMethod]
	public void Nameof_DeniedMember_IsAllowed()
	{
		AssertAllowed( "public static class Probe { public const string Name = nameof( System.IO.File.ReadAllText ); }" );
	}
	// Compile to metadata flags rather than attributes, so published packages relied on them passing
	[DataTestMethod]
	[DataRow( "[System.Serializable] public class A { [System.NonSerialized] public int X; }" )]
	[DataRow( "public static class A { [System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining )] public static int F() => 1; }" )]
	[DataRow( "public static class A { public static int F( [System.Runtime.InteropServices.Optional] int x ) => x; }" )]
	[DataRow( "public static class A { public static void F( [System.Runtime.InteropServices.In] ref int x, [System.Runtime.InteropServices.Out] out int y ) { y = x; } }" )]
	[DataRow( "public class A { [System.ComponentModel.DataAnnotations.Required( ErrorMessage = \"x\", AllowEmptyStrings = true )] public string N { get; set; } }" )]
	[DataRow( "public class A { [System.ComponentModel.DataAnnotations.Display( Name = \"x\", Description = \"y\", Order = 1 )] public string N { get; set; } }" )]
	public void PseudoAndAnnotationAttributes_AreAllowed( string source )
	{
		AssertAllowed( source );
	}

	[TestMethod]
	public void DisplayResourceType_IsDenied()
	{
		AssertDenied( "public class A { [System.ComponentModel.DataAnnotations.Display( Name = \"x\", ResourceType = typeof( string ) )] public string N { get; set; } }", "set_ResourceType" );
	}

	[TestMethod]
	public void QueryCast_TypeArgument_IsDenied()
	{
		AssertDenied( """
			public sealed class Query
			{
				public object Value;
				public Query Cast<T>() { Value = System.Activator.CreateInstance<T>(); return this; }
				public Query Select( System.Func<int, int> selector ) => this;
			}

			public static class Probe
			{
				public static object Run() => (from System.IO.FileInfo x in new Query() select x).Value;
			}
			""", "System.IO.FileInfo" );
	}

	[TestMethod]
	public void BoxedEnumConstant_OfDeniedType_IsDenied()
	{
		AssertDenied( "public static class Probe { public static object Run() => System.Diagnostics.ProcessWindowStyle.Hidden; }", "ProcessWindowStyle" );
	}

	[TestMethod]
	public void Linq_IsAllowed()
	{
		AssertAllowed( "using System.Linq; public static class Probe { public static int Run( int[] a ) => (from x in a join y in a on x equals y let z = x * 2 where z > 1 orderby z select z).Sum(); }" );
	}
	[TestMethod]
	public void InterfaceMappedToInheritedStatic_IsDenied()
	{
		AssertDenied( """
			interface IShared { static abstract System.Buffers.ArrayPool<int> Shared { get; } }
			class P : System.Buffers.ArrayPool<int>, IShared
			{
				public override int[] Rent( int n ) => null;
				public override void Return( int[] a, bool c = false ) { }
			}
			class C
			{
				static System.Buffers.ArrayPool<int> Get<T>() where T : IShared => T.Shared;
				object M() => Get<P>();
			}
			""", "ArrayPool`1.get_Shared" );
	}

	[TestMethod]
	public void InterfaceMappedToInheritedInstance_IsDenied()
	{
		AssertDenied( """
			interface IStart { void Start(); }
			class T : System.Threading.Tasks.Task<int>, IStart { public T() : base( () => 0 ) { } }
			""", "Task.Start()" );
	}

	[DataTestMethod]
	[DataRow( "class L : System.Collections.Generic.List<int>, System.Collections.Generic.IReadOnlyList<int> { }" )]
	[DataRow( "class D : System.IDisposable { public void Dispose() { } }" )]
	[DataRow( "class E : System.Collections.ObjectModel.Collection<int> { }" )]
	[DataRow( "class F : System.Collections.Generic.Dictionary<string, int> { }" )]
	[DataRow( "class G : System.Collections.Generic.HashSet<int>, System.Collections.Generic.IReadOnlySet<int> { }" )]
	public void InterfaceMappedToAllowedMembers_IsAllowed( string source )
	{
		AssertAllowed( source );
	}
	// Valid code the IL check passed: built-in operators are opcodes, virtual calls name the slot's introducing method
	[DataTestMethod]
	[DataRow( "public static class A { public static bool F( System.Net.HttpStatusCode c ) => c == System.Net.HttpStatusCode.OK; }" )]
	[DataRow( "public static class A { public static System.IO.FileMode F( System.IO.FileMode a ) => a | System.IO.FileMode.Append; }" )]
	[DataRow( "public static class A { public static bool F( System.Threading.Tasks.Task t ) => t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion; }" )]
	[DataRow( "public static class A { public static string F( System.Type t ) => t.ToString() + t.GetHashCode() + t.Equals( (object)t ); }" )]
	[DataRow( "public class A { public event System.Action Changed; public int F() => Changed?.GetInvocationList().Length ?? 0; }" )]
	[DataRow( "public static class A { public static int F( System.Action a ) => a.GetHashCode(); }" )]
	[DataRow( "public static class A { public static int F() => (int)System.Diagnostics.ProcessWindowStyle.Hidden; }" )]
	public void PreviouslyValidCode_IsAllowed( string source )
	{
		AssertAllowed( source );
	}
}
