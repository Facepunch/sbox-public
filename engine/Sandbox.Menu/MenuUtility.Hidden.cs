using System;

namespace Sandbox;

public static partial class MenuUtility
{
	/// <summary>
	/// What the player's hidden from discovery and search - packages, and whole organizations. Kept
	/// here once fetched and kept up to date as things are hidden and shown again, so anything showing a
	/// package can ask whether it's hidden without a round trip.
	/// </summary>
	public static class Hidden
	{
		static readonly HashSet<string> packages = new( StringComparer.OrdinalIgnoreCase );
		static readonly HashSet<string> organizations = new( StringComparer.OrdinalIgnoreCase );

		/// <summary>
		/// The hidden packages' idents (org.package).
		/// </summary>
		public static IReadOnlyCollection<string> Packages => packages;

		/// <summary>
		/// The hidden organizations' idents.
		/// </summary>
		public static IReadOnlyCollection<string> Organizations => organizations;

		/// <summary>
		/// The list's been fetched at least once.
		/// </summary>
		public static bool IsLoaded { get; private set; }

		/// <summary>
		/// Bumped whenever the list changes - for UI to redraw on.
		/// </summary>
		public static int Revision { get; private set; }

		/// <summary>
		/// Fetch the list again. False if the backend couldn't be reached - the last list is kept.
		/// </summary>
		public static async Task<bool> Refresh()
		{
			try
			{
				var hidden = await Backend.Account.GetHiddenContent();

				packages.Clear();
				packages.UnionWith( hidden?.Packages ?? [] );

				organizations.Clear();
				organizations.UnionWith( hidden?.Organizations ?? [] );

				IsLoaded = true;
				Revision++;
				return true;
			}
			catch ( Exception e )
			{
				Log.Warning( $"Couldn't fetch hidden content ({e.Message})" );
				return false;
			}
		}

		/// <summary>
		/// Hidden - itself, or its organization.
		/// </summary>
		public static bool Contains( Package package )
		{
			if ( package is null ) return false;

			return packages.Contains( package.FullIdent ) || IsOrganizationHidden( package.Org?.Ident );
		}

		public static bool IsOrganizationHidden( string orgIdent ) => !string.IsNullOrEmpty( orgIdent ) && organizations.Contains( orgIdent );

		/// <summary>
		/// Hide or show an organization, and everything it makes. False if the backend refused.
		/// </summary>
		public static async Task<bool> SetOrganizationHidden( string orgIdent, bool hidden )
		{
			if ( string.IsNullOrEmpty( orgIdent ) ) return false;

			try
			{
				await Backend.Account.SetOrganizationHidden( orgIdent, hidden );
				Set( organizations, orgIdent, hidden );
				return true;
			}
			catch ( Exception e )
			{
				Log.Warning( $"Couldn't {(hidden ? "hide" : "unhide")} {orgIdent} ({e.Message})" );
				return false;
			}
		}

		/// <summary>
		/// Hide or show a package by its ident (org.package). False if the backend refused.
		/// </summary>
		public static async Task<bool> SetPackageHidden( string ident, bool hidden )
		{
			if ( string.IsNullOrEmpty( ident ) ) return false;

			try
			{
				await Backend.Account.SetPackageHidden( ident, hidden );
				Set( packages, ident, hidden );
				return true;
			}
			catch ( Exception e )
			{
				Log.Warning( $"Couldn't {(hidden ? "hide" : "unhide")} {ident} ({e.Message})" );
				return false;
			}
		}

		static void Set( HashSet<string> set, string ident, bool hidden )
		{
			if ( hidden ? set.Add( ident ) : set.Remove( ident ) )
				Revision++;
		}
	}
}
