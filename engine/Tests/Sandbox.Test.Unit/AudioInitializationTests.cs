using Sandbox.Audio;

namespace Sandbox.Test;

/// <summary>
/// Keeps managed audio defaults usable by tests that have not loaded the native engine.
/// </summary>
[TestClass]
public class AudioInitializationTests
{
	/// <summary>
	/// Reading the default channel count must not call an uninitialized native function pointer.
	/// </summary>
	[TestMethod]
	public void ManagedAudioDefaultsDoNotRequireNativeInterop()
	{
		Assert.AreEqual( 2, AudioEngine.ChannelCount );
	}
}
