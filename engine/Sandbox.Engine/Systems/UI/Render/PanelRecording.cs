using Sandbox.Rendering;

namespace Sandbox.UI;

/// <summary>
/// What a root's last recording drew from besides its panels' layout and style: the keys of the panels whose OnDraw
/// describes what it draws, and whether anything drew in a way that can't be compared. While nothing in the root has
/// changed since, the root keeps the recording and the render thread replays it, instead of the root recording again.
/// </summary>
internal sealed class PanelRecording
{
	[ConVar( "ui_reuse_recordings", Help = "Replay a UI root's last recorded drawing while nothing it draws has changed" )]
	internal static bool Enabled { get; set; } = true;

	[ThreadStatic] static PanelRecording _current;

	/// <summary>
	/// The recording being made on this thread, which panels report their custom drawing to. Null outside one.
	/// </summary>
	internal static PanelRecording Current => _current;

	/// <summary>
	/// What a whole root records from that isn't a panel's own: where it draws, and the settings that change every panel.
	/// </summary>
	internal readonly record struct RootKey( Rect Bounds, float Scale, float Opacity, Matrix? World, bool Manual, bool RenderText, bool VisualizeBatches );

	[ConVar( "ui_reuse_subtrees", Help = "While a UI root records, replay last frame's drawing of each subtree that hasn't changed" )]
	internal static bool SubtreesEnabled { get; set; } = true;

	[ConVar( "ui_retain_subtrees", Help = "Keep a large UI subtree that stops changing in a command list of its own, which its root's recording inserts while it stays unchanged" )]
	internal static bool RetainEnabled { get; set; } = true;

	/// <summary>Frames in a row a subtree replays before it's kept in a list of its own.</summary>
	internal const int RetainAfter = 2;

	/// <summary>Instances (boxes and glyphs) a subtree draws at least to be kept in a list of its own.</summary>
	internal const int RetainMinimum = 128;

	// The retained lists this recording inserts, whose textures a replay of the whole root checks too
	readonly List<Retained> _retained = [];

	// Above zero while a retained list records: one isn't kept inside another
	int _retainedDepth;

	/// <summary>Frames a retained subtree's list was inserted as it was, for tests and diagnostics.</summary>
	internal int RetainedInserts { get; private set; }

	/// <summary>Times a retained subtree's list was recorded, for tests and diagnostics.</summary>
	internal int RetainedRecordings { get; private set; }

	// This recording's draw keys, and the last one's, which replayed subtrees check theirs against
	List<(Panel Panel, Panel.DrawKey Key)> _keys = [];
	List<(Panel Panel, Panel.DrawKey Key)> _previousKeys = [];
	int _uncomparable;
	long _recordedAt = -1;
	long _recordingAt;
	RootKey _key;

	/// <summary>Frames this root replayed its recording instead of recording, for tests and diagnostics.</summary>
	internal int Replays { get; private set; }

	/// <summary>Subtrees replayed inside this root's recordings, for tests and diagnostics.</summary>
	internal int SubtreeReplays { get; private set; }

	/// <summary>
	/// A panel subtree's last recording: where its draws sit in that frame's tables, what it drew from and what it was
	/// drawn under. Panels keep one and update it each frame they're recorded or replayed.
	/// </summary>
	internal sealed class Subtree
	{
		internal PainterBatcher.Run Run;
		internal bool Replayable;
		internal long RecordedAt = -1;
		internal int Keys, KeyCount;
		internal int Panels, Culled;

		// Frames in a row this subtree replayed; and its own list, once it's been kept in one
		internal int Streak;
		internal Retained Kept;

		// What the parent hands down: the inherited opacity and blend mode, and the clip
		internal float Opacity;
		internal BlendMode Blend;
		internal Painter.Scissoring Scissor;
	}

	/// <summary>
	/// A panel subtree recorded into a command list of its own, with its own tables and GPU buffers. The root's recording
	/// inserts the list; while nothing in the subtree changes, the list is inserted again without recording or uploading
	/// anything, so a change elsewhere in the root re-records only what changed.
	/// </summary>
	internal sealed class Retained
	{
		internal readonly CommandList List = new( "UI Retained Subtree" );
		internal readonly Painter.Context Context;

		/// <summary>The subtree records into this list while it's drawn where it can be inserted.</summary>
		internal bool Active;

		/// <summary>The list's recording can be inserted again while the subtree is unchanged: its keys describe it all.</summary>
		internal bool Valid;

		// The draw keys of the subtree's custom drawing panels, as recorded
		internal readonly List<(Panel Panel, Panel.DrawKey Key)> Keys = [];

		// Where it was drawn: what the parent's painter had when the list recorded
		internal PainterBatcher.Target Destination;
		internal float Opacity, Scale;
		internal BlendMode Blend;
		internal Rect Bounds;
		internal Matrix BaseTransform;

		internal Retained()
		{
			Context = Painter.Context.Get( List );
		}

		internal void Capture( Painter.Context parent )
		{
			Destination = parent.Batcher.Destination;
			Opacity = parent.InheritedOpacity;
			Scale = parent.ScaleToScreen;
			Blend = parent.InitialBlendMode;
			Bounds = parent.Bounds;
			BaseTransform = parent.BaseTransform;
		}

		internal bool DrawnLike( Painter.Context parent )
		{
			return Opacity == parent.InheritedOpacity && Blend == parent.InitialBlendMode && Scale == parent.ScaleToScreen
				&& Bounds == parent.Bounds && BaseTransform == parent.BaseTransform && Destination.DrawsLike( in parent.Batcher.Destination );
		}
	}

	internal readonly record struct SubtreeCapture( PainterBatcher.Capture Batch, int Keys, int Uncomparable, int Panels, int Culled );

	/// <summary>
	/// Called by Panel.Render before recording a subtree: notes what it's drawn under and where its draws start.
	/// </summary>
	internal SubtreeCapture BeginSubtree( Panel panel, Painter painter, in RootPanel.FrameStats stats )
	{
		var batcher = painter.Batcher;
		var subtree = panel.SubtreeRecording ??= new Subtree();
		subtree.Opacity = painter.InheritedOpacity;
		subtree.Blend = painter.InheritedBlendMode;
		subtree.Scissor = batcher.Destination.Scissor;
		return new SubtreeCapture( batcher.BeginCapture(), _keys.Count, _uncomparable, stats.Panels, stats.PanelsCulled );
	}

	/// <summary>
	/// Called by Panel.Render after recording a subtree: keeps where its draws went, and whether they can be replayed.
	/// </summary>
	internal void EndSubtree( Panel panel, Painter painter, in SubtreeCapture capture, in RootPanel.FrameStats stats )
	{
		var subtree = panel.SubtreeRecording;
		var batcher = painter.Batcher;
		subtree.Replayable = batcher.EndCapture( capture.Batch, out subtree.Run ) && _uncomparable == capture.Uncomparable;
		subtree.RecordedAt = _recordingAt;
		subtree.Keys = capture.Keys;
		subtree.KeyCount = _keys.Count - capture.Keys;
		subtree.Panels = stats.Panels - capture.Panels;
		subtree.Culled = stats.PanelsCulled - capture.Culled;
		subtree.Streak = 0;
	}

	/// <summary>
	/// Whether <paramref name="panel"/>'s subtree is drawn from a list of its own here: it's been kept in one and can still
	/// be inserted, or it's large and has replayed unchanged for a few frames. Not inside layers or another retained list.
	/// </summary>
	internal bool WantsRetained( Panel panel, Painter painter )
	{
		var subtree = panel.SubtreeRecording;
		if ( subtree is null || !Enabled || !SubtreesEnabled || !RetainEnabled || _retainedDepth > 0 ) return false;
		if ( !painter.Batcher.CanInsertRetained ) return false;
		if ( subtree.Kept is { Active: true } ) return true;
		return subtree.Streak >= RetainAfter && subtree.Run.BoxCount + subtree.Run.TextCount >= RetainMinimum;
	}

	/// <summary>
	/// Inserts the subtree's retained list as it was recorded, if nothing it draws from has changed: no change stamped in
	/// the subtree, its custom drawing panels still have their keys, its textures are where they were, and the parent
	/// draws it in the same place. Call when <see cref="WantsRetained"/>.
	/// </summary>
	internal bool TryInsertRetained( Panel panel, Painter painter, ref RootPanel.FrameStats stats )
	{
		var subtree = panel.SubtreeRecording;
		if ( subtree.Kept is not { Active: true, Valid: true } kept ) return false;
		if ( panel.PaintChangedAt > subtree.RecordedAt ) return false;
		if ( !kept.DrawnLike( painter.RecordingContext ) ) return false;

		foreach ( var (keyed, drawn) in kept.Keys )
		{
			if ( !keyed.TryGetDrawKey( out var now ) || now != drawn ) return false;
		}

		if ( !kept.Context.Batcher.TexturesUnchanged() ) return false;

		_keys.AddRange( kept.Keys );
		_retained.Add( kept );
		painter.Batcher.InsertRetained( kept.List );
		subtree.RecordedAt = _recordingAt;
		stats.Panels += subtree.Panels;
		stats.PanelsCulled += subtree.Culled;
		RetainedInserts++;
		return true;
	}

	/// <summary>
	/// Called by Panel.Render before recording a subtree into its retained list: makes the list if it has none, clears it
	/// and notes where it's drawn. Record with a painter from <see cref="Retained.Context"/> begun from the parent's.
	/// </summary>
	internal Retained BeginRetained( Panel panel, Painter painter )
	{
		var subtree = panel.SubtreeRecording ??= new Subtree();
		var kept = subtree.Kept ??= new Retained();
		kept.List.Reset();
		kept.Capture( painter.RecordingContext );
		_retainedDepth++;
		return kept;
	}

	/// <summary>
	/// Called by Panel.Render after recording a subtree into its retained list (after <see cref="EndSubtree"/>, before the
	/// list's painting ends): keeps its keys, and whether it can be inserted again unrecorded. One that can't goes back to
	/// recording into its parent from the next frame.
	/// </summary>
	internal void EndRetained( Panel panel, in SubtreeCapture capture )
	{
		_retainedDepth--;
		var subtree = panel.SubtreeRecording;
		var kept = subtree.Kept;
		kept.Valid = _uncomparable == capture.Uncomparable && kept.Context.Batcher.TargetsPushed == 0;
		kept.Active = kept.Valid;
		kept.Keys.Clear();
		kept.Keys.AddRange( System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _keys )[capture.Keys..] );
		_retained.Add( kept );
		RetainedRecordings++;
	}

	/// <summary>
	/// Called by Panel.Render before recording a subtree: replays last frame's recording of it instead, if nothing in it has
	/// changed, its custom drawing panels still have their keys, and its parent draws it under the same opacity, blend and clip.
	/// </summary>
	internal bool TryReplay( Panel panel, Painter painter, ref RootPanel.FrameStats stats )
	{
		var subtree = panel.SubtreeRecording;
		if ( subtree is null || !subtree.Replayable || !Enabled || !SubtreesEnabled ) return false;
		if ( panel.PaintChangedAt > subtree.RecordedAt ) return false;

		var batcher = painter.Batcher;
		if ( painter.InheritedOpacity != subtree.Opacity || painter.InheritedBlendMode != subtree.Blend ) return false;
		if ( !batcher.Destination.Scissor.Equals( in subtree.Scissor ) || !batcher.CanReplay( in subtree.Run ) ) return false;

		var keys = System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _previousKeys ).Slice( subtree.Keys, subtree.KeyCount );
		foreach ( ref readonly var entry in keys )
		{
			if ( !entry.Panel.TryGetDrawKey( out var now ) || now != entry.Key ) return false;
		}

		subtree.Keys = _keys.Count;
		_keys.AddRange( keys );
		batcher.Replay( ref subtree.Run );
		subtree.RecordedAt = _recordingAt;
		subtree.Streak++;
		stats.Panels += subtree.Panels;
		stats.PanelsCulled += subtree.Culled;
		SubtreeReplays++;
		return true;
	}

	internal ref struct Scope
	{
		internal PanelRecording Previous;
		internal PanelRecording Recording;

		public void Dispose()
		{
			if ( Recording is null ) return;
			_current = Previous;
			Recording = null;
		}
	}

	/// <summary>
	/// Called by RootPanel before it records: forgets the last recording and collects what the new one draws from.
	/// </summary>
	internal Scope Begin( long recordingAt )
	{
		(_previousKeys, _keys) = (_keys, _previousKeys);
		_keys.Clear();
		_retained.Clear();
		_retainedDepth = 0;
		_uncomparable = 0;
		_recordedAt = -1;
		_recordingAt = recordingAt;
		var scope = new Scope { Previous = _current, Recording = this };
		_current = this;
		return scope;
	}

	/// <summary>
	/// Called by RootPanel once the recording is complete, with the paint clock it was made at.
	/// </summary>
	internal void Recorded( long recordedAt, in RootKey key )
	{
		_recordedAt = recordedAt;
		_key = key;
	}

	/// <summary>
	/// Called after a panel's OnDraw: keeps the panel's draw key, or notes drawing that can't be compared.
	/// </summary>
	internal void Drew( Panel panel )
	{
		if ( panel.TryGetDrawKey( out var key ) ) _keys.Add( (panel, key) );
		else _uncomparable++;
	}

	/// <summary>
	/// Called for drawing nothing describes, like native commands or inline paragraphs: the recording can't be kept.
	/// </summary>
	internal void DrewUncomparable() => _uncomparable++;

	/// <summary>
	/// Whether the last recording draws what recording <paramref name="root"/> again would: nothing in it has changed since,
	/// every custom drawing panel still has the key it drew with, and it records to the same place with the same settings.
	/// </summary>
	internal bool CanReplay( RootPanel root, in RootKey key, PainterBatcher batcher )
	{
		if ( !Enabled || _recordedAt < 0 || _uncomparable > 0 ) return false;
		if ( root.PaintChangedAt > _recordedAt || !key.Equals( _key ) ) return false;

		foreach ( var (panel, drawn) in _keys )
		{
			if ( !panel.TryGetDrawKey( out var now ) || now != drawn ) return false;
		}

		if ( batcher is null || !batcher.TexturesUnchanged() ) return false;

		foreach ( var kept in _retained )
		{
			if ( !kept.Context.Batcher.TexturesUnchanged() ) return false;
		}

		Replays++;
		return true;
	}

	/// <summary>
	/// Forgets the recording, so the next frame records. Called when the root's command list is reset outside a recording.
	/// </summary>
	internal void Clear()
	{
		_keys.Clear();
		_previousKeys.Clear();
		_retained.Clear();
		_recordedAt = -1;
	}
}
