namespace Sandbox.Diagnostics;

internal static partial class DevTools
{
	internal static partial class NetworkLog
	{
		/// <summary>
		/// Which entries a view shows. <see cref="ActiveOnly"/> hides objects that only have dormancy changes.
		/// </summary>
		internal readonly record struct Show( bool Sync, bool Rpc, bool Dormancy, bool Snapshots, bool Transforms, bool Resends, bool ActiveOnly )
		{
			public bool Includes( in Entry e ) =>
				(e.Kind switch
				{
					Kind.SyncVar => Sync,
					Kind.Rpc => Rpc,
					Kind.Dormancy => Dormancy,
					Kind.Snapshot or Kind.Object => Snapshots,
					Kind.Transform => Transforms,
					_ => true,
				}) && (!e.Resend || Resends);
		}

		/// <summary>
		/// Picks the entries a <see cref="Show"/> and <see cref="Filter"/> keep. The session lane is never filtered out.
		/// </summary>
		internal sealed class Selection
		{
			readonly HashSet<Guid> _active = new();
			readonly HashSet<Guid> _held = new();
			readonly Dictionary<Guid, bool> _objectMatches = new();
			Show _show;
			Filter _filter;

			public void Select( List<Entry> events, in Show show, in Filter filter, List<Entry> into )
			{
				into.Clear();
				Begin( events, show, filter );
				foreach ( var e in events )
				{
					if ( Keeps( e ) ) into.Add( e );
				}
			}

			/// <summary>
			/// Starts a new selection. Entries appended afterwards go through <see cref="Activate"/> before <see cref="Keeps"/>.
			/// </summary>
			public void Begin( List<Entry> events, in Show show, in Filter filter )
			{
				_show = show;
				_filter = filter;
				_active.Clear();
				_held.Clear();
				_objectMatches.Clear();

				if ( !show.ActiveOnly ) return;
				foreach ( var e in events )
				{
					Activate( e );
				}
			}

			/// <summary>
			/// Marks the entry's object active. True if that object already had entries held back, so the selection needs rebuilding.
			/// </summary>
			public bool Activate( in Entry e ) => _show.ActiveOnly && e.Kind != Kind.Dormancy && _show.Includes( e ) && _active.Add( e.ObjectId ) && _held.Contains( e.ObjectId );

			public bool Keeps( in Entry e )
			{
				if ( !_show.Includes( e ) ) return false;
				if ( _show.ActiveOnly && !_active.Contains( e.ObjectId ) )
				{
					_held.Add( e.ObjectId );
					return false;
				}
				if ( e.ObjectId == SessionLane ) return true;

				if ( !_objectMatches.TryGetValue( e.ObjectId, out var matches ) )
				{
					matches = _filter.MatchesObject( e.ObjectId, e.ObjectName, Game.ActiveScene?.Directory.FindByGuid( e.ObjectId ) );
					_objectMatches[e.ObjectId] = matches;
				}

				return matches && _filter.MatchesTrack( e.ObjectName, e.Name );
			}
		}

		internal static string OwnerName( GameObject go )
		{
			if ( go is null ) return null;
			var owner = go.Network.OwnerId;
			if ( owner == Guid.Empty ) return "no owner";
			return Connection.Find( owner ) is { } c ? PeerName( c ) : "owner gone";
		}

		/// <summary>
		/// The track's Type.Member, without the namespace.
		/// </summary>
		internal static string TrackLabel( string name ) => name[TrackLabelOffset( name.AsSpan() )..];

		static int TrackLabelOffset( ReadOnlySpan<char> name )
		{
			var method = name.LastIndexOf( '.' );
			var type = method > 0 ? name[..method].LastIndexOf( '.' ) : -1;
			return type + 1;
		}

		/// <summary>
		/// Space-separated terms must all match; commas inside a term mean any of: <c>tag:door,crate</c>.
		/// Plain words match object or track names; name:, tag:, owner:, comp:, id: match one thing. * and ? are wildcards.
		/// </summary>
		internal readonly struct Filter
		{
			readonly record struct Alt( string Value, bool Wildcard );
			readonly record struct Term( string Key, Alt[] Alts );

			readonly Term[] _terms;

			Filter( Term[] terms ) => _terms = terms;

			public static Filter Parse( string text )
			{
				if ( string.IsNullOrWhiteSpace( text ) ) return new Filter( [] );
				var terms = new List<Term>();
				foreach ( var word in text.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
				{
					var colon = word.IndexOf( ':' );
					var key = colon > 0 ? word[..colon].ToLowerInvariant() : "";
					if ( key is not ("name" or "id" or "tag" or "owner" or "comp") ) key = "";
					var value = key.Length > 0 ? word[(colon + 1)..] : word;
					var alts = value.Split( ',', StringSplitOptions.RemoveEmptyEntries )
						.Select( v => new Alt( v, v.AsSpan().IndexOfAny( '*', '?' ) >= 0 ) )
						.ToArray();
					if ( alts.Length > 0 ) terms.Add( new Term( key, alts ) );
				}
				return new Filter( terms.ToArray() );
			}

			public bool MatchesObject( Guid id, string name, GameObject go )
			{
				string idText = null;
				string owner = null;
				foreach ( var term in _terms )
				{
					if ( term.Key is not ("name" or "id" or "tag" or "owner") ) continue;
					var matched = false;
					foreach ( var alt in term.Alts )
					{
						matched = term.Key switch
						{
							"name" => Matches( name, alt ),
							"id" => Matches( idText ??= id.ToString(), alt, prefix: true ),
							"owner" => Matches( owner ??= OwnerName( go ), alt ),
							_ => MatchesTag( go, alt ),
						};
						if ( matched ) break;
					}
					if ( !matched ) return false;
				}
				return true;
			}

			public bool MatchesTrack( string objectName, string track )
			{
				foreach ( var term in _terms )
				{
					if ( term.Key is not ("" or "comp") ) continue;
					var matched = false;
					foreach ( var alt in term.Alts )
					{
						matched = term.Key == "comp"
							? track is not null && Matches( track.AsSpan( TrackLabelOffset( track.AsSpan() ) ), alt, prefix: true )
							: Matches( objectName, alt ) || Matches( track, alt );
						if ( matched ) break;
					}
					if ( !matched ) return false;
				}
				return true;
			}

			static bool MatchesTag( GameObject go, Alt alt )
			{
				if ( go is null ) return false;
				if ( !alt.Wildcard ) return go.Tags.Has( alt.Value );
				foreach ( var tag in go.Tags.TryGetAll() )
				{
					if ( tag.WildcardMatch( alt.Value ) ) return true;
				}

				return false;
			}

			static bool Matches( string text, Alt alt, bool prefix = false ) => text is not null && Matches( text.AsSpan(), alt, prefix );

			static bool Matches( ReadOnlySpan<char> text, Alt alt, bool prefix = false ) =>
				alt.Wildcard ? System.IO.Enumeration.FileSystemName.MatchesSimpleExpression( alt.Value, text, ignoreCase: true )
					: prefix ? text.StartsWith( alt.Value, StringComparison.OrdinalIgnoreCase )
					: text.Contains( alt.Value, StringComparison.OrdinalIgnoreCase );
		}
	}
}
