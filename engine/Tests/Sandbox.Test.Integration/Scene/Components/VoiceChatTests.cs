using System;
using System.Linq;
using Sandbox.Platform;

namespace SceneTests.Components;

/// <summary>
/// What gets sent when the local player talks, depending on settings, preferences and <see cref="Voice"/> components.
/// There's no audio device in tests, so playback (mixer routing, blocked users) isn't covered here.
/// </summary>
[TestClass]
public class VoiceChatTests
{
	private static readonly byte[] Packet = [1, 2, 3, 4];

	[TestMethod]
	public void PlayerWithoutVoiceTalksToEveryone()
	{
		using var network = Helpers.InitializeHostWithTestConnection();

		WithScene( ( scene, system ) =>
		{
			system.Tick();

			Assert.IsTrue( system.IsListening );

			system.BroadcastPacket( Packet );

			Assert.AreEqual( Guid.Empty, SingleMessage( network ).VoiceId );
		} );
	}

	[TestMethod]
	public void NothingSentWhenGameDisablesVoiceChat()
	{
		using var network = Helpers.InitializeHostWithTestConnection();

		WithScene( ( scene, system ) =>
		{
			ProjectSettings.Platform.VoiceChatEnabled = false;

			system.Tick();
			system.BroadcastPacket( Packet );

			Assert.IsFalse( system.IsListening );
			Assert.AreEqual( 0, network.GetMessageCount<VoiceChatBroadcastMsg>() );
		} );
	}

	/// <summary>
	/// The game's voice chat setting is only about talking to everyone, <see cref="Voice"/> components keep working.
	/// </summary>
	[TestMethod]
	public void VoiceComponentWorksWhenGameDisablesVoiceChat()
	{
		using var network = Helpers.InitializeHostWithTestConnection();

		WithScene( ( scene, system ) =>
		{
			var voice = AddVoice( scene );
			ProjectSettings.Platform.VoiceChatEnabled = false;

			system.Tick();
			system.BroadcastPacket( Packet );

			Assert.IsTrue( system.IsListening );
			Assert.AreEqual( voice.Id, SingleMessage( network ).VoiceId );
		} );
	}

	[TestMethod]
	public void NothingSentWhenPlayerDisablesVoiceChat()
	{
		using var network = Helpers.InitializeHostWithTestConnection();

		WithScene( ( scene, system ) =>
		{
			Preferences.VoiceEnabledSetting = false;

			system.Tick();
			system.BroadcastPacket( Packet );

			Assert.IsFalse( system.IsListening );
			Assert.AreEqual( 0, network.GetMessageCount<VoiceChatBroadcastMsg>() );
		} );
	}

	[TestMethod]
	public void VoiceComponentCarriesPlayersVoice()
	{
		using var network = Helpers.InitializeHostWithTestConnection();

		WithScene( ( scene, system ) =>
		{
			var voice = AddVoice( scene );

			system.Tick();
			system.BroadcastPacket( Packet );

			Assert.AreEqual( voice, system.LocalVoice );
			Assert.AreEqual( voice.Id, SingleMessage( network ).VoiceId );
		} );
	}

	/// <summary>
	/// A game can turn a player's voice off by disabling their <see cref="Voice"/>, including
	/// one that starts disabled. That mustn't fall back to everyone hearing them.
	/// </summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void DisabledVoiceComponentMutesPlayer( bool startEnabled )
	{
		using var network = Helpers.InitializeHostWithTestConnection();

		WithScene( ( scene, system ) =>
		{
			var voice = AddVoice( scene, startEnabled );
			voice.Enabled = false;

			system.Tick();
			system.BroadcastPacket( Packet );

			Assert.IsFalse( system.IsListening );
			Assert.AreEqual( 0, network.GetMessageCount<VoiceChatBroadcastMsg>() );
		} );
	}

	/// <summary>
	/// Once the game uses <see cref="Voice"/> components, a player without one isn't heard at all,
	/// rather than talking to everyone.
	/// </summary>
	[TestMethod]
	public void PlayerWithoutVoiceSilentWhenOthersHaveOne()
	{
		WithScene( ( scene, system ) =>
		{
			using var connections = new ClientAndHost( Game.TypeLibrary );
			connections.BecomeClient();

			system.Tick();
			system.BroadcastPacket( Packet );

			Assert.IsTrue( system.IsListening );
			Assert.AreEqual( 1, CountMessages( connections.Host ) );

			var hostVoice = AddVoice( scene );
			hostVoice.GameObject.NetworkSpawn( connections.Host );

			Assert.IsTrue( hostVoice.IsProxy );

			system.Tick();
			system.BroadcastPacket( Packet );

			Assert.IsNull( system.LocalVoice );
			Assert.IsFalse( system.IsListening );
			Assert.AreEqual( 1, CountMessages( connections.Host ) );
		} );
	}

	/// <summary>
	/// Games with <see cref="Voice"/> components show their own list of who's talking, so ours goes away.
	/// </summary>
	[TestMethod]
	public void SpeakerListHiddenWhenGameUsesVoiceComponents()
	{
		WithScene( ( scene, system ) =>
		{
			Assert.IsTrue( VoiceChat.ShowSpeakers );

			AddVoice( scene, startEnabled: false );

			Assert.IsFalse( VoiceChat.ShowSpeakers );
		} );
	}

	[TestMethod]
	public void RemovingVoiceComponentGoesBackToEveryone()
	{
		using var network = Helpers.InitializeHostWithTestConnection();

		WithScene( ( scene, system ) =>
		{
			var voice = AddVoice( scene );

			system.Tick();

			voice.GameObject.Destroy();
			scene.ProcessDeletes();

			system.Tick();
			system.BroadcastPacket( Packet );

			Assert.IsNull( system.LocalVoice );
			Assert.AreEqual( Guid.Empty, SingleMessage( network ).VoiceId );
		} );
	}

	/// <summary>
	/// Capture keeps going for a moment after push-to-talk is released, so the end of what
	/// they said still has to go out through the <see cref="Voice"/> they were talking through.
	/// </summary>
	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	public void ReleaseTailGoesThroughSameVoice( int talkingIndex )
	{
		using var network = Helpers.InitializeHostWithTestConnection();

		WithScene( ( scene, system ) =>
		{
			// Two local Voices, so the one we talked through has to be remembered rather than found again

			Voice[] voices = [AddVoice( scene ), AddVoice( scene )];

			foreach ( var voice in voices )
			{
				voice.Mode = Voice.ActivateMode.Manual;
			}

			var talking = voices[talkingIndex];
			talking.IsListening = true;

			system.Tick();

			Assert.AreEqual( talking, system.LocalVoice );

			talking.IsListening = false;

			system.Tick();
			system.BroadcastPacket( Packet );

			Assert.IsFalse( system.IsListening );
			Assert.AreEqual( talking.Id, SingleMessage( network ).VoiceId );
		} );
	}

	/// <summary>
	/// The player's Voice Mode wins over the component's <see cref="Voice.Mode"/>: open microphone
	/// doesn't need a key, push to talk always does, and off is off.
	/// </summary>
	[TestMethod]
	[DataRow( VoiceMode.OpenMicrophone, Voice.ActivateMode.AlwaysOn, false, true )]
	[DataRow( VoiceMode.OpenMicrophone, Voice.ActivateMode.PushToTalk, false, true )]
	[DataRow( VoiceMode.OpenMicrophone, Voice.ActivateMode.Manual, false, false )]
	[DataRow( VoiceMode.OpenMicrophone, Voice.ActivateMode.Manual, true, true )]
	[DataRow( VoiceMode.PushToTalk, Voice.ActivateMode.AlwaysOn, false, false )]
	[DataRow( VoiceMode.PushToTalk, Voice.ActivateMode.PushToTalk, false, false )]
	[DataRow( VoiceMode.PushToTalk, Voice.ActivateMode.Manual, true, false )]
	[DataRow( VoiceMode.Disabled, Voice.ActivateMode.AlwaysOn, false, false )]
	[DataRow( VoiceMode.Disabled, Voice.ActivateMode.Manual, true, false )]
	public void VoiceListensByPlayerVoiceMode( VoiceMode voiceMode, Voice.ActivateMode activateMode, bool manualListening, bool expected )
	{
		WithScene( ( scene, system ) =>
		{
			Preferences.VoiceMode = voiceMode;

			var voice = AddVoice( scene );
			voice.Mode = activateMode;
			voice.IsListening = manualListening;

			Assert.AreEqual( expected, voice.IsListening );
		} );
	}

	/// <summary>
	/// Platform.config files saved before <see cref="PlatformSettings.VoiceChatEnabled"/> existed
	/// still get voice chat, so legacy multiplayer games keep working.
	/// </summary>
	[TestMethod]
	public void VoiceChatOnWhenMissingFromLoadedSettings()
	{
		var settings = LoadPlatformSettings(
			"""
			{
				"ChatEnabled": true,
				"ChatShowUI": true,
				"ChatMaxMessageLength": 100,
				"__guid": "4da94f96-67be-44bb-913a-6f8f74398220",
				"__schema": "configdata",
				"__type": "PlatformSettings",
				"__version": 1
			}
			""" );

		// Make sure the file was actually applied

		Assert.AreEqual( 100, settings.ChatMaxMessageLength );

		Assert.IsTrue( settings.VoiceChatEnabled );
		Assert.IsTrue( settings.VoiceChatShowSpeakers );
	}

	[TestMethod]
	public void VoiceChatOffWhenLoadedSettingsTurnItOff()
	{
		var settings = LoadPlatformSettings(
			"""
			{
				"VoiceChatEnabled": false,
				"VoiceChatShowSpeakers": false,
				"__schema": "configdata",
				"__type": "PlatformSettings",
				"__version": 1
			}
			""" );

		Assert.IsFalse( settings.VoiceChatEnabled );
		Assert.IsFalse( settings.VoiceChatShowSpeakers );
	}

	#region Helpers

	private static int CountMessages( TestConnection connection )
	{
		return connection.Messages.Count( x => x.Payload is VoiceChatBroadcastMsg );
	}

	private static VoiceChatBroadcastMsg SingleMessage( Helpers.TestNetworkSystem network )
	{
		return network.Connection.Messages
			.Select( x => x.Payload )
			.OfType<VoiceChatBroadcastMsg>()
			.Single();
	}

	/// <summary>
	/// Runs the body with a pushed scene, voice chat on and an open microphone, then destroys
	/// the scene and puts the settings and preferences back.
	/// </summary>
	private static void WithScene( Action<Scene, VoiceChatSystem> body )
	{
		var settings = ProjectSettings.Platform;

		var previousEnabled = settings.VoiceChatEnabled;
		var previousVoiceEnabled = Preferences.VoiceEnabledSetting;
		var previousVoiceMode = Preferences.VoiceMode;

		settings.VoiceChatEnabled = true;
		Preferences.VoiceEnabledSetting = true;
		Preferences.VoiceMode = VoiceMode.OpenMicrophone;

		Scene scene = null;

		try
		{
			scene = new Scene();

			using var sceneScope = scene.Push();

			var system = scene.GetSystem<VoiceChatSystem>();

			Assert.IsNotNull( system, "VoiceChatSystem should be created for the scene" );

			body( scene, system );
		}
		finally
		{
			scene?.Destroy();

			settings.VoiceChatEnabled = previousEnabled;
			Preferences.VoiceEnabledSetting = previousVoiceEnabled;
			Preferences.VoiceMode = previousVoiceMode;
		}
	}

	private static Voice AddVoice( Scene scene, bool startEnabled = true )
	{
		var go = scene.CreateObject();
		go.Name = "Voice";

		return go.AddComponent<Voice>( startEnabled );
	}

	/// <summary>
	/// Loads <see cref="PlatformSettings"/> from a Platform.config containing <paramref name="json"/>.
	/// </summary>
	private static PlatformSettings LoadPlatformSettings( string json )
	{
		var fs = new MemoryFileSystem();

		try
		{
			// Load lowercases the filename, and MemoryFileSystem is case-sensitive

			fs.WriteAllText( "platform.config", json );

			var settings = ProjectSettings.Load<PlatformSettings>( fs, "Platform.config" );

			Assert.IsTrue( settings.LoadedFromDisk );

			return settings;
		}
		finally
		{
			fs.Dispose();
		}
	}

	#endregion Helpers
}
