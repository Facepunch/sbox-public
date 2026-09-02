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
public partial class NumberEntry : TextEntry
{
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
	}
}
