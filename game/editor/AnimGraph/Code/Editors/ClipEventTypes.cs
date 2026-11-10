namespace Editor.AnimGraph;

/// <summary>
/// What the timeline knows about the event types it authors, and the compiler rules it has to
/// keep the clip inside. Kept apart from the widgets so adding a type is one table to fill in.
/// </summary>
internal static class ClipEventTypes
{
	public const string ID = "CIDEvent";
	public const string Generic = "CGenericEvent";
	public const string ManagedName = "AE_MANAGED_EVENT";
	public const string Foot = "CFootEvent";

	public static Color TrackColor( string eventClass ) => eventClass switch
	{
		Generic => Theme.Blue,
		ID => Theme.Pink,
		Foot => Theme.Green,
		_ => Theme.TextControl
	};

	public static string Icon( string eventClass ) => eventClass switch
	{
		Generic => "bolt",
		ID => "label_important",
		Foot => "directions_walk",
		_ => "label"
	};

	public static string Title( string eventClass ) => eventClass switch
	{
		ID => "ID",
		Generic => "Animation",
		Foot => "Foot",
		// Types this doesn't author still have to read back as something
		_ => eventClass?.TrimStart( 'C' ).Replace( "Event", "" ) ?? "Unknown"
	};

	/// <summary>
	/// Which field an event of this type carries, if it's one we can author.
	/// </summary>
	public static string PayloadProperty( string eventClass ) => eventClass switch
	{
		ID => nameof( ClipDocument.Event.ID ),
		Generic => nameof( ClipDocument.Event.Name ),
		Foot => nameof( ClipDocument.Event.Phase ),
		_ => null
	};

	/// <summary>
	/// What an event is worth showing on its block - its payload, or nothing when it carries one
	/// this doesn't author.
	/// </summary>
	public static string Summary( ClipDocument.Event item, string eventClass ) => eventClass switch
	{
		ID => item.ID,
		Generic when item.IsTypedEvent => item.DataType?.Title ?? item.DataTypeName,
		Generic => item.Name,
		Foot => item.Phase.ToString().ToTitleCase(),
		_ => null
	};

	/// <summary>
	/// Two sync events too close together don't compile at all - the clip fails outright. The
	/// compiler compares them once normalized over the clip and within a tolerance, so this has
	/// to measure the same gap rather than looking for an exact match.
	/// </summary>
	public static bool Clashes( ClipDocument.Track track, ClipDocument.Event item, float lastFrame )
	{
		if ( !track.IsSyncTrack || track.IsDisabled )
			return false;

		foreach ( var other in track.Events )
		{
			if ( other != item && MathF.Abs( other.StartFrame - item.StartFrame ) / lastFrame <= ClashTolerance )
				return true;
		}

		return false;
	}

	/// <summary>
	/// The compiler throws away anything that doesn't overlap the clip at all. Reachable by
	/// typing a frame rather than dragging to one, and by a clip being re-exported shorter than
	/// it was marked up at.
	/// </summary>
	public static bool IsOutsideClip( ClipDocument.Event item, float lastFrame )
		=> item.EndFrame < 0f || item.StartFrame > lastFrame;

	/// <summary>
	/// What the compiler would object to, if anything. Worth saying out loud - some of it fails
	/// the whole clip, and an event off the end of it can't be seen on the lanes at all.
	/// </summary>
	public static string Problem( ClipDocument document, float lastFrame )
	{
		foreach ( var track in document?.Tracks ?? [] )
		{
			// A disabled track isn't compiled at all, so nothing on it can be wrong
			if ( track.IsDisabled )
				continue;

			foreach ( var item in track.Events )
			{
				if ( Clashes( track, item, lastFrame ) )
					return "two sync events too close - this clip won't compile";

				if ( IsOutsideClip( item, lastFrame ) )
					return "an event is outside the clip and won't compile";
			}
		}

		return null;
	}

	private const float ClashTolerance = 0.001f;
}
