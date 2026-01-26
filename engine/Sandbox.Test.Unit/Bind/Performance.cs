using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Bind;

namespace TestBind;

/// <summary>
/// Not real tests, just indicative of relative performance
/// </summary>
[TestClass]
public class Performance
{
	const int Iterations = 1000000;

	[TestMethod]
	public void Create_Name()
	{
		var bind = new BindSystem( "UnitTest" );
		var target = new BindingTarget();

		for ( int i = 0; i < Iterations; i++ )
		{
			var source = PropertyProxy.Create( target, nameof( BindingTarget.One ) );

		}
	}

	[TestMethod]
	public void Create_Method()
	{
		var bind = new BindSystem( "UnitTest" );
		var target = new BindingTarget();

		for ( int i = 0; i < Iterations; i++ )
		{
			var source = new MethodProxy<string>( () => target.One, x => target.One = x );
		}
	}

	[TestMethod]
	public void ValueRead_Name()
	{
		var bind = new BindSystem( "UnitTest" );
		var target = new BindingTarget();
		var source = PropertyProxy.Create( target, nameof( BindingTarget.One ) );

		for ( int i = 0; i < Iterations; i++ )
		{
			var val = source.Value;
		}
	}

	[TestMethod]
	public void ValueRead_Method()
	{
		var bind = new BindSystem( "UnitTest" );
		var target = new BindingTarget();
		var source = new MethodProxy<string>( () => target.One, x => target.One = x );

		for ( int i = 0; i < Iterations; i++ )
		{
			var val = source.Value;
		}
	}

	[TestMethod]
	public void ValueWrite_Name()
	{
		var bind = new BindSystem( "UnitTest" );
		var target = new BindingTarget();
		var source = PropertyProxy.Create( target, nameof( BindingTarget.One ) );

		for ( int i = 0; i < Iterations; i++ )
		{
			source.Value = "Poops";
		}
	}

	[TestMethod]
	public void ValueWrite_Method()
	{
		var bind = new BindSystem( "UnitTest" );
		var target = new BindingTarget();
		var source = new MethodProxy<string>( () => target.One, x => target.One = x );

		for ( int i = 0; i < Iterations; i++ )
		{
			source.Value = "Poops";
		}
	}

	[TestMethod]
	public void Link_TwoWay()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		var target = new BindingTarget();

		for ( int i = 0; i < 1000; i++ )
		{
			bind.Build.Set( target, nameof( BindingTarget.One ) ).From( target, nameof( BindingTarget.Two ) );
		}

		for ( int i = 0; i < 1000; i++ )
		{
			bind.Tick();
		}
	}

	[TestMethod]
	public void Link_TwoWay_WithThrottling()
	{
		var bind = new Sandbox.Bind.BindSystem( "test" );
		bind.ThrottleUpdates = true;
		var target = new BindingTarget();

		for ( int i = 0; i < 1000; i++ )
		{
			bind.Build.Set( target, nameof( BindingTarget.One ) ).From( target, nameof( BindingTarget.Two ) );
		}

		for ( int i = 0; i < 1000; i++ )
		{
			bind.Tick();
		}
	}

	private sealed class BindingTarget
	{
		public string One { get; set; }
		public string Two { get; set; }
	}

}

