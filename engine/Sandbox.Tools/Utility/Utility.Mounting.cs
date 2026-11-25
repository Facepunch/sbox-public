namespace Editor;

public static partial class EditorUtility
{
	public static partial class Mounting
	{
		/// <summary>
		/// Get the mount
		/// </summary>
		public static Sandbox.Mounting.BaseGameMount Get( string name )
		{
			return Sandbox.Mounting.Directory.Get( name );
		}

		/// <summary>
		/// Release a game mounted temporarily for an editor operation, preserving the project's mount settings.
		/// </summary>
		public static Task UnmountForSession( string name ) => Sandbox.Mounting.Directory.SetMountState( name, false );

		/// <summary>
		/// Look at the current project's config and mount any mounts.
		/// Complain if a mount is missing.
		/// </summary>
		internal static async Task InitMountsFromConfig( Project project )
		{
			if ( project.Config.Mounts == null ) return;
			if ( project.Config.Mounts.Count == 0 ) return;

			foreach ( var mountIdent in project.Config.Mounts )
			{
				var mount = Get( mountIdent );
				if ( mount == null )
				{
					Log.Warning( $"Mount {mountIdent} is missing" );
					continue;
				}

				if ( !mount.IsInstalled )
				{
					Log.Warning( $"Mount {mountIdent} is not available (not installed?)" );
					continue;
				}

				Log.Info( $"Mounting {mountIdent}" );
				await Sandbox.Mounting.Directory.SetMountState( mountIdent, true );
				AssetSystem.AddAssetsFromMount( mount );
			}
		}

		/// <summary>
		/// Set a mount state. This state will be saved in the project, and your game will require it if you publish it.
		/// </summary>
		public static async Task SetMounted( string name, bool state )
		{
			// Update the mounted state
			{
				Project.Current.Config.SetMountState( name, state );
				Project.Current.Save();
			}

			await Sandbox.Mounting.Directory.SetMountState( name, state );

			if ( state && Get( name ) is { } mount )
			{
				AssetSystem.AddAssetsFromMount( mount );
			}
		}

		/// <summary>
		/// Flush this source to force a refresh. Unmount and re-mount, updating and getting a list of all the new files.
		/// This is used during development to force an update of the files, so you don't have to restart the editor.
		/// </summary>
		public static async Task Refresh( string name )
		{
			var source = Get( name );
			await source.RefreshInternal();
		}
	}
}
