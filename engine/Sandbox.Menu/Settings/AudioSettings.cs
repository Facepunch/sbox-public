using NativeEngine;

namespace Sandbox.Internal;

public static partial class AudioSettings
{
	public struct AudioDevice
	{
		public string Id { get; private set; }
		public string Name { get; private set; }
		public bool IsAvailable { get; private set; }
		public bool IsDefault { get; private set; }

		internal AudioDevice( string id, string name, AudioDeviceDesc desc )
		{
			Id = id;
			Name = name;
			IsAvailable = desc.IsAvailable;
			IsDefault = desc.IsDefault;
		}
	}

	/// <summary>
	/// Set the active audio device by id
	/// </summary>
	/// <param name="id"></param>
	public static void SetActiveDevice( string id )
	{
		g_pSoundSystem.SetActiveAudioDevice( id );
	}

	/// <summary>
	/// Get the active audio device
	/// </summary>
	/// <returns></returns>
	public static AudioDevice GetActiveDevice()
	{
		var activeId = g_pSoundSystem.GetActiveAudioDevice();
		var devices = GetAudioDevices();
		var device = devices.FirstOrDefault( d => d.Id == activeId );

		if ( device.IsAvailable )
			return device;

		return devices.FirstOrDefault();
	}

	/// <summary>
	/// Names of the microphones voice chat can record from
	/// </summary>
	public static IEnumerable<string> GetRecordingDevices() => VoiceManager.GetRecordingDevices();

	/// <summary>
	/// Name of the microphone the system is using as its default right now, empty if there isn't one
	/// </summary>
	public static string GetDefaultRecordingDevice() => VoiceManager.GetDefaultRecordingDevice();

	/// <summary>
	/// Keep this microphone open without sending anything, so <see cref="MicrophoneLevel"/> can be shown.
	/// Call it every frame while testing, empty for the system default.
	/// </summary>
	public static void TestMicrophone( string device ) => VoiceManager.TestMicrophone( device );

	/// <summary>
	/// How loud the microphone is right now in decibels, 0 being as loud as it goes
	/// </summary>
	public static float MicrophoneLevel => VoiceManager.Level;

	/// <summary>
	/// Get all audio devices supported by the current platform
	/// </summary>
	public static IEnumerable<AudioDevice> GetAudioDevices()
	{
		var deviceCount = g_pSoundSystem.GetNumAudioDevices();

		for ( var i = 0; i < deviceCount; i++ )
		{
			yield return new AudioDevice(
				g_pSoundSystem.GetAudioDeviceId( i ),
				g_pSoundSystem.GetAudioDeviceName( i ),
				g_pSoundSystem.GetAudioDeviceDesc( i )
			);
		}
	}
}
