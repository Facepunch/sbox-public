using System;

namespace HotloadTests;

/// <summary>
/// Copying fields and properties to upgraded instances, including when types change shape.
/// </summary>
[TestClass]
public class MemberTest : HotloadFixtureTestBase
{
	[TestMethod]
	public async Task StaticField()
	{
		await HotloadFixtureAsync(
			"""
			public class StaticField : IHotloadTestFixture
			{
				public static int StaticIntField;

				void IHotloadTestFixture.Test()
				{
			-		StaticIntField = 81;
			+		Assert.AreEqual( 81, StaticIntField );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task InstanceField()
	{
		await HotloadFixtureAsync(
			"""
			public class InstanceField : IHotloadTestFixture
			{
				public int IntField;
			-	public int RemovedField;

				void IHotloadTestFixture.Test()
				{
			-		IntField = 32;
			+		Assert.AreEqual( 32, IntField );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task AutoProperty()
	{
		await HotloadFixtureAsync(
			"""
			public class AutoProperty : IHotloadTestFixture
			{
				public int IntProperty { get; set; }

				void IHotloadTestFixture.Test()
				{
			-		IntProperty = 22;
			+		Assert.AreEqual( 22, IntProperty );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task EnumField()
	{
		await HotloadFixtureAsync(
			"""
			public enum TestEnum { A, B, C, D }

			public class EnumField : IHotloadTestFixture
			{
				public TestEnum EnumValue;

				void IHotloadTestFixture.Test()
				{
			-		EnumValue = TestEnum.C;
			+		Assert.AreEqual( TestEnum.C, EnumValue );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task EnumArray()
	{
		await HotloadFixtureAsync(
			"""
			public enum TestEnum { A, B, C, D }

			public class EnumArray : IHotloadTestFixture
			{
				public TestEnum[] Values;

				void IHotloadTestFixture.Test()
				{
			-		Values = [TestEnum.B];
			+		Assert.AreEqual( 1, Values.Length );
			+		Assert.AreEqual( TestEnum.B, Values[0] );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task StaticReadonlyField()
	{
		// The new type's static constructor creates its own instance, which is upgraded in place

		await HotloadFixtureAsync(
			"""
			public class StaticReadonlyField : IHotloadTestFixture
			{
				public static readonly StaticReadonlyField Shared = new();

				public int IntValue;

				void IHotloadTestFixture.Test()
				{
			-		Shared.IntValue = 20;
			+		Assert.AreEqual( 20, Shared.IntValue );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task InstanceReadonlyField()
	{
		await HotloadFixtureAsync(
			"""
			public class InstanceReadonlyField : IHotloadTestFixture
			{
				public readonly int IntValue;

				public InstanceReadonlyField()
				{
			-		IntValue = 21;
			+		// Not called for the upgraded instance
			+		IntValue = 99;
				}

				void IHotloadTestFixture.Test()
				{
					Assert.AreEqual( 21, IntValue );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task FieldTypeChanged()
	{
		await HotloadFixtureAsync(
			"""
			public class FieldTypeChanged : IHotloadTestFixture
			{
				public class ExampleClass1
				{
					public int Field;
				}

				public class ExampleClass2
				{
					public int Field;
				}

			-	public ExampleClass1 Field = new();
			+	public ExampleClass2 Field;

				void IHotloadTestFixture.Test()
				{
			+		Assert.IsNull( Field );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task AddedBaseClass()
	{
		await HotloadFixtureAsync(
			"""
			public class AddedBaseClass : IHotloadTestFixture
			{
				public class TestBaseClass
				{
					public int Field1;
				}

			-	public sealed class TestDerivingClass
			+	public sealed class TestDerivingClass : TestBaseClass
				{
					public int Field2;
				}

				public TestBaseClass BaseClassField;
				public TestDerivingClass DerivingClassField;

				void IHotloadTestFixture.Test()
				{
			-		BaseClassField = new TestBaseClass { Field1 = 7 };
			-		DerivingClassField = new TestDerivingClass { Field2 = 21 };
			+		Assert.AreEqual( 7, BaseClassField.Field1 );
			+		Assert.AreEqual( 0, DerivingClassField.Field1 );
			+		Assert.AreEqual( 21, DerivingClassField.Field2 );
				}
			}
			""" );
	}

	/// <summary>
	/// If types A, B derive C, but type A is changed to derive B, hotload should still process fields from
	/// type C when processing instances of type A.
	/// </summary>
	[TestMethod]
	public async Task InsertedBaseType()
	{
		await HotloadFixtureAsync(
			"""
			public class InsertedBaseType : IHotloadTestFixture
			{
			-	public class A : C
			+	public class A : B
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

				public A Value;

				void IHotloadTestFixture.Test()
				{
			-		Value = new A { AField = 10, CField = 20 };
			+		Assert.AreEqual( 10, Value.AField );
			+		Assert.AreEqual( 20, Value.CField );
				}
			}
			""" );
	}

	/// <summary>
	/// If type A derives B, and B derive C, but type A is changed to derive C directly, hotload should still
	/// process fields from type C when processing instances of type A.
	/// </summary>
	[TestMethod]
	public async Task RemovedBaseType()
	{
		await HotloadFixtureAsync(
			"""
			public class RemovedBaseType : IHotloadTestFixture
			{
			-	public class A : B
			+	public class A : C
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

				public A Value;

				void IHotloadTestFixture.Test()
				{
			-		Value = new A { AField = 10, BField = 15, CField = 20 };
			+		Assert.AreEqual( 10, Value.AField );
			+		Assert.AreEqual( 20, Value.CField );
				}
			}
			""" );
	}

	/// <summary>
	/// If type A derives B&lt;T1&gt;, but changes to derive B&lt;T2&gt;, hotload should try to process
	/// fields that are common to B&lt;T1&gt; and B&lt;T2&gt;.
	/// </summary>
	[TestMethod]
	public async Task ChangedBaseTypeGenericArgument()
	{
		await HotloadFixtureAsync(
			"""
			public class ChangedBaseTypeGenericArgument : IHotloadTestFixture
			{
			-	public class A : B<int>
			+	public class A : B<string>
				{
					public int AField;
				}

				public class B<T>
				{
					public int BField1;
					public T BField2;
				}

				public A Value;

				void IHotloadTestFixture.Test()
				{
			-		Value = new A { AField = 10, BField1 = 15, BField2 = 20 };
			+		Assert.AreEqual( 10, Value.AField );
			+		Assert.AreEqual( 15, Value.BField1 );
			+		Assert.IsNull( Value.BField2 );
				}
			}
			""" );
	}

	/// <summary>
	/// Making sure that we don't get any stack overflows if a generic instance uses its definition as a type arg.
	/// </summary>
	[TestMethod]
	public async Task SelfReferencingGeneric()
	{
		await HotloadFixtureAsync(
			"""
			public class SelfReferencingGeneric : IHotloadTestFixture
			{
				public class Generic<T>
				{
					public T Value;
				}

				public Generic<Generic<int>> Value;

				void IHotloadTestFixture.Test()
				{
			-		Value = new() { Value = new() { Value = 123 } };
			+		Assert.AreEqual( 123, Value.Value.Value );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests <see cref="IHotloadManaged.Created"/> / <see cref="IHotloadManaged.Destroyed"/> being called.
	/// </summary>
	[TestMethod]
	public async Task HotloadManagedState()
	{
		await HotloadFixtureAsync(
			"""
			using System.Collections.Generic;
			using Sandbox;

			public class HotloadManagedState : IHotloadTestFixture, IHotloadManaged
			{
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

				void IHotloadTestFixture.Test()
				{
			-		Field1 = "World";
			+		Assert.AreEqual( "World", Field1 );
			+		Assert.AreEqual( "World", Field2 );
				}
			}
			""" );
	}

	/// <summary>
	/// Tests <see cref="IHotloadManaged.Failed"/> being called.
	/// </summary>
	[TestMethod]
	public async Task HotloadManagedFailed()
	{
		using var fixture = await CreateFixtureAsync(
			"""
			using Sandbox;

			public class HotloadManagedFailed : IHotloadTestFixture
			{
			-	public class RemovedClass : IHotloadManaged
			-	{
			-		public HotloadManagedFailed Parent;
			-
			-		void IHotloadManaged.Failed()
			-		{
			-			Parent.FailureHandled = true;
			-		}
			-	}
			-
				public bool FailureHandled;
				public object RemovedInstance;

				void IHotloadTestFixture.Test()
				{
			-		RemovedInstance = new RemovedClass { Parent = this };
			+		Assert.IsNull( RemovedInstance );
				}
			}
			""" );

		Assert.IsFalse( (bool)GetField( fixture.OldInstance, "FailureHandled" ) );

		await fixture.HotloadAsync();

		// Failed is called on the instance that couldn't be upgraded, which tells its old parent

		Assert.IsTrue( (bool)GetField( fixture.OldInstance, "FailureHandled" ) );
	}

	[TestMethod]
	public async Task JsonSerializerOptionsField()
	{
		// The options cache metadata about the old types, which mustn't break serializing new ones

		await HotloadFixtureAsync(
			"""
			using System.Text.Json;

			public class JsonSerializerOptionsField : IHotloadTestFixture
			{
				public class ContainerClass
				{
					public SerializableClass ObjectProperty { get; set; }
				}

				public class SerializableClass
				{
					public int IntProperty { get; set; }
				}

				public static JsonSerializerOptions Options;

				void IHotloadTestFixture.Test()
				{
			-		Options = new JsonSerializerOptions();
			+		Assert.IsNotNull( Options );

					var instance = new ContainerClass { ObjectProperty = new SerializableClass { IntProperty = 23 } };

					Assert.AreEqual( "{\"ObjectProperty\":{\"IntProperty\":23}}", JsonSerializer.Serialize( instance, instance.GetType(), Options ) );
				}
			}
			""" );
	}

	[TestMethod]
	public async Task CompletedTask()
	{
		await HotloadFixtureAsync(
			"""
			using System.Threading.Tasks;

			public class CompletedTask : IHotloadTestFixture
			{
				public static Task<int> Pending;

			-	public static async Task<float> RemovedAsyncMethod( float arg )
			-	{
			-		await Task.Delay( 10 );
			-		return arg;
			-	}
			-
				public static async Task<int> AsyncMethod( int arg )
				{
					await Task.Delay( 10 );
					return arg;
				}

				void IHotloadTestFixture.Test()
				{
			-		Pending = AsyncMethod( 30 );
			-		Pending.Wait();
			-
					Assert.IsTrue( Pending.IsCompletedSuccessfully );
					Assert.AreEqual( 30, Pending.Result );
				}
			}
			""" );
	}

	/// <summary>
	/// Definitely persist anonymous types that don't change members.
	/// </summary>
	[TestMethod]
	public async Task AnonymousTypeUnchanged()
	{
		await HotloadFixtureAsync(
			"""
			public class AnonymousTypeUnchanged : IHotloadTestFixture
			{
				public object Value;

				public void Create()
				{
					Value = new { Property1 = "World" };
				}

				void IHotloadTestFixture.Test()
				{
			-		Create();
			-
					Assert.AreEqual( "{ Property1 = World }", Value?.ToString() );
				}
			}
			""" );
	}

	/// <summary>
	/// Anonymous type substitution looks for identical property names / types, even if the compiler-generated
	/// name of the type has changed.
	/// </summary>
	[TestMethod]
	public async Task AnonymousTypeReordered()
	{
		await HotloadFixtureAsync(
			"""
			public class AnonymousTypeReordered : IHotloadTestFixture
			{
				public object Value;

				public void Create()
				{
			-		_ = new { Property2 = "World" };
					Value = new { Property3 = "World" };
			+		_ = new { Property2 = "World" };
				}

				void IHotloadTestFixture.Test()
				{
			-		Create();
			-
					Assert.AreEqual( "{ Property3 = World }", Value?.ToString() );
				}
			}
			""" );
	}

	/// <summary>
	/// We can't reliably handle adding / removing properties from anonymous types, just warn for now.
	/// </summary>
	private static async Task TestAnonymousTypeChange( string diff, [System.Runtime.CompilerServices.CallerMemberName] string testName = "" )
	{
		using var fixture = await CreateFixtureAsync( diff, testName: testName );

		var anonType = GetField( fixture.OldInstance, "Value" ).GetType().GetGenericTypeDefinition();
		var result = await fixture.HotloadAsync();

		Assert.IsTrue( result.Warnings.Any( x => x.Member == anonType ) );
	}

	/// <inheritdoc cref="TestAnonymousTypeChange" />
	[TestMethod]
	public async Task AnonymousTypeAddProperty()
	{
		await TestAnonymousTypeChange(
			"""
			public class AnonymousTypeAddProperty : IHotloadTestFixture
			{
				public object Value;

				public void Create()
				{
					Value = new
					{
						Property4 = "World",
			+			Property5 = 0
					};
				}

				void IHotloadTestFixture.Test()
				{
			-		Create();
			-		Assert.AreEqual( "{ Property4 = World }", Value?.ToString() );
			+		Assert.IsNull( Value );
				}
			}
			""" );
	}

	/// <inheritdoc cref="TestAnonymousTypeChange" />
	[TestMethod]
	public async Task AnonymousTypeRemoveProperty()
	{
		await TestAnonymousTypeChange(
			"""
			public class AnonymousTypeRemoveProperty : IHotloadTestFixture
			{
				public object Value;

				public void Create()
				{
					Value = new
					{
						Property6 = "World",
			-			Property7 = 0
					};
				}

				void IHotloadTestFixture.Test()
				{
			-		Create();
			-		Assert.AreEqual( "{ Property6 = World, Property7 = 0 }", Value?.ToString() );
			+		Assert.IsNull( Value );
				}
			}
			""" );
	}

	/// <inheritdoc cref="TestAnonymousTypeChange" />
	[TestMethod]
	public async Task AnonymousTypeChangeProperty()
	{
		await TestAnonymousTypeChange(
			"""
			public class AnonymousTypeChangeProperty : IHotloadTestFixture
			{
				public object Value;

				public void Create()
				{
					Value = new
					{
						Property8 = "World",
			-			Property9 = 0
			+			Property10 = 0
					};
				}

				void IHotloadTestFixture.Test()
				{
			-		Create();
			-		Assert.AreEqual( "{ Property8 = World, Property9 = 0 }", Value?.ToString() );
			+		Assert.IsNull( Value );
				}
			}
			""" );
	}
}
