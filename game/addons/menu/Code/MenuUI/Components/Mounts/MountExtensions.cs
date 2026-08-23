using MenuProject;
using Sandbox.Mounting;

namespace Sandbox.UI.Mounts;

/// <summary>
/// Menu-side behaviour for mounts - listing them, turning them on and off, and the Steam links.
/// </summary>
public static class MountExtensions
{
	extension( BaseGameMount mount )
	{
		/// <summary>
		/// Mount or unmount this game. Shows a toast while it works, and another saying what
		/// happened. Never throws - callers are UI event handlers with nowhere to put an exception.
		/// </summary>
		public async Task SetMounted( bool enabled )
		{
			var toast = new Toast()
			{
				Title = $"{(enabled ? "Mounting" : "Unmounting")} {mount.Title}",
				Icon = "album",
				ExtraClasses = "loading"
			};

			MenuOverlay.Instance?.BottomRight?.Queue( toast, duration: 0, clickToDismiss: false );

			try
			{
				await MenuUtility.SetMountState( mount.Ident, enabled );
			}
			catch ( System.Exception e )
			{
				// A mount parses someone else's game files, so a broken install throws from deep
				// inside it. Nothing above here handles that, so swallow it and say so on screen.
				Log.Warning( e, $"Couldn't {(enabled ? "mount" : "unmount")} {mount.Ident}: {e.Message}" );
			}
			finally
			{
				// In the finally so a throw doesn't leave the spinner up forever.
				toast.Dismiss();
			}

			ReportResult( mount, enabled );
		}

		/// <summary>
		/// Open this game's Steam store page, so it can be bought.
		/// </summary>
		public void OpenSteamStore()
		{
			if ( mount.SteamAppId is not long appId ) return;

			MenuUtility.OpenUrl( $"steam://store/{appId}" );
		}

		/// <summary>
		/// Ask Steam to install a game you already own. Sending someone to the store page for
		/// something in their own library is a dead end - this is the one click that fixes it.
		/// </summary>
		public void OpenSteamInstall()
		{
			if ( mount.SteamAppId is not long appId ) return;

			MenuUtility.OpenUrl( $"steam://install/{appId}" );
		}
	}

	extension( BaseGameMount )
	{
		/// <summary>
		/// Every mount we know about. Installed games first, then alphabetical.
		/// </summary>
		public static IEnumerable<BaseGameMount> All => Sandbox.Mounting.Directory.GetAll()
			.Select( x => Sandbox.Mounting.Directory.Get( x.Ident ) )
			.OrderByDescending( x => x.IsInstalled )
			.ThenBy( x => x.Title );
	}

	/// <summary>
	/// Report what actually happened. Mounting can refuse or fail, and claiming success either
	/// way leaves you staring at a game you think is on.
	/// </summary>
	static void ReportResult( BaseGameMount mount, bool enabled )
	{
		var worked = mount.IsMounted == enabled;

		MenuOverlay.Instance?.BottomRight?.Queue( new Toast()
		{
			Title = worked
				? $"{(enabled ? "Mounted" : "Unmounted")} {mount.Title}"
				: $"Couldn't {(enabled ? "mount" : "unmount")} {mount.Title}",
			Icon = worked ? "download_done" : "error_outline",
		} );
	}
}
