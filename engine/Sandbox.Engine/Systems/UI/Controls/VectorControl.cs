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
[CustomEditor( typeof( Vector2 ) )]
[CustomEditor( typeof( Vector3 ) )]
[CustomEditor( typeof( Vector4 ) )]
[StyleSheet.Inline( "vectorcontrol", Styles )]
public partial class VectorControl : BaseControl
{
	// Each component is its own entry with a coloured chip in front of it, the way the
	// editor's vector widget does it - x red, y green, z blue, w yellow
	const string Styles = """
		VectorControl
		{
			gap: 2px;
			flex-grow: 1;
			flex-shrink: 1;
			flex-basis: 0px;
			min-width: 0px;
			flex-direction: row;
			align-items: center;
		}

		VectorControl NumberEntry
		{
			flex-grow: 1;
			flex-shrink: 1;
			flex-basis: 0px;
			min-width: 0px;
			overflow: hidden;
		}

		VectorControl NumberEntry.x > .prefix-label { color: #FB5A5A; background-color: #FB5A5A1a; }
		VectorControl NumberEntry.x:hover > .prefix-label { background-color: #FB5A5A33; }
		VectorControl NumberEntry.y > .prefix-label { color: #B0E24D; background-color: #B0E24D1a; }
		VectorControl NumberEntry.y:hover > .prefix-label { background-color: #B0E24D33; }
		VectorControl NumberEntry.z > .prefix-label { color: #3273EB; background-color: #3273EB1a; }
		VectorControl NumberEntry.z:hover > .prefix-label { background-color: #3273EB33; }
		VectorControl NumberEntry.w > .prefix-label { color: #E6DB74; background-color: #E6DB741a; }
		VectorControl NumberEntry.w:hover > .prefix-label { background-color: #E6DB7433; }
		""";

	public override bool SupportsMultiEdit => true;

	NumberEntry _x;
	NumberEntry _y;
	NumberEntry _z;
	NumberEntry _w;

	public VectorControl()
	{
		_x = AddChild<NumberEntry>( "x" );
		_y = AddChild<NumberEntry>( "y" );
		_z = AddChild<NumberEntry>( "z" );
		_w = AddChild<NumberEntry>( "w" );

		_x.Prefix = "X";
		_y.Prefix = "Y";
		_z.Prefix = "Z";
		_w.Prefix = "W";
	}

	public override void Rebuild()
	{
		if ( Property == null ) return;

		// get the vector3 as a so
		if ( Property.TryGetAsObject( out var so ) )
		{
			_x.Property = so.GetProperty( "x" );
			_y.Property = so.GetProperty( "y" );

			_z.Property = so.GetProperty( "z" );
			_z.Style.Display = _z.Property is not null ? DisplayMode.Flex : DisplayMode.None;

			_w.Property = so.GetProperty( "w" );
			_w.Style.Display = _w.Property is not null ? DisplayMode.Flex : DisplayMode.None;
		}
	}
}
