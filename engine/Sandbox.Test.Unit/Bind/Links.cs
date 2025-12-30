using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestBind;

[TestClass]
public class TwoWayBind
{
	public string Primary { get; set; }
	public string Secondary { get; set; }

	[TestMethod]
	public void TwoWay()
	{
		Primary = "Dog";
		Secondary = "Cat";

		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( this, "Primary" ).From( this, "Secondary" );

		Assert.AreEqual( "Dog", Primary );
		Assert.AreEqual( "Cat", Secondary );

		bind.Tick();

		Assert.AreEqual( "Cat", Primary );
		Assert.AreEqual( "Cat", Secondary );

		Secondary = "Dog";

		bind.Tick();

		Assert.AreEqual( "Dog", Primary );
		Assert.AreEqual( "Dog", Secondary );

		Primary = "Horse";

		bind.Tick();

		Assert.AreEqual( "Horse", Primary );
		Assert.AreEqual( "Horse", Secondary );
	}

	[TestMethod]
	public void Priority()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( this, "Primary" ).From( this, "Secondary" );

		Primary = "Dog";
		Secondary = "Cat";

		bind.Tick();

		Assert.AreEqual( "Cat", Primary );
		Assert.AreEqual( "Cat", Secondary );

		Primary = "Dog";
		Secondary = "Cat";

		bind.Tick();

		Assert.AreEqual( "Dog", Primary );
		Assert.AreEqual( "Dog", Secondary );

		Secondary = "Horse";

		bind.Tick();

		Assert.AreEqual( "Horse", Primary );
		Assert.AreEqual( "Horse", Secondary );
	}

	[TestMethod]
	public void PriorityReadOnly()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( this, "Primary" ).ReadOnly().From( this, "Secondary" );

		Primary = "Dog";
		Secondary = "Cat";

		bind.Tick();

		Assert.AreEqual( "Cat", Primary );
		Assert.AreEqual( "Cat", Secondary );

		Primary = "Dog";
		Secondary = "Wolf";

		bind.Tick();

		Assert.AreEqual( "Wolf", Primary );
		Assert.AreEqual( "Wolf", Secondary );

		Primary = "Horse";

		bind.Tick();

		Assert.AreEqual( "Horse", Primary );
		Assert.AreEqual( "Wolf", Secondary );
	}

	[TestMethod]
	public void ObjectBased()
	{
		var obj = "Hello Gordon";
		Primary = "Dog";
		Secondary = "Cat";

		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( this, "Primary" ).FromObject( obj );

		bind.Tick();

		Assert.AreEqual( "Hello Gordon", Primary );

		Primary = "Horse";

		bind.Tick();

		Assert.AreEqual( "Horse", Primary ); // primary retains its changed value, because object didn't change
	}

	[TestMethod]
	public void PriorityNulls()
	{
		{
			Primary = null;
			Secondary = "Cat";

			var bind = new Sandbox.Bind.BindSystem( "test" );
			bind.Build.Set( this, "Primary" ).From( this, "Secondary" );

			// should prioritize the initial non null value

			bind.Tick();

			Assert.AreEqual( "Cat", Primary );
			Assert.AreEqual( "Cat", Secondary );

			Secondary = null;

			// should prioritize the change

			bind.Tick();

			Assert.AreEqual( null, Primary );
			Assert.AreEqual( null, Secondary );

			Primary = "Cat";

			// should prioritize the change

			bind.Tick();

			Assert.AreEqual( "Cat", Primary );
			Assert.AreEqual( "Cat", Secondary );
		}

		{
			Primary = "Cat";
			Secondary = null;

			var bind = new Sandbox.Bind.BindSystem( "test" );
			bind.Build.Set( this, "Primary" ).From( this, "Secondary" );

			// should prioritize the initial non null value

			bind.Tick();

			Assert.AreEqual( "Cat", Primary );
			Assert.AreEqual( "Cat", Secondary );

			Secondary = null;

			// should prioritize the change

			bind.Tick();

			Assert.AreEqual( null, Primary );
			Assert.AreEqual( null, Secondary );

			Primary = "Cat";

			// should prioritize the change

			bind.Tick();

			Assert.AreEqual( "Cat", Primary );
			Assert.AreEqual( "Cat", Secondary );
		}

	}
}
