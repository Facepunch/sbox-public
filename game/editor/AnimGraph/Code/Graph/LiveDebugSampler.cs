using Sandbox.Animation;

namespace Editor.AnimGraph;

/// <summary>
/// Samples requested runtime nodes once per editor update. Sync-track buffers survive between
/// updates, but samples never do: a paused graph can still change through recording playback.
/// </summary>
internal sealed class LiveDebugSampler
{
	private sealed class Sample
	{
		public int Frame = -1;
		public bool HasInfo;
		public GraphDebug.NodeInfo Info;
		public int PoseFrame = -1;
		public GraphDebug.SyncEvent[] Events = [];
		public string EventID = "";
		public int ValueFrame = -1;
		public bool HasValue;
		public ValueDebugInfo Value;
	}

	private readonly Dictionary<int, Sample> _samples = new();
	private readonly Dictionary<StringToken, string> _idNames = new();
	private SceneModel _model;
	private int _frame;

	public void BeginFrame( SceneModel model )
	{
		if ( !ReferenceEquals( model, _model ) )
			Clear();

		_model = model;
		_frame++;
	}

	public void Clear()
	{
		_samples.Clear();
		_idNames.Clear();
		_model = null;
	}

	private Sample GetSample( int nodeIndex )
	{
		if ( _model is null )
			return null;

		if ( !_samples.TryGetValue( nodeIndex, out var sample ) )
		{
			sample = new Sample();
			_samples.Add( nodeIndex, sample );
		}

		if ( sample.Frame != _frame )
		{
			sample.Frame = _frame;
			sample.HasInfo = _model.Animation.Debug.TryGetNode( nodeIndex, out sample.Info );
		}

		return sample;
	}

	public bool TryGetInfo( int nodeIndex, out GraphDebug.NodeInfo info )
	{
		var sample = GetSample( nodeIndex );
		info = sample?.Info ?? default;
		return sample?.HasInfo ?? false;
	}

	public void ReadPose( int nodeIndex, out GraphDebug.SyncEvent[] events, out string eventID )
	{
		events = [];
		eventID = "";
		var sample = GetSample( nodeIndex );
		if ( sample is not { HasInfo: true, Info.IsActive: true, Info.Kind: GraphValueKind.Pose } )
			return;

		if ( sample.PoseFrame != _frame )
		{
			sample.PoseFrame = _frame;
			if ( sample.Events.Length != sample.Info.SyncEventCount )
				sample.Events = new GraphDebug.SyncEvent[sample.Info.SyncEventCount];

			for ( var i = 0; i < sample.Events.Length; i++ )
				_model.Animation.Debug.TryGetSyncEvent( nodeIndex, i, out sample.Events[i] );

			var currentEvent = sample.Events.ElementAtOrDefault( sample.Info.SyncEventIndex );
			var name = currentEvent.Id.Value == 0 ? "" : _model.Animation.Debug.GetIdName( currentEvent.Id );
			sample.EventID = string.IsNullOrWhiteSpace( name ) ? "No ID" : name;
		}

		events = sample.Events;
		eventID = sample.EventID;
	}

	public bool TryGetValue( int nodeIndex, out ValueDebugInfo value )
	{
		value = default;
		var sample = GetSample( nodeIndex );
		if ( sample is not { HasInfo: true, Info.IsActive: true } )
			return false;

		if ( sample.ValueFrame != _frame )
		{
			sample.ValueFrame = _frame;
			sample.HasValue = _model.Animation.Debug.TryGetValue( nodeIndex, out var raw );
			sample.Value = sample.HasValue
				? new ValueDebugInfo(
					raw,
					sample.Info.Kind == GraphValueKind.Id ? ValueIDName( raw.Id ) : "",
					sample.Info.Kind == GraphValueKind.Target && raw.IsBoneTarget ? ValueIDName( raw.TargetBoneId ) : "" )
				: default;
		}

		value = sample.Value;
		return sample.HasValue;
	}

	private string IDName( StringToken id )
	{
		if ( id.Value == 0 )
			return "";

		if ( !_idNames.TryGetValue( id, out var name ) )
		{
			name = _model.Animation.Debug.GetIdName( id );
			_idNames.Add( id, name );
		}

		return name;
	}

	private string ValueIDName( StringToken id )
	{
		var name = IDName( id );
		return string.IsNullOrWhiteSpace( name ) ? "Invalid" : name;
	}
}
