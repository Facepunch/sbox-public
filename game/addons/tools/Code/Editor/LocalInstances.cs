using Editor.Preferences;
using System.Diagnostics;

namespace Editor;

/// <summary>
/// Extra game instances on this machine that join the editor's session.
/// </summary>
public static class LocalInstances
{
	/// <summary>
	/// Launch sbox.exe with -joinlocal. Returns the process id.
	/// </summary>
	public static int Spawn( bool? windowed = null )
	{
		using var p = new Process();

		p.StartInfo.FileName = "sbox.exe";
		p.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
		p.StartInfo.CreateNoWindow = true;
		p.StartInfo.RedirectStandardOutput = true;
		p.StartInfo.RedirectStandardError = true;
		p.StartInfo.UseShellExecute = false;

		p.StartInfo.ArgumentList.Add( "-joinlocal" );
		p.StartInfo.ArgumentList.Add( "+net_local_port" );
		p.StartInfo.ArgumentList.Add( ConsoleSystem.GetValueInt( "net_local_port" ).ToString() );

		// Count existing instances and assign the next possible instance id
		var instanceCount = Process.GetProcessesByName( "sbox" ).Length;
		p.StartInfo.ArgumentList.Add( "+instanceid" );
		p.StartInfo.ArgumentList.Add( (instanceCount + 1).ToString() );

		if ( windowed ?? EditorPreferences.WindowedLocalInstances )
		{
			p.StartInfo.ArgumentList.Add( "-sw" );
			p.StartInfo.ArgumentList.Add( "-720" );
		}

		var extra = EditorPreferences.NewInstanceCommandLineArgs;
		if ( !string.IsNullOrWhiteSpace( extra ) )
		{
			foreach ( var arg in extra.Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
			{
				p.StartInfo.ArgumentList.Add( arg );
			}
		}

		p.Start();
		return p.Id;
	}

	/// <summary>
	/// Spawn an instance, wait for it to join, then disconnect so it takes over as host.
	/// Returns false if it didn't join in time or we stopped hosting meanwhile.
	/// </summary>
	public static async Task<bool> MigrateHostAsync( bool? windowed = null, float timeoutSeconds = 120f )
	{
		if ( !EditorUtility.Network.Hosting ) return false;

		var before = Connection.All.Count;
		Spawn( windowed );

		RealTimeUntil deadline = timeoutSeconds;

		while ( !deadline )
		{
			await Task.Delay( 500 );

			if ( !EditorUtility.Network.Hosting ) return false;

			if ( Connection.All.Count > before && Connection.All.All( c => c.IsActive ) )
			{
				Log.Info( "Handing the game to the new instance" );
				EditorUtility.Network.Disconnect();
				return true;
			}
		}

		return false;
	}
}
