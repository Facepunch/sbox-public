using Sandbox.Engine;
using Sandbox.UI;
using System;

namespace UITest;

[TestClass]
public partial class CalcTests
{
	[TestMethod]
	public void Add()
	{
		Assert.AreEqual( Length.Pixels( 2 ), Calc.Evaluate( "1px + 1px" ) );
		Assert.AreEqual( Length.Pixels( 161 ), Calc.Evaluate( "34px + 42px + 85px" ) );
	}

	[TestMethod]
	public void Multiply()
	{
		Assert.AreEqual( Length.Pixels( 148 ), Calc.Evaluate( "74px * 2px" ) );
		Assert.AreEqual( Length.Pixels( 592 ), Calc.Evaluate( "74px * 2px * 4px" ) );
	}

	[TestMethod]
	public void Subtract()
	{
		Assert.AreEqual( Length.Pixels( 0 ), Calc.Evaluate( "1px - 1px" ) );
		Assert.AreEqual( Length.Pixels( 7 ), Calc.Evaluate( "12px - 4px - 1px" ) );
	}

	[TestMethod]
	public void Divide()
	{
		Assert.AreEqual( Length.Pixels( 2.5f ), Calc.Evaluate( "5px / 2px" ) );
		Assert.AreEqual( Length.Pixels( 6.25f ), Calc.Evaluate( "50px / 2px / 4px" ) );
	}

	[TestMethod]
	public void DivideByZero()
	{
		Assert.ThrowsException<DivideByZeroException>( () => Calc.Evaluate( "82px / 0px" ) );
	}

	[TestMethod]
	public void Filtering()
	{
		Assert.AreEqual( Length.Pixels( 2 ), Calc.Evaluate( "calc( 1px + 1px )" ) );

		Assert.AreEqual( Length.Pixels( 2 ), Calc.Evaluate( "1px + 1px  " ) );
		Assert.AreEqual( Length.Pixels( 2 ), Calc.Evaluate( "  1px + 1px" ) );
		Assert.AreEqual( Length.Pixels( 2 ), Calc.Evaluate( "  1px + 1px  " ) );

		Assert.AreEqual( Length.Pixels( 2 ), Calc.Evaluate( "           1px            +              1px     " ) );

		Assert.AreEqual( Length.Pixels( 2 ), Calc.Evaluate( "( 1px + 1px )" ) );
		Assert.AreEqual( Length.Pixels( 3 ), Calc.Evaluate( "( ( 1px + 1px ) + 1px )" ) );
	}

	[TestMethod]
	public void CalcConstant()
	{
		// The <calc-constant> CSS data type represents well-defined constants such as e and pi.
		// Rather than require authors to manually type out several digits of these mathematical
		// constants or calculate them, a few of them are provided directly by CSS for convenience.

		Assert.AreEqual( float.PositiveInfinity, Calc.Evaluate( "infinity" ) );
		Assert.AreEqual( float.NegativeInfinity, Calc.Evaluate( "-infinity" ) );

		Assert.AreEqual( float.E, Calc.Evaluate( "e" ) );
		Assert.AreEqual( float.Pi, Calc.Evaluate( "pi" ) );

		Assert.AreEqual( float.NaN, Calc.Evaluate( "nan" ) );

		Assert.AreEqual( float.Pi * 2f, Calc.Evaluate( "pi * 2" ) );
	}

	[TestMethod]
	public void Invalid()
	{
		Assert.ThrowsException<Exception>( () => Calc.Evaluate( "82px ^ 0px" ) );
		Assert.ThrowsException<Exception>( () => Calc.Evaluate( "82px 0px" ) );
	}

	[TestMethod]
	public void Nesting()
	{
		Assert.AreEqual( Length.Pixels( 6 ), Calc.Evaluate( "calc( 1px + 1px + calc( 1px + 1px + calc( 1px + 1px ) ) )" ) );
	}

	[TestMethod]
	public void MixedUnits()
	{
		Assert.AreEqual( Length.Pixels( 54 ), Calc.Evaluate( "50% + 4px", 100f ) );
	}

	[TestMethod]
	public void Percentages()
	{
		// 100px size
		Assert.AreEqual( Length.Pixels( 75 ), Calc.Evaluate( "50% + 25%", 100f ) );
		Assert.AreEqual( Length.Pixels( 25 ), Calc.Evaluate( "50% - 25%", 100f ) );

		// 200px size
		Assert.AreEqual( Length.Pixels( 150 ), Calc.Evaluate( "50% + 25%", 200f ) );
		Assert.AreEqual( Length.Pixels( 50 ), Calc.Evaluate( "50% - 25%", 200f ) );
	}

	[TestMethod]
	public void Parse()
	{
		var parsedLength = Length.Parse( "calc( 1px + 1px )" );

		Assert.AreEqual( LengthUnit.Expression, parsedLength?.Unit );
		Assert.AreEqual( 2.0f, parsedLength.Value.GetPixels( 1.0f ) );
	}

	[TestMethod]
	public void Variables()
	{
		var style = """
			$width: 100px;

			.test {
				width: calc( $width * 4 );
			}
		""";

		GlobalContext.Current.FileMount = new AggregateFileSystem();
		FileSystem.Mounted.Mount( new LocalFileSystem( Environment.CurrentDirectory ) );

		var sheet = StyleSheet.FromString( style );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 400f, sheet.Nodes.First().Styles.Width.Value.GetPixels( 1.0f ) );
		Assert.AreEqual( LengthUnit.Expression, sheet.Nodes.First().Styles.Width.Value.Unit );
	}

	[TestMethod]
	public void Literals()
	{
		Assert.AreEqual( Length.Pixels( 100 ), Calc.Evaluate( "100px", 1f ) );
		Assert.AreEqual( Length.Pixels( 1000 ), Calc.Evaluate( "1000px", 1f ) );

		Assert.AreEqual( Length.Pixels( 512f ), Calc.Evaluate( "100%", 512f ) );
	}
}
