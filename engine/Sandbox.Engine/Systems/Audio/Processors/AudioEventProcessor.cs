using System.Runtime.InteropServices;

namespace Sandbox.Audio;

/// <summary>
/// An audio processor that can be applied to individual sound events/handles.
/// Unlike AudioProcessor which works at the mixer level (processing all mixed sounds),
/// AudioEventProcessor processes samples for a single sound before mixing.
/// </summary>
[Expose]
public abstract class AudioEventProcessor
{
	/// <summary>
	/// Is this processor active?
	/// </summary>
	[Group( "Processor Settings" )]
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// Should we fade the influence of this processor in?
	/// </summary>
	[Range( 0, 1 )]
	[Group( "Processor Settings" )]
	public float Mix { get; set; } = 1;

	private MultiChannelBuffer scratch;

	/// <summary>
	/// The sound handle this processor is attached to.
	/// </summary>
	[Hide]
	protected SoundHandle Sound { get; private set; }

	/// <summary>
	/// Called when the processor is attached to a sound handle.
	/// </summary>
	internal void SetSound( SoundHandle sound )
	{
		Sound = sound;
		OnAttached();
	}

	/// <summary>
	/// Called when this processor is attached to a sound handle.
	/// Override to initialize any state.
	/// </summary>
	protected virtual void OnAttached()
	{
	}

	/// <summary>
	/// Should process input into output
	/// </summary>
	internal virtual void Process( MultiChannelBuffer input, MultiChannelBuffer output )
	{
		Assert.True( input.ChannelCount <= output.ChannelCount );

		output.CopyFrom( input );
		ProcessEachChannel( output );
	}

	/// <summary>
	/// Will process the buffer, and copy it back to output
	/// </summary>
	internal void ProcessInPlace( MultiChannelBuffer inputoutput )
	{
		if ( scratch is null || scratch.ChannelCount != inputoutput.ChannelCount )
		{
			scratch?.Dispose();
			scratch = new MultiChannelBuffer( inputoutput.ChannelCount );
		}

		scratch.Silence();
		Process( inputoutput, scratch );

		for ( int i = 0; i < inputoutput.ChannelCount; i++ )
		{
			inputoutput.Get( i ).CopyFrom( scratch.Get( i ) );
		}
	}

	/// <summary>
	/// Called internally to process each channel in a buffer
	/// </summary>
	private unsafe void ProcessEachChannel( MultiChannelBuffer buffer )
	{
		for ( int i = 0; i < buffer.ChannelCount; i++ )
		{
			using ( buffer.Get( i ).DataPointer( out var ptr ) )
			{
				Span<float> memory = new Span<float>( (float*)ptr, AudioEngine.MixBufferSize );
				ProcessSingleChannel( new AudioChannel( i ), memory );
			}
		}
	}

	/// <summary>
	/// For implementations that process each channel individually.
	/// Override this method for simple per-channel effects.
	/// </summary>
	protected virtual unsafe void ProcessSingleChannel( AudioChannel channel, Span<float> samples )
	{
	}

	/// <summary>
	/// Called when the processor is being destroyed or removed.
	/// </summary>
	protected virtual void OnDestroy()
	{
	}

	internal void DestroyInternal()
	{
		OnDestroy();
		scratch?.Dispose();
		scratch = null;
	}

	public override string ToString() => GetType().Name;
}
