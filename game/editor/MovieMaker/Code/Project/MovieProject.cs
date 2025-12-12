using System.Collections;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// All the info needed to compile a <see cref="MovieClip"/>. Gets serialized
/// and stored in <see cref="IMovieResource.EditorData"/>.
/// </summary>
public sealed partial class MovieProject : IMovieClip, IMovieProject
{
	public const int DefaultSampleRate = 30;

	private readonly List<IProjectTrack> _rootTrackList = new();
	private readonly List<IProjectTrack> _trackList = new();
	private readonly Dictionary<Guid, IProjectTrack> _trackDict = new();

	/// <summary>
	/// Set to true when tracks need sorting / root tracks might have changed.
	/// </summary>
	private bool _tracksChanged;

	/// <summary>
	/// When compiling, what sample rate to use.
	/// </summary>
	public int SampleRate { get; set; } = DefaultSampleRate;

	/// <summary>
	/// When exporting video, which config to use.
	/// </summary>
	public VideoExportConfig? ExportConfig { get; set; }

	public bool IsEmpty => Tracks.Count == 0;

	public MovieTime Duration => _trackList.OfType<IProjectBlockTrack>()
		.Select( x => x.TimeRange.End )
		.DefaultIfEmpty( 0d )
		.Max();

	public IReadOnlyList<IProjectTrack> Tracks
	{
		get
		{
			UpdateTracks();
			return _trackList;
		}
	}

	public IEnumerable<MovieResource> References
	{
		get
		{
			var resources = new HashSet<MovieResource>();

			foreach ( var track in Tracks )
			{
				foreach ( var reference in track.References )
				{
					resources.Add( reference );
				}
			}

			return resources;
		}
	}

	IEnumerable<ITrack> IMovieClip.Tracks
	{
		get
		{
			foreach ( var track in Tracks )
			{
				if ( track is not ProjectSequenceTrack sequenceTrack )
				{
					yield return track;
					continue;
				}

				foreach ( var subTrack in sequenceTrack.ReferenceTracks )
				{
					yield return subTrack;
				}

				foreach ( var subTrack in sequenceTrack.PropertyTracks )
				{
					yield return subTrack;
				}
			}
		}
	}

	public IReadOnlyList<IProjectTrack> RootTracks
	{
		get
		{
			UpdateTracks();
			return _rootTrackList;
		}
	}

	public MovieProject()
	{

	}

	/// <summary>
	/// Create a project based on a compiled clip, so that clip can be edited.
	/// </summary>
	internal MovieProject( MovieClip clip )
	{
		var source = new ProjectSourceClip( Guid.NewGuid(), clip, null );

		foreach ( var compiledTrack in clip.Tracks )
		{
			var parentTrack = compiledTrack.Parent is { } parent ? GetTrack( parent ) : null;

			switch ( compiledTrack )
			{
				case ICompiledReferenceTrack refTrack:
				{
					var track = IProjectReferenceTrack.Create( this, refTrack.Id, refTrack.Name, refTrack.TargetType );

					AddTrackInternal( track, parentTrack );
					continue;
				}
				case ICompiledPropertyTrack propertyTrack:
				{
					var track = IProjectPropertyTrack.Create( this, Guid.NewGuid(), propertyTrack.Name, propertyTrack.TargetType );

					track.SetBlocks( track.CreateSourceBlocks( source ) );

					AddTrackInternal( track, parentTrack );
					continue;
				}
				default:
					throw new NotImplementedException();
			}
		}
	}

	public IProjectTrack? GetTrack( Guid trackId )
	{
		return _trackDict!.GetValueOrDefault( trackId );
	}

	IReferenceTrack? IMovieClip.GetTrack( Guid trackId ) => GetTrack( trackId ) as IReferenceTrack;

	public IProjectTrack? GetTrack( ITrack track )
	{
		if ( track is IProjectTrack projTrack && projTrack.Project == this )
		{
			return projTrack;
		}

		if ( track is IReferenceTrack refTrack )
		{
			return GetTrack( refTrack.Id );
		}

		return track.Parent is { } parent
			? GetTrack( parent )?.GetChild( track.Name )
			: null;
	}

	internal sealed class CompileResult : IEnumerable<ICompiledTrack>
	{
		private readonly List<ICompiledTrack> _allCompiled = new();
		private readonly Dictionary<IProjectTrack, ICompiledTrack> _compiledProjectTracks = new();

		public ICompiledTrack Get( IProjectTrack track ) => _compiledProjectTracks[track];

		public void Add( ICompiledTrack compiled ) => _allCompiled.Add( compiled );

		public void Add( IProjectTrack track, ICompiledTrack compiled )
		{
			Add( compiled );

			_compiledProjectTracks[track] = compiled;
		}

		public IEnumerator<ICompiledTrack> GetEnumerator() => _allCompiled.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	[ThreadStatic]
	internal static CompileResult? RootCompileResult;

	public MovieClip Compile()
	{
		var result = new CompileResult();

		RootCompileResult ??= result;

		try
		{
			foreach ( var track in RootTracks )
			{
				if ( track.IsEmpty ) continue;

				CompileTrack( track, result );
			}
		}
		finally
		{
			if ( RootCompileResult == result )
			{
				RootCompileResult = null;
			}
		}

		return MovieClip.FromTracks( result );
	}

	private void CompileTrack( IProjectTrack track, CompileResult result )
	{
		if ( track is ProjectSequenceTrack sequenceTrack )
		{
			CompileSequenceTrack( sequenceTrack, result );
			return;
		}

		var compiled = track.Compile( track.Parent is { } parent ? result.Get( parent ) : null, false );

		result.Add( track, compiled );

		foreach ( var childTrack in track.Children )
		{
			if ( childTrack.IsEmpty ) continue;

			CompileTrack( childTrack, result );
		}
	}

	private void CompileSequenceTrack( ProjectSequenceTrack track, CompileResult result )
	{
		foreach ( var inner in track.PropertyTracks )
		{
			result.Add( inner.Compile() );
		}
	}

	public IProjectTrack GetOrAddTrack( ITrack track )
	{
		switch ( track )
		{
			case IProjectTrack projectTrack when projectTrack.Project == this:
				return projectTrack;

			case ProjectSequenceTrack sequenceTrack:
				throw new NotImplementedException();

			case IReferenceTrack referenceTrack:
			{
				if ( GetTrack( referenceTrack.Id ) is { } refTrackCopy ) return refTrackCopy;

				refTrackCopy = IProjectReferenceTrack.Create( this, referenceTrack.Id, referenceTrack.Name, referenceTrack.TargetType );

				var parentCopy = track.Parent is { } parent ? GetOrAddTrack( parent ) : null;

				AddTrackInternal( refTrackCopy, parentCopy );

				return refTrackCopy;
			}

			case IPropertyTrack propertyTrack:
			{
				var parentCopy = GetOrAddTrack( propertyTrack.Parent );

				if ( parentCopy.GetChild( propertyTrack.Name ) is { } existing && existing.TargetType == propertyTrack.TargetType )
				{
					return existing;
				}

				return AddPropertyTrack( propertyTrack.Name, propertyTrack.TargetType, GetOrAddTrack( propertyTrack.Parent ) );
			}

			default:
				throw new NotImplementedException();
		}
	}

	public IProjectReferenceTrack AddReferenceTrack( string name, Type targetType, IProjectTrack? parentTrack = null )
	{
		var guid = Guid.NewGuid();
		var track = IProjectReferenceTrack.Create( this, guid, name, targetType );

		AddTrackInternal( track, parentTrack );

		return track;
	}

	public IProjectPropertyTrack AddPropertyTrack( string name, Type targetType, IProjectTrack? parentTrack = null )
	{
		var guid = Guid.NewGuid();
		var track = IProjectPropertyTrack.Create( this, guid, name, targetType );

		AddTrackInternal( track, parentTrack );

		return track;
	}

	public ProjectSequenceTrack AddSequenceTrack( string name, IProjectTrack? parentTrack = null )
	{
		var guid = Guid.NewGuid();
		var track = new ProjectSequenceTrack( this, guid, name );

		AddTrackInternal( track, parentTrack );

		return track;
	}

	private void AddTrackInternal( IProjectTrack track, IProjectTrack? parentTrack )
	{
		if ( !_trackDict.TryAdd( track.Id, track ) )
		{
			throw new Exception( "Conflicting track IDs!" );
		}

		((IProjectTrackInternal?)parentTrack)?.AddChild( (IProjectTrackInternal)track );

		_trackList.Add( track );

		InvalidateTracks();
	}

	internal void RemoveTrackInternal( IProjectTrackInternal projectTrack )
	{
		if ( !_trackList.Remove( projectTrack ) || !_trackDict.Remove( projectTrack.Id ) ) return;

		foreach ( var child in projectTrack.Children.ToArray() )
		{
			child.Remove();
		}

		projectTrack.Parent?.RemoveChild( projectTrack );

		InvalidateTracks();
	}

	internal void InvalidateTracks()
	{
		_tracksChanged = true;
	}

	/// <summary>
	/// Makes sure tracks are sorted / root tracks are correct.
	/// </summary>
	private void UpdateTracks()
	{
		if ( !_tracksChanged ) return;

		_tracksChanged = false;

		_trackList.Sort();
		_rootTrackList.Clear();

		foreach ( var track in _trackList )
		{
			if ( track.Parent is null )
			{
				_rootTrackList.Add( track );
			}
		}
	}

	/// <summary>
	/// Force any project tracks using this resource to update.
	/// </summary>
	public void RefreshSequenceTracks( MovieResource resource )
	{
		foreach ( var track in Tracks.OfType<ProjectSequenceTrack>() )
		{
			if ( track.References.Contains( resource ) )
			{
				track.Invalidate();
			}
		}
	}
}

public static class MovieResourceExtensions
{
	public static MovieClip GetCompiled( this MovieResource resource )
	{
		if ( resource.Compiled is { } compiled ) return compiled;

		// To avoid cycles

		resource.Compiled = MovieClip.Empty;

		// TODO: hack because resource compiler might try to compile a movie before its references are compiled

		if ( AssetSystem.FindByPath( resource.ResourcePath ) is not { HasSourceFile: true } asset ) return MovieClip.Empty;

		using var stream = File.OpenRead( asset.GetSourceFile( true ) );

		var model = JsonSerializer.Deserialize<EmbeddedMovieResource>( stream, EditorJsonOptions );
		var project = model?.EditorData?.Deserialize<MovieProject>( EditorJsonOptions );

		return resource.Compiled = project?.Compile() ?? MovieClip.Empty;
	}

	public static MovieTime GetDuration( this MovieResource resource )
	{
		return resource.EditorData?["Duration"]?.Deserialize<MovieTime>( EditorJsonOptions )
			?? resource.Compiled?.Duration
			?? default;
	}
}
