using Sandbox.Utility;

namespace SystemTest;

[TestClass]
public class EasingTest
{
	[TestMethod]
	public void CubicBezierEndpoints()
	{
		var f = Easing.CubicBezier( 0.16f, 1f, 0.3f, 1f );

		Assert.AreEqual( 0f, f( 0f ) );
		Assert.AreEqual( 1f, f( 1f ) );

		// out of range clamps to 0/1
		Assert.AreEqual( 0f, f( -0.5f ) );
		Assert.AreEqual( 1f, f( 1.5f ) );
	}

	[TestMethod]
	public void CubicBezierLinear()
	{
		// (0,0, 1,1) is the linear identity curve
		var f = Easing.CubicBezier( 0f, 0f, 1f, 1f );

		Assert.AreEqual( 0.25f, f( 0.25f ), 0.001f );
		Assert.AreEqual( 0.5f, f( 0.5f ), 0.001f );
		Assert.AreEqual( 0.75f, f( 0.75f ), 0.001f );
	}

	[TestMethod]
	public void CubicBezierSymmetric()
	{
		// symmetric curve must return 0.5 at t=0.5
		var f = Easing.CubicBezier( 0.42f, 0f, 0.58f, 1f );
		Assert.AreEqual( 0.5f, f( 0.5f ), 0.001f );

		f = Easing.CubicBezier( 0.25f, 0.1f, 0.75f, 0.9f );
		Assert.AreEqual( 0.5f, f( 0.5f ), 0.001f );
	}

	[TestMethod]
	public void GetFunctionCubicBezier()
	{
		var f = Easing.GetFunction( "cubic-bezier(0.16, 1, 0.3, 1)" );

		Assert.IsNotNull( f );
		Assert.AreEqual( 0f, f( 0f ) );
		Assert.AreEqual( 1f, f( 1f ) );

		Assert.IsTrue( Easing.TryGetFunction( "cubic-bezier(0.16, 1, 0.3, 1)", out var g ) );
		Assert.IsNotNull( g );

		// malformed strings fall through
		Assert.IsFalse( Easing.TryGetFunction( "cubic-bezier(0.1, 0.2)", out _ ) );
		Assert.IsFalse( Easing.TryGetFunction( "cubic-bezier(a, b, c, d)", out _ ) );
	}

	[TestMethod]
	public void GetFunctionNamedStillWorks()
	{
		Assert.IsTrue( Easing.TryGetFunction( "ease-in", out _ ) );
		Assert.IsTrue( Easing.TryGetFunction( "linear", out _ ) );
		Assert.IsFalse( Easing.TryGetFunction( "not-a-real-easing", out _ ) );
	}

	[TestMethod]
	public void StepStartEnd()
	{
		Assert.AreEqual( 0f, Easing.StepStart( 0f ) );
		Assert.AreEqual( 1f, Easing.StepStart( 0.0001f ) );
		Assert.AreEqual( 1f, Easing.StepStart( 1f ) );

		Assert.AreEqual( 0f, Easing.StepEnd( 0f ) );
		Assert.AreEqual( 0f, Easing.StepEnd( 0.9999f ) );
		Assert.AreEqual( 1f, Easing.StepEnd( 1f ) );

		var start = Easing.GetFunction( "step-start" );
		Assert.AreEqual( 0f, start( 0f ) );
		Assert.AreEqual( 1f, start( 0.5f ) );

		var end = Easing.GetFunction( "step-end" );
		Assert.AreEqual( 0f, end( 0.5f ) );
		Assert.AreEqual( 1f, end( 1f ) );
	}

	[TestMethod]
	public void StepsEnd()
	{
		var f = Easing.Steps( 4 );

		Assert.AreEqual( 0f, f( 0f ) );
		Assert.AreEqual( 0f, f( 0.24f ), 0.001f );
		Assert.AreEqual( 0.25f, f( 0.25f ), 0.001f );
		Assert.AreEqual( 0.5f, f( 0.5f ), 0.001f );
		Assert.AreEqual( 0.75f, f( 0.99f ), 0.001f );
		Assert.AreEqual( 1f, f( 1f ) );
	}

	[TestMethod]
	public void StepsStart()
	{
		var f = Easing.Steps( 4, true );

		Assert.AreEqual( 0f, f( 0f ) );
		Assert.AreEqual( 0.25f, f( 0.0001f ), 0.001f );
		Assert.AreEqual( 0.25f, f( 0.25f ), 0.001f );
		Assert.AreEqual( 0.5f, f( 0.26f ), 0.001f );
		Assert.AreEqual( 1f, f( 1f ) );
	}

	[TestMethod]
	public void GetFunctionSteps()
	{
		var f = Easing.GetFunction( "steps(4, end)" );
		Assert.IsNotNull( f );
		Assert.AreEqual( 0.5f, f( 0.5f ), 0.001f );

		Assert.IsTrue( Easing.TryGetFunction( "steps(4)", out _ ) );
		Assert.IsTrue( Easing.TryGetFunction( "steps(4, start)", out _ ) );
		Assert.IsTrue( Easing.TryGetFunction( "steps(2, jump-end)", out _ ) );

		Assert.IsFalse( Easing.TryGetFunction( "steps()", out _ ) );
		Assert.IsFalse( Easing.TryGetFunction( "steps(0)", out _ ) );
		Assert.IsFalse( Easing.TryGetFunction( "steps(4, banana)", out _ ) );
	}
}
