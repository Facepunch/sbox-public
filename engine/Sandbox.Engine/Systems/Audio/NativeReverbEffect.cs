namespace Sandbox.Audio;

/// <summary>
/// Thin managed wrapper around <see cref="CReverbEffect"/> — one instance per voice.
/// Stateful FDN reverb: delay lines maintain tail across frames.
/// </summary>
sealed class NativeReverbEffect : IDisposable
{
	CReverbEffect _native;

	internal NativeReverbEffect()
	{
		_native = CReverbEffect.Create();
	}

	~NativeReverbEffect() => Dispose();

	public void Dispose()
	{
		if ( _native.IsNull )
			return;

		GC.SuppressFinalize( this );
		MainThread.QueueDispose( _native );
		_native = default;
	}

	internal bool IsValid => !_native.IsNull;

	internal void Apply( float t60Low, float t60Mid, float t60High, MultiChannelBuffer input, MultiChannelBuffer output )
	{
		if ( !IsValid )
			return;

		_native.Apply( t60Low, t60Mid, t60High, input._native, output._native );
	}
}
