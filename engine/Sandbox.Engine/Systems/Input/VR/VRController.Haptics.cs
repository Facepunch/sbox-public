using Facepunch.XR;

namespace Sandbox.VR;

partial record VRController
{
	/// <summary>
	/// Triggers a haptic vibration event on the controller for this hand.
	/// </summary>
	/// <remarks>
	/// If a haptic event is already running it will be interrupted immediately.
	/// </remarks>
	/// <param name="duration">How long the haptic action should last (in seconds - can be 0 to "pulse" it)</param>
	/// <param name="frequency">How often the haptic motor should bounce (0 - 320 in hz. The lower end being more useful)</param>
	/// <param name="amplitude">How intense the haptic should be (0 - 1)</param>
	[Obsolete( "Use TriggerHaptics instead" )]
	public void TriggerHapticVibration( float duration, float frequency, float amplitude )
	{
		Rumble( duration, frequency, amplitude );
	}

	internal HapticEffect ActiveHapticEffect = new();

	/// <summary>
	/// Stop all vibration events on this controller.
	/// </summary>
	public void StopAllVibrations()
	{
		StopAllHaptics();
	}

	internal void UpdateHaptics()
	{
		ActiveHapticEffect.Update( this );
	}

	/// <summary>
	/// Trigger a vibration based on a predefined <see cref="HapticPattern"/>.
	/// All <see cref="HapticPattern"/>s are normalized (start at 0, peak at 1).
	/// </summary>
	/// <param name="effect">The pattern to use</param>
	/// <param name="lengthScale">The amount to scale the pattern's length by.</param>
	/// <param name="frequencyScale">The amount to scale the pattern's frequency by.</param>
	/// <param name="amplitudeScale">The amount to scale the pattern's amplitude by.</param>
	public void TriggerHaptics( HapticEffect effect, float lengthScale = 1.0f, float frequencyScale = 1.0f, float amplitudeScale = 1.0f )
	{
		ActiveHapticEffect = effect;
		ActiveHapticEffect.Reset();

		ActiveHapticEffect.FrequencyScale = frequencyScale;
		ActiveHapticEffect.AmplitudeScale = amplitudeScale;
		ActiveHapticEffect.LengthScale = lengthScale;
	}

	/// <summary>
	/// Stops all rumble and haptic events on this controller.
	/// </summary>
	public void StopAllHaptics()
	{
		VRSystem.TriggerHapticVibration( 0, 0, 0, InputSource.LeftHand );
		VRSystem.TriggerHapticVibration( 0, 0, 0, InputSource.RightHand );

		ActiveHapticEffect = null;
	}

	internal void Rumble( float duration, float frequency, float amplitude )
	{
		//
		// Set some limitations, cap duration to 10s because OpenVR doesn't let us stop it (OpenXR does though)
		//
		if ( duration < 0.0f || duration > 10.0f ) throw new ArgumentOutOfRangeException( "Duration needs to be between 0.0 and 10.0 seconds", "duration" );
		if ( frequency < 0.0f || frequency > 320.0f ) throw new ArgumentOutOfRangeException( "Frequency needs to be between 0.0 and 320.0 hz", "frequency" );
		if ( amplitude < 0.0f || amplitude > 1.0f ) throw new ArgumentOutOfRangeException( "Amplitude needs to be between 0.0 and 1.0", "amplitude" );

		VRSystem.TriggerHapticVibration( duration, frequency, amplitude, _trackedDevice.InputSource );
	}
}
