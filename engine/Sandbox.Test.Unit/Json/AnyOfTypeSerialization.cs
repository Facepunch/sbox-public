namespace JsonTests;

public abstract class TestBaseClass
{
	public string Name { get; set; }
	public float Value { get; set; }
}

public class ConcreteA : TestBaseClass
{
	public string Extra { get; set; }
	public bool Flag { get; set; } = true;
}

public class ConcreteB : TestBaseClass
{
	public int Count { get; set; } = 9;
	public bool Enabled { get; set; }
}

[TestClass]
[DoNotParallelize]
public class AnyOfTypeSerialization
{
	[TestMethod]
	public void RoundTrip_ConcreteType()
	{
		var original = new AnyOfType<TestBaseClass>( new ConcreteA { Name = "A", Value = 1.5f, Extra = "hello", Flag = true } );
		var json = Json.Serialize( original );
		var result = Json.Deserialize<AnyOfType<TestBaseClass>>( json );

		Assert.IsTrue( result.HasValue );
		Assert.IsInstanceOfType<ConcreteA>( result.Value );

		var a = (ConcreteA)result.Value;
		Assert.AreEqual( "A", a.Name );
		Assert.AreEqual( 1.5f, a.Value );
		Assert.AreEqual( "hello", a.Extra );
		Assert.AreEqual( true, a.Flag );
	}

	[TestMethod]
	public void RoundTrip_DifferentConcreteType()
	{
		var original = new AnyOfType<TestBaseClass>( new ConcreteB { Name = "B", Value = 2.5f, Count = 7, Enabled = true } );
		var json = Json.Serialize( original );
		var result = Json.Deserialize<AnyOfType<TestBaseClass>>( json );

		Assert.IsTrue( result.HasValue );
		Assert.IsInstanceOfType<ConcreteB>( result.Value );

		var b = (ConcreteB)result.Value;
		Assert.AreEqual( "B", b.Name );
		Assert.AreEqual( 2.5f, b.Value );
		Assert.AreEqual( 7, b.Count );
		Assert.AreEqual( true, b.Enabled );
	}


	[TestMethod]
	public void ImplicitConversion_FromValue()
	{
		AnyOfType<TestBaseClass> wrapper = new ConcreteA { Name = "X" };

		Assert.IsTrue( wrapper.HasValue );
		Assert.AreEqual( "X", wrapper.Value.Name );
	}

	[TestMethod]
	public void ImplicitConversion_ToValue()
	{
		var wrapper = new AnyOfType<TestBaseClass>( new ConcreteA { Name = "Y" } );
		TestBaseClass value = wrapper;

		Assert.IsNotNull( value );
		Assert.AreEqual( "Y", value.Name );
	}

	[TestMethod]
	public void RoundTrip_SwitchType()
	{
		var jsonA = Json.Serialize( new AnyOfType<TestBaseClass>( new ConcreteA { Name = "A", Extra = "e" } ) );
		var jsonB = Json.Serialize( new AnyOfType<TestBaseClass>( new ConcreteB { Name = "B", Count = 3 } ) );

		var fromA = Json.Deserialize<AnyOfType<TestBaseClass>>( jsonA );
		var fromB = Json.Deserialize<AnyOfType<TestBaseClass>>( jsonB );

		Assert.IsInstanceOfType<ConcreteA>( fromA.Value );
		Assert.IsInstanceOfType<ConcreteB>( fromB.Value );
		Assert.AreEqual( "e", ((ConcreteA)fromA.Value).Extra );
		Assert.AreEqual( 3, ((ConcreteB)fromB.Value).Count );
	}
}
