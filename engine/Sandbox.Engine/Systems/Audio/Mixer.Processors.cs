using Sandbox.Utility;
using System.Runtime.InteropServices;

namespace Sandbox.Audio;

public partial class Mixer
{
	// Copy-on-write: main thread replaces the reference; mix thread reads it atomically and iterates a stable snapshot.
	volatile List<AudioProcessor> _processorList = [];

	public void AddProcessor( AudioProcessor ap )
	{
		ThreadSafe.AssertIsMainThread();
		_processorList = [.. _processorList, ap];
	}

	public void ClearProcessors()
	{
		ThreadSafe.AssertIsMainThread();
		var old = _processorList;
		_processorList = [];
		foreach ( var p in old ) p.OnRemovedInternal();
	}

	public void RemoveProcessor( AudioProcessor ap )
	{
		ThreadSafe.AssertIsMainThread();
		ap.OnRemovedInternal();
		_processorList = [.. _processorList.Where( p => p != ap )];
	}

	[Hide]
	public int ProcessorCount => _processorList.Count;

	public AudioProcessor[] GetProcessors() => _processorList.ToArray();

	public T GetProcessor<T>() where T : AudioProcessor => _processorList.OfType<T>().FirstOrDefault();

	static readonly Superluminal _processors = new Superluminal( "ApplyProcessors", "#4d5e73" );

	// Instance field: each Mixer has its own processor scratch buffer.
	// Previously static, which was a data race when multiple mixers ran on the mix thread.
	MultiChannelBuffer _processorBuffer;

	/// <summary>
	/// Actually apply the processors to the output buffer
	/// </summary>
	void ApplyProcessors()
	{
		using var _ = _processors.Start();

		var processors = _processorList;

		if ( _snapshot is not null && _snapshot.RemovedListeners.Count > 0 )
		{
			foreach ( var processor in processors )
				processor.RemoveListeners( _snapshot.RemovedListeners );
		}

		foreach ( var listener in _usedListeners )
		{
			if ( !_outputBuffers.TryGetValue( listener, out var targetBuffer ) )
				continue;

			var mixTransform = default( Transform );
			if ( _snapshot is not null )
			{
				var listeners = CollectionsMarshal.AsSpan( _snapshot.Listeners );
				for ( var i = 0; i < listeners.Length; i++ )
				{
					if ( listeners[i].Listener == listener )
					{
						mixTransform = listeners[i].MixTransform;
						break;
					}
				}
			}

			ApplyProcessors( processors, targetBuffer, listener, mixTransform );

			_outputBuffer.MixFrom( targetBuffer, 1.0f );
		}
	}

	void ApplyProcessors( List<AudioProcessor> processors, MultiChannelBuffer targetBuffer, Listener listener, Transform mixTransform )
	{
		foreach ( var processor in processors )
		{
			if ( !processor.Enabled ) continue;
			if ( processor.Mix <= 0 ) continue;
			if ( processor.TargetListener is not null && processor.TargetListener != listener ) continue;

			try
			{
				if ( _processorBuffer is not null && _processorBuffer.ChannelCount != targetBuffer.ChannelCount )
				{
					_processorBuffer.Dispose();
					_processorBuffer = null;
				}

				_processorBuffer ??= new MultiChannelBuffer( targetBuffer.ChannelCount );
				_processorBuffer.CopyFrom( targetBuffer );

				processor._listener = mixTransform;
				processor.SetListener( listener );
				processor.ProcessInPlace( _processorBuffer );

				targetBuffer.Scale( (1.0f - processor.Mix).Clamp( 0f, 1f ) );
				targetBuffer.MixFrom( _processorBuffer, processor.Mix.Clamp( 0, 1 ) );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Exception running processor: {processor} - {e.Message}" );
			}
		}
	}
}
