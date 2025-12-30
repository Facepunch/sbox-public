using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace bytePack;

[TestClass]
public partial class RoundTrip
{
	[TestMethod] public void Null() => DoRoundTrip( null );
	[TestMethod] public void Byte() => DoRoundTrip( (byte)1 );
	[TestMethod] public void SByte() => DoRoundTrip( (sbyte)1 );
	[TestMethod] public void Short() => DoRoundTrip( (short)1 );
	[TestMethod] public void UShort() => DoRoundTrip( (ushort)1 );
	[TestMethod] public void Float() => DoRoundTrip( 5.0f );
	[TestMethod] public void Double() => DoRoundTrip( 6.0 );
	[TestMethod] public void Int() => DoRoundTrip( 7 );
	[TestMethod] public void UInt() => DoRoundTrip( 7u );
	[TestMethod] public void String() => DoRoundTrip( "String" );
	[TestMethod] public void String_Huge() => DoRoundTrip( new String( 'j', 1_000_000 ) );
	[TestMethod] public void Guid() => DoRoundTrip( System.Guid.NewGuid() );
	[TestMethod] public void TimeSpan() => DoRoundTrip( System.TimeSpan.FromSeconds( 10.0f ) );
	[TestMethod] public void DateTime() => DoRoundTrip( System.DateTime.Now.AddSeconds( 4564 ) );
	[TestMethod] public void DateTimeOffset() => DoRoundTrip( System.DateTimeOffset.Now.AddSeconds( 4564 ) );

}
