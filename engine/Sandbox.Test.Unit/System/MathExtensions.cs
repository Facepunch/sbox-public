using System;

namespace SystemTest;

[TestClass]
public class MathExtensions
{
	[TestMethod]
	[DataRow( 0f, 0f, 0f )]
	[DataRow( 0f, 360f, 0f )]
	[DataRow( 180f, 180f, 0f )]
	[DataRow( 180f, -180f, 0f )]
	[DataRow( 0f, 179f, 179f )]
	[DataRow( 0f, 181f, -179f )]
	[DataRow( 179f, 181f, 2f )]
	[DataRow( 181f, 179f, -2f )]
	public void DeltaDegrees( float a, float b, float delta )
	{
		Assert.IsTrue( delta.AlmostEqual( MathX.DeltaDegrees( a, b ) ) );
	}

	[TestMethod]
	[DataRow( 0f, 0f, 0f )]
	[DataRow( 0f, MathF.Tau, 0f )]
	[DataRow( MathF.PI, MathF.PI, 0f )]
	[DataRow( MathF.PI, -MathF.PI, 0f )]
	[DataRow( 0f, MathF.PI * 0.99f, MathF.PI * 0.99f )]
	[DataRow( 0f, MathF.PI * 1.01f, -MathF.PI * 0.99f )]
	[DataRow( MathF.PI * 0.99f, MathF.PI * 1.01f, MathF.PI * 0.02f )]
	[DataRow( MathF.PI * 1.01f, MathF.PI * 0.99f, -MathF.PI * 0.02f )]
	public void DeltaRadians( float a, float b, float delta )
	{
		Assert.IsTrue( delta.AlmostEqual( MathX.DeltaRadians( a, b ) ) );
	}

	[TestMethod]
	[DataRow( 0f, 0f, 0f, 0f )]
	[DataRow( 0f, 0f, 1f, 0f )]
	[DataRow( 0f, 90f, 0f, 0f )]
	[DataRow( 0f, 90f, 0.5f, 45f )]
	[DataRow( 0f, 90f, 1f, 90f )]
	[DataRow( 0f, -90f, 0f, 0f )]
	[DataRow( 0f, -90f, 0.5f, -45f )]
	[DataRow( 0f, -90f, 1f, -90f )]
	[DataRow( 130f, -140f, 0.5f, 175f )]
	[DataRow( -140f, 130f, 0.5f, 175f )]
	public void LerpDegrees( float a, float b, float t, float result )
	{
		Assert.IsTrue( result.AlmostEqual( MathX.LerpDegrees( a, b, t ) ) );
	}

	[TestMethod]
	[DataRow( 0f, 0f, 0f, 0f )]
	[DataRow( 0f, 0f, 1f, 0f )]
	[DataRow( 0f, MathF.PI * 0.5f, 0f, 0f )]
	[DataRow( 0f, MathF.PI * 0.5f, 0.5f, MathF.PI * 0.25f )]
	[DataRow( 0f, MathF.PI * 0.5f, 1f, MathF.PI * 0.5f )]
	[DataRow( 0f, -MathF.PI * 0.5f, 0f, 0f )]
	[DataRow( 0f, -MathF.PI * 0.5f, 0.5f, -MathF.PI * 0.25f )]
	[DataRow( 0f, -MathF.PI * 0.5f, 1f, -MathF.PI * 0.5f )]
	[DataRow( MathF.PI * 0.7f, -MathF.PI * 0.9f, 0.5f, MathF.PI * 0.9f )]
	[DataRow( -MathF.PI * 0.9f, MathF.PI * 0.7f, 0.5f, MathF.PI * 0.9f )]
	public void LerpRadians( float a, float b, float t, float result )
	{
		Assert.IsTrue( result.AlmostEqual( MathX.LerpRadians( a, b, t ) ) );
	}

	[TestMethod]
	public void SolveQuadraticTest()
	{
		{
			// Test case: x² - 5x + 6 = 0 (Roots at x=2 and x=3)
			var roots = MathX.SolveQuadratic( 1, -5, 6 );
			Assert.AreEqual( 2, roots.Count );
			Assert.IsTrue( roots[0].AlmostEqual( 2f ) || roots[1].AlmostEqual( 2f ) );
			Assert.IsTrue( roots[0].AlmostEqual( 3f ) || roots[1].AlmostEqual( 3f ) );
		}

		{
			// Test case: x² + 4x + 4 = 0 (Single root at x=-2)
			var roots = MathX.SolveQuadratic( 1, 4, 4 );
			Assert.AreEqual( 1, roots.Count );
			Assert.IsTrue( roots[0].AlmostEqual( -2f ) );
		}

		{
			// Test case: x² + 1 = 0 (No real roots)
			var roots = MathX.SolveQuadratic( 1, 0, 1 );
			Assert.AreEqual( 0, roots.Count );
		}

		{
			// Test case: 0x² + 2x - 8 = 0 (Linear equation x = 4)
			var roots = MathX.SolveQuadratic( 0f, 2f, -8f );
			Assert.AreEqual( 1, roots.Count );
			Assert.IsTrue( roots[0].AlmostEqual( 4f ) );
		}

		{
			// Test case: 0x² + 0x + 0 = 0 (Infinite solutions, expect empty list)
			var roots = MathX.SolveQuadratic( 0f, 0f, 0f );
			Assert.AreEqual( 0, roots.Count );
		}
	}

	[TestMethod]
	public void SolveCubicTest()
	{
		{
			// Test case: x³ - 6x² + 11x - 6 = 0 (Roots at x=1, x=2, x=3)
			var roots = MathX.SolveCubic( 1f, -6f, 11f, -6f );
			Assert.AreEqual( 3, roots.Count );
			Assert.IsTrue( roots[0].AlmostEqual( 1f ) || roots[1].AlmostEqual( 1f ) || roots[2].AlmostEqual( 1f ) );
			Assert.IsTrue( roots[0].AlmostEqual( 2f ) || roots[1].AlmostEqual( 2f ) || roots[2].AlmostEqual( 2f ) );
			Assert.IsTrue( roots[0].AlmostEqual( 3f ) || roots[1].AlmostEqual( 3f ) || roots[2].AlmostEqual( 3f ) );
		}

		{
			// Test case: x³ - 4x² + 5x - 2 = 0 (Roots at x=1 and x=2)
			var roots = MathX.SolveCubic( 1f, -4f, 5f, -2f );
			Assert.AreEqual( 2, roots.Count );
			Assert.IsTrue( roots[0].AlmostEqual( 1f ) || roots[1].AlmostEqual( 1f ) );
			Assert.IsTrue( roots[0].AlmostEqual( 2f ) || roots[1].AlmostEqual( 2f ) );
		}

		{
			// Test case: x³ + x² + x + 1 = 0 (Single real root at x=-1)
			var roots = MathX.SolveCubic( 1f, 1f, 1f, 1f );
			Assert.AreEqual( 1, roots.Count );
			Assert.IsTrue( roots[0].AlmostEqual( -1f ) );
		}

		{
			// Test case: x³ - x = 0 (Roots at x=0, x=1, x=-1)
			var roots = MathX.SolveCubic( 1f, 0f, -1f, 0f );
			Assert.AreEqual( 3, roots.Count );
			Assert.IsTrue( roots[0].AlmostEqual( 0f ) || roots[1].AlmostEqual( 0f ) || roots[2].AlmostEqual( 0f ) );
			Assert.IsTrue( roots[0].AlmostEqual( 1f ) || roots[1].AlmostEqual( 1f ) || roots[2].AlmostEqual( 1f ) );
			Assert.IsTrue( roots[0].AlmostEqual( -1f ) || roots[1].AlmostEqual( -1f ) || roots[2].AlmostEqual( -1f ) );
		}

		{
			// Test case: 0x³ + x² - 1 = 0 (Quadratic equation x² -1 = 0)
			var roots = MathX.SolveCubic( 0f, 1f, 0f, -1f );
			Assert.AreEqual( 2, roots.Count );
			Assert.IsTrue( roots[0].AlmostEqual( 1f ) || roots[1].AlmostEqual( 1f ) );
			Assert.IsTrue( roots[0].AlmostEqual( -1f ) || roots[1].AlmostEqual( -1f ) );
		}

		{
			// Test case: x³ + 1 = 0 (Single real root at x=-1)
			var roots = MathX.SolveCubic( 1f, 0f, 0f, 1f );
			Assert.AreEqual( 1, roots.Count );
			Assert.IsTrue( roots[0].AlmostEqual( -1f ) );
		}
	}
}
