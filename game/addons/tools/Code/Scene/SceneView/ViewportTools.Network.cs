using Editor.Preferences;
using Sandbox.Network;
using System.Diagnostics;

namespace Editor;

partial class ViewportTools
{
	class LobbySettings
	{
		/// <summary>
		/// Who can join this lobby?
		/// </summary>
		public LobbyPrivacy LobbyPrivacy
		{
			get => EditorUtility.Network.HostPrivacy;
			set => EditorUtility.Network.HostPrivacy = value;
		}

		/// <summary>
		/// Simulate lag by overriding latency to the specified value in ms.
		/// </summary>
		[Range( 0f, 500f ), Step( 25f )]
		public int SimulateLag
		{
			get => ConsoleSystem.GetValueInt( "net_fakelag" );
			set => ConsoleSystem.SetValue( "net_fakelag", value.ToString() );
		}

		/// <summary>
		/// Simulate packet loss as a percentage of packets lost.
		/// </summary>
		[Range( 0f, 100f ), Step( 0.5f )]
		public float SimulatePacketLoss
		{
			get => ConsoleSystem.GetValueFloat( "net_fakepacketloss" );
			set => ConsoleSystem.SetValue( "net_fakepacketloss", value.ToString() );
		}
	}

	private void OpenNetworkSettings()
	{
		var menu = new ContextMenu( this );

		{
			var widget = new Widget( menu );
			widget.OnPaintOverride = () =>
			{
				Paint.SetBrushAndPen( Theme.WidgetBackground.WithAlpha( 0.5f ) );
				Paint.DrawRect( widget.LocalRect.Shrink( 2f ), 2f );
				return true;
			};

			var cs = new ControlSheet();
			var settings = new LobbySettings();
			var settingsSo = settings.GetSerialized();

			cs.AddRow( settingsSo.GetProperty( nameof( LobbySettings.LobbyPrivacy ) ) );
			cs.AddRow( settingsSo.GetProperty( nameof( LobbySettings.SimulateLag ) ) );
			cs.AddRow( settingsSo.GetProperty( nameof( LobbySettings.SimulatePacketLoss ) ) );

			widget.Layout = cs;
			widget.Layout.Margin = 8;
			widget.MaximumWidth = 400f;

			menu.AddWidget( widget );
		}

		menu.AddSeparator();

		menu.AddOption( new( "Start Hosting", "dns", EditorUtility.Network.StartHosting ) { Enabled = !EditorUtility.Network.Active } );
		menu.AddOption( new( "Disconnect", "phonelink_erase", EditorUtility.Network.Disconnect ) { Enabled = EditorUtility.Network.Active } );

		menu.AddSeparator();
		menu.AddOption( new( "Join via new instance", "connected_tv", SpawnProcess ) { Enabled = EditorUtility.Network.Hosting } );
		menu.AddOption( new( "Start dedicated server", "terminal", SpawnDedicatedServer ) );
		menu.AddSeparator();
		menu.AddOption( new( "Preferences", "tune", OpenPreferences ) );

		menu.OpenAtCursor();
	}

	void OpenPreferences()
	{
		var window = EditorPreferencesWindow.OpenEditorPreferences();
		window.SwitchPage<PageNetworking>();
	}

	static void AddUserCommandLineArgs( ProcessStartInfo startInfo, string argumentString )
	{
		if ( string.IsNullOrWhiteSpace( argumentString ) )
			return;

		var args = argumentString.Split( ' ',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

		foreach ( var arg in args )
		{
			startInfo.ArgumentList.Add( arg );
		}
	}

	void SpawnDedicatedServer()
	{
		var p = new Process();
		p.StartInfo.FileName = "sbox-server.exe";
		p.StartInfo.WorkingDirectory = Environment.CurrentDirectory;

		p.StartInfo.ArgumentList.Add( "+game" );
		p.StartInfo.ArgumentList.Add( Project.Current.GetProjectPath() );

		AddUserCommandLineArgs( p.StartInfo, EditorPreferences.DedicatedServerCommandLineArgs );

		p.Start();
	}

	void SpawnProcess()
	{
		var p = new Process();
		p.StartInfo.FileName = "sbox.exe";
		p.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
		p.StartInfo.CreateNoWindow = true;
		p.StartInfo.RedirectStandardOutput = true;
		p.StartInfo.RedirectStandardError = true;
		p.StartInfo.UseShellExecute = false;

		p.StartInfo.ArgumentList.Add( "-joinlocal" );

		if ( EditorPreferences.WindowedLocalInstances )
		{
			p.StartInfo.ArgumentList.Add( "-sw" );
			p.StartInfo.ArgumentList.Add( "-720" );
		}

		AddUserCommandLineArgs( p.StartInfo, EditorPreferences.NewInstanceCommandLineArgs );

		p.Start();
	}
}
