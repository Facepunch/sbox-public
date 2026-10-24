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
	// The letter in front of a property's number sits in a chip - a handle you drag to scrub,
	// like the editor's. Tinted with the number's colour, stronger while hovered.
	const string Styles = """
		NumberEntry.prefixed > .prefix-label
		{
			flex-shrink: 0;
			align-self: center;
			width: 24px;
			height: 24px;
			margin-left: -7px;
			margin-right: 8px;
			border-radius: 5px;
			font-weight: 600;
			text-align: center;
			justify-content: center;
			align-items: center;
		}

		NumberEntry.is-float > .prefix-label { color: #B0E24D; background-color: #B0E24D1a; }
		NumberEntry.is-float:hover > .prefix-label { background-color: #B0E24D33; }
		NumberEntry.is-int > .prefix-label { color: #3273EB; background-color: #3273EB1a; }
		NumberEntry.is-int:hover > .prefix-label { background-color: #3273EB33; }
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
