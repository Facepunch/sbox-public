using Borealis.Source2;
using Borealis.Source2.Shaders;

namespace BorealisGpuTests;

/// <summary>
/// Lit shaders leave light modulators out unless the scene has some: whether it does is a specialization constant
/// (<c>LightModulators_Enabled</c>, which LightModulatorSystem sets), so the modulator code isn't in the pipelines of
/// scenes without rectangles. Reads the checkout's compiled shaders; inconclusive where they aren't built.
/// </summary>
[TestClass]
public class LightModulatorShaderTests
{
	[TestMethod]
	public void LitShadersSpecializeOnLightModulators()
	{
		var files = ShaderFileTests.Files;
		foreach ( var name in new[] { "complex", "blendable", "foliage", "static_overlay" } )
		{
			var shader = ShaderFile.Read( files.Read( $"shaders/{name}.shader" ), name );
			var pixel = shader[ShaderProgramType.Pixel];
			Assert.IsTrue( pixel.Variables.Any( v => v.Register == RegisterType.Specialization && v.Attribute == "LightModulators_Enabled" ),
				$"{name}'s pixel shader specializes on LightModulators_Enabled" );
		}
	}
}
