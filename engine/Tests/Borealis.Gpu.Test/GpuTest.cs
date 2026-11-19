using Borealis.Gpu;
using System.IO;
using System.IO.Compression;

namespace BorealisGpuTests;

/// <summary>
/// A test with an offscreen device of its own and the validation layer on, synchronisation checks included. A test is
/// inconclusive where there's no Vulkan 1.3 GPU, and fails if validation reported any error, shutdown included.
/// </summary>
public abstract class GpuTest
{
	readonly List<DeviceMessage> messages = new();

	protected Device Device { get; private set; }

	/// <summary>Whether the test's device also has GPU-assisted validation (<see cref="DeviceOptions.GpuAssistedValidation"/>).</summary>
	protected virtual bool GpuAssistedValidation => false;

	/// <summary>With <see cref="GpuAssistedValidation"/>, whether it also checks descriptors in the shader itself (<see cref="DeviceOptions.GpuAssistedDescriptorChecks"/>).</summary>
	protected virtual bool GpuAssistedDescriptorChecks => true;

	public TestContext TestContext { get; set; }

	[TestInitialize]
	public void CreateDevice()
	{
		if ( !Device.IsSupported ) Assert.Inconclusive( "No Vulkan loader is installed" );

		try
		{
			Device = Device.Create( new DeviceOptions
			{
				ApplicationName = "Borealis.Gpu.Test",
				Validation = true,
				SynchronizationValidation = true,
				GpuAssistedValidation = GpuAssistedValidation,
				GpuAssistedDescriptorChecks = GpuAssistedDescriptorChecks,
				Presentation = false,
				Message = m =>
				{
					lock ( messages ) messages.Add( m );
				},
			} );
		}
		catch ( GpuException e )
		{
			Assert.Inconclusive( e.Message );
		}

		// Tests check the pixels of the frames they draw: no draw is skipped for a pipeline workers are still making
		Device.SkipDrawsUntilReady = false;

		// Casters move into the sun's cached far cascade once they've stayed put for a while of wall-clock time. Tests compare
		// frames rendered seconds apart (a first frame can take seconds), so in them casters stay where they are: every frame a
		// test draws has the same cache, whenever it's drawn
		Borealis.Shadows.SettledCasters.Reset();
		Borealis.Shadows.SettledCasters.Seconds = float.PositiveInfinity;
		Borealis.Shadows.SettledCasters.RestlessSeconds = float.PositiveInfinity;
	}

	[TestCleanup]
	public void DestroyDevice()
	{
		Borealis.Shadows.SettledCasters.Seconds = Borealis.Shadows.SettledCasters.DefaultSeconds;
		Borealis.Shadows.SettledCasters.RestlessSeconds = Borealis.Shadows.SettledCasters.DefaultRestlessSeconds;
		Device?.Dispose();
		Device = null;

		string[] errors;
		lock ( messages ) errors = messages.Where( m => m.Severity == MessageSeverity.Error ).Select( m => $"{m.Id}: {m.Text}" ).ToArray();
		Assert.AreEqual( 0, errors.Length, "Validation errors:\n" + string.Join( "\n", errors.Take( 10 ) ) );
	}

	/// <summary>
	/// Take the errors reported so far, so a test that provokes them on purpose can check them and still pass.
	/// </summary>
	protected string[] TakeValidationErrors()
	{
		lock ( messages )
		{
			var errors = messages.Where( m => m.Severity == MessageSeverity.Error ).Select( m => m.Id ).ToArray();
			messages.Clear();
			return errors;
		}
	}

	/// <summary>The warnings reported so far, each as its id and text.</summary>
	protected string[] ValidationWarnings()
	{
		lock ( messages ) return messages.Where( m => m.Severity == MessageSeverity.Warning ).Select( m => $"{m.Id}: {m.Text}" ).ToArray();
	}

	/// <summary>
	/// A render target's mip 0, read back as bytes, after it was last used in <paramref name="from"/>.
	/// </summary>
	protected byte[] ReadBack( TextureHandle texture, ImageLayout from )
	{
		var info = Device.InfoOf( texture );
		var size = info.Width * info.Height * info.Format.BytesPerBlock();
		var buffer = Device.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Readback" } );

		var list = Device.BeginCommandList( QueueType.Graphics, "Read back" );
		list.Barrier( TextureBarrier.Transition( texture, from, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.CopyTextureToBuffer( texture, 0, 0, buffer );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );

		var data = Device.Map( buffer ).ToArray();
		Device.Destroy( buffer );
		return data;
	}

	/// <summary>A pixel of 8-bit RGBA data.</summary>
	protected static (byte R, byte G, byte B, byte A) Pixel( byte[] rgba, int width, int x, int y )
	{
		var i = (y * width + x) * 4;
		return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
	}

	/// <summary>
	/// Save 8-bit RGBA as a PNG to look at, in <c>%TEMP%/borealis-gpu-tests</c>, and attach it to the test result.
	/// </summary>
	protected void Save( byte[] rgba, int width, int height, string name )
	{
		var folder = Path.Combine( Path.GetTempPath(), "borealis-gpu-tests" );
		Directory.CreateDirectory( folder );
		var path = Path.Combine( folder, $"{name}.png" );
		File.WriteAllBytes( path, Png( rgba, width, height ) );
		TestContext?.AddResultFile( path );
	}

	static byte[] Png( byte[] rgba, int width, int height )
	{
		var raw = new MemoryStream();
		for ( int y = 0; y < height; y++ )
		{
			raw.WriteByte( 0 );
			raw.Write( rgba, y * width * 4, width * 4 );
		}

		var compressed = new MemoryStream();
		using ( var z = new ZLibStream( compressed, CompressionLevel.Fastest, leaveOpen: true ) ) raw.WriteTo( z );

		var png = new MemoryStream();
		png.Write( [137, 80, 78, 71, 13, 10, 26, 10] );
		var header = new byte[13];
		WriteBigEndian( header, 0, (uint)width );
		WriteBigEndian( header, 4, (uint)height );
		header[8] = 8;
		header[9] = 6;
		Chunk( png, "IHDR", header );
		Chunk( png, "IDAT", compressed.ToArray() );
		Chunk( png, "IEND", [] );
		return png.ToArray();
	}

	static void Chunk( Stream stream, string type, byte[] data )
	{
		var length = new byte[4];
		WriteBigEndian( length, 0, (uint)data.Length );
		stream.Write( length );

		var typed = new byte[4 + data.Length];
		for ( int i = 0; i < 4; i++ ) typed[i] = (byte)type[i];
		data.CopyTo( typed, 4 );
		stream.Write( typed );

		var crc = new byte[4];
		WriteBigEndian( crc, 0, Crc32( typed ) );
		stream.Write( crc );
	}

	static void WriteBigEndian( byte[] buffer, int offset, uint value )
	{
		buffer[offset] = (byte)(value >> 24);
		buffer[offset + 1] = (byte)(value >> 16);
		buffer[offset + 2] = (byte)(value >> 8);
		buffer[offset + 3] = (byte)value;
	}

	static uint Crc32( byte[] data )
	{
		var crc = 0xFFFFFFFFu;
		foreach ( var b in data )
		{
			crc ^= b;
			for ( int k = 0; k < 8; k++ ) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
		}

		return ~crc;
	}
}
