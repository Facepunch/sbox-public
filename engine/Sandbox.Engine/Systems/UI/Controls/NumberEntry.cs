using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.UI;

/// <summary>
/// Like TextEntry, except just for numbers
/// </summary>
[CustomEditor( typeof( float ) )]
[CustomEditor( typeof( double ) )]
[CustomEditor( typeof( int ) )]
[CustomEditor( typeof( long ) )]
[CustomEditor( typeof( short ) )]
[CustomEditor( typeof( byte ) )]
[StyleSheet.Inline( "numberentry", Styles )]
public partial class NumberEntry : TextEntry
{
	// The letter in front of a property's number, like the editor's - drag it to scrub
	const string Styles = """
		NumberEntry.prefixed > .prefix-label
		{
			font-weight: 600;
			margin-right: 10px;
			padding-right: 6px;
			border-right: 1px solid #ffffff1a;
			opacity: 0.9;
		}

		NumberEntry.is-float > .prefix-label { color: #B0E24D; }
		NumberEntry.is-int > .prefix-label { color: #3273EB; }
		""";

	public NumberEntry()
	{
		Numeric = true;
		NumberFormat = "0.###";
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		// A whole number entry shouldn't accept a decimal point, or format one back out
		WholeNumbers = Property.PropertyType != typeof( float ) && Property.PropertyType != typeof( double );

		if ( Property.TryGetAttribute<MinMaxAttribute>( out var rangeAttribute ) )
		{
			MinValue = rangeAttribute.MinValue;
			MaxValue = rangeAttribute.MaxValue;
		}

		// f for decimals, i for whole numbers, unless the owner gave it one (vector axes)
		if ( Prefix is null )
		{
			Prefix = WholeNumbers ? "i" : "f";
			SetClass( "is-int", WholeNumbers );
			SetClass( "is-float", !WholeNumbers );
		}

		AddClass( "prefixed" );
	}
}
