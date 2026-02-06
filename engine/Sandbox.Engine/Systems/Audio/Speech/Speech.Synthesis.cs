using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;

namespace Sandbox.Speech;

/// <summary>
/// A speech synthesis stream. Lets you write text into speech and output it to a <see cref="SoundHandle"/>.
/// </summary>
public sealed class Synthesizer
{
	public record struct InstalledVoice( string Name, string Gender, string Age );

	private PromptBuilder Builder;
	private SpeechSynthesizer SpeechSynthesizer;

	/// <summary>
	/// Called by SpeechSynthesizer to populate viseme data.
	/// </summary>
	private Action<int, TimeSpan> OnVisemeReachedEvent { get; set; }

	private List<InstalledVoice> _installedVoices { get; set; }

	/// <summary>
	/// Gets a list of currently installed voices on the user's system.
	/// </summary>
	public ReadOnlyCollection<InstalledVoice> InstalledVoices
	{
		get => _installedVoices.AsReadOnly();
	}

	/// <summary>
	/// Gets the current voice being used by <see cref="SpeechSynthesizer"/>.
	/// </summary>
	public string CurrentVoice => SpeechSynthesizer.Voice.Name;

	public Synthesizer()
	{
		Builder = new();
		SpeechSynthesizer = new();
		SpeechSynthesizer.VisemeReached += new EventHandler<VisemeReachedEventArgs>( ( object obj, VisemeReachedEventArgs e ) => OnVisemeReachedEvent?.Invoke( e.Viseme, e.AudioPosition ) );

		_installedVoices = SpeechSynthesizer.GetInstalledVoices()
			.Where( x => x.Enabled )
			// Convert to our record struct type
			.Select( x => new InstalledVoice() { Name = x.VoiceInfo.Name, Gender = x.VoiceInfo.Gender.ToString(), Age = x.VoiceInfo.Age.ToString() } )
			.ToList();
	}

	/// <summary>
	/// Tries to set the voice to a matching voice name installed on the user's system.
	/// </summary>
	/// <param name="voiceName"></param>
	/// <returns></returns>
	public Synthesizer TrySetVoice( string voiceName )
	{
		// Only set the voice if we have it installed.
		if ( InstalledVoices.Select( x => x.Name ).FirstOrDefault( x => x.Equals( voiceName, StringComparison.InvariantCulture ) ) != null )
		{
			SpeechSynthesizer.SelectVoice( voiceName );
		}
		return this;
	}

	/// <summary>
	/// Tries to set the voice matching gender and age criteria.
	/// </summary>
	/// <param name="gender"></param>
	/// <param name="age"></param>
	/// <returns></returns>
	public Synthesizer TrySetVoice( string gender = "Male", string age = null )
	{
		foreach ( var voice in InstalledVoices )
		{
			// Try to first set by gender, if the age isn't set
			if ( voice.Gender.ToString().Equals( gender, StringComparison.InvariantCulture ) && string.IsNullOrEmpty( age ) )
			{
				SpeechSynthesizer.SelectVoice( voice.Name );
			}
			if ( voice.Gender.ToString().Equals( gender, StringComparison.InvariantCulture )
				&& voice.Age.ToString().Equals( age, StringComparison.InvariantCulture ) )
			{
				SpeechSynthesizer.SelectVoice( voice.Name );
			}
		}
		return this;
	}

	/// <summary>
	/// Adds some text to the speech.
	/// </summary>
	/// <param name="input"></param>
	/// <returns></returns>
	public Synthesizer WithText( string input )
	{
		Builder.AppendText( input );
		return this;
	}

	/// <summary>
	/// Registers an action to fetch all viseme data.
	/// </summary>
	/// <param name="action"></param>
	/// <returns></returns>
	public Synthesizer OnVisemeReached( Action<int, TimeSpan> action )
	{
		OnVisemeReachedEvent += action;
		return this;
	}

	/// <summary>
	/// Sets the playback rate of the synthesizer.
	/// </summary>
	/// <param name="rate"></param>
	/// <returns></returns>
	public Synthesizer WithRate( int rate )
	{
		SpeechSynthesizer.Rate = rate;
		return this;
	}

	/// <summary>
	/// Adds a break to the speech.
	/// </summary>
	/// <returns></returns>
	public Synthesizer WithBreak()
	{
		Builder.AppendBreak();
		return this;
	}

	/// <summary>
	/// Takes info from <see cref="Builder"/> and creates a <see cref="System.Speech.Synthesis.SpeechSynthesizer"/>, outputting to a stream object.
	/// Using <see cref="AudioStreamHelpers"/> we then read all the PCM samples, and write it to a SoundStream.
	/// This means it'll work like any other sound.
	/// </summary>
	/// <returns></returns>
	public SoundHandle Play()
	{
		const int sampleRate = 44100;

#pragma warning disable CA2000 // Dispose objects before losing scope
		// TODO we dont do any liefecycle management on SpeechSynthesizer sounds whatsoever, this needs a complete rework to fix the diagnoser warning
		// related: https://github.com/Facepunch/sbox-public/issues/4184
		var soundStream = new SoundStream( sampleRate );
#pragma warning restore CA2000 // Dispose objects before losing scope
		var stream = new MemoryStream();

		SpeechSynthesizer.SetOutputToAudioStream( stream, new SpeechAudioFormatInfo( sampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono ) );
		SpeechSynthesizer.Speak( Builder );

		// Seek to the start of the stream.
		stream.Seek( 0, SeekOrigin.Begin );

		var data = AudioStreamHelpers.ReadPcmSamplesToEnd( stream );
		soundStream.WriteData( data );

		return soundStream.Play();
	}

	/// <summary>
	/// A collection of helper methods to help read PCM samples. Taken mostly from https://github.com/Facepunch/sbox-arcade/
	/// </summary>
	private static class AudioStreamHelpers
	{
		private static short ReadPcmSample( byte[] buffer, int offset, int bps )
		{
			if ( bps != 16 )
			{
				throw new ArgumentException( "This function only supports 16-bit PCM data" );
			}

			short val = 0;
			for ( var i = 0; i < (bps >> 3); ++i )
			{
				val |= (short)(buffer[offset + i] << (8 * i));
			}

			return val;
		}

		[ThreadStatic]
		private static byte[] _sBuffer;

		private static int ReadPcmSamples( Stream stream, short[] dest )
		{
			const int bufferSize = 8192;
			const int channels = 1;
			const int bps = (int)AudioBitsPerSample.Sixteen;
			const int sampleSize = (bps >> 3) * channels;

			if ( _sBuffer == null )
			{
				_sBuffer = new byte[bufferSize];
			}

			var readAmount = (bufferSize / sampleSize) * sampleSize;
			var totalBytes = dest.Length * sampleSize;
			var readBytes = 0;
			var offset = 0;

			while ( readBytes < totalBytes )
			{
				var chunkBytes = stream.Read( _sBuffer, 0, Math.Min( readAmount, totalBytes - readBytes ) );
				if ( chunkBytes <= 0 ) break;

				readBytes += chunkBytes;

				var readSamples = chunkBytes / sampleSize;

				// Assumes channels = 1
				for ( var i = 0; i < readSamples; ++i )
				{
					dest[offset + i] = (short)ReadPcmSample( _sBuffer, i * sampleSize, bps );
				}

				offset += readSamples;
			}

			return readBytes / sampleSize;
		}

		public static short[] ReadPcmSamplesToEnd( Stream stream )
		{
			const int frameSize = 4096;

			var frames = new List<short[]>();
			var buffer = new short[frameSize];

			int read;
			while ( (read = ReadPcmSamples( stream, buffer )) > 0 )
			{
				var copy = new short[read];
				Array.Copy( buffer, copy, read );
				frames.Add( copy );
			}

			return frames.SelectMany( x => x ).ToArray();
		}
	}

}
