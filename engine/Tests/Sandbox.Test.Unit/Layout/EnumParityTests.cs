using System;

namespace LayoutTests;

/// <summary>
/// The panel layer casts its public style enums (Sandbox.UI, in Sandbox.System) straight to the layout
/// engine's enums by value, so their members must line up exactly.
/// </summary>
[TestClass]
public class EnumParityTests
{
	private static void AssertParity<TPublic, TLayout>() where TPublic : struct, Enum where TLayout : struct, Enum
	{
		foreach ( var value in Enum.GetValues<TPublic>() )
		{
			var name = value.ToString();
			var numeric = Convert.ToInt32( value );

			Assert.IsTrue( Enum.TryParse<TLayout>( name, out var layoutValue ), $"{typeof( TLayout ).Name} is missing {typeof( TPublic ).Name}.{name}" );
			Assert.AreEqual( numeric, Convert.ToInt32( layoutValue ), $"{typeof( TPublic ).Name}.{name} = {numeric} but {typeof( TLayout ).Name}.{name} = {Convert.ToInt32( layoutValue )}" );
		}
	}

	[TestMethod] public void Display() => AssertParity<Sandbox.UI.DisplayMode, Sandbox.Layout.Display>();
	[TestMethod] public void Position() => AssertParity<Sandbox.UI.PositionMode, Sandbox.Layout.PositionType>();
	[TestMethod] public void FlexDirection() => AssertParity<Sandbox.UI.FlexDirection, Sandbox.Layout.FlexDirection>();
	[TestMethod] public void Justify() => AssertParity<Sandbox.UI.Justify, Sandbox.Layout.Justify>();
	[TestMethod] public void Align() => AssertParity<Sandbox.UI.Align, Sandbox.Layout.Align>();
	[TestMethod] public void Wrap() => AssertParity<Sandbox.UI.Wrap, Sandbox.Layout.Wrap>();
	[TestMethod] public void GridAutoFlow() => AssertParity<Sandbox.UI.GridAutoFlow, Sandbox.Layout.GridAutoFlow>();
}
