using Sandbox.Audio;

namespace Sandbox;

partial class SoundHandle
{
	/// <summary>
	/// List of audio processors applied to this sound.
	/// </summary>
	private List<AudioEventProcessor> _processors;

	/// <summary>
	/// Scratch buffer for processor mixing.
	/// </summary>
	private MultiChannelBuffer _processorBuffer;

	/// <summary>
	/// Add an audio processor to this sound handle.
	/// Processors are applied in order before the sound is mixed.
	/// </summary>
	public void AddProcessor( AudioEventProcessor processor )
	{
		if ( processor is null )
			return;

		lock ( this )
		{
			_processors ??= new List<AudioEventProcessor>();
			_processors.Add( processor );
			processor.SetSound( this );
		}
	}

	/// <summary>
	/// Remove an audio processor from this sound handle.
	/// </summary>
	public void RemoveProcessor( AudioEventProcessor processor )
	{
		if ( processor is null )
			return;

		lock ( this )
		{
			if ( _processors?.Remove( processor ) == true )
			{
				processor.DestroyInternal();
			}
		}
	}

	/// <summary>
	/// Remove all audio processors from this sound handle.
	/// </summary>
	public void ClearProcessors()
	{
		lock ( this )
		{
			if ( _processors is null )
				return;

			foreach ( var processor in _processors )
			{
				processor.DestroyInternal();
			}

			_processors.Clear();
		}
	}

	/// <summary>
	/// Get a copy of the current processor list.
	/// </summary>
	public AudioEventProcessor[] GetProcessors()
	{
		lock ( this )
		{
			return _processors?.ToArray() ?? Array.Empty<AudioEventProcessor>();
		}
	}

	/// <summary>
	/// Get the first processor of a specific type.
	/// </summary>
	public T GetProcessor<T>() where T : AudioEventProcessor
	{
		lock ( this )
		{
			return _processors?.OfType<T>().FirstOrDefault();
		}
	}

	/// <summary>
	/// Check if this sound has any processors attached.
	/// </summary>
	internal bool HasProcessors => _processors is not null && _processors.Count > 0;

	/// <summary>
	/// Apply all processors to the sample buffer.
	/// Called from the mixing thread before spatialization.
	/// </summary>
	internal void ApplyEventProcessors( MultiChannelBuffer samples )
	{
		if ( _processors is null || _processors.Count == 0 )
			return;

		lock ( this )
		{
			foreach ( var processor in _processors )
			{
				if ( !processor.Enabled )
					continue;

				if ( processor.Mix <= 0 )
					continue;

				try
				{
					if ( processor.Mix >= 1.0f )
					{
						// Full wet - process in place
						processor.ProcessInPlace( samples );
					}
					else
					{
						// Mix dry/wet
						if ( _processorBuffer is null || _processorBuffer.ChannelCount != samples.ChannelCount )
						{
							_processorBuffer?.Dispose();
							_processorBuffer = new MultiChannelBuffer( samples.ChannelCount );
						}

						_processorBuffer.CopyFrom( samples );
						processor.ProcessInPlace( _processorBuffer );

						// Blend: output = dry * (1 - mix) + wet * mix
						samples.Scale( 1.0f - processor.Mix );
						samples.MixFrom( _processorBuffer, processor.Mix );
					}
				}
				catch ( Exception e )
				{
					Log.Warning( e, $"Exception running event processor: {processor} - {e.Message}" );
				}
			}
		}
	}

	/// <summary>
	/// Clean up processor resources when sound is disposed.
	/// </summary>
	private void DisposeProcessors()
	{
		if ( _processors is not null )
		{
			foreach ( var processor in _processors )
			{
				processor.DestroyInternal();
			}

			_processors.Clear();
			_processors = null;
		}

		_processorBuffer?.Dispose();
		_processorBuffer = null;
	}
}
