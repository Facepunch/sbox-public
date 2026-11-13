using Sandbox.Diagnostics;
using static Sandbox.Diagnostics.DevTools.NetworkLog;

namespace Editor.Mcp;

internal static partial class PerfTools
{
	[McpTool.ReadOnly( "perf_net_timeline" )]
	[Description( "Network tab's Timeline data: per-object sync values, RPC arguments and dormancy changes, plus a Session lane for scene snapshots, object messages (create, destroy, refresh), hosting, joins and leaves. Returns counts, bytes, current dormancy, busiest tracks and newest events. Recording requires capture enabled and an open Network tab or collection request. Pass seconds to collect first (waits). Existing events remain until cleared or the log fills; results start at the last session end." )]
	internal static async Task<object> NetTimeline(
		[Description( "Seconds to collect before returning, clamped to 1-60. Default 0 reads existing events." )] float seconds = 0,
		[Description( "Timeline filter syntax: space-separated terms all match; comma-separated alternatives match any. Plain words match object or track names; name:, tag:, owner:, comp:, id: restrict the field. Supports * and ? wildcards. The Session lane is not filtered. Example: \"tag:player comp:PlayerController*\"." )] string filter = "",
		[Description( "Include sync var events." )] bool sync = true,
		[Description( "Include RPC events." )] bool rpc = true,
		[Description( "Include dormancy changes, known only for objects sent by this machine." )] bool dormancy = true,
		[Description( "Include Session lane snapshots and object messages. Session events are always included." )] bool snapshots = true,
		[Description( "Include sent and received position, rotation and scale updates. Recording is opt-in: pass true with seconds to collect them." )] bool transforms = false,
		[Description( "Include sync vars resent or received again with unchanged wire bytes." )] bool resends = false,
		[Description( "Exclude objects with only dormancy events; require an included non-dormancy event." )] bool activeOnly = false,
		[Description( "Maximum objects and events returned, busiest objects and newest events first. Default 50, clamped to 1-500." )] int limit = 50 )
	{
		if ( !Networking.IsActive )
		{
			return new { Session = "not connected" };
		}

		limit = Math.Clamp( limit, 1, 500 );

		if ( seconds > 0 )
		{
			if ( transforms ) TransformRequests++;
			try
			{
				await CollectFor( "network", Math.Clamp( seconds, 1, 60 ), Linger );
			}
			finally
			{
				if ( transforms ) TransformRequests--;
			}
		}

		var events = new List<Entry>();
		CopyTimeline( events, SessionEnded );

		if ( events.Count == 0 )
		{
			return new
			{
				Session = Networking.IsHost ? "host" : "client",
				Recorded = 0,
				Note = LogsMessages ? "recording, nothing sent or received yet" : !Capturing ? "capture is off - press record in the Network tab" : "nothing recorded - pass seconds to record, or open the Network tab in devtools",
			};
		}

		var matching = new List<Entry>();
		var show = new Show( sync, rpc, dormancy, snapshots, transforms, resends, activeOnly );
		new Selection().Select( events, show, Filter.Parse( filter ), matching );
		var directory = Game.ActiveScene?.Directory;
		var now = Now;

		var groups = matching.GroupBy( e => e.ObjectId ).ToList();
		var objects = groups.Select( g =>
		{
			Entry last = default;
			string state = null;
			int syncs = 0;
			int rpcs = 0;
			int resendCount = 0;
			long sent = 0;
			long received = 0;
			var tracks = new Dictionary<(string Name, Kind Kind), (int Count, long Bytes)>();

			foreach ( var e in g )
			{
				last = e;
				if ( e.Kind == Kind.SyncVar ) syncs++;
				if ( e.Kind == Kind.Rpc ) rpcs++;
				if ( e.Resend ) resendCount++;
				if ( e.Kind == Kind.Dormancy )
				{
					state = e.Value;
					continue;
				}

				if ( e.Outbound ) sent += e.Size;
				else received += e.Size;

				var key = (e.Name, e.Kind);
				var track = tracks.GetValueOrDefault( key );
				tracks[key] = (track.Count + 1, track.Bytes + e.Size);
			}

			return new
			{
				Name = last.ObjectName,
				Id = g.Key,
				Owner = OwnerName( directory?.FindByGuid( g.Key ) ),
				Dormancy = state,
				Syncs = syncs,
				Rpcs = rpcs,
				Resends = resendCount,
				BytesSent = sent,
				BytesReceived = received,
				LastSecondsAgo = Round( now - last.Seconds, 3 ),
				Tracks = tracks.Select( t => new { Track = t.Key.Name, Kind = t.Key.Kind.ToString(), t.Value.Count, t.Value.Bytes } )
					.OrderByDescending( t => t.Bytes ).ThenByDescending( t => t.Count )
					.Take( 5 ).ToList(),
			};
		} )
		.OrderByDescending( o => o.BytesSent + o.BytesReceived ).ThenByDescending( o => o.Syncs + o.Rpcs )
		.Take( limit ).ToList();

		var recent = Enumerable.Range( 0, Math.Min( limit, matching.Count ) ).Select( i =>
		{
			var e = matching[matching.Count - 1 - i];
			return new
			{
				SecondsAgo = Round( now - e.Seconds, 3 ),
				Object = e.ObjectName,
				Kind = e.Kind.ToString(),
				Direction = e.Kind switch { Kind.Dormancy => "state", Kind.Session => "event", _ => e.Outbound ? "sent" : "received" },
				Track = e.Name,
				Peer = e.Kind == Kind.Dormancy || string.IsNullOrEmpty( e.Peer ) ? null : e.Peer,
				Bytes = e.Size,
				e.Value,
				e.Resend,
			};
		} ).ToList();

		return new
		{
			Session = Networking.IsHost ? "host" : "client",
			Recording = LogsMessages,
			Recorded = events.Count,
			Matching = matching.Count,
			MatchingObjects = groups.Count,
			SpanSeconds = Round( events[^1].Seconds - events[0].Seconds, 1 ),
			Dropped,
			Objects = objects,
			Events = recent,
		};
	}
}
