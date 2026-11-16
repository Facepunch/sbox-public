using Borealis.Gpu;
using System.Runtime.InteropServices;

namespace BorealisGpuTests;

/// <summary>
/// Bindless slots that hold nothing read as the device's defaults, checked on the GPU by GPU-assisted validation: slot 0
/// (s&amp;box's shaders read a texture index of 0 as none, and a material with a texture field it was never given writes 0)
/// and a slot whose texture was freed (a record written before the texture streamed out). Without defaults the first is a
/// descriptor that was never written and the second one whose image view is destroyed: either can read through a garbage
/// address and lose the device.
/// </summary>
[TestClass]
[DoNotParallelize]
public class BindlessDefaultsTests : GpuTest
{
	protected override bool GpuAssistedValidation => true;

	[StructLayout( LayoutKind.Sequential )]
	struct SlotConstants
	{
		public uint Target, First, Second, Pad;
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
		list.PushConstants( new SlotConstants { Target = (uint)Device.StorageIndex( target ), First = 0, Second = (uint)freedSlot } );
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
