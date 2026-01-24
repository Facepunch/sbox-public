using System.Diagnostics.CodeAnalysis;
using Sandbox.MovieMaker;
using System.Linq;
using Sandbox.MovieMaker.Properties;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// Describes how a track should be displayed in the track list / dope sheet.
/// </summary>
public sealed partial class TrackView : IComparable<TrackView>
{
	public TrackListView TrackList { get; }

	public float Position { get; private set; } = -1f;
	public float Height { get; private set; } = -1f;

	public TrackView? Parent { get; }
	public IProjectTrack Track { get; }
	public ITrackTarget Target { get; }

	public string Name => Track.Name;

	public MovieTime Duration => _blocks.Select( x => x.TimeRange.End )
		.Concat( _previewBlocks.Select( x => x.TimeRange.End ) )
		.DefaultIfEmpty( MovieTime.Zero )
		.Max();

	private IPropertyTrack<Transform>? _transformTrack;

	public IPropertyTrack<Transform> TransformTrack => _transformTrack ??= CreateTransformTrack();

	private bool _isExpanded;
	private bool _isLockedSelf;
	private bool _isHovered;
	private bool _isSelected;

	private bool _wasExpanded;

	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			if ( _isSelected == value ) return;

			_isSelected = value;
			DispatchChanged( false );
		}
	}

	public bool IsHovered
	{
		get => _isHovered;
		set
		{
			if ( _isHovered == value ) return;

			_isHovered = value;
			DispatchChanged( true );
		}
	}

	public bool IsExpanded
	{
		get => _isExpanded;
		set
		{
			if ( _isExpanded == value ) return;

			_isExpanded = value;

			SetCookie( nameof( IsExpanded ), value );
			TrackList.Update();
		}
	}

	public bool IsLockedSelf
	{
		get => _isLockedSelf;
		set
		{
			if ( _isLockedSelf == value ) return;

			_isLockedSelf = value;

			SetCookie( nameof( IsLockedSelf ), value );
			DispatchChanged( true );
		}
	}

	public bool IsLocked => IsLockedSelf || Parent?.IsLocked is true;

	public string Title => Track.Name;
	public string Description
	{
		get
		{
			var path = Track.GetPath();
			string[] propertyNames = [path.ReferenceTrack.Name, .. path.PropertyNames];
			return string.Join( " \u2192 ", propertyNames );
		}
	}

	private readonly SynchronizedSet<IProjectTrack, TrackView> _children;

	private bool _dispatchValueChanged = false;

	public IReadOnlyList<TrackView> Children => _children;

	public int StateHash { get; private set; }
	public bool IsEmpty => _children.Count == 0 && Track.IsEmpty;

	/// <summary>
	/// Is this track representing the transform of a bone accessed through a <see cref="SkinnedModelRenderer"/>?
	/// </summary>
	public bool IsBoneTransform => Track is IPropertyTrack<Transform> && Parent is
		{ Track.Name: "Bones", Parent.Track: IReferenceTrack<SkinnedModelRenderer> };

	/// <summary>
	/// Is this track representing a (procedural) bone object?
	/// </summary>
	public bool IsBoneObject => Parent is not null && Target is ITrackReference<GameObject> { Value.Flags: var flags } && (flags & GameObjectFlags.Bone) != 0;

	public BoneCollection.Bone? Bone =>
		Parent?.Parent?.Target is ITrackReference<SkinnedModelRenderer> { Value.Model: { } model }
			? model.Bones.GetBone( Track.Name )
			: null;

	/// <summary>
	/// Invoked when properties of this track are changed.
	/// </summary>
	public event Action<TrackView>? Changed;

	/// <summary>
	/// Invoked when the contents of the track are modified.
	/// </summary>
	public event Action<TrackView>? ValueChanged;

	/// <summary>
	/// Invoked when this track is removed.
	/// </summary>
	public event Action<TrackView>? Removed;

	public TrackView( TrackListView trackList, TrackView? parent, IProjectTrack track, ITrackTarget target )
	{
		TrackList = trackList;
		Parent = parent;
		Track = track;
		Target = target;

		_isExpanded = GetCookie( nameof( IsExpanded ), parent is null );
		_isLockedSelf = GetCookie( nameof( IsLockedSelf ), false );

		_children = new SynchronizedSet<IProjectTrack, TrackView>(
			AddChildTrack, RemoveChildTrack, UpdateChildTrack );
	}

	private void DispatchChanged( bool recurse )
	{
		Changed?.Invoke( this );

		if ( !recurse ) return;

		foreach ( var child in _children )
		{
			child.DispatchChanged( true );
		}
	}

	private static PropertySignal<object?> DefaultSignal { get; } = (object?)null;

	private TrackView AddChildTrack( IProjectTrack source )
	{
		return new( TrackList, this, source, TrackList.Session.Binder.Get( source ) );
	}

	private void RemoveChildTrack( TrackView item ) => item.OnRemoved();
	private bool UpdateChildTrack( IProjectTrack source, TrackView item ) => item.Update();

	/// <summary>
	/// Select this track, deselect others.
	/// </summary>
	public void Select()
	{
		TrackList.DeselectAll();
		TrackList.LastSelected = this;

		IsSelected = true;
	}

	/// <summary>
	/// Toggle whether this track is selected, don't deselect others.
	/// </summary>
	public void ToggleSelect()
	{
		IsSelected = !IsSelected;
		TrackList.LastSelected = this;
	}

	/// <summary>
	/// Select this track, and all tracks between it and the last selected track.
	/// </summary>
	public void RangeSelect()
	{
		var lastSelected = TrackList.LastSelected;

		TrackList.LastSelected = this;
		IsSelected = true;

		if ( lastSelected is null || lastSelected == this ) return;

		var visibleTracks = TrackList.VisibleTracks.ToArray();

		var lastIndex = visibleTracks.IndexOf( lastSelected );
		var thisIndex = visibleTracks.IndexOf( this );

		if ( lastIndex == -1 || thisIndex == -1 ) return;

		var minIndex = Math.Min( lastIndex, thisIndex );
		var maxIndex = Math.Max( lastIndex, thisIndex );

		for ( var i = minIndex; i <= maxIndex; i++ )
		{
			visibleTracks[i].IsSelected = true;
		}
	}

	private readonly record struct SubOrderedTrack( IProjectTrack Track, int SubOrder ) : IComparable<SubOrderedTrack>
	{
		public int CompareTo( SubOrderedTrack other )
		{
			if ( Track.Order != other.Track.Order )
			{
				return Track.Order - other.Track.Order;
			}

			if ( SubOrder != other.SubOrder )
			{
				return SubOrder - other.SubOrder;
			}

			return Track.CompareTo( other.Track );
		}
	}

	[ThreadStatic]
	private static List<SubOrderedTrack>? _tempOrderedTracks;

	public bool Update()
	{
		_transformTrack = null;

		var ordered = _tempOrderedTracks ??= [];

		ordered.Clear();
		ordered.AddRange( Track.Children.Select( x => new SubOrderedTrack( x, GetSubOrder( x ) ) ) );
		ordered.Sort();

		return _children.Update( ordered.Select( x => x.Track ) ) || _wasExpanded != IsExpanded;
	}

	private int GetSubOrder( IProjectTrack track )
	{
		// Keep Component tracks ordered the same as in the inspector

		if ( Track.TargetType != typeof(GameObject) ) return 0;
		if ( Target is not { IsBound: true, Value: GameObject go } ) return 0;
		if ( track is not IProjectReferenceTrack refTrack ) return 0;
		if ( !refTrack.TargetType.IsAssignableTo( typeof(Component) ) ) return 0;

		var index = 0;

		foreach ( var cmp in go.Components.GetAll() )
		{
			if ( refTrack.TargetType.IsInstanceOfType( cmp ) )
			{
				return index;
			}

			index++;
		}

		return int.MaxValue;
	}

	public bool UpdatePosition( ref float position )
	{
		var changed = !Position.Equals( position ) || _wasExpanded != IsExpanded;
		var hashCode = new HashCode();

		hashCode.Add( Track );
		hashCode.Add( IsExpanded );

		Position = position;
		_wasExpanded = IsExpanded;

		position += Timeline.TrackHeight;

		var childPosition = position;

		foreach ( var child in _children )
		{
			changed |= child.UpdatePosition( ref childPosition );
			hashCode.Add( child.StateHash );
		}

		if ( IsExpanded )
		{
			position = childPosition;
		}

		Height = position - Position;
		StateHash = hashCode.ToHashCode();

		if ( changed ) Changed?.Invoke( this );

		return changed;
	}

	private bool _removed;

	internal void OnRemoved()
	{
		if ( _removed ) return;
		_removed = true;

		_children.Clear();

		Removed?.Invoke( this );
	}

	public void Remove()
	{
		Track.Remove();
		TrackList.Update();
	}

	public bool MarkValueChanged()
	{
		_blocksInvalid = true;
		_previewBlocksInvalid = true;
		_dispatchValueChanged = true;

		Parent?.MarkValueChanged();
		TrackList.Session.ClipModified();
		TrackList.Session.RefreshNextFrame();

		return true;
	}

	public void Frame()
	{
		if ( _dispatchValueChanged )
		{
			_dispatchValueChanged = false;
			ValueChanged?.Invoke( this );
		}

		foreach ( var child in _children )
		{
			child.Frame();
		}
	}

	public int CompareTo( TrackView? other )
	{
		if ( ReferenceEquals( this, other ) )
		{
			return 0;
		}

		if ( other is null )
		{
			return 1;
		}

		var childrenCompare = (Children.Count > 0).CompareTo( other.Children.Count > 0 );
		if ( childrenCompare != 0 ) return childrenCompare;

		return string.Compare( Track.Name, other.Track.Name, StringComparison.Ordinal );
	}

	public T GetCookie<T>( string name, T fallback ) =>
		TrackList.Session.GetCookie( $"{Track.Id}.{name}", fallback );

	public void SetCookie<T>( string name, T value ) =>
		TrackList.Session.SetCookie( $"{Track.Id}.{name}", value );

	public void InspectProperty()
	{
		Select();

		if ( Target is not { } property ) return;
		if ( property.GetTargetGameObject() is not { } go ) return;

		SceneEditorSession.Active.Selection.Clear();
		SceneEditorSession.Active.Selection.Add( go );

		if ( Track.Parent is not IReferenceTrack<GameObject> )
		{
			return;
		}

		EditorToolManager.SetTool( nameof(ObjectEditorTool) );

		switch ( property.Name )
		{
			case nameof( GameObject.LocalPosition ):
				EditorToolManager.SetSubTool( nameof( PositionEditorTool ) );
				break;

			case nameof( GameObject.LocalRotation ):
				EditorToolManager.SetSubTool( nameof( RotationEditorTool ) );
				break;

			case nameof( GameObject.LocalScale ):
				EditorToolManager.SetSubTool( nameof( ScaleEditorTool ) );
				break;
		}
	}

	public TrackView? Find( string propertyPath )
	{
		var parent = this;

		while ( parent is not null && propertyPath.Length > 0 )
		{
			var propertyName = propertyPath;

			// TODO: Hack for anim graph parameters including periods

			if ( parent.Track.TargetType != typeof( SkinnedModelRenderer.ParameterAccessor ) && propertyPath.IndexOf( '.' ) is var index and > -1 )
			{
				propertyName = propertyPath[..index];
				propertyPath = propertyPath[(index + 1)..];
			}
			else
			{
				propertyPath = string.Empty;
			}

			parent = parent.Children.FirstOrDefault( x => x.Track.Name == propertyName );
		}

		return parent;
	}

	public void ApplyFrame( MovieTime time )
	{
		switch ( Track )
		{
			case ProjectSequenceTrack sequenceTrack:
				var session = TrackList.Session;
				var binder = session.Binder;

				var sequenceBlock = sequenceTrack.Blocks.GetBlock( time );
				if ( sequenceBlock is null ) break;

				// If we're editing this sequence, its Session will handle applying so we don't need
				// to do it here

				if ( session.Editor.IsMovieOpen( sequenceBlock.Resource ) ) break;

				foreach ( var propertyTrack in sequenceTrack.PropertyTracks )
				{
					propertyTrack.Update( time, binder );
				}

				break;

			case IPropertyTrack propertyTrack:
				// Don't apply this track if we're recording to it

				if ( !IsLocked && TrackList.Session.IsRecording ) break;
				if ( Target is not ITrackProperty { CanWrite: true } property ) break;

				UpdatePreviewBlocks();

				if ( _previewBlocks.GetBlock( time ) is IPropertySignal block )
				{
					property.Value = block.GetValue( time );
				}
				else
				{
					property.Update( propertyTrack, time );
				}

				break;
		}
	}

	public bool TryGetValue<T>( MovieTime time, [MaybeNullWhen( false )] out T value )
	{
		value = default;

		if ( Track is not IPropertyTrack<T> track ) return false;

		UpdatePreviewBlocks();

		if ( _previewBlocks.GetBlock( time ) is IPropertySignal<T> signal )
		{
			value = signal.GetValue( time );
			return true;
		}

		return track.TryGetValue( time, out value );
	}

	private IPropertyTrack<Transform> CreateTransformTrack()
	{
		if ( IsBoneTransform )
		{
			return new BoneTransformTrack( Parent!, Track.Name );
		}

		if ( Track is not IReferenceTrack<GameObject> )
		{
			return Parent?.TransformTrack ?? new TransformTrack( this );
		}

		return new TransformTrack( this,
			Find( nameof(GameObject.Enabled) ),
			Find( nameof(GameObject.LocalPosition) ),
			Find( nameof(GameObject.LocalRotation) ),
			Find( nameof(GameObject.LocalScale) ) );
	}
}

/// <summary>
/// Helper for drawing a trail gizmo of a track inside a game object reference track.
/// </summary>
file sealed class TransformTrack : IPropertyTrack<Transform>
{
	public string Name => "Transform";
	public ITrack Parent => View.Track;

	public TrackView View { get; }

	public TrackView? Enabled { get; }
	public TrackView? LocalPosition { get; }
	public TrackView? LocalRotation { get; }
	public TrackView? LocalScale { get; }

	public TransformTrack( TrackView view,
		TrackView? enabled = null,
		TrackView? localPosition = null,
		TrackView? localRotation = null,
		TrackView? localScale = null )
	{
		View = view;

		Enabled = enabled;
		LocalPosition = localPosition;
		LocalRotation = localRotation;
		LocalScale = localScale;
	}

	public bool TryGetValue( MovieTime time, out Transform value )
	{
		value = Transform.Zero;

		// This track only returns a value if:
		//   1) Enabled is true, or
		//   2) Enabled is undefined, and any component track is defined

		if ( Enabled?.TryGetValue( time, out bool enabled ) is true )
		{
			if ( !enabled ) return false;
		}
		else
		{
			enabled = false;
		}

		if ( LocalPosition?.TryGetValue( time, out Vector3 pos ) is true )
		{
			value.Position = pos;
			enabled = true;
		}

		if ( LocalRotation?.TryGetValue( time, out Rotation rot ) is true )
		{
			value.Rotation = rot;
			enabled = true;
		}

		if ( LocalScale?.TryGetValue( time, out Vector3 scale ) is true )
		{
			value.Scale = scale;
			enabled = true;
		}

		if ( View.Parent?.TransformTrack.TryGetValue( time, out var parentTransform ) is true )
		{
			value = parentTransform.ToWorld( value );
		}

		return enabled;
	}
}

/// <summary>
/// Helper for drawing a trail gizmo of a bone track.
/// </summary>
file sealed class BoneTransformTrack : IPropertyTrack<Transform>
{
	public string Name { get; }
	public ITrack Parent { get; }

	private readonly TrackView _boneAccessorView;

	public BoneTransformTrack( TrackView boneAccessorView, string boneName )
	{
		_boneAccessorView = boneAccessorView;

		Name = boneName;
		Parent = boneAccessorView.Track;
	}

	public bool TryGetValue( MovieTime time, out Transform value )
	{
		value = default;

		if ( _boneAccessorView.Parent?.Target is not ITrackReference<SkinnedModelRenderer> { Value: { } renderer } )
		{
			return false;
		}

		if ( renderer.Model is not { } model )
		{
			return false;
		}

		if ( model.Bones.GetBone( Name ) is not { } bone )
		{
			return false;
		}

		if ( GetBoneTransform( renderer, bone, time ) is { } transform )
		{
			value = transform;

			if ( _boneAccessorView.Parent?.TransformTrack.TryGetValue( time, out var parentTransform ) is true )
			{
				value = parentTransform.ToWorld( value );
				return true;
			}

			return false;
		}

		return false;
	}

	private Transform? GetBoneTransform( SkinnedModelRenderer renderer, BoneCollection.Bone bone, MovieTime time )
	{
		var parentTransform = bone.Parent is { } parent
			? GetBoneTransform( renderer, parent, time ) ?? Transform.Zero
			: Transform.Zero;

		Transform localTransform;

		if ( _boneAccessorView.Children.FirstOrDefault( x => x.Track.Name == bone.Name ) is { } boneView )
		{
			if ( !boneView.TryGetValue( time, out localTransform ) )
			{
				return null;
			}
		}
		else
		{
			localTransform = MovieBoneAnimatorSystem.Current.GetParentSpaceBone( renderer, bone.Index );
		}

		return parentTransform.ToWorld( localTransform );
	}
}
