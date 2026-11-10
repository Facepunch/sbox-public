using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Sandbox;

[AttributeUsage( AttributeTargets.Field )]
public sealed class ResetAttribute : Attribute { }

public class TestClass1
{
	[Reset]
	public static int StaticIntField;

	[Reset]
	public static TestClass1 Instance;

	public int IntField;

#if TEST_BEFORE
	public int RemovedField;
#endif

#if TEST_AFTER
	public int AddedField = 83;
	public int AddedProperty { get; set; } = 84;
#endif

	public int IntProperty { get; set; }

	public Action ActionSimple;

	public Action CreateAction1( int foo )
	{
		return () =>
		{
			IntField = foo;
		};
	}

	public static int SecretValue()
	{
#if TEST_BEFORE
		return 1;
#endif

#if TEST_AFTER
		return 2;
#endif
	}

	public Action CreateAction2( int foo )
	{
		return () =>
		{
			IntField = SecretValue() + foo;
		};
	}

#if TEST_BEFORE
	public Action CreateAction3( int foo )
	{
		return () =>
		{
			IntField = SecretValue() + foo;
		};
	}
#endif

	public Action<bool> CreateAction4( int field, int property )
	{
		return value =>
		{
			if ( value ) IntField = field;
			else IntProperty = property;
		};
	}

	public Action CreateDynamic()
	{
		var throwExpr = Expression.Throw( Expression.Constant( new NotImplementedException( "This isn't implemented!" ) ), typeof( void ) );
		var lambda = Expression.Lambda<Action>( throwExpr );

		return lambda.Compile();
	}
}

public class TestClass2
{
	[Reset]
	public static TestClass2 Instance;

	public Action ActionSimple;

	public Action CreateAction1( Action<bool> inner )
	{
		return () =>
		{
			inner( true );
			inner( false );
		};
	}
}

public class TestClass3
{
	[Reset]
	public static TestClass3 Instance;

	public int IntField;
	public int IntProperty { get; set; }

	public TestClass3( int field, int property )
	{
		TestClass2.Instance.ActionSimple = TestClass2.Instance.CreateAction1( value =>
		{
			if ( value ) IntField = field;
			else IntProperty = property;
		} );
	}
}

public class TestClass4
{
	[Reset]
	public static TestClass4 Instance;

	public Type Type;
	public FieldInfo FieldInfo;
	public PropertyInfo PropertyInfo;
	public MethodInfo MethodInfo;

	public int Field;
	public int Property { get; set; }
	public int Method( int a ) => a * 2;

	public void FetchReflectionInstances()
	{
		Type = typeof( TestClass4 );

		FieldInfo = Type.GetField( nameof( Field ), BindingFlags.Instance | BindingFlags.Public );
		PropertyInfo = Type.GetProperty( nameof( Property ), BindingFlags.Instance | BindingFlags.Public );
		MethodInfo = Type.GetMethod( nameof( Method ), BindingFlags.Instance | BindingFlags.Public );
	}
}

public class TestClass5
{
	[Reset]
	public static TestClass5 Instance;

	public WeakReference<TestClass1> WeakReference;

	public ConditionalWeakTable<TestClass1, string> ConditionalWeakTable1 = new ConditionalWeakTable<TestClass1, string>();
	public ConditionalWeakTable<TestClass1, TestClass2> ConditionalWeakTable2 = new ConditionalWeakTable<TestClass1, TestClass2>();
}

public class TestClass6
{
	[Reset]
	public static TestClass6 Instance;

	public int IntField;

	public Func<int> Delegate;

	public Func<int> Foo( int x )
	{
#if TEST_BEFORE
		return () => x + IntField;
#elif TEST_AFTER
		return () => x;
#endif
	}

	public Func<int> Bar( int x )
	{
		if ( x == 0 )
		{
#if TEST_BEFORE
			return () => IntField + x + 1;
#elif TEST_AFTER
			return () => 1;
#endif
		}

#if TEST_BEFORE
		return () => x + IntField;
#elif TEST_AFTER
		return () => IntField;
#endif
	}

	public Func<int> Baz( int x )
	{
		if ( x == 0 )
		{
#if TEST_BEFORE
			return () => IntField + x + 1;
#elif TEST_AFTER
			return () => 1;
#endif
		}

#if TEST_BEFORE
		return x < 0 ? () => IntField : () => x - IntField;
#elif TEST_AFTER
		return x < 0 ? () => x + IntField : () => -IntField;
#endif
	}
}

public class TestClass7
{
	public struct ExampleStruct : IEquatable<ExampleStruct>
	{
		public readonly int Field;

		public ExampleStruct( int field )
		{
			Field = field;
		}

		public bool Equals( ExampleStruct other )
		{
			return Field == other.Field;
		}

		public override bool Equals( object obj )
		{
			return obj is ExampleStruct other && Equals( other );
		}

		public override int GetHashCode()
		{
			return Field;
		}
	}

	public class ExampleClass : IEquatable<ExampleClass>
	{
		public readonly int Field;

		public ExampleClass( int field )
		{
			Field = field;
		}

		public bool Equals( ExampleClass other )
		{
			if ( ReferenceEquals( null, other ) )
			{
				return false;
			}

			if ( ReferenceEquals( this, other ) )
			{
				return true;
			}

			return Field == other.Field;
		}

		public override bool Equals( object obj )
		{
			if ( ReferenceEquals( null, obj ) )
			{
				return false;
			}

			if ( ReferenceEquals( this, obj ) )
			{
				return true;
			}

			if ( obj.GetType() != this.GetType() )
			{
				return false;
			}

			return Equals( (ExampleClass)obj );
		}

		public override int GetHashCode()
		{
			return Field;
		}
	}

	[Reset]
	public static TestClass7 Instance;

	public readonly HashSet<ExampleStruct> HashSet1 = new HashSet<ExampleStruct>();
	public readonly HashSet<ExampleClass> HashSet2 = new HashSet<ExampleClass>();
}


public class TestClass8
{
	[Reset]
	public static TestClass8 Instance;

	public struct ExampleStruct1
	{
		public int Value;
	}

	public struct ExampleStruct2
	{
		public int Value;
#if TEST_AFTER
		public int Added;
#endif
	}

	public int[,] IntArray2D;

	public TestClass1[,] ObjArray2D;

	public ExampleStruct1[,] StructArray2D1;
	public ExampleStruct2[,] StructArray2D2;
}

public class TestClass9
{
	[Reset]
	public static TestClass9 Instance;

	public Dictionary<string, int> Dict1;
	public Dictionary<string, TestClass1> Dict2;
	public SortedDictionary<string, TestClass1> Dict3;
}

public class TestClass10
{
	[Reset]
	public static TestClass10 Instance;

	[Reset]
	public static bool Bool1;

	[Reset]
	public static bool Bool2;

	public Action<TestClass10> Action = instance => Bool1 = true;

	public TestClass10()
	{
		Action += ActionHandler;
	}

	private static void ActionHandler( TestClass10 instance )
	{
		Bool2 = true;
	}
}

public class TestClass11
{
	[Reset] public static TestClass11 Instance;

	public Action Action;

	public int IntValue;

	public void TestMethod()
	{
#if TEST_BEFORE
		IntValue = 1;
#endif

#if TEST_AFTER
		IntValue = 2;
#endif
	}
}

public enum TestEnum
{
	A,
	B,
	C,
	D
}

public class TestClass12
{
	[Reset] public static TestClass12 Instance;

	public TestEnum EnumValue;
	public TestEnum[] EnumArray;
}

public class TestClass13
{
	public int IntValue;

	public static readonly TestClass13 Instance = new();
}

public class TestClass14
{
	[Reset] public static TestClass14 Instance;

	public TypeDescription TypeDesc;
	public PropertyDescription PropertyDesc;
	public MethodDescription MethodDesc;
	public FieldDescription FieldDesc;
}

public class TestClass15
{
	[Reset] public static TestClass15 Instance;

	public readonly int IntValue;

	public TestClass15( int value )
	{
		IntValue = value;
	}
}

public class TestClass16
{
	[Reset] public static TestClass16 Instance;

	public Action Action1;
	public Action Action2;

	public int IntField;

#if TEST_AFTER
	public void DifferentNameMethod()
	{
		Action1 = () => IntField = 3;
	}
#endif

	public void SameNameMethod()
	{
		Action1 = () => IntField = 1;
	}

	public void SameNameMethod( int value )
	{
		Action2 = () => IntField = 2;
	}
}

public class TestClass17
{
	[Reset] public static TestClass17 Instance;

	public Func<int> Func;

	public IEnumerable<int> GeneratorMethod()
	{
		var counter = 0;

		Func = () => counter++;

		yield return 1;
	}

	public void ComplexParameterMethod( (int A, string B)[] arg )
	{
		var counter = 0;

		Func = () => counter++;
	}

	public void MultiDimArrayMethod( int[,] arg )
	{
		var counter = 0;

		Func = () => counter++;
	}

	public void ByRefMethod( ref int arg )
	{
		var counter = 0;

		Func = () => counter++;
	}

	public async Task AsyncMethod()
	{
		var counter = 0;

		Func = () => counter++;

		await Task.Yield();
	}

	public void NoNamespaceTypeMethod( TestClass1 inst )
	{
		var counter = inst.IntField;

		Func = () => counter++;
	}

	public void GenericMethod<T>( T arg )
	{
		var counter = 0;

		Func = () => counter++;
	}

	public void GenericMethod<T>( Func<T, int> func, T value )
	{
		Func = () => func( value );
	}

	public void ExternalGenericMethod<T>( T a, T b )
	{
		var inner = Hotload.TestHelper.GenericMethod( a );
		Func = () => inner( b ) ? 1 : 0;
	}

#if TEST_BEFORE
	public void RemovedMethod()
	{
		var counter = 0;

		Func = () => counter++;
	}
#endif

	public void LambdaInLambda()
	{
		var counter = 0;

		Func = () => Enumerable.Range( 1, counter++ ).Sum( x => Enumerable.Range( 1, x ).Sum( y => y * x ) );
	}

	public static void StaticMethod()
	{
		var counter = 0;

		Instance.Func = () => counter++;
	}

	public void NestedMethod1()
	{
		var counter = 0;

		int Example()
		{
			return counter++;
		}

		Func<int, int> func = x => Example() + x;

		FuncTarget = func.Target;
	}

	public int Counter;

	public void NestedMethod2()
	{
		int Example()
		{
			return Counter++;
		}

		Func<int, int> func = x => Example() + x;

		FuncTarget = func.Target;
	}

	public object FuncTarget;
}

public class TestClass18
{
	public static Func<int> Func;
	public static event Action OnEvent;

	public static int Counter;

	static TestClass18()
	{
		Func = () => 1;
		OnEvent += () => Counter++;
	}

	public static void InvokeEvent()
	{
		OnEvent?.Invoke();
	}
}

public class TestClass19
{
	[Reset]
	public static Task<int> Task;

#if TEST_BEFORE
	public static async Task<float> RemovedAsyncMethod( float arg )
	{
		await System.Threading.Tasks.Task.Delay( 10 );
		return arg;
	}
#endif

	public static async Task<int> AsyncMethod( int arg )
	{
		await System.Threading.Tasks.Task.Delay( 10 );
		return arg;
	}
}

public class TestClass20
{
	[Reset]
	public static MethodInfo MethodInfo;

	public string GenericMethod<T>()
	{
		return typeof( T ).Name;
	}
}

public class TestClass21<T>
{
	public string GenericMethod()
	{
		return typeof( T ).Name;
	}
}

public class TestClass22A
{
	[Reset]
	public static Func<string> Lambda;
}

public class TestClass22B<T1>
{
	public static void LambdaScopeMethod( int param )
	{
		TestClass22A.Lambda = () => typeof( T1 ).Name.Substring( 0, param );
	}

	public static void GenericLambdaScopeMethod1<T2>( int param )
	{
		TestClass22A.Lambda = () => typeof( T1 ).Name.Substring( 0, param ) + typeof( T2 ).Name.Substring( 0, param );
	}

#pragma warning disable CS0693
	public static void GenericLambdaScopeMethod2<T1>( int param )
#pragma warning restore CS0693
	{
		TestClass22A.Lambda = () => typeof( T1 ).Name.Substring( 0, param );
	}
}

public struct ListElement
{
	public object Object;
}

public class TestClass23
{
	[Reset] public static TestClass23 Instance;

	public List<ListElement> List { get; } = new List<ListElement>();
}

public struct BlockCopyableStruct
{
	public int IntValue;
	public float FloatValue;
	public Vector3 InnerStructValue;
}

public struct BlockCopyableStruct2
{
	public int Index;
}

public class TestClass24
{
	[Reset] public static TestClass24 Instance;

	public List<BlockCopyableStruct> List;
	public List<BlockCopyableStruct2> List2;
	public BlockCopyableStruct[] Array;
}

public class TestClass25
{
	[Reset] public static TestClass25 Instance;

	public Func<int> Func1;
	public Func<int> Func2;

	public void ScopeMethod1()
	{
		Func<int> NestedMethod1( int y ) => () => y;

		Func1 = NestedMethod1( 4 );
	}

	public void ScopeMethod2( int x )
	{
		Func<int> NestedMethod2( int y ) => () => x + y;

		Func2 = NestedMethod2( 4 );
	}
}

public class TestClass26
{
	public class TestBaseClass
	{
		public int Field1;
	}

	public sealed class TestDerivingClass
#if TEST_AFTER
	: TestBaseClass
#endif
	{
		public int Field2;
	}

	[Reset] public static TestClass26 Instance;

	public TestBaseClass BaseClassField;
	public TestDerivingClass DerivingClassField;
}

public class TestClass27
{
	public class ExampleClass1
	{
		public int Field;
	}

	public class ExampleClass2
	{
		public int Field;
	}

	[Reset] public static TestClass27 Instance;

#if TEST_BEFORE
	public ExampleClass1 Field;
#else
	public ExampleClass2 Field;
#endif
}

public class TestClass28
{
	public class ExampleGenericClass<T>
	{
		public T Field;

		public ExampleGenericClass( T field )
		{
			Field = field;
		}
	}

	[Reset] public static TestClass28 Instance;

#if TEST_AFTER
	public ExampleGenericClass<int> Field = new( 21 );
#endif
}

public class TestClass29
{
	public static void Method1( string arg )
	{

	}

	public static string Method2( string arg )
	{
		return arg;
	}

	public void Method3( string arg )
	{

	}

	public string Method4( string arg )
	{
		return arg;
	}

	[Reset] public static TestClass29 Instance;

	public string StringField = Method2( "Hello" );

#if TEST_AFTER
	public int AddedField = 123;
#endif

	public TestClass29()
	{
		Method1( "Hello" );
		Method2( "Hello" );
		Method3( "Hello" );
		Method4( "Hello" );
	}
}

public class TestClass30
{
	public readonly struct ValueChangedEvent<T>
	{
		public ValueChangedEvent( T value, T old )
		{
			New = value;
			Old = old;
		}

		public T New { get; }
		public T Old { get; }
	}

	[Reset] public static TestClass30 Instance;

	public Delegate Delegate { get; set; }

	public void OnStringChanged( in ValueChangedEvent<string> ev )
	{
		Console.WriteLine( $"Old: {ev.Old}" );
		Console.WriteLine( $"New: {ev.New}" );
	}

	public void StoreDelegate()
	{
		Delegate = OnStringChanged;
	}
}

public class TestClass31
{
	[Reset]
	public static TestClass31 Instance;

	public Func<int, int> Func { get; set; }

	public static void CreateFunc<T>()
	{
		Instance.Func = static value => value + 1;
	}
}

public class TestClass32
{
	[Reset]
	public static TestClass32 Instance;

	public Delegate Delegate { get; set; }

	public void AssignDelegate()
	{
#if TEST_BEFORE
		Delegate = ( int x ) => x + 1;
#else
		Delegate = ( float x ) => x + 1f;
#endif
	}
}

public class TestClass33 : IHotloadManaged
{
	[Reset]
	public static TestClass33 Instance;

	public string Field1;
	public string Field2;

	void IHotloadManaged.Destroyed( Dictionary<string, object> state )
	{
		state["Hello"] = Field1;
	}

	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state )
	{
		Field2 = state["Hello"] as string;
	}
}

public class TestClass34
{
	[Reset]
	public static TestClass34 Instance;

#if TEST_BEFORE
	public class RemovedClass : IHotloadManaged
	{
		public TestClass34 Parent;

		void IHotloadManaged.Failed()
		{
			Parent.FailureHandled = true;
		}
	}
#endif

	public bool FailureHandled;
	public object RemovedInstance;
}

public class TestClass35
{
	public class StringWrapper
	{
		public static implicit operator StringWrapper( string x )
		{
			return new StringWrapper() { Value = x };
		}

		public string Value;
	}

	[Reset]
	public static TestClass35 Instance;

	public Dictionary<int, StringWrapper> Dictionary;
	public HashSet<StringWrapper> Set;
}

public class TestClass36
{
	[Reset]
	public static ParameterInfo ParameterInfo;

	public void Method1( int parameter )
	{

	}

	public void Method2<T>( T parameter )
	{

	}
}

public class TestClass37
{
	[Reset]
	public static TestClass37 Instance;

	public Func<Task> Func;
}

public class TestClass38
{
	[Reset]
	public static TestClass38 Instance;

	public interface IVoxel { }

	public record struct Voxel( byte R, byte G, byte B ) : IVoxel;

	public class Chunk
	{
		public Voxel[] Array { get; set; }
		public Voxel[,,] Array3D { get; set; }
		public IVoxel[] InterfaceArray { get; set; }
		public IVoxel[,,] InterfaceArray3D { get; set; }
	}

	public Chunk[] Chunks { get; set; }
}

public class TestClass39
{
	[Reset] public static TestClass39 Instance;

	public class Generic<T>
	{
		public T Value;
	}

	public Generic<Generic<int>> SelfReferencingGeneric;
}

public class TestClass40
{
	[Reset] public static TestClass40 Instance;

	public MethodInfo MethodInfo;

	public void GenericMethod( ref int parameter )
	{

	}

	public void GenericMethod<T>( ref T parameter )
	{

	}
}

public class TestClass41
{
	[Reset] public static A Instance;

#if TEST_BEFORE
	public class A : C
#else
	public class A : B
#endif
	{
		public int AField;
	}

	public class B : C
	{
		public int BField;
	}

	public class C
	{
		public int CField;
	}
}

public class TestClass42
{
	[Reset] public static A Instance;

#if TEST_BEFORE
	public class A : B
#else
	public class A : C
#endif
	{
		public int AField;
	}

	public class B : C
	{
		public int BField;
	}

	public class C
	{
		public int CField;
	}
}

public class TestClass43
{
	[Reset] public static A Instance;

#if TEST_BEFORE
	public class A : B<int>
#else
	public class A : B<string>
#endif
	{
		public int AField;
	}

	public class B<T>
	{
		public int BField1;
		public T BField2;
	}
}

public class TestClass44
{
	[Reset] public static object Instance;

	public static void Unchanged()
	{
		Instance = new { Property1 = "World" };
	}


	public static void Reordered()
	{
#if TEST_BEFORE
		_ = new { Property2 = "World" };
#endif

		Instance = new { Property3 = "World" };

#if TEST_AFTER
		_ = new { Property2 = "World" };
#endif
	}

	public static void AddProperty()
	{
		Instance = new
		{
			Property4 = "World",
#if TEST_AFTER
			Property5 = 0
#endif
		};
	}

	public static void RemoveProperty()
	{
		Instance = new
		{
			Property6 = "World",
#if TEST_BEFORE
			Property7 = 0
#endif
		};
	}

	public static void ChangeProperty()
	{
		Instance = new
		{
			Property8 = "World",
#if TEST_BEFORE
			Property9 = 0
#else
			Property10 = 0
#endif
		};
	}
}

public class TestClass45
{
	[Reset] public static Action Instance;

	public class BaseType
	{
		public string Message => "Hello";

		public void Populate()
		{
			Instance = () =>
			{
				Console.WriteLine( Message );
			};
		}
	}

#if TEST_BEFORE
	public class RemovedType : BaseType;
#endif
}

public class TestClass46
{
	public static void Handler()
	{
		Console.WriteLine( "Hello, world!" );
	}
}

public class TestClass47
{
#if TEST_BEFORE
	public float MySyncFloat { get; set; }
#endif

#if TEST_AFTER
	[Sync] public float MySyncFloat { get; set; }
#endif
}

public class TestClass48
{
	[Reset] public static IEnumerable<int> Collection;

	public static void InitializeSingle()
	{
		Collection = [123];
	}

	public static void InitializeMultiple()
	{
		Collection = [123, 456];
	}
}

public class TestClass49
{
	[Reset] public static ConcurrentDictionary<int, TestClass49> Dictionary;

	public int IntProperty;
}

public class ContainerClass
{
	public SerializableClass ObjectProperty { get; set; }
}

public class SerializableClass
{
	[Reset]
	public static JsonSerializerOptions Options;

	public int IntProperty { get; set; }
}

public class SerializableClassConverter : JsonConverter<SerializableClass>
{
	public override bool CanConvert( Type typeToConvert )
	{
		return typeToConvert == typeof( SerializableClass );
	}

	public override SerializableClass Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		throw new NotImplementedException();
	}

	public override void Write( Utf8JsonWriter writer, SerializableClass value, JsonSerializerOptions options )
	{
		writer.WriteStartObject();
		writer.WritePropertyName( "int" );
		writer.WriteNumberValue( value.IntProperty );
		writer.WriteEndObject();
	}
}

#if TEST_BEFORE
public class RemovedClass;
#endif
