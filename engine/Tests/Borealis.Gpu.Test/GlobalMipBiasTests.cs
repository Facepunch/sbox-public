using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// The temporal upscalers' texture mip bias (native's global texture mip bias): it reaches anisotropic samplers, remade in
/// place, leaves the rest alone, and goes away again.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GlobalMipBiasTests : GpuTest
{
	[TestMethod]
	public void GlobalMipBiasRemakesAnisotropicSamplersInPlaceAndGoesAway()
	{
		var anisotropic = Device.CreateSampler( SamplerInfo.Linear( AddressMode.Wrap, 8 ) with { MipBias = 0.5f } );
		var trilinear = Device.CreateSampler( SamplerInfo.Linear( AddressMode.Wrap ) );
		var comparison = Device.CreateSampler( SamplerInfo.Linear( AddressMode.Clamp, 8 ) with { Compare = CompareFunction.LessEqual } );
		var fixedBias = Device.CreateSampler( SamplerInfo.Linear( AddressMode.Clamp, 8 ) with { FixedMipBias = true } );

		// A persistent set that binds them, rewritten where a sampler is remade
		var layout = Device.CreateBindingLayout( [
			new BindingSlot( 0, BindingType.Sampler, ShaderStages.Fragment ), new BindingSlot( 1, BindingType.Sampler, ShaderStages.Fragment ),
			new BindingSlot( 2, BindingType.Sampler, ShaderStages.Fragment ), new BindingSlot( 3, BindingType.Sampler, ShaderStages.Fragment )] );
		var set = Device.CreateBindingSet( layout, [Binding.SamplerState( 0, anisotropic ), Binding.SamplerState( 1, trilinear ), Binding.SamplerState( 2, comparison ), Binding.SamplerState( 3, fixedBias )] );
		var slot = Device.SamplerIndex( anisotropic );
		var rewritten = Device.SamplerBindingsRewritten;

		try
		{
			Assert.AreEqual( 0, Device.GlobalMipBias, "none until an upscaler sets it" );
			Assert.AreEqual( 0.5f, Device.SamplerMipBias( anisotropic ) );

			// FSR3 at Performance: half the display size
			Device.SetGlobalMipBias( -2 );
			Assert.AreEqual( -1.5f, Device.SamplerMipBias( anisotropic ), "added to the sampler's own" );
			Assert.AreEqual( 0, Device.SamplerMipBias( trilinear ), "not a material texture's sampler" );
			Assert.AreEqual( 0, Device.SamplerMipBias( comparison ), "comparison samplers keep theirs" );
			Assert.AreEqual( 0, Device.SamplerMipBias( fixedBias ), "a SamplerState's keeps its own" );
			Assert.AreEqual( slot, Device.SamplerIndex( anisotropic ), "remade in its bindless slot" );
			Assert.AreEqual( rewritten + 1, Device.SamplerBindingsRewritten, "only the biased sampler's binding is rewritten" );

			// Samplers made while it's set take it; setting it again changes nothing
			var later = Device.CreateSampler( SamplerInfo.Linear( AddressMode.Clamp, 4 ) );
			Assert.AreEqual( -2, Device.SamplerMipBias( later ) );
			Device.SetGlobalMipBias( -2 );
			Assert.AreEqual( rewritten + 1, Device.SamplerBindingsRewritten );

			// Off again: every sampler back to its own
			Device.SetGlobalMipBias( 0 );
			Assert.AreEqual( 0.5f, Device.SamplerMipBias( anisotropic ) );
			Assert.AreEqual( 0, Device.SamplerMipBias( later ) );
			Assert.AreEqual( slot, Device.SamplerIndex( anisotropic ) );
			Device.Destroy( later );

			// It doesn't stop the anisotropy limit remaking them either
			Device.SetGlobalMipBias( -1 );
			Device.SetAnisotropyLimit( 2 );
			Assert.AreEqual( -0.5f, Device.SamplerMipBias( anisotropic ) );
			Assert.AreEqual( Math.Min( 2, Device.Capabilities.MaxAnisotropy ), Device.SamplerAnisotropy( anisotropic ) );
		}
		finally
		{
			Device.SetGlobalMipBias( 0 );
			Device.SetAnisotropyLimit( float.MaxValue );
			Device.Destroy( set );
			Device.Destroy( anisotropic );
			Device.Destroy( trilinear );
			Device.Destroy( comparison );
			Device.Destroy( fixedBias );
		}
	}
}
