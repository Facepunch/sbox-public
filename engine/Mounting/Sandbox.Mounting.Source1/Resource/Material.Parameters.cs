using System;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceMaterialBuilder
{
	private static Vector3 ToLinear( Vector3 color, bool preserveHdr = false ) => new(
		ToLinear( color.x, preserveHdr ), ToLinear( color.y, preserveHdr ), ToLinear( color.z, preserveHdr ) );

	private static float ToLinear( float value, bool preserveHdr ) =>
		preserveHdr && value > 1 ? value : MathF.Pow( Math.Max( 0, value ), 2.2f );
}
