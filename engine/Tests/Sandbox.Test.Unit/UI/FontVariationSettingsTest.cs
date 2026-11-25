using Sandbox.UI;
using System.Collections.Generic;

namespace UITests;

/// <summary>
/// Checks CSS axis syntax and interpolation without loading a font.
/// </summary>
[TestClass]
public class FontVariationSettingsTest
{
	/// <summary>
	/// CSS fills the same four properties that a panel can set directly.
	/// </summary>
	[TestMethod]
	public void NamedAxes()
	{
		Assert.IsTrue( FontVariationSettings.TryParse( "'wght' +5e+2, \"FILL\" .25, 'GRAD' -10, 'opsz' 32, 'XXXX' 9", out var settings ) );
		Assert.AreEqual( new FontVariationSettings { Fill = 0.25f, Weight = 500, Grade = -10, OpticalSize = 32 }, settings );

		Assert.IsTrue( FontVariationSettings.TryParse( "'FILL' 0, 'fill' 1", out settings ) );
		Assert.AreEqual( new FontVariationSettings { Fill = 0 }, settings );
		Assert.AreNotEqual( default( FontVariationSettings ), settings );
		Assert.AreNotEqual( default( FontVariationSettings ).GetHashCode(), settings.GetHashCode() );

		Assert.IsTrue( FontVariationSettings.TryParse( "  NoRmAl  ", out settings ) );
		Assert.AreEqual( default( FontVariationSettings ), settings );
	}

	/// <summary>
	/// One invalid tag or value invalidates the whole declaration.
	/// </summary>
	[DataTestMethod]
	[DataRow( "'FILL' 1," )]
	[DataRow( "'FILL' 1 'wght' 400" )]
	[DataRow( "FILL 1" )]
	[DataRow( "\"FILL' 1" )]
	[DataRow( "'FILL' 1f" )]
	[DataRow( "'FILL' 1." )]
	[DataRow( "'FILL' 1.e2" )]
	[DataRow( "'FILÉ' 1" )]
	[DataRow( "'FILL' 1e999" )]
	public void InvalidDeclaration( string css )
	{
		Assert.IsFalse( FontVariationSettings.TryParse( css, out _ ) );
	}

	/// <summary>
	/// Replaying a transition at different frame times reuses a bounded set of axis values.
	/// </summary>
	[TestMethod]
	public void Transition()
	{
		Assert.IsTrue( FontVariationSettings.TryParse( "'FILL' 0", out var from ) );
		Assert.IsTrue( FontVariationSettings.TryParse( "'FILL' 1", out var to ) );
		var values = new HashSet<FontVariationSettings>();
		for ( int i = 0; i <= 1000; i++ )
			values.Add( FontVariationSettings.Lerp( from, to, i / 1000f ) );
		Assert.AreEqual( 257, values.Count );

		Assert.IsTrue( FontVariationSettings.Lerp( from, to, 0.5f ).TryGetValue( "FILL", out var fill ) );
		Assert.AreEqual( 0.5f, fill );
		Assert.IsTrue( FontVariationSettings.TryParse( "'GRAD' 100", out var other ) );
		Assert.AreEqual( from, FontVariationSettings.Lerp( from, other, 0.4999f ) );
		Assert.AreEqual( other, FontVariationSettings.Lerp( from, other, 0.5f ) );
	}
}
