using Borealis.Gpu;
using System.Runtime.InteropServices;

namespace BorealisGpuTests;

/// <summary>
/// Bindless slots that hold nothing read as the device's defaults, checked by GPU-assisted validation: slot 0 (s&amp;box's
/// shaders read a texture index of 0 as none, and a material with a texture field it was never given writes 0), a slot
/// whose texture was freed (a record written before the texture streamed out) and the table's last slot, which nothing
/// has handed out (an index read from a stale or garbage record). Without defaults the first and last are descriptors that
/// were never written and the second one whose image view is destroyed: any can read through a garbage address and lose
/// the device. On RDNA4 validation's in-shader descriptor checks are off: their own lookups reset the GPU (driver 26.11,
/// validation layer 1.4.309) however the slots are filled, so there only the descriptors read are checked afterwards and
/// the pixel shows a freed slot reads the default, not the white the freed texture held.
/// </summary>
[TestClass]
public class BindlessDefaultsTests : GpuTest
{
	protected override bool GpuAssistedValidation => true;
	protected override bool GpuAssistedDescriptorChecks => !Rdna4.Value;

	// AMD Navi 4x (RX 9000 series): the GPU-AV in-shader descriptor checks reset it
	static readonly Lazy<bool> Rdna4 = new( () =>
	{
		try
		{
			using var probe = Device.Create( new DeviceOptions { ApplicationName = "Borealis.Gpu.Test probe", Presentation = false } );
			return probe.Adapter.VendorId == 0x1002 && probe.Adapter.DeviceId is >= 0x7550 and < 0x7600;
		}
		catch ( GpuException )
		{
			return false;
		}
	} );

	[StructLayout( LayoutKind.Sequential )]
	struct SlotConstants
	{
		public uint Target, First, Second, Third;
	}

	[TestMethod]
	public void EmptySlotsReadAsDefaults()
	{
		var info = TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, name: "Slots target" );
		info.Usage |= TextureUsage.Storage;
		var target = Device.CreateTexture( info );

		// A texture freed once its frames are done: its slot goes back to the table, which nothing has handed out again
		var freed = Device.CreateTexture( TextureInfo.Texture2D( 1, 1, Format.RGBA8Unorm, name: "Freed" ) );
		Device.WriteTexture( freed, [255, 255, 255, 255] );
		var freedSlot = Device.BindlessIndex( freed );
		Device.Destroy( freed );
		Device.WaitIdle();
		Device.BeginFrame();

		var shader = Device.CreateShader( LoadShader( "slots.cs.spv" ), "slots" );
		var pipeline = Device.CreateComputePipeline( new ComputePipelineInfo { Shader = shader, Entry = "MainCs" } );

		var list = Device.BeginCommandList( QueueType.Graphics, "Read empty slots" );
		list.Barrier( TextureBarrier.Transition( target, ImageLayout.Undefined, ImageLayout.General, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite ) );
		list.SetPipeline( pipeline );
		var unused = (uint)Device.Capabilities.BindlessTextures - 2;
		list.PushConstants( new SlotConstants { Target = (uint)Device.StorageIndex( target ), First = 0, Second = (uint)freedSlot, Third = unused } );
		list.Dispatch( 1, 1 );
		Device.Wait( Device.Submit( list ) );
		Device.WaitIdle();

		var errors = TakeValidationErrors();
		Assert.AreEqual( 0, errors.Length, "GPU-assisted validation saw an empty slot read: " + string.Join( ", ", errors ) );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 0, 0 }, ReadBack( target, ImageLayout.General ), "empty slots read as transparent black" );
	}

	static byte[] LoadShader( string name )
	{
		using var stream = typeof( BorealisGpuSample.SampleScene ).Assembly.GetManifestResourceStream( name );
		var bytes = new byte[stream.Length];
		stream.ReadExactly( bytes );
		return bytes;
	}
}
