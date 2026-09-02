using Sandbox.UI;
using System;
using System.Globalization;

namespace UITests.Parsers;

/// <summary>
/// Style values are CSS, so they read the same whatever culture the machine is set to. A comma
/// in a number is a group separator to the invariant culture, not a decimal point - taking one
/// silently turns a value written out on a comma-decimal machine into a number a thousand times
/// too big rather than rejecting it.
///
/// This is what the launcher's staggered list ran into: an animation-delay of "0,500s" became a
/// 500 second delay, so a project row sat at opacity zero for eight minutes.
/// </summary>
[TestClass]
public class CultureParseTest
{
	/// <summary>
	/// A grouped number isn't a CSS number. Better to drop the declaration than to read it as
	/// something a thousand times larger.
	/// </summary>
	[DataRow( "0,500s" )]
	[DataRow( "1,250s" )]
	[DataRow( "2,000s" )]
	[TestMethod]
	public void GroupSeparatorIsNotADecimalPoint( string value )
	{
		var sheet = StyleParser.ParseSheet( $".one {{ animation-delay: {value}; }}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		var delay = sheet.Nodes[0].Styles.AnimationDelay;

		// Unset, or zero - anything but the whole-seconds reading
		Assert.IsTrue( delay is null or 0.0f, $"'{value}' parsed as {delay} seconds" );
	}

	/// <summary>
	/// The same value written with a decimal point is the fraction of a second it looks like.
	/// </summary>
	[DataRow( "0.500s", 0.5f )]
	[DataRow( "0.025s", 0.025f )]
	[DataRow( "250ms", 0.25f )]
	[TestMethod]
	public void DecimalPointParses( string value, float expected )
	{
		var sheet = StyleParser.ParseSheet( $".one {{ animation-delay: {value}; }}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( expected, sheet.Nodes[0].Styles.AnimationDelay );
	}

	/// <summary>
	/// Code that builds a style value has to write it invariantly, because the parser reads it
	/// invariantly. Running the launcher's stagger under a comma-decimal culture is the check.
	/// </summary>
	[TestMethod]
	public void StaggerDelaySurvivesACommaDecimalCulture()
	{
		var previous = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = new CultureInfo( "fr-FR" );

			for ( var index = 0; index < 30; index++ )
			{
				// Same as LauncherWindow.Stagger
				var written = FormattableString.Invariant( $"{index * 0.025f:0.000}s" );

				var sheet = StyleParser.ParseSheet( $".one {{ animation-delay: {written}; }}" );
				var delay = sheet.Nodes[0].Styles.AnimationDelay;

				Assert.AreEqual( index * 0.025f, delay.Value, 0.0001f, $"row {index} wrote '{written}'" );

				// Nobody waits on a list that takes this long to turn up
				Assert.IsTrue( delay.Value < 1.0f, $"row {index} would be hidden for {delay} seconds" );
			}
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}
}
