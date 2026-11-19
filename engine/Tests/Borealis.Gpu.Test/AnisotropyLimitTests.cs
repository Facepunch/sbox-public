using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// The player's anisotropy setting (native's <c>r_max_anisotropy</c>) limits every anisotropic sampler, as native clamps
/// each one to it, and changing it remakes the samplers already made in place.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AnisotropyLimitTests : GpuTest
{
	[TestMethod]
	public void AnisotropyLimitClampsSamplersAndRemakesThemInPlace()
	{
		if ( Device.Capabilities.MaxAnisotropy < 8 ) Assert.Inconclusive( "The device filters with less than 8x anisotropy" );

		var anisotropic = Device.CreateSampler( SamplerInfo.Linear( AddressMode.Wrap, 16 ) );
		var trilinear = Device.CreateSampler( SamplerInfo.Linear( AddressMode.Wrap ) );
		Assert.AreEqual( Math.Min( 16, Device.Capabilities.MaxAnisotropy ), Device.SamplerAnisotropy( anisotropic ), "unlimited until a limit is set" );

		// A persistent set that binds it, rewritten when the sampler is remade
		var layout = Device.CreateBindingLayout( [new BindingSlot( 0, BindingType.Sampler, ShaderStages.Fragment ), new BindingSlot( 1, BindingType.Sampler, ShaderStages.Fragment )] );
		var set = Device.CreateBindingSet( layout, [Binding.SamplerState( 0, anisotropic ), Binding.SamplerState( 1, trilinear )] );
		var slot = Device.SamplerIndex( anisotropic );

		Device.SetAnisotropyLimit( 4 );
		Assert.AreEqual( 4, Device.SamplerAnisotropy( anisotropic ) );
		Assert.AreEqual( 1, Device.SamplerAnisotropy( trilinear ) );
		Assert.AreEqual( slot, Device.SamplerIndex( anisotropic ), "remade in its bindless slot" );
		Assert.AreEqual( 1, Device.SamplerBindingsRewritten, "only the anisotropic sampler's binding is rewritten" );

		// Samplers made after take the limit; raising it again gives them back their own
		var later = Device.CreateSampler( SamplerInfo.Linear( AddressMode.Clamp, 8 ) );
		Assert.AreEqual( 4, Device.SamplerAnisotropy( later ) );
		Device.SetAnisotropyLimit( 16 );
		Assert.AreEqual( 8, Device.SamplerAnisotropy( later ) );
		Assert.AreEqual( Math.Min( 16, Device.Capabilities.MaxAnisotropy ), Device.SamplerAnisotropy( anisotropic ) );

		Device.Destroy( set );
		Device.Destroy( anisotropic );
		Device.Destroy( trilinear );
		Device.Destroy( later );
	}
}
