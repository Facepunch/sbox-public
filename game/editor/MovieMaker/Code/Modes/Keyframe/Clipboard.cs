using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

partial class KeyframeEditMode
{
	public record ClipboardData( MovieTime Time, IReadOnlyList<ClipboardTrackData> Keyframes );
	public record ClipboardTrackData( Guid Guid, Type TargetType, JsonArray Keyframes );

	private ClipboardData? _clipboardData;
	private int _clipboardHash;

	public ClipboardData? Clipboard
	{
		get
		{
			var text = EditorUtility.Clipboard.Paste();
			var hash = text?.GetHashCode() ?? 0;

			if ( hash == _clipboardHash ) return _clipboardData;

			_clipboardHash = hash;

			try
			{
				var data = JsonSerializer.Deserialize<ClipboardData>( text ?? "null", EditorJsonOptions );

				if ( data?.Keyframes.Count is not > 0 )
				{
					return _clipboardData = null;
				}

				return _clipboardData = data;
			}
			catch
			{
				return _clipboardData = null;
			}
		}

		set
		{
			if ( value?.Keyframes.Count is not > 0 )
			{
				_clipboardData = null;
				_clipboardHash = 0;

				EditorUtility.Clipboard.Copy( "" );
				return;
			}

			var text = JsonSerializer.Serialize( value, EditorJsonOptions );

			_clipboardData = value;
			_clipboardHash = text.GetHashCode();

			EditorUtility.Clipboard.Copy( text );
		}
	}

	protected override void OnCut()
	{
		Copy();
		Delete();
	}

	protected override void OnCopy()
	{
		var groupedByTrack = SelectedKeyframes
			.GroupBy( x => x.View.Track );

		var time = SelectedKeyframes.Select( x => x.Time )
			.DefaultIfEmpty( MovieTime.Zero )
			.Min();

		var data = new ClipboardData( time, [
			..groupedByTrack.Select( x => new ClipboardTrackData(
				x.Key.Id, x.Key.TargetType,
				JsonSerializer.SerializeToNode(
					x.Select( y => y.Keyframe ).ToImmutableArray(),
					EditorJsonOptions )!.AsArray() ) )
		] );

		if ( data.Keyframes.Count == 0 ) return;

		Clipboard = data;
	}

	protected override void OnPaste()
	{
		if ( Clipboard is { } data )
		{
			var selectedTrack = Session.TrackList.SelectedTracks.FirstOrDefault();

			Paste( data, Session.PlayheadTime - data.Time, selectedTrack );
		}
	}

	public void Paste( ClipboardData data, MovieTime offset, TrackView? selectedTrackView = null )
	{
		if ( data.Keyframes.Count == 0 ) return;

		using var historyScope = Session.History.Push( "Paste" );

		Timeline.DeselectAll();

		if ( IsCompatibleSingleTrackClipboard( data, selectedTrackView ) )
		{
			PasteCore( data.Keyframes[0], offset, selectedTrackView );
			return;
		}

		foreach ( var trackData in data.Keyframes )
		{
			var trackView = Session.TrackList.EditablePropertyTracks.FirstOrDefault( x => x.Track.Id == trackData.Guid );

			if ( IsCompatibleTrackClipboard( trackData, trackView ) )
			{
				PasteCore( trackData, offset, trackView );
			}
		}
	}

	private static bool IsCompatibleSingleTrackClipboard( ClipboardData data, [NotNullWhen( true )] TrackView? selectedTrackView )
	{
		return data.Keyframes.Count == 1 && IsCompatibleTrackClipboard( data.Keyframes[0], selectedTrackView );
	}

	private static bool IsCompatibleTrackClipboard( ClipboardTrackData data, [NotNullWhen( true )] TrackView? trackView )
	{
		return trackView?.Track is IProjectPropertyTrack { TargetType: { } propertyType } && data.TargetType.IsAssignableTo( propertyType );
	}

	private void PasteCore( ClipboardTrackData data, MovieTime offset, TrackView trackView )
	{
		if ( GetTimelineTrack( trackView ) is not { } timelineTrack ) return;
		if ( GetHandles( timelineTrack ) is not { } handles ) return;

		var keyframeType = typeof( Keyframe<> ).MakeGenericType( data.TargetType );
		var arrayType = typeof( ImmutableArray<> ).MakeGenericType( keyframeType );
		var keyframes = (IEnumerable)data.Keyframes.Deserialize( arrayType, EditorJsonOptions )!;

		handles.AddRange( keyframes.Cast<IKeyframe>(), offset );

		trackView.MarkValueChanged();
	}
}
