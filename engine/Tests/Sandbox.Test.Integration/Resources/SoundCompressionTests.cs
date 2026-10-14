using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Sandbox.Audio;
using NativeEngine;

namespace ResourceTests;

[TestClass]
public class SoundCompressionTests
{
	static readonly int[] ResampledRates = [65536, 96000, 192000];
	static readonly int[] PreservedRates = [44100, 48000, 65535];

	/// <summary>
	/// A tone.ogg variant: the source written to disk, the source as it was after compiling, and the result.
	/// </summary>
	sealed record Variant( byte[] Source, byte[] SourceAfter, Compiled Compiled );

	/// <summary>
	/// Every tone.ogg variant the tests need, compiled in one resourcecompiler run. Each run costs
	/// ~0.75s of startup, so this is much cheaper than one run per test. Payload returns a copy,
	/// so tests can modify it.
	/// </summary>
	static readonly Lazy<(Compiled Plain, Dictionary<int, Variant> Resampled, Dictionary<int, Variant> Preserved)> Tones = new( () =>
	{
		using var files = new Fixtures();
		var tone = File.ReadAllBytes( Path.Combine( AppContext.BaseDirectory, "Resources", "Audio", "tone.ogg" ) );

		var paths = new List<string> { files.Write( "tone.ogg", tone ) };
		var sources = new List<byte[]> { tone };

		foreach ( var rate in ResampledRates )
		{
			var source = VorbisWithRate( (byte[])tone.Clone(), rate );
			var path = files.Write( $"resampled_{rate}.ogg", source );
			File.WriteAllText( path + ".meta", JsonSerializer.Serialize( new { guid = Guid.NewGuid(), loop = true, start = 0.05, end = 0.1 } ) );
			paths.Add( path );
			sources.Add( source );
		}

		foreach ( var rate in PreservedRates )
		{
			var source = VorbisWithRate( (byte[])tone.Clone(), rate );
			paths.Add( files.Write( $"preserved_{rate}.ogg", source ) );
			sources.Add( source );
		}

		var compiled = files.Compile( paths.ToArray() );
		Variant At( int i ) => new( sources[i], File.ReadAllBytes( paths[i] ), compiled[i] );

		var resampled = ResampledRates.Select( ( rate, i ) => (rate, variant: At( 1 + i )) ).ToDictionary( x => x.rate, x => x.variant );
		var preserved = PreservedRates.Select( ( rate, i ) => (rate, variant: At( 1 + ResampledRates.Length + i )) ).ToDictionary( x => x.rate, x => x.variant );
		return (compiled[0], resampled, preserved);
	} );

	static Compiled ToneOgg() => Tones.Value.Plain;

	[TestMethod]
	public void CompressesPcmAndFlacByDefaultWithoutChangingSources()
	{
		using var files = new Fixtures();
		var wave = Wave( 48123, 2 );
		var path = files.Write( "stereo.wav", wave );
		var compiled = files.Compile( path, files.Copy( "tone.flac" ) );
		Assert.AreEqual( SoundFormat.Opus, compiled[0].Format );
		Assert.AreEqual( 48123, compiled[0].Frames );
		Assert.IsTrue( compiled[0].Bytes.Length < wave.Length / 2 );
		CollectionAssert.AreEqual( wave, File.ReadAllBytes( path ) );

		Assert.AreEqual( SoundFormat.Opus, compiled[1].Format );
		Assert.AreEqual( 48000, compiled[1].Frames );
	}

	[TestMethod]
	public void PreservesCompressedPayloadsAndAppliesRequestedProcessing()
	{
		var extensions = new[] { "mp3", "ogg", "opus" };

		// Write every source first so they all compile in one run
		using var files = new Fixtures();
		var paths = new List<string>();
		foreach ( var extension in extensions )
		{
			var original = File.ReadAllBytes( Path.Combine( AppContext.BaseDirectory, "Resources", "Audio", $"tone.{extension}" ) );
			paths.Add( files.Write( $"tone_{extension}.{extension}", original ) );

			// Gain/mono/trim edits decode the original source before encoding the result.
			var processed = files.Write( $"processed_{extension}.{extension}", original );
			File.WriteAllText( processed + ".meta", JsonSerializer.Serialize( new { guid = Guid.NewGuid(), gain = -6 } ) );
			paths.Add( processed );
		}

		var compiled = files.Compile( paths.ToArray() );

		for ( var i = 0; i < extensions.Length; i++ )
		{
			var extension = extensions[i];
			var original = File.ReadAllBytes( paths[i * 2] );
			var plain = compiled[i * 2];
			var expected = extension switch { "mp3" => SoundFormat.MP3, "ogg" => SoundFormat.Vorbis, _ => SoundFormat.Opus };
			Assert.AreEqual( expected, plain.Format );
			if ( extension == "ogg" ) CollectionAssert.AreEqual( original, plain.Payload );
			if ( extension == "opus" ) CollectionAssert.AreEqual( OpusPackets( original ), plain.Payload );
			if ( extension == "mp3" )
			{
				var start = original.AsSpan( 0, 3 ).SequenceEqual( "ID3"u8 )
					? 10 + (original[6] << 21 | original[7] << 14 | original[8] << 7 | original[9]) : 0;
				CollectionAssert.AreEqual( original[start..], plain.Payload );
			}

			Assert.AreEqual( SoundFormat.Opus, compiled[i * 2 + 1].Format );
			CollectionAssert.AreEqual( original, File.ReadAllBytes( paths[i * 2 + 1] ) );
		}
	}

	[TestMethod]
	[DataRow( 65536 )]
	[DataRow( 96000 )]
	[DataRow( 192000 )]
	public void ResamplesVorbisRatesThatDoNotFitTheCompiledHeader( int rate )
	{
		var originalFrames = ToneOgg().Frames;
		var (source, sourceAfter, compiled) = Tones.Value.Resampled[rate];
		Assert.AreEqual( SoundFormat.Opus, compiled.Format );
		Assert.AreEqual( 48000, compiled.Rate );
		Assert.AreEqual( originalFrames * 48000.0 / rate, compiled.Frames, 1.0 );
		Assert.AreEqual( 2400, compiled.LoopStart, 1 );
		Assert.AreEqual( 4800, compiled.LoopEnd, 1 );
		CollectionAssert.AreEqual( source, sourceAfter );
	}

	[TestMethod]
	[DataRow( 44100 )]
	[DataRow( 48000 )]
	[DataRow( 65535 )]
	public void PreservesVorbisRatesThatFitTheCompiledHeader( int rate )
	{
		var (source, _, compiled) = Tones.Value.Preserved[rate];
		Assert.AreEqual( SoundFormat.Vorbis, compiled.Format );
		Assert.AreEqual( rate, compiled.Rate );
		CollectionAssert.AreEqual( source, compiled.Payload );
	}

	// Change the identification packet's rate and repair its Ogg page checksum.
	// Vorbis audio packets are independent of this rate, so no encoder is needed.
	static byte[] VorbisWithRate( byte[] source, int rate )
	{
		var segments = source[26];
		var packet = 27 + segments;
		Assert.IsTrue( source.AsSpan( packet, 7 ).SequenceEqual( "\x01vorbis"u8 ) );
		BitConverter.GetBytes( rate ).CopyTo( source, packet + 12 );
		RepairOggPage( source, 0 );
		return source;
	}

	static int RepairOggPage( byte[] source, int page )
	{
		Array.Clear( source, page + 22, 4 );
		var segments = source[page + 26];
		var end = page + 27 + segments;
		for ( var i = 0; i < segments; i++ ) end += source[page + 27 + i];
		uint crc = 0;
		for ( var i = page; i < end; i++ )
		{
			crc ^= (uint)source[i] << 24;
			for ( var bit = 0; bit < 8; bit++ )
				crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? 0x04c11db7u : 0);
		}
		BitConverter.GetBytes( crc ).CopyTo( source, page + 22 );
		return end;
	}

	[TestMethod]
	[DataRow( 65536 )]
	[DataRow( 96000 )]
	[DataRow( 192000 )]
	public void DecodesHighRateVorbisToPcm( int rate )
	{
		var source = File.ReadAllBytes( Path.Combine( AppContext.BaseDirectory, "Resources", "Audio", "tone.ogg" ) );
		var original = SoundData.FromOGG( source );
		var decoded = SoundData.FromOGG( VorbisWithRate( source, rate ) );
		Assert.AreEqual( (uint)rate, decoded.SampleRate );
		Assert.AreEqual( original.SampleCount, decoded.SampleCount );
		CollectionAssert.AreEqual( original.PCMData, decoded.PCMData );
		Assert.AreEqual( (float)decoded.SampleCount / rate, decoded.Duration );
	}

	[TestMethod]
	public void DecodesAndMixesMatchingVorbisChains()
	{
		using var files = new Fixtures();
		var source = File.ReadAllBytes( files.Copy( "tone.ogg" ) );
		var decoded = SoundData.FromOGG( source );
		var chain = ChainedVorbis( source, (byte[])source.Clone() );
		var chained = SoundData.FromOGG( chain );
		Assert.AreEqual( decoded.SampleCount * 2, chained.SampleCount );
		CollectionAssert.AreEqual( decoded.PCMData.Concat( decoded.PCMData ).ToArray(), chained.PCMData );

		var compiled = files.Compile( files.Write( "chained.ogg", chain ) )[0];
		Assert.AreEqual( SoundFormat.Vorbis, compiled.Format );
		CollectionAssert.AreEqual( chain, compiled.Payload );
		using var sampler = VorbisSampler( compiled.Payload, compiled.Rate, compiled.Channels, compiled.Frames );
		Assert.IsTrue( sampler.IsReadyToMix );
		for ( var i = 0; i < 400 && sampler.ShouldContinueMixing; i++ ) sampler.Sample( 1 );
		Assert.IsTrue( sampler.SamplePosition >= decoded.SampleCount );
		Assert.IsFalse( sampler.ShouldContinueMixing );
	}

	[TestMethod]
	public void RejectsVorbisChainsWithDifferentRates()
	{
		var source = File.ReadAllBytes( Path.Combine( AppContext.BaseDirectory, "Resources", "Audio", "tone.ogg" ) );
		var chain = ChainedVorbis( source, VorbisWithRate( (byte[])source.Clone(), 48000 ) );
		Assert.ThrowsException<ArgumentException>( () => SoundData.FromOGG( chain ) );
	}

	static byte[] ChainedVorbis( byte[] first, byte[] second )
	{
		// Each logical stream needs a distinct serial number on all of its pages.
		var serial = BitConverter.GetBytes( BitConverter.ToUInt32( first, 14 ) ^ 1u );
		for ( var page = 0; page < second.Length; page = RepairOggPage( second, page ) )
			serial.CopyTo( second, page + 14 );
		return first.Concat( second ).ToArray();
	}

	[TestMethod]
	[DataRow( "open" )]
	[DataRow( "rate" )]
	[DataRow( "channels" )]
	[DataRow( "frames" )]
	[DataRow( "high-rate" )]
	public void FailedVorbisMixerFinishesInsteadOfRemainingAnActiveVoice( string failure )
	{
		var compiled = ToneOgg();
		var source = compiled.Payload;
		var rate = compiled.Rate;
		var channels = compiled.Channels;
		var frames = compiled.Frames;
		switch ( failure )
		{
			case "open": source[0] = 0; break;
			case "rate": rate++; break;
			case "channels": channels++; break;
			case "frames": frames++; break;
			case "high-rate": source = VorbisWithRate( source, 96000 ); rate = 96000 & 0xffff; break;
		}

		using var sampler = VorbisSampler( source, rate, channels, frames );
		Assert.IsFalse( sampler.IsReadyToMix );
		Assert.IsFalse( sampler.ShouldContinueMixing );
		// A later seek/sample must not reopen a failed decoder or revive the voice.
		sampler.SamplePosition = 0;
		sampler.Sample( 1 );
		Assert.IsFalse( sampler.IsReadyToMix );
		Assert.IsFalse( sampler.ShouldContinueMixing );
	}

	[TestMethod]
	public void ValidVorbisMixerAdvancesAndFinishes()
	{
		var compiled = ToneOgg();
		using var sampler = VorbisSampler( compiled.Payload, compiled.Rate, compiled.Channels, compiled.Frames );
		Assert.IsTrue( sampler.IsReadyToMix );
		for ( var i = 0; i < 200 && sampler.ShouldContinueMixing; i++ ) sampler.Sample( 1 );
		Assert.IsTrue( sampler.SamplePosition > 0 );
		Assert.IsFalse( sampler.ShouldContinueMixing );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task FailedVorbisSampleExtractionReturnsPromptly( bool interleaved )
	{
		var compiled = ToneOgg();
		var source = compiled.Payload;
		source[0] = 0;
		var native = VorbisSound( source, compiled.Rate, compiled.Channels, compiled.Frames );
		// Wrap the in-memory sound without the public factory's headless-mode early return.
		var sound = (SoundFile)Activator.CreateInstance( typeof( SoundFile ), BindingFlags.Instance | BindingFlags.NonPublic,
			null, new object[] { native }, null );
		var timer = Stopwatch.StartNew();
		var samples = await (interleaved ? sound.GetSamplesInterleavedAsync() : sound.GetSamplesAsync());
		Assert.IsNull( samples );
		Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 1 ), $"Permanent decoder failure took {timer.Elapsed.TotalSeconds:F2} seconds to return" );
	}

	static AudioSampler VorbisSampler( byte[] source, int rate, int channels, int frames )
		=> new( VorbisSound( source, rate, channels, frames ).CreateMixer() );

	static unsafe CSfxTable VorbisSound( byte[] source, int rate, int channels, int frames )
	{
		fixed ( byte* data = source )
		{
			// Use an in-memory native sound so decoder lifecycle tests need no device or async file load.
			var sound = g_pSoundSystem.CreateSound( $"vorbis_test_{Guid.NewGuid():N}.vsnd", channels, rate,
				(int)SoundFormat.Vorbis, frames, (float)frames / rate, -1, 0, (IntPtr)data, source.Length );
			Assert.IsFalse( sound.IsNull );
			return sound;
		}
	}

	[TestMethod]
	public void PreservesOptOutSmallSoundsAndLoopBoundaries()
	{
		using var files = new Fixtures();
		var uncompressed = files.Write( "uncompressed.wav", Wave( 48000, 1 ) );
		File.WriteAllText( uncompressed + ".meta", JsonSerializer.Serialize( new { guid = Guid.NewGuid(), compress = false } ) );
		var tiny = files.Write( "tiny.wav", Wave( 8, 1 ) );
		var loop = files.Write( "loop.wav", Wave( 48000, 1 ) );
		File.WriteAllText( loop + ".meta", JsonSerializer.Serialize( new { guid = Guid.NewGuid(), loop = true, start = 0.25, end = 0.5 } ) );

		var compiled = files.Compile( uncompressed, tiny, loop );
		Assert.AreEqual( SoundFormat.PCM16, compiled[0].Format );
		Assert.AreEqual( SoundFormat.PCM16, compiled[1].Format );
		Assert.AreEqual( 12000, compiled[2].LoopStart );
		Assert.AreEqual( 24000, compiled[2].LoopEnd );
	}

	static byte[] Wave( int frames, int channels )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( "RIFF"u8 ); writer.Write( 36 + frames * channels * 2 ); writer.Write( "WAVEfmt "u8 );
		writer.Write( 16 ); writer.Write( (short)1 ); writer.Write( (short)channels ); writer.Write( 48000 );
		writer.Write( 48000 * channels * 2 ); writer.Write( (short)(channels * 2) ); writer.Write( (short)16 );
		writer.Write( "data"u8 ); writer.Write( frames * channels * 2 );
		for ( var i = 0; i < frames; i++ )
			for ( var channel = 0; channel < channels; channel++ )
				writer.Write( (short)(12000 * Math.Sin( i * 2 * Math.PI * (440 + channel * 440) / 48000 )) );
		return stream.ToArray();
	}

	static byte[] OpusPackets( byte[] source )
	{
		using var result = new MemoryStream();
		using var packet = new MemoryStream();
		var index = 0;
		for ( var page = 0; page < source.Length; )
		{
			var segments = source[page + 26];
			var cursor = page + 27 + segments;
			for ( var i = 0; i < segments; i++ )
			{
				var count = source[page + 27 + i];
				packet.Write( source, cursor, count ); cursor += count;
				if ( count == 255 ) continue;
				if ( index++ >= 2 ) result.Write( packet.ToArray() );
				packet.SetLength( 0 );
			}
			page = cursor;
		}
		return result.ToArray();
	}

	sealed class Compiled( byte[] bytes )
	{
		public byte[] Bytes => bytes;
		int Data
		{
			get
			{
				var table = 8 + BitConverter.ToInt32( bytes, 8 );
				for ( var i = 0; i < BitConverter.ToInt32( bytes, 12 ); i++ )
				{
					var entry = table + i * 12;
					if ( BitConverter.ToUInt32( bytes, entry ) == 0x41544144 ) return entry + 4 + BitConverter.ToInt32( bytes, entry + 4 );
				}
				throw new InvalidDataException( "Missing DATA block" );
			}
		}
		public SoundFormat Format => (SoundFormat)bytes[Data + 2];
		public int Rate => BitConverter.ToUInt16( bytes, Data );
		public int Channels => bytes[Data + 3];
		public int Frames => BitConverter.ToInt32( bytes, Data + 8 );
		public int LoopStart => BitConverter.ToInt32( bytes, Data + 4 );
		public int LoopEnd => BitConverter.ToInt32( bytes, Data + 44 );
		public byte[] Payload => bytes[BitConverter.ToInt32( bytes, 0 )..];
	}

	sealed class Fixtures : IDisposable
	{
		readonly string engine = Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" );
		readonly string sources;
		public Fixtures()
		{
			sources = Path.Combine( Path.GetTempPath(), $"rc_sound_{Guid.NewGuid():N}" );
			Directory.CreateDirectory( sources );
		}
		public string Write( string file, byte[] bytes ) { var path = Path.Combine( sources, file ); File.WriteAllBytes( path, bytes ); return path; }
		public string Copy( string file ) => Write( file, File.ReadAllBytes( Path.Combine( AppContext.BaseDirectory, "Resources", "Audio", file ) ) );
		/// <summary>
		/// Compile the given sources in one resourcecompiler run, which saves ~0.75s of startup per
		/// source. They need distinct names, since each compiles to name.vsnd_c.
		/// </summary>
		public Compiled[] Compile( params string[] paths )
		{
			Assert.AreEqual( paths.Length, paths.Select( Path.GetFileNameWithoutExtension ).Distinct().Count(), "Sources must have distinct names" );
			var start = new ProcessStartInfo( Path.Combine( engine, "bin", "win64", "resourcecompiler.exe" ) )
			{ UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
			foreach ( var arg in new[] { "-f", "-noassert", "-skiprendersystem", "-searchpaths", $"core|{sources}" }.Concat( paths ) ) start.ArgumentList.Add( arg );
			using var process = Process.Start( start );
			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();
			if ( !process.WaitForExit( 60000 ) ) { process.Kill( entireProcessTree: true ); Assert.Fail( "Sound compiler timed out" ); }
			Assert.AreEqual( 0, process.ExitCode, output.Result + error.Result );
			return paths.Select( path => new Compiled( File.ReadAllBytes( Path.ChangeExtension( path, ".vsnd_c" ) ) ) ).ToArray();
		}
		public void Dispose()
		{
			Directory.Delete( sources, recursive: true );
		}
	}
}
