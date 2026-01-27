
using Editor;
using Sandbox.Engine;
using Sandbox.Internal;
using System;

namespace EditorTests;

[TestClass]
public class ControlWidgets
{
	public class Example
	{
		public float Single { get; set; }
		public double Double { get; set; }
		public int Int32 { get; set; }
		public long Int64 { get; set; }
	}

	/// <summary>
	/// <para>
	/// Integer values should survive a round trip through <see cref="IntegerControlWidget.ValueToString"/> and <see cref="IntegerControlWidget.StringToValue"/>.
	/// </para>
	/// <para>
	/// Reproduces <see href="https://github.com/Facepunch/sbox-public/issues/3385">Facepunch/sbox-public#3385</see>.
	/// </para>
	/// </summary>
	[TestMethod]
	[DataRow( nameof( Example.Int32 ), 123 )]
	[DataRow( nameof( Example.Int32 ), int.MinValue )]
	[DataRow( nameof( Example.Int32 ), int.MaxValue )]
	[DataRow( nameof( Example.Int32 ), int.MinValue + 1 )]
	[DataRow( nameof( Example.Int32 ), int.MaxValue - 1 )]
	[DataRow( nameof( Example.Int32 ), 0x163de6ac )]
	[DataRow( nameof( Example.Int32 ), -0x5988f536 )]
	[DataRow( nameof( Example.Int64 ), 123L )]
	[DataRow( nameof( Example.Int64 ), long.MinValue )]
	[DataRow( nameof( Example.Int64 ), long.MaxValue )]
	[DataRow( nameof( Example.Int64 ), long.MinValue + 1 )]
	[DataRow( nameof( Example.Int64 ), long.MaxValue - 1 )]
	[DataRow( nameof( Example.Int64 ), 0x58722b8d5f4facL )]
	[DataRow( nameof( Example.Int64 ), -0x57569783dae951d1L )]
	public void IntegerRoundTrip( string propertyName, object value )
	{
		var sprop = PrepareProperty( propertyName, value );

		var text = IntegerControlWidget.ValueToStringImpl( sprop );
		var roundTrip = IntegerControlWidget.StringToValueImpl( text, sprop );

		Console.WriteLine( $"{value} => \"{text}\" => {roundTrip}" );

		Assert.AreEqual( value, roundTrip );
	}

	/// <summary>
	/// <para>
	/// Integer values should survive a round trip through <see cref="FloatControlWidget.ValueToString"/> and <see cref="FloatControlWidget.StringToValue"/>.
	/// </para>
	/// <para>
	/// Reproduces <see href="https://github.com/Facepunch/sbox-public/issues/3385">Facepunch/sbox-public#3385</see>.
	/// </para>
	/// </summary>
	[TestMethod]
	[DataRow( nameof( Example.Single ), 123f )]
	[DataRow( nameof( Example.Single ), 1726243328f )]
	[DataRow( nameof( Example.Single ), -1726243328f )]
	[DataRow( nameof( Example.Double ), 123d )]
	[DataRow( nameof( Example.Double ), 1726243328d )]
	[DataRow( nameof( Example.Double ), -1726243328d )]
	public void FloatRoundTrip( string propertyName, object value )
	{
		var sprop = PrepareProperty( propertyName, value );

		var text = FloatControlWidget.ValueToStringImpl( sprop );
		var roundTrip = FloatControlWidget.StringToValueImpl( text, sprop );

		Console.WriteLine( $"{value:R} => \"{text}\" => {roundTrip:R}" );

		Assert.AreEqual( value, roundTrip );
	}

	private static SerializedProperty PrepareProperty( string propertyName, object value )
	{
		var inst = new Example();
		var prop = typeof( Example )
			.GetProperty( propertyName )!;

		Assert.AreEqual( prop.PropertyType, value.GetType() );

		prop.SetValue( inst, value );

		var sobj = GlobalToolsNamespace.EditorTypeLibrary.GetSerializedObject( inst );

		return sobj.GetProperty( propertyName );
	}
}
