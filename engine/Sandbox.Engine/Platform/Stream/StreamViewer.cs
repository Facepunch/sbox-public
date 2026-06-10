namespace Sandbox;

public static partial class Streamer
{
	/// <summary>
	/// Someone we currently believe is in chat. Tracked from join/leave/message events,
	/// so this is a best-effort roster of active chatters - not silent lurkers.
	/// </summary>
	public class Viewer
	{
		/// <summary>
		/// The viewer's login name (lowercase). This is the key we track them by in the roster, but a
		/// viewer can change it - use <see cref="StreamerId"/> as their persistent identity.
		/// </summary>
		public string Username { get; internal set; }

		/// <summary>
		/// The viewer's stable numeric platform user id. Unlike <see cref="Username"/> this never changes,
		/// so it's the key to use when persisting per-viewer state. Null until we've seen them chat, sub,
		/// or appear in the chatter list.
		/// </summary>
		public string StreamerId { get; internal set; }

		/// <summary>
		/// Their display name. Falls back to <see cref="Username"/> until we've seen a chat message from them.
		/// </summary>
		public string DisplayName { get; internal set; }

		/// <summary>
		/// Their chat name color, if we've seen a message from them with a valid color set.
		/// </summary>
		public Color? Color { get; internal set; }

		/// <summary>
		/// Their chat badges, if we've seen a message from them.
		/// </summary>
		public string[] Badges { get; internal set; }

		/// <summary>
		/// Whether we've seen this viewer actually chat, or only join.
		/// </summary>
		public bool HasChatted { get; internal set; }

		/// <summary>
		/// Whether this viewer is the broadcaster (channel owner).
		/// </summary>
		public bool IsBroadcaster => HasBadge( "broadcaster" );

		/// <summary>
		/// Whether this viewer is a channel moderator.
		/// </summary>
		public bool IsModerator => HasBadge( "moderator" );

		/// <summary>
		/// Whether this viewer is a subscriber (founders count as subscribers).
		/// </summary>
		public bool IsSubscriber => HasBadge( "subscriber" ) || HasBadge( "founder" );

		/// <summary>
		/// Whether this viewer is a VIP.
		/// </summary>
		public bool IsVip => HasBadge( "vip" );

		/// <summary>
		/// Whether <see cref="Badges"/> contains a badge of the given kind. Badges look like
		/// "subscriber/12", so we match the kind exactly or up to its '/' version separator.
		/// </summary>
		bool HasBadge( string name )
		{
			if ( Badges is null )
				return false;

			foreach ( var badge in Badges )
			{
				if ( badge.StartsWith( name, StringComparison.OrdinalIgnoreCase ) &&
					(badge.Length == name.Length || badge[name.Length] == '/') )
					return true;
			}

			return false;
		}

		DataBag _data;

		/// <summary>
		/// Arbitrary per-viewer data - stash gameplay state here, e.g. <c>viewer.Data.Set( "score", 10 )</c>.
		/// Lazily created, so viewers with no data cost nothing. This lives only as long as the viewer is
		/// in the roster: if they leave (or get pruned) and come back it's a fresh viewer with an empty bag,
		/// so keep anything you can't afford to lose in your own game state instead.
		/// </summary>
		public DataBag Data => _data ??= new();
	}
}
