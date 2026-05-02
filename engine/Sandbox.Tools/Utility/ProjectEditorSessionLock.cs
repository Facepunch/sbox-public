using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Editor;

/// <summary>
/// Cross-process lock so only one editor session "owns" a given .sbproj path at a time.
/// Uses the same path normalization as <see cref="Project.AddFromFile"/> for the mutex key.
/// </summary>
internal static class ProjectEditorSessionLock
{
	static Mutex _sessionMutex;

	/// <summary>
	/// Match <see cref="Project.AddFromFile"/> path cleanup, then <c>System.IO.Path.GetFullPath(string)</c>.
	/// </summary>
	internal static string NormalizeProjectConfigPath( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			throw new ArgumentException( "Project path is empty.", nameof( path ) );

		if ( !path.EndsWith( ".sbproj", StringComparison.OrdinalIgnoreCase ) )
			path = Path.Combine( path, ".sbproj" );

		return Path.GetFullPath( path );
	}

	internal static string MutexNameForPath( string fullPathToSbproj )
	{
		var hash = SHA256.HashData( Encoding.UTF8.GetBytes( fullPathToSbproj ) );
		var hex = Convert.ToHexString( hash.AsSpan( 0, 16 ) );
		if ( OperatingSystem.IsWindows() )
			return @$"Local\SboxEditorProject_{hex}";
		return $"SboxEditorProject_{hex}";
	}

	/// <summary>
	/// Call from <c>EditorAppSystem.CheckProject</c> after <c>LoadMinimal</c> succeeds, before <c>InitGame</c>,
	/// so the mutex exists before long bootstrap / splash work. Returns false if the user cancels the second instance.
	/// </summary>
	internal static bool TryEnterSessionAtStartupBlocking( string fullPathToSbproj )
	{
		var name = MutexNameForPath( fullPathToSbproj );
		bool createdNew;
		var attempt = new Mutex( initiallyOwned: true, name: name, createdNew: out createdNew );

		if ( createdNew )
		{
			_sessionMutex?.Dispose();
			_sessionMutex = attempt;
			return true;
		}

		attempt.Dispose();

		return ShowSecondInstanceDialogBlocking(
			"This project may already be open in another editor instance.\n\nStart another editor for the same project anyway?" );
	}

	/// <summary>
	/// Launcher: if an editor already holds the session lock, confirm before spawning <c>sbox-dev</c>.
	/// </summary>
	internal static bool TryConfirmLaunchFromHubBeforeSpawn( string configFilePath )
	{
		string fullPath;
		try
		{
			fullPath = NormalizeProjectConfigPath( configFilePath );
		}
		catch
		{
			return true;
		}

		var name = MutexNameForPath( fullPath );

		try
		{
			using var existing = Mutex.OpenExisting( name );
			if ( !existing.WaitOne( TimeSpan.Zero ) )
				return ShowSecondInstanceDialogBlocking(
					"This project may already be open in another editor instance.\n\nStart another editor for the same project anyway?" );

			existing.ReleaseMutex();
		}
		catch ( WaitHandleCannotBeOpenedException )
		{
		}

		return true;
	}

	internal static void ReleaseIfHeld()
	{
		_sessionMutex?.Dispose();
		_sessionMutex = null;
	}

	static bool ShowSecondInstanceDialogBlocking( string message )
	{
		var tcs = new TaskCompletionSource<bool>();

		var popup = new PopupDialogWidget( "⚠️" );
		popup.WindowTitle = "Project already open";
		popup.MessageLabel.Text = message;

		popup.ButtonLayout.AddStretchCell();
		popup.ButtonLayout.Add( new Button( "Cancel" )
		{
			Clicked = () =>
		{
			popup.Destroy();
			tcs.TrySetResult( false );
		}
		} );
		popup.ButtonLayout.Add( new Button.Primary( "Open anyway" )
		{
			Clicked = () =>
		{
			popup.Destroy();
			tcs.TrySetResult( true );
		}
		} );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();

		while ( !tcs.Task.IsCompleted )
		{
			Application.Spin();
			Native.QApp.processEvents();
			Thread.Yield();
		}

		return tcs.Task.GetAwaiter().GetResult();
	}
}
