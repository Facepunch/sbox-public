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
}
