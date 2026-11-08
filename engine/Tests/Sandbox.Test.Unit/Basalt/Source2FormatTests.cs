using NativeEngine;
using Basalt;
using Basalt.Gpu.Source2;

namespace BasaltTests;

/// <summary>
/// The Source 2 backend's formats: sample counts as native reads them, and every Basalt format's engine format.
/// </summary>
[TestClass]
public class Source2FormatTests
{
	[TestMethod]
	public void SampleCountsMatchNative()
	{
		// RenderMultisampleTypeNumSamples
		Assert.AreEqual( 1, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_NONE ) );
		Assert.AreEqual( 1, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_INVALID ) );
		Assert.AreEqual( 2, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_2X ) );
		Assert.AreEqual( 4, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_4X ) );
		Assert.AreEqual( 6, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_6X ) );
		Assert.AreEqual( 8, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_8X ) );
		Assert.AreEqual( 16, Source2Formats.Samples( RenderMultisampleType.RENDER_MULTISAMPLE_16X ) );
	}

	[TestMethod]
	public void EveryFormatHasAnEngineFormat()
	{
		// The backend maps by name: a renamed format would only fail when something made one
		foreach ( var format in System.Enum.GetValues<TextureFormat>() )
		{
			if ( format == TextureFormat.Unknown ) continue;
			Assert.AreEqual( format, Source2Formats.From( format.ToEngine() ), format.ToString() );
		}

		Assert.AreEqual( TextureFormat.Unknown, Source2Formats.From( ImageFormat.BC7 ) );
	}
}
