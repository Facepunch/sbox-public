using Borealis.Source2;
using Borealis.Source2.Shaders;

namespace BorealisGpuTests;

/// <summary>
/// Lit shaders leave the local lights' shadow filters out of their baked and clustered light loops unless a light each
/// reaches has a shadow map: whether one does is a specialization constant each (<c>NoBakedLightShadows</c>,
/// <c>NoClusteredLightShadows</c>, which LightBinnerFeature sets). Reads the checkout's
/// compiled shaders; inconclusive where they aren't built.
/// </summary>
[TestClass]
public class BakedLightShadowShaderTests
{
	[TestMethod]
	public void LitShadersSpecializeOnBakedLightShadows()
	{
		var files = ShaderFileTests.Files;
		var shader = ShaderFile.Read( files.Read( "shaders/surface_test_rock.shader" ), "surface_test_rock" );
		var pixel = shader[ShaderProgramType.Pixel];
		foreach ( var name in new[] { "NoBakedLightShadows", "NoClusteredLightShadows" } )
		{
			Assert.IsTrue( pixel.Variables.Any( v => v.Register == RegisterType.Specialization && v.Attribute == name ),
				$"surface_test_rock's pixel shader specializes on {name}" );
		}
	}
}
