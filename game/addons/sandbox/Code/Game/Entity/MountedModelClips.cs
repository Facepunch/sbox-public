using System.Collections;
using System.Runtime.CompilerServices;
using Sandbox.Animation;

/// <summary>Where a mounted animated model's skeleton and clips are, as resources of their own. Same game data as the mount writes.</summary>
[Sandbox.ModelEditor.GameData( "animation_data" ), Hide]
public sealed class ModelAnimationData
{
	public string Skeleton { get; set; }

	/// <summary>The mounted folder holding a .anim for each of <see cref="Clips"/>.</summary>
	public string ClipFolder { get; set; }

	public string[] Clips { get; set; } = [];
}

/// <summary>
/// A mounted model's animation skeleton and clips, read for spawn menu icons; a clip is loaded the first time it is looked up.
/// </summary>
public sealed class MountedModelClips : IReadOnlyDictionary<string, Clip>
{
	static readonly ConditionalWeakTable<Model, MountedModelClips> Cache = new();
	static readonly MountedModelClips None = new( null );

	readonly ModelAnimationData _data;
	readonly HashSet<string> _names;
	readonly Dictionary<string, Clip> _loaded = new( StringComparer.OrdinalIgnoreCase );
	Skeleton _skeleton;
	bool _skeletonLoaded;

	MountedModelClips( ModelAnimationData data )
	{
		_data = data;
		_names = new( data?.Clips ?? [], StringComparer.OrdinalIgnoreCase );
	}

	public static MountedModelClips Of( Model model ) => model is null ? None : Cache.GetValue( model, x => new MountedModelClips( x.GetData<ModelAnimationData>() ) );

	public Skeleton Skeleton
	{
		get
		{
			if ( _skeletonLoaded || string.IsNullOrEmpty( _data?.Skeleton ) ) return _skeleton;
			_skeletonLoaded = true;
			return _skeleton = Skeleton.Load( _data.Skeleton );
		}
	}

	public bool TryGetValue( string key, out Clip value )
	{
		value = null;
		if ( key is null || !_names.TryGetValue( key, out var name ) ) return false;
		if ( !_loaded.TryGetValue( name, out value ) )
		{
			value = Clip.Load( $"{_data.ClipFolder}/{name}.anim" );
			if ( value is null ) _names.Remove( name );
			else _loaded[name] = value;
		}

		return value is not null;
	}

	public Clip this[string key] => TryGetValue( key, out var clip ) ? clip : throw new KeyNotFoundException( $"The model has no animation clip '{key}'." );
	public bool ContainsKey( string key ) => key is not null && _names.Contains( key );
	public int Count => _names.Count;
	public IEnumerable<string> Keys => _names;
	public IEnumerable<Clip> Values => _names.ToArray().Select( name => TryGetValue( name, out var clip ) ? clip : null ).Where( clip => clip is not null );
	public IEnumerator<KeyValuePair<string, Clip>> GetEnumerator() => _names.ToArray()
		.Select( name => new KeyValuePair<string, Clip>( name, TryGetValue( name, out var clip ) ? clip : null ) ).Where( x => x.Value is not null ).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class MountedModelClipExtensions
{
	public static MountedModelClips AnimationClips( this Model model ) => MountedModelClips.Of( model );
	public static Skeleton AnimationSkeleton( this Model model ) => MountedModelClips.Of( model ).Skeleton;
}
