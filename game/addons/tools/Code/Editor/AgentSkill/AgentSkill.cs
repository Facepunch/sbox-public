using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor;

/// <summary>
/// The s&amp;box bootstrap skill, which tells coding agents like Claude Code and Codex how to find
/// sbox-cli and the skills that ship with the engine. sbox-cli does the installing, so the editor
/// and the command line behave the same and only ever touch copies sbox-cli recognises as its own.
/// </summary>
internal static class AgentSkill
{
	internal record Target( string Name, string Agents, string Path, bool Detected, string State );

	internal record Result( int ExitCode, string Output, string Error )
	{
		public string Message => $"{Output}{Error}".Trim();
	}

	static string Cli => System.IO.Path.Combine( Environment.CurrentDirectory, "bin", "managed", OperatingSystem.IsWindows() ? "sbox-cli.exe" : "sbox-cli" );

	/// <summary>
	/// Set when the user declines the startup prompt or removes the skill, so the editor stops asking.
	/// </summary>
	internal static bool NeverAsk
	{
		get => EditorCookie.Get( "AgentSkill.NeverAsk", false );
		set => EditorCookie.Set( "AgentSkill.NeverAsk", value );
	}

	/// <summary>
	/// Where the skill is installed for each agent sbox-cli knows about, or null if sbox-cli couldn't run.
	/// </summary>
	internal static Target[] Status()
	{
		if ( Run( "--skill-status" ) is not { ExitCode: 0 } result )
			return null;

		return JsonDocument.Parse( result.Output ).RootElement.EnumerateArray()
			.Select( x => new Target(
				x.GetProperty( "target" ).GetString(),
				x.GetProperty( "agents" ).GetString(),
				x.GetProperty( "path" ).GetString(),
				x.GetProperty( "detected" ).GetBoolean(),
				x.GetProperty( "state" ).GetString() ) )
			.ToArray();
	}

	internal static Result Install( string target ) => Run( $"--skill-install {target}" );

	internal static Result Uninstall( string target ) => Run( $"--skill-uninstall {target}" );

	static Result Run( string arguments )
	{
		try
		{
			using var process = Process.Start( new ProcessStartInfo( Cli, arguments )
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			} );

			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();
			if ( !process.WaitForExit( 10000 ) )
				return null;

			return new Result( process.ExitCode, output.Result, error.Result );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Agent skill: couldn't run {Cli}: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// When an agent is installed but doesn't have the skill, offer it once. Copies the user already
	/// accepted are quietly kept up to date.
	/// </summary>
	[Event( "editor.created" )]
	static async void OnEditorCreated( EditorMainWindow window )
	{
		// Let the editor finish restoring its layout.
		await Task.Delay( 3000 );

		var targets = await Task.Run( Status );
		if ( targets is null )
			return;

		foreach ( var target in targets.Where( x => x.Detected && x.State == "outdated" ) )
		{
			await Task.Run( () => Install( target.Name ) );
		}

		var missing = targets.Where( x => x.Detected && x.State == "missing" ).ToArray();
		if ( missing.Length == 0 || NeverAsk )
			return;

		Ask( missing );
	}

	static void Ask( Target[] missing )
	{
		var agents = string.Join( " and ", missing.Select( x => x.Name == "agents" ? "Codex" : x.Agents ) );

		var popup = new PopupDialogWidget( "smart_toy" );
		popup.WindowTitle = "AI Agents";
		popup.MessageLabel.Text = $"{agents} {(missing.Length == 1 ? "is" : "are")} installed on this computer. " +
			"Install the s&box skill so it knows how to work with s&box projects and drive the editor?\n\n" +
			"It's installed for your user, not into your projects, and stays current as s&box updates. " +
			"You can change this later in Preferences › AI Agents.";

		popup.ButtonLayout.Add( new Button( "Don't ask again" ) { Clicked = () => { NeverAsk = true; popup.Destroy(); } } );
		popup.ButtonLayout.AddStretchCell();
		popup.ButtonLayout.Add( new Button( "Not now" ) { Clicked = () => popup.Destroy() } );
		popup.ButtonLayout.Add( new Button.Primary( "Install" )
		{
			Clicked = () =>
			{
				popup.Destroy();

				foreach ( var target in missing )
				{
					if ( Install( target.Name ) is not { ExitCode: 0 } result )
						Log.Warning( $"Agent skill: couldn't install for {target.Agents}." );
					else
						Log.Info( result.Message );
				}
			}
		} );

		popup.Show();
	}
}
