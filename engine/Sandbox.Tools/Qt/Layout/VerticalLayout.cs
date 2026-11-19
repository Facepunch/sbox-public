using Native;
using Sandbox;
using System;

namespace Editor;

//
// garry:
//
// I wanted a layout that flowed from top to bottom with no springyness or stretching
// this kind of works but at the same time it kind of doesn't work. Theres weirdness when 
// they're inside another layout, or another layout is inside them. I don't love it.

internal class VerticalLayout : Layout
{
	internal Native.QVerticalLayout _verticalLayout;

	internal VerticalLayout( Widget parent ) : base( Native.QVerticalLayout.Create( parent?._widget ?? default ) )
	{

	}

	internal override void NativeInit( nint ptr )
	{
		base.NativeInit( ptr );

		_verticalLayout = (Native.QVerticalLayout)ptr;
	}

	public override Layout Add( Layout layout )
	{
		Assert.IsValid( layout );
		_verticalLayout.addItem( layout._layout );
		return layout;
	}
}
