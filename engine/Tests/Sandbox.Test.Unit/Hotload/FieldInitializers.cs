using System;

namespace HotloadTests;

/// <summary>
/// Fields added by a hotload should get the value from their initializer.
/// </summary>
[TestClass]
public class FieldInitializerTest : HotloadFixtureTestBase
{
	[TestMethod]
	public async Task AddedFieldDefaultValue()
	{
		await HotloadFixtureAsync(
			"""
			public class AddedFieldDefaultValue : IHotloadTestFixture
			{
			-	public int RemovedField = 20;
			+	public int AddedField = 83;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 83, AddedField );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedFieldInitializerWithCtorBody()
	{
		await HotloadFixtureAsync(
			"""
			public class FieldInitializerWithCtorBody : IHotloadTestFixture
			{
				public static void Method1( string arg ) { }
				public static string Method2( string arg ) => arg;
				public void Method3( string arg ) { }
				public string Method4( string arg ) => arg;

				public string StringField = Method2( "Hello" );
			+	public int AddedField = 123;

				public FieldInitializerWithCtorBody()
				{
					Method1( "Hello" );
					Method2( "Hello" );
					Method3( "Hello" );
					Method4( "Hello" );
				}
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( "Hello", StringField );
			+		Assert.AreEqual( 123, AddedField );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedFieldInitializerWithLocals()
	{
		// Initializers can declare locals, here with a pattern and an out variable.

		await HotloadFixtureAsync(
			"""
			public class FieldInitializerWithLocals : IHotloadTestFixture
			{
				private static long Number() => 1L;
				private static long Pick( bool first, bool second ) => 0L;
				private static bool Capture( out object slot )
				{
					slot = new object();
					return true;
				}

			+	public long Computed = Pick( Number() is var n, Capture( out object held ) ) + n;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		// Sanity check: the initializer evaluates to 1L through the real constructor
			+		Assert.AreEqual( 1L, new FieldInitializerWithLocals().Computed );
			+
			+		// Mixing up the locals would give the address of the object created in Capture
			+		Assert.AreEqual( 1L, Computed );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedFieldInitializerWithBranches()
	{
		await HotloadFixtureAsync(
			"""
			public class FieldInitializerWithBranches : IHotloadTestFixture
			{
				private static uint Small() => 1u;
				private static uint Big() => uint.MaxValue;
				private static bool Yes() => true;
				private static string Missing() => null;

			+	// Unsigned comparison, ternary and null-coalescing compile to short branches.
			+	// A signed comparison would treat Big() as -1 and pick 20 instead of 10.
			+	public int Branched = (Small() < Big() ? 10 : 20) + (Yes() ? 100 : 200) + (Missing() ?? "abc").Length;
			+
			+	// These branch straight to the end of the initializer
			+	public int Ternary = Yes() ? 1 : 2;
			+	public string Coalesced = Missing() ?? "abc";
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 113, Branched );
			+		Assert.AreEqual( 1, Ternary );
			+		Assert.AreEqual( "abc", Coalesced );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedFieldInitializerStoringArgument()
	{
		// An initializer that uses a primary constructor parameter can't be run without the
		// constructor, so the field keeps its default with a warning.

		var result = await HotloadFixtureAsync(
			"""
			public class FieldInitializerStoringArgument : IHotloadTestFixture
			{
				public Inner Value = new( 1 );

				public class Inner( int value )
				{
			+		// Doesn't use the parameter, so can still be evaluated
			+		public int Earlier = 3;
			+
			+		public int Stored = value = 5;
				}
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 3, Value.Earlier );
			+		Assert.AreEqual( 0, Value.Stored );
			+	}
			}
			""" );

		var warned = result.Warnings.Select( x => x.Member?.Name ).ToArray();
		CollectionAssert.AreEqual( new[] { "Stored" }, warned );
	}

	[TestMethod]
	public async Task AddedFieldInitializerAfterCapturedArgument()
	{
		// A captured primary constructor parameter is stored first, but later initializers still work.

		var result = await HotloadFixtureAsync(
			"""
			public class FieldInitializerAfterCapturedArgument : IHotloadTestFixture
			{
				public Inner Value = new( 1 );

				public class Inner( int seed )
				{
					// Captured, so the constructor starts by storing it in a hidden field
					public int Seed() => seed;

			+		public float Rate = 2f;
				}
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 1, Value.Seed() );
			+		Assert.AreEqual( 2f, Value.Rate );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedFieldInitializerInGenericType()
	{
		var result = await HotloadFixtureAsync(
			"""
			public class FieldInitializerInGenericType : IHotloadTestFixture
			{
				public Generic<string> Value = new();

				public class Generic<T>
				{
			+		public int Count = 5;
				}
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 5, Value.Count );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedFieldInitializerUsingTypeParameter()
	{
		// The initializer constructs a type built from T, which has to resolve to the
		// type argument of the instance being upgraded.

		var result = await HotloadFixtureAsync(
			"""
			using System.Collections.Generic;

			public class FieldInitializerUsingTypeParameter : IHotloadTestFixture
			{
				public Generic<string> Value = new();

				public class Generic<T>
				{
			+		public List<T> Items = new List<T>();
				}
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.IsNotNull( Value.Items );
			+		Assert.AreEqual( 0, Value.Items.Count );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task GenericTypeFieldInitializer()
	{
		// The added field's type is generic, rather than the type declaring it.

		await HotloadFixtureAsync(
			"""
			public class GenericTypeFieldInitializer : IHotloadTestFixture
			{
				public class ExampleGenericClass<T>
				{
					public T Field;

					public ExampleGenericClass( T field )
					{
						Field = field;
					}
				}

			+	public ExampleGenericClass<int> Field = new( 21 );
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.IsNotNull( Field );
			+		Assert.AreEqual( 21, Field.Field );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedStructFieldAssignedInConstructor()
	{
		// Struct constructors don't call a base constructor, so body statements
		// mustn't be mistaken for field initializers.

		var result = await HotloadFixtureAsync(
			"""
			public class StructFieldAssignedInConstructor : IHotloadTestFixture
			{
				public Inner Value = new();

				public struct Inner
				{
					public int A;
			+		public int B;

					public Inner()
					{
						A = 1;
			+
			+			// Part of the constructor body, not an initializer
			+			B = 5;
					}
				}
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 1, Value.A );
			+		Assert.AreEqual( 0, Value.B );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedStructFieldInitializer()
	{
		// Struct field initializers look just like constructor body statements in IL,
		// but should still be applied to existing instances.

		var result = await HotloadFixtureAsync(
			"""
			public class StructFieldInitializer : IHotloadTestFixture
			{
				public Inner Value = new();

				public struct Inner
				{
					public int A;
			+		public int B = 5;

					public Inner()
					{
						A = 1;
					}
				}
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		// Sanity check: a new instance gets the initializer
			+		Assert.AreEqual( 5, new Inner().B );
			+
			+		Assert.AreEqual( 1, Value.A );
			+		Assert.AreEqual( 5, Value.B );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedFieldSetByOtherInstanceInitializer()
	{
		// Child's initializer stores Depth on a new instance, which isn't an initializer for Depth

		var result = await HotloadFixtureAsync(
			"""
			public class FieldSetByOtherInstanceInitializer : IHotloadTestFixture
			{
				public static int Made;

			+	public FieldSetByOtherInstanceInitializer Child = Made++ == 0 ? new FieldSetByOtherInstanceInitializer { Depth = 1 } : null;
			+
			+	public int Depth;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 0, Depth );
			+		Assert.AreEqual( 1, Child.Depth );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedFieldInitializerThrows()
	{
		// An initializer that throws should only leave that instance at its default, keep
		// working for later instances, and only be logged once.

		using var fixture = await CreateFixtureAsync(
			"""
			using System;
			using System.Diagnostics;
			using System.Linq;

			public class FieldInitializerThrows : IHotloadTestFixture
			{
				public Inner[] Instances = [new(), new(), new()];

			+	// Where the initializer is when it runs in the constructor
			+	public static int ConstructorLine;
			+

				public class Inner
				{
					public static int Calls;

					// Throws for the first two instances, then succeeds
					private static int Next() => ++Calls <= 2 ? throw new InvalidOperationException( "Initializer failed" ) : 5;

			+		public int Value = Next();
				}
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		var values = Instances.Select( x => x.Value ).Order().ToArray();
			+		CollectionAssert.AreEqual( new[] { 0, 0, 5 }, values );
			+
			+		Inner.Calls = 0;
			+
			+		var e = Assert.ThrowsException<InvalidOperationException>( () => new Inner() );
			+
			+		ConstructorLine = new StackTrace( e, true ).GetFrames()
			+			.First( x => x.GetMethod().IsConstructor )
			+			.GetFileLineNumber();
			+	}
			}
			""" );

		var result = await fixture.HotloadAsync( allowErrors: true );

		var errors = result.Errors.ToArray();

		Assert.AreEqual( 1, errors.Length );
		Assert.AreEqual( "Value", errors[0].Member?.Name );
		Assert.AreEqual( "Inner", errors[0].Member?.DeclaringType?.Name );
		Assert.IsInstanceOfType<InvalidOperationException>( errors[0].Exception );

		// The generated method maps to the same line as the initializer in the constructor

		var constructorLine = (int)fixture.Instance.GetType().GetField( "ConstructorLine" ).GetValue( null );

		Assert.AreNotEqual( 0, constructorLine );
		StringAssert.Contains( errors[0].Exception.StackTrace, $"__AddedFieldInitializer_Value() in Fixture.cs:line {constructorLine}" );
	}

	[TestMethod]
	public async Task AddedFieldInitializerWithLoop()
	{
		// Spreading a type that's only enumerable by pattern compiles to a foreach loop.
		// Fields with no initializer stay at their default without a warning.

		var result = await HotloadFixtureAsync(
			"""
			public class FieldInitializerWithLoop : IHotloadTestFixture
			{
				public readonly struct Numbers
				{
					public Enumerator GetEnumerator() => default;

					public struct Enumerator
					{
						public int Current { get; private set; }
						public bool MoveNext() => ++Current <= 3;
					}
				}

			+	public int Plain;
			+
			+	public int[] Spread = [.. new Numbers(), 4];
			+
			+	public int Later = 7;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 0, Plain );
			+		CollectionAssert.AreEqual( new[] { 1, 2, 3, 4 }, Spread );
			+		Assert.AreEqual( 7, Later );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedAutoPropertyInitializer()
	{
		// Initializes the property's backing field

		var result = await HotloadFixtureAsync(
			"""
			public class AddedAutoPropertyInitializer : IHotloadTestFixture
			{
			+	public int AddedProperty { get; set; } = 84;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 84, AddedProperty );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedFieldKeywordPropertyInitializer()
	{
		// A property using the field keyword, so it has a backing field and an initializer

		var result = await HotloadFixtureAsync(
			"""
			using System;

			public class AddedFieldKeywordPropertyInitializer : IHotloadTestFixture
			{
			+	public int Clamped { get => field; set => field = Math.Max( 0, value ); } = 5;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 5, Clamped );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedEventInitializer()
	{
		var result = await HotloadFixtureAsync(
			"""
			using System;

			public class AddedEventInitializer : IHotloadTestFixture
			{
			+	public int Calls;
			+
			+	public event Action Changed = () => { };
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Changed += () => Calls++;
			+		Changed();
			+
			+		Assert.AreEqual( 1, Calls );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedRecordParameterDefault()
	{
		// Positional record properties are initialized from their primary constructor parameter's default value

		var result = await HotloadFixtureAsync(
			"""
			public class AddedRecordParameterDefault : IHotloadTestFixture
			{
				public Inner Value = new( 1 );

			-	public record Inner( int A )
			+	public record Inner( int A, int B = 2 )
				{
				}
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 1, Value.A );
			+		Assert.AreEqual( 2, Value.B );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedRecordParameterDefaultWithoutBody()
	{
		// The record has no body to generate methods in, so it gets one, keeping the line numbers after it

		var result = await HotloadFixtureAsync(
			"""
			public class AddedRecordParameterDefaultWithoutBody : IHotloadTestFixture
			{
				public Inner Value = new( 1 );

			-	public record Inner( int A );
			+	public record Inner( int A, int B = 2, string C = "c" );
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 2, Value.B );
			+		Assert.AreEqual( "c", Value.C );
			+		Assert.AreEqual( 11, new System.Diagnostics.StackTrace( true ).GetFrame( 0 ).GetFileLineNumber() );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedRecordStructParameterDefault()
	{
		var result = await HotloadFixtureAsync(
			"""
			public class AddedRecordStructParameterDefault : IHotloadTestFixture
			{
				public Inner Value = new( 1 );

			-	public record struct Inner( int A );
			+	public record struct Inner( int A, int B = 2 );
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 1, Value.A );
			+		Assert.AreEqual( 2, Value.B );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedRecordParameterWithoutDefault()
	{
		// Without a default, the parameter is always passed in like a field assigned in a constructor,
		// so existing instances get the default value without a warning

		var result = await HotloadFixtureAsync(
			"""
			public class AddedRecordParameterWithoutDefault : IHotloadTestFixture
			{
			-	public Inner Value = new( 1 );
			+	public Inner Value = new( 1, 5 );

			-	public record Inner( int A );
			+	public record Inner( int A, int B );
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 1, Value.A );
			+		Assert.AreEqual( 0, Value.B );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedFieldsInOneDeclaration()
	{
		var result = await HotloadFixtureAsync(
			"""
			public class AddedFieldsInOneDeclaration : IHotloadTestFixture
			{
			+	public int A = 1, B, C = 3;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 1, A );
			+		Assert.AreEqual( 0, B );
			+		Assert.AreEqual( 3, C );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedFieldArrayInitializer()
	{
		// { 1, 2, 3 } is only allowed in a declaration, so the generated method needs to add "new int[]"

		var result = await HotloadFixtureAsync(
			"""
			public class AddedFieldArrayInitializer : IHotloadTestFixture
			{
			+	public int[] Values = { 1, 2, 3 };
			+	public int[,] Grid = { { 1, 2 }, { 3, 4 } };
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, Values );
			+		Assert.AreEqual( 4, Grid[1, 1] );
			+	}
			}
			""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedFieldMultiLineInitializer()
	{
		// The generated method is copied onto one line, so comments and line breaks need replacing

		var result = await HotloadFixtureAsync(
			""""
			public class AddedFieldMultiLineInitializer : IHotloadTestFixture
			{
			+	public int Sum = 1 // one
			+		+ 2 /* two
			+		*/ + 3;
			+
			+	public string Text = """
			+		Hello
			+		World
			+		""";
			+
			+	public string Verbatim = @"A
			+B";
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 6, Sum );
			+		Assert.AreEqual( "Hello\nWorld", Text );
			+		Assert.AreEqual( "A\nB", Verbatim );
			+	}
			}
			"""" );

		Assert.IsFalse( result.HasWarnings );
	}

	[TestMethod]
	public async Task AddedFieldKeepsLineNumbers()
	{
		// Generated methods end with a #line directive, so line numbers after them don't change

		await HotloadFixtureAsync(
			"""
			public class AddedFieldKeepsLineNumbers : IHotloadTestFixture
			{
			+	public int Added = 1;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 7, new System.Diagnostics.StackTrace( true ).GetFrame( 0 ).GetFileLineNumber() );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedFieldBeforeClosingBrace()
	{
		// The type ends on the same line as the field, so the generated method goes before the closing brace

		await HotloadFixtureAsync(
			"""
			public class Inner { public int Before = 1;
			+public int Added = 2; }
			-}

			public class AddedFieldBeforeClosingBrace : IHotloadTestFixture
			{
				public Inner Value = new();
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 2, Value.Added );
			+		Assert.AreEqual( 11, new System.Diagnostics.StackTrace( true ).GetFrame( 0 ).GetFileLineNumber() );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedFieldChangedType()
	{
		// Hotload treats a field that changed type as new, so it needs an initializer too

		await HotloadFixtureAsync(
			"""
			public class AddedFieldChangedType : IHotloadTestFixture
			{
			-	public int Health = 50;
			+	public float Health = 100f;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 100f, Health );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedBaseTypeInitializer()
	{
		// Fields of a new base type are new for existing instances of the derived type

		await HotloadFixtureAsync(
			"""
			using System.Collections.Generic;

			+public class AddedBase
			+{
			+	public List<int> Items = new() { 1 };
			+}
			+
			-public class Player
			+public class Player : AddedBase
			{
			}

			public class AddedBaseTypeInitializer : IHotloadTestFixture
			{
				public Player Value = new();
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 1, Value.Items?.Count );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task ExistingBaseTypeInitializer()
	{
		// Fields of an existing base type are new for instances of a type that didn't derive from it before

		await HotloadFixtureAsync(
			"""
			public class ExistingBase
			{
				public int Health = 100;
			}

			-public class Player
			+public class Player : ExistingBase
			{
			}

			public class ExistingBaseTypeInitializer : IHotloadTestFixture
			{
				public ExistingBase Unrelated = new();
				public Player Value = new();
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 100, Value.Health );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedPartialPropertyInitializer()
	{
		// Only the implementing declaration of a partial property has the initializer

		await HotloadFixtureAsync(
			"""
			public partial class AddedPartialPropertyInitializer : IHotloadTestFixture
			{
			+	public partial int Value { get; set; }
			}

			public partial class AddedPartialPropertyInitializer
			{
			+	public partial int Value { get => field; set => field = value; } = 5;
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 5, Value );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedUnsafeFieldInitializer()
	{
		// The generated method needs the field's unsafe modifier

		await HotloadFixtureAsync(
			"""
			public struct Item
			{
				public int A;
				public int B;
			}

			public class AddedUnsafeFieldInitializer : IHotloadTestFixture
			{
			+	public unsafe int Size = sizeof( Item );
			+
			+	void IHotloadTestFixture.Test()
			+	{
			+		Assert.AreEqual( 8, Size );
			+	}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedFieldInitializerWarnings()
	{
		// The copied initializer can report different warnings to the original, like CS8603 instead of CS8625 here.
		// Those are disabled, then the file's own warning state is restored, so CS0169 stays disabled below.

		await HotloadFixtureAsync(
			"""
			#nullable enable
			#pragma warning disable CS0169

			public class AddedFieldInitializerWarnings : IHotloadTestFixture
			{
			#pragma warning disable CS8625
			+	public string Name = null;
			#pragma warning restore CS8625

				private int _neverUsed;

				void IHotloadTestFixture.Test()
				{
			+		Assert.IsNull( Name );
				}
			}
			""",
			warningsAsErrors: true );
	}
}
