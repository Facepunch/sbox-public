using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.Rendering;
using Sandbox.Rendering.Bridge;

namespace BorealisTests;

/// <summary>
/// Borealis packs fog exactly as the engine does for native's views, until native's pipeline uses Borealis's.
/// </summary>
[TestClass]
public class FogPackingTests
{
	static IEnumerable<FogSetup> Cases()
	{
		yield return default;
		yield return new FogSetup();
		yield return new FogSetup { Mode = FogMode.Distance, StartDistance = -160, EndDistance = 1600, FalloffExponent = 2, Weight = 0.5f };
		yield return new FogSetup { HeightEnabled = true, BaseHeight = 320, HeightFalloff = -200, HeightExponent = 3, HeightColor = new Color( 0.2f, 0.4f, 0.6f, 0.5f ), HeightContribution = 0.25f };
		yield return new FogSetup { DirectionalColorEnabled = true, SecondaryColor = Color.Red, ColorDirection = new Vector3( 0, 1, 1 ), ColorScale = 2, DistanceMetric = FogDistanceMetric.ViewDepth };
		yield return new FogSetup { Enabled = false, Weight = 1, DistanceContribution = 0.5f, EnabledContribution = 0.75f, SunLobeWidth = 0, SunFadeDistance = -50, SkyRotation = Rotation.From( 10, 20, 30 ) };
	}

	[TestMethod]
	public void MatchesTheEngine()
	{
		Assert.AreEqual( Marshal.SizeOf<Sandbox.Rendering.FogConstants>(), Marshal.SizeOf<Borealis.Gpu.FogConstants>() );

		foreach ( var fog in Cases() )
		{
			foreach ( var (scale, offset) in new[] { (1f, 0f), (16f, -40f) } )
			{
				var engine = fog.ToConstants( scale, offset, new Vector3( 1, 2, 0 ) );
				var borealis = fog.ToBorealis().ToConstants( scale, offset, new Vector3( 1, 2, 0 ) );
				CollectionAssert.AreEqual( Bytes( engine ), Bytes( borealis ), $"{fog.Mode} fog at scale {scale}" );
			}
		}
	}

	static byte[] Bytes<T>( T value ) where T : struct => MemoryMarshal.AsBytes( MemoryMarshal.CreateReadOnlySpan( ref value, 1 ) ).ToArray();
}
