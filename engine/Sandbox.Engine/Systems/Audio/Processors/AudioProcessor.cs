using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Sandbox.Audio;

/// <summary>
/// Takes a bunch of samples and processes them. It's common for these to be chained together.
/// It's also common for the processor to store state between calls.
/// </summary>
public abstract partial class AudioProcessor
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

	private MultiChannelBuffer scratch = new MultiChannelBuffer( 8 );

	internal Transform _listener;

	/// <summary>
	/// The listener's position in this frame.
	/// </summary>
	[Hide]
	protected Transform Listener => _listener;

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
	/// For implementations that process each channel individually
	/// </summary>
	protected virtual unsafe void ProcessSingleChannel( AudioChannel channel, Span<float> input )
	{

	}

	public JsonObject Serialize()
	{
		var js = Json.SerializeAsObject( this );
		js["__type"] = GetType().Name;

		return js;
	}

	public void Deserialize( JsonObject node )
	{
		Json.DeserializeToObject( this, node );
		OnDeserialized( node );
	}

	/// <summary>
	/// Lets processors migrate properties saved by older versions
	/// </summary>
	internal virtual void OnDeserialized( JsonObject node )
	{

	}

	public override string ToString() => GetType().Name;

	protected virtual void OnDestroy()
	{

	}

	internal virtual void OnRemovedInternal()
	{
		OnDestroy();
	}
}

/// <summary>
/// Represents an audio channel, between 0 and 7. This is used to index into buffers.
/// This is used rather than an int to avoid unfortuate bugs.
/// </summary>
public struct AudioChannel
{
	public static AudioChannel Left => new AudioChannel( 0 );
	public static AudioChannel Right => new AudioChannel( 1 );

	internal int channel;

	public AudioChannel( int i )
	{
		channel = i;
	}

	public int Get() => channel;
}

/// <summary>
/// Stores a variable per channel
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage( "Compiler", "CS0169" )]
public struct PerChannel<T>
{
#pragma warning disable CS0169, CS0649
	private T _0, _1, _2, _3, _4, _5, _6, _7;
#pragma warning restore CS0169, CS0649

	private Span<T> Values => MemoryMarshal.CreateSpan( ref _0, 8 );

	[Obsolete]
	public T[] Value => Values.ToArray();

	public PerChannel()
	{

	}

	public static implicit operator PerChannel<T>( T v )
	{
		PerChannel<T> x = new PerChannel<T>();

		for ( int i = 0; i < 8; i++ )
		{
			x.Values[i] = v;
		}

		return x;
	}

	/// <summary>
	/// Get the value in a channel
	/// </summary>
	public T Get( AudioChannel i ) => Values[i.channel];

	/// <summary>
	/// Set the value in a channel
	/// </summary>
	public void Set( AudioChannel i, T value ) => Values[i.channel] = value;

}
