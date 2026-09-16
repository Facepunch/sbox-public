using System.Text;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Installs git hooks that restore the prebuilt public artifacts and interop bindings whenever the
/// working tree moves to a different commit (pull, merge, rebase, branch switch), so a public source
/// checkout stays runnable without rerunning Setup. Modelled on Unreal Engine's Setup hooks.
/// Public source distribution only: refuses to run in a full source checkout, and the hooks it writes
/// exit immediately if they find themselves in one.
/// </summary>
internal class InstallGitHooks
{
	/// <summary>Present in every hook we write; a hook without it belongs to the user and is left alone.</summary>
	private const string Marker = "Installed by s&box Setup";

	private static readonly (string Name, string Guard)[] Hooks =
	{
		// post-checkout: <previous HEAD> <new HEAD> <1 = branch checkout, 0 = file checkout>
		("post-checkout", "[ \"$3\" = \"1\" ] && [ \"$1\" != \"$2\" ] || exit 0"),
		// post-merge: <1 = squash merge>. Every merge (including a fast-forward pull) can change HEAD.
		("post-merge", null),
		// post-rewrite: <amend|rebase>. A rebase pull moves HEAD without running post-checkout or post-merge.
		("post-rewrite", "[ \"$1\" = \"rebase\" ] || exit 0"),
	};

	internal ExitCode Run() => BuildDisplay.Run( "Install git hooks", Install );

	private static ExitCode Install()
	{
		try
		{
			// Public only. The full distribution builds its native binaries locally, and a hook that
			// pulled public artifacts over them would silently replace a developer's own build.
			if ( !Build.IsPublicSourceDistribution() )
			{
				Log.Error( "install-git-hooks is only for the public source distribution (sbox-public); refusing to install in a full source checkout." );
				return ExitCode.Failure;
			}

			BuildDisplay.Status( "Locating git hooks directory" );
			var hooksDirectory = ResolveHooksDirectory();
			if ( hooksDirectory is null )
			{
				// A zip download has no repository; nothing to hook and nothing to fail.
				Log.Warning( "Not a git repository; skipping git hook installation. Rerun Setup after pulling to refresh artifacts." );
				return ExitCode.Success;
			}

			Directory.CreateDirectory( hooksDirectory );

			var installed = 0;
			var current = 0;
			var foreign = 0;
			for ( var i = 0; i < Hooks.Length; i++ )
			{
				var (name, guard) = Hooks[i];
				BuildDisplay.Status( $"Installing {name} hook" );
				BuildDisplay.Progress( i, Hooks.Length );

				var path = Path.Combine( hooksDirectory, name );
				var content = Render( name, guard );

				if ( File.Exists( path ) )
				{
					var existing = File.ReadAllText( path );
					if ( !existing.Contains( Marker, StringComparison.Ordinal ) )
					{
						Log.Warning( $"Leaving existing {name} hook alone (not installed by Setup): {path}" );
						Log.Warning( $"  To keep artifacts current, have it run: dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- {RestoreCommand}" );
						foreign++;
						continue;
					}

					if ( string.Equals( existing, content, StringComparison.Ordinal ) )
					{
						EnsureExecutable( path );
						current++;
						continue;
					}
				}

				File.WriteAllText( path, content, new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ) );
				EnsureExecutable( path );
				Log.Detail( $"Installed {name} hook: {path}" );
				installed++;
			}

			BuildDisplay.Progress( Hooks.Length, Hooks.Length );
			Log.Summary( $"Git hooks: {installed} installed, {current} already current, {foreign} left unchanged. " +
				"Artifacts now refresh after pull, rebase and checkout (set SBOX_SKIP_HOOKS=1 to skip once)." );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Git hook installation failed: {ex}" );
			return ExitCode.Failure;
		}
	}

	/// <summary>
	/// In a public checkout this is Artifacts + Bindings: exactly what a new commit can invalidate
	/// and nothing that the developer's IDE builds anyway.
	/// </summary>
	private const string RestoreCommand = "build --skip-native --skip-managed";

	private static string Render( string name, string guard )
	{
		// LF only: Git for Windows runs hooks through its bundled sh, which does not want CRLF.
		var sb = new StringBuilder();
		sb.Append( "#!/bin/sh\n" );
		sb.Append( $"# {Marker} (SboxBuild install-git-hooks). Edits are overwritten by the next Setup run.\n" );
		sb.Append( "# Restores the prebuilt engine artifacts and interop bindings for the new HEAD after a pull,\n" );
		sb.Append( "# merge, rebase or branch switch, so Setup does not need rerunning.\n" );
		sb.Append( "# Set SBOX_SKIP_HOOKS=1 to skip; delete this file to uninstall.\n" );
		sb.Append( '\n' );
		if ( guard is not null )
		{
			sb.Append( $"# {name} guard\n" );
			sb.Append( guard ).Append( '\n' );
		}
		sb.Append( "[ -z \"$SBOX_SKIP_HOOKS\" ] || exit 0\n" );
		sb.Append( "cd \"$(git rev-parse --show-toplevel)\" || exit 0\n" );
		sb.Append( "[ -f engine/Tools/SboxBuild/SboxBuild.csproj ] || exit 0\n" );
		// Mirrors Build.IsPublicSourceDistribution: a full source checkout builds native locally and must
		// never have public binaries pulled over its own, even if this hook was copied in by hand.
		sb.Append( "if [ -d public ] && [ -d steamworks ]; then exit 0; fi\n" );
		sb.Append( "if ! command -v dotnet >/dev/null 2>&1; then\n" );
		sb.Append( "    echo \"s&box: dotnet not found on PATH; run Setup to restore engine artifacts.\"\n" );
		sb.Append( "    exit 0\n" );
		sb.Append( "fi\n" );
		sb.Append( '\n' );
		sb.Append( "echo \"s&box: restoring engine artifacts for the new checkout (SBOX_SKIP_HOOKS=1 skips)...\"\n" );
		sb.Append( $"if ! dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- {RestoreCommand}; then\n" );
		sb.Append( "    echo \"s&box: artifact restore failed; rerun Setup to retry.\"\n" );
		sb.Append( "fi\n" );
		// A hook failure must never abort the user's checkout or merge.
		sb.Append( "exit 0\n" );
		return sb.ToString();
	}

	/// <summary>
	/// Honours core.hooksPath and worktrees. Returns null outside a git repository.
	/// </summary>
	private static string ResolveHooksDirectory()
	{
		string hooksPath = null;
		var ok = Utility.RunProcess( "git", "rev-parse --git-path hooks", onDataReceived: ( _, e ) =>
		{
			if ( !string.IsNullOrWhiteSpace( e.Data ) )
				hooksPath ??= e.Data.Trim();
		} );

		if ( !ok || string.IsNullOrEmpty( hooksPath ) )
			return null;

		return Path.GetFullPath( hooksPath );
	}

	private static void EnsureExecutable( string path )
	{
		// Git for Windows runs any hook that starts with a shebang; the mode only matters elsewhere.
		if ( OperatingSystem.IsWindows() )
			return;

		var mode = File.GetUnixFileMode( path );
		var wanted = mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
		if ( wanted != mode )
			File.SetUnixFileMode( path, wanted );
	}
}
