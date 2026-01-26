using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestBind;

[TestClass]
public class TwoWayBind
{
	[TestMethod]
	public void TwoWay()
	{
		var target = new BindingTarget
		{
			Primary = "Dog",
			Secondary = "Cat"
		};

		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( target, nameof( BindingTarget.Primary ) ).From( target, nameof( BindingTarget.Secondary ) );

		Assert.AreEqual( "Dog", target.Primary );
		Assert.AreEqual( "Cat", target.Secondary );

		bind.Tick();

		Assert.AreEqual( "Cat", target.Primary );
		Assert.AreEqual( "Cat", target.Secondary );

		target.Secondary = "Dog";

		bind.Tick();

		Assert.AreEqual( "Dog", target.Primary );
		Assert.AreEqual( "Dog", target.Secondary );

		target.Primary = "Horse";

		bind.Tick();

		Assert.AreEqual( "Horse", target.Primary );
		Assert.AreEqual( "Horse", target.Secondary );
	}

	[TestMethod]
	public void Priority()
	{
		var target = new BindingTarget();
		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( target, nameof( BindingTarget.Primary ) ).From( target, nameof( BindingTarget.Secondary ) );

		target.Primary = "Dog";
		target.Secondary = "Cat";

		bind.Tick();

		Assert.AreEqual( "Cat", target.Primary );
		Assert.AreEqual( "Cat", target.Secondary );

		target.Primary = "Dog";
		target.Secondary = "Cat";

		bind.Tick();

		Assert.AreEqual( "Dog", target.Primary );
		Assert.AreEqual( "Dog", target.Secondary );

		target.Secondary = "Horse";

		bind.Tick();

		Assert.AreEqual( "Horse", target.Primary );
		Assert.AreEqual( "Horse", target.Secondary );
	}

	[TestMethod]
	public void PriorityReadOnly()
	{
		var target = new BindingTarget();
		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( target, nameof( BindingTarget.Primary ) ).ReadOnly().From( target, nameof( BindingTarget.Secondary ) );

		target.Primary = "Dog";
		target.Secondary = "Cat";

		bind.Tick();

		Assert.AreEqual( "Cat", target.Primary );
		Assert.AreEqual( "Cat", target.Secondary );

		target.Primary = "Dog";
		target.Secondary = "Wolf";

		bind.Tick();

		Assert.AreEqual( "Wolf", target.Primary );
		Assert.AreEqual( "Wolf", target.Secondary );

		target.Primary = "Horse";

		bind.Tick();

		Assert.AreEqual( "Horse", target.Primary );
		Assert.AreEqual( "Wolf", target.Secondary );
	}

	[TestMethod]
	public void ObjectBased()
	{
		var obj = "Hello Gordon";
		var target = new BindingTarget
		{
			Primary = "Dog",
			Secondary = "Cat"
		};

		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.Build.Set( target, nameof( BindingTarget.Primary ) ).FromObject( obj );

		bind.Tick();

		Assert.AreEqual( "Hello Gordon", target.Primary );

		target.Primary = "Horse";

		bind.Tick();

		Assert.AreEqual( "Horse", target.Primary ); // primary retains its changed value, because object didn't change
	}

	[TestMethod]
	public void PriorityNulls()
	{
		{
			var target = new BindingTarget
			{
				Primary = null,
				Secondary = "Cat"
			};

			var bind = new Sandbox.Bind.BindSystem( "test" );
			bind.Build.Set( target, nameof( BindingTarget.Primary ) ).From( target, nameof( BindingTarget.Secondary ) );

			// should prioritize the initial non null value

			bind.Tick();

			Assert.AreEqual( "Cat", target.Primary );
			Assert.AreEqual( "Cat", target.Secondary );

			target.Secondary = null;

			// should prioritize the change

			bind.Tick();

			Assert.AreEqual( null, target.Primary );
			Assert.AreEqual( null, target.Secondary );

			target.Primary = "Cat";

			// should prioritize the change

			bind.Tick();

			Assert.AreEqual( "Cat", target.Primary );
			Assert.AreEqual( "Cat", target.Secondary );
		}

		{
			var target = new BindingTarget
			{
				Primary = "Cat",
				Secondary = null
			};

			var bind = new Sandbox.Bind.BindSystem( "test" );
			bind.Build.Set( target, nameof( BindingTarget.Primary ) ).From( target, nameof( BindingTarget.Secondary ) );

			// should prioritize the initial non null value

			bind.Tick();

			Assert.AreEqual( "Cat", target.Primary );
			Assert.AreEqual( "Cat", target.Secondary );

			target.Secondary = null;

			// should prioritize the change

			bind.Tick();

			Assert.AreEqual( null, target.Primary );
			Assert.AreEqual( null, target.Secondary );

			target.Primary = "Cat";

			// should prioritize the change

			bind.Tick();

			Assert.AreEqual( "Cat", target.Primary );
			Assert.AreEqual( "Cat", target.Secondary );
		}

	}

	private sealed class BindingTarget
	{
		public string Primary { get; set; }
		public string Secondary { get; set; }
	}
}
