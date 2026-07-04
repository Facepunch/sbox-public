using System;
using System.Globalization;

namespace SystemTests;

[TestClass]
public class NumberExtensionsTest
{
	/// <summary>
	/// Runs an assertion block under the invariant culture, so tests of
	/// culture-sensitive number formatting are deterministic regardless of the
	/// machine's locale.
	/// </summary>
	private static void WithInvariantCulture( Action action )
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

		try
		{
			action();
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	/// <summary>
	/// FormatBytes should pick the right suffix per magnitude, with the short
	/// format dropping the decimals.
	/// </summary>
	[TestMethod]
	public void FormatBytes()
	{
		WithInvariantCulture( () =>
		{
			Assert.AreEqual( "500b", 500.FormatBytes() );
			Assert.AreEqual( "2.00kb", 2048.FormatBytes() );
			Assert.AreEqual( "2kb", 2048.FormatBytes( shortFormat: true ) );
			Assert.AreEqual( "5.00mb", (5L * 1024 * 1024).FormatBytes() );
			Assert.AreEqual( "3.00gb", (3L * 1024 * 1024 * 1024).FormatBytes() );
		} );
	}

	/// <summary>
	/// Clamp should constrain values to the inclusive range.
	/// </summary>
	[TestMethod]
	public void Clamp()
	{
		Assert.AreEqual( 5, 3.Clamp( 5, 10 ) );
		Assert.AreEqual( 10, 30.Clamp( 5, 10 ) );
		Assert.AreEqual( 7, 7.Clamp( 5, 10 ) );
		Assert.AreEqual( 0.5f, 0.9f.Clamp( 0f, 0.5f ) );
	}

	/// <summary>
	/// FormatSeconds should use the compact "1w2d3h4m5s" style, only showing
	/// the largest relevant units.
	/// </summary>
	[TestMethod]
	public void FormatSeconds()
	{
		Assert.AreEqual( "45s", 45L.FormatSeconds() );
		Assert.AreEqual( "1m30s", 90L.FormatSeconds() );
		Assert.AreEqual( "1h1m40s", 3700L.FormatSeconds() );
	}

	/// <summary>
	/// FormatSecondsLong should spell out the units.
	/// </summary>
	[TestMethod]
	public void FormatSecondsLong()
	{
		Assert.AreEqual( "45 seconds", 45L.FormatSecondsLong() );
		Assert.AreEqual( "1 minutes, 30 seconds", 90L.FormatSecondsLong() );
	}

	/// <summary>
	/// FormatNumberShort should thousands-separate small numbers and switch
	/// to a K suffix from five digits up.
	/// </summary>
	[TestMethod]
	public void FormatNumberShort()
	{
		WithInvariantCulture( () =>
		{
			Assert.AreEqual( "999", 999L.FormatNumberShort() );
			Assert.AreEqual( "1,500", 1500L.FormatNumberShort() );
			Assert.AreEqual( "15K", 15000L.FormatNumberShort() );
		} );
	}

	/// <summary>
	/// UnsignedMod should wrap negative values into the positive range,
	/// unlike the % operator.
	/// </summary>
	[TestMethod]
	public void UnsignedMod()
	{
		Assert.AreEqual( 2, 5.UnsignedMod( 3 ) );
		Assert.AreEqual( 2, (-1).UnsignedMod( 3 ) );
		Assert.AreEqual( 0, (-3).UnsignedMod( 3 ) );
	}

	/// <summary>
	/// BitsSet should count set bits (population count).
	/// </summary>
	[TestMethod]
	public void BitsSet()
	{
		Assert.AreEqual( 0, 0.BitsSet() );
		Assert.AreEqual( 1, 8.BitsSet() );
		Assert.AreEqual( 8, 255.BitsSet() );
	}

	/// <summary>
	/// Plural should pick the singular form only for exactly one.
	/// </summary>
	[TestMethod]
	public void Plural()
	{
		Assert.AreEqual( "1 apple", $"1 {1.Plural( "apple", "apples" )}" );
		Assert.AreEqual( "2 apples", $"2 {2.Plural( "apple", "apples" )}" );
		Assert.AreEqual( "0 apples", $"0 {0.Plural( "apple", "apples" )}" );
	}

	/// <summary>
	/// IsPowerOfTwo should accept exact powers of two and reject everything else.
	/// Note the bit-trick implementation reports 0 as a power of two - pinned
	/// here so a change in that quirk is a deliberate one.
	/// </summary>
	[TestMethod]
	public void IsPowerOfTwo()
	{
		Assert.IsTrue( 1.IsPowerOfTwo() );
		Assert.IsTrue( 64.IsPowerOfTwo() );
		Assert.IsTrue( 1024.IsPowerOfTwo() );
		Assert.IsTrue( 0.IsPowerOfTwo() );

		Assert.IsFalse( 3.IsPowerOfTwo() );
		Assert.IsFalse( 1000.IsPowerOfTwo() );
	}

	/// <summary>
	/// Enum flag helpers should test, set and clear flags without boxing
	/// surprises.
	/// </summary>
	[TestMethod]
	public void EnumFlags()
	{
		var value = AttributeTargets.Class | AttributeTargets.Method;

		Assert.IsTrue( value.Contains( AttributeTargets.Class ) );
		Assert.IsFalse( value.Contains( AttributeTargets.Field ) );

		var added = value.WithFlag( AttributeTargets.Field, true );
		Assert.IsTrue( added.Contains( AttributeTargets.Field ) );

		var removed = added.WithFlag( AttributeTargets.Field, false );
		Assert.IsFalse( removed.Contains( AttributeTargets.Field ) );

		Assert.AreEqual( (int)AttributeTargets.Class, AttributeTargets.Class.AsInt() );
	}
}
