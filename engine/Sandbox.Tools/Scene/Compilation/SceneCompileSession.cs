using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace Editor;

public sealed partial class SceneCompileSession : AssetSystem.IEventListener
{
	public static SceneCompileSession Current { get; } = new();

	static readonly ConditionalWeakTable<Asset, SourceStatus> SourceStatuses = new();
	sealed record SourceStatus( string Path,
		(long Length, DateTime Modified) Source, (long Length, DateTime Modified) Binary, (long Length, DateTime Modified) Bake,
		string OutputFolder, DateTime FolderModified, bool IsCompiled );

	SceneCompiler.Sources _sources;
	SceneCompilerSettings _settings = new();
	SceneCompileReport _result;
	CancellationTokenSource _cancel = new();
	List<string> _lines = new();
	readonly List<SceneCompileWarning> _warnings = new();
	readonly List<SceneCompileStage> _stages = new();
	string _path;
	string _scanError;
	string _settingsError;
	string _failure;
	string _phase;
	bool _unsaved;
	bool _playing;
	bool _notifying;
	FastTimer _elapsed;
	FastTimer _phaseElapsed;

	public Scene Scene { get; private set; }
	public string Name => _sources?.Report.Name ?? Scene?.Name ?? "No scene";
	public SceneCompileReport Report => _result ?? _sources?.Report;
	public bool HasSources => Report is not null;
	public bool HasCompileGeometry => _sources?.HasCompileGeometry == true;
	public string Error => _settingsError ?? _scanError ?? _failure;
	public bool Running { get; private set; }
	public bool Cancelling => Running && _cancel.IsCancellationRequested;
	public bool HasResult { get; private set; }
	public bool IsCompiled { get; private set; }
	public string Status => Cancelling ? "Cancelling" : _status;
	string _status = "";
	public float Fraction { get; private set; }
	public IReadOnlyList<string> Lines => _lines;
	/// <summary>
	/// Completed job and geometry stage timings for this compile.
	/// </summary>
	public IReadOnlyList<SceneCompileStage> Stages => _stages;
	public IReadOnlyList<SceneCompileWarning> Warnings => _warnings;
	/// <summary>
	/// Geometry results and output totals from the most recent compile.
	/// </summary>
	public string[] Summary { get; internal set; }
	public SceneCompileStatistics Statistics { get; internal set; }
	internal CancellationToken Cancel => _cancel.Token;
	public event Action Changed;

	public bool CanCompile => !Running && !Game.IsPlaying && _sources?.Asset is not null
		&& _scanError is null && _settingsError is null
		&& SceneEditorSession.Active is { IsPrefabSession: false, IsMounted: false, HasUnsavedChanges: false } editor
		&& editor.Scene == Scene;

	public float AggregateCost
	{
		get => _settings.AggregateCost;
		set => Settings = _settings with { AggregateCost = value };
	}

	public float MaxChunkSize
	{
		get => _settings.MaxChunkSize;
		set => Settings = _settings with { MaxChunkSize = value };
	}

	public void ResetSettings() => Settings = new();

	SceneCompilerSettings Settings
	{
		set
		{
			if ( Running )
				throw new InvalidOperationException( "Cannot change settings while compiling." );

			value.Validate();
			_settings = value;
			_settingsError = null;
			Notify();
		}
	}

	SceneCompileSession() => EditorEvent.Register( this );

	public static bool HasCurrentBake( Asset asset )
	{
		var path = asset?.GetSourceFile( true );
		if ( string.IsNullOrEmpty( path ) )
			return false;

		var source = FileStamp( path );
		var binary = FileStamp( path + "_d" );
		var bakePath = SceneResourceCompiler.GetBakePath( path );
		var bake = FileStamp( bakePath );
		if ( SourceStatuses.TryGetValue( asset, out var cached ) && cached.Path == path
			&& cached.Source == source && cached.Binary == binary && cached.Bake == bake
			&& cached.FolderModified == Directory.GetLastWriteTimeUtc( cached.OutputFolder ) )
			return cached.IsCompiled;

		var current = SceneResourceCompiler.IsBakeCurrent( path, out var folder );
		var status = new SourceStatus( path, source, binary, bake, folder, Directory.GetLastWriteTimeUtc( folder ), current );
		SourceStatuses.AddOrUpdate( asset, status );
		return status.IsCompiled;
	}

	static (long Length, DateTime Modified) FileStamp( string path )
	{
		var file = new FileInfo( path );
		return file.Exists ? (file.Length, file.LastWriteTimeUtc) : (-1, default);
	}

	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( !Running && asset.Path == _path )
			Refresh();
	}

	[EditorEvent.Frame]
	void FollowActiveScene()
	{
		var scene = SceneEditorSession.Active?.Scene;
		if ( Running && (scene != Scene || Game.IsPlaying) )
			RequestCancel();
		if ( scene != Scene || scene?.Source?.ResourcePath != _path
			|| (scene?.Editor?.HasUnsavedChanges ?? false) != _unsaved || Game.IsPlaying != _playing )
			Refresh();
	}

	public void Refresh()
	{
		if ( Running )
			return;

		RefreshSources();
		Notify();
	}

	void ClearResult()
	{
		_result = null;
		_failure = null;
		_status = "";
		_phase = null;
		Summary = null;
		Statistics = null;
		HasResult = false;
		Fraction = 0;
		_lines = new();
		_warnings.Clear();
		_stages.Clear();
	}

	void RefreshSources( bool clean = false )
	{
		var editor = SceneEditorSession.Active;
		var scene = editor?.Scene;
		var path = scene?.Source?.ResourcePath;
		if ( scene != Scene || path != _path )
		{
			Scene = scene;
			_path = path;
			ClearResult();
			_settings = new();
			_settingsError = null;
			try
			{
				_settings = SceneCompilerSettings.Load( path is null ? null : AssetSystem.FindByPath( path ) );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException )
			{
				Log.Error( e, "Could not load scene compile settings" );
				_settingsError = e.Message;
			}
		}

		_unsaved = editor?.HasUnsavedChanges ?? false;
		_playing = Game.IsPlaying;
		_sources = null;
		_jobs.Clear();
		_runtimeWork = null;
		_outputs = null;
		WorkGroups = new Dictionary<string, int>();
		ClearableGroups = new HashSet<string>();
		_scanError = null;
		IsCompiled = false;
		if ( Game.IsPlaying || editor is not { IsPrefabSession: false } || !scene.IsValid() )
		{
			_scanError = Game.IsPlaying ? "Stop playing before compiling." : "Open a scene to compile.";
			return;
		}

		try
		{
			_sources = SceneCompiler.Scan( scene, out _scanError );
			DiscoverWork( clean );
			IsCompiled = !_unsaved && HasCurrentBake( _sources?.Asset );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException )
		{
			Log.Error( e, "Could not scan scene for compilation" );
			_scanError = e.Message;
		}
	}

	/// <summary>
	/// Runs all available work for the active saved scene.
	/// </summary>
	public Task StartAsync() => StartAsync( null );

	/// <summary>
	/// Runs the selected groups in order and streams their status to the compile window.
	/// </summary>
	public Task StartAsync( IReadOnlySet<string> selected ) => StartAsync( selected, CompileAction.Compile );

	/// <summary>
	/// Runs every available job in the selected stage.
	/// </summary>
	public Task RunStageAsync( string group ) => StartAsync( new HashSet<string> { group } );

	/// <summary>
	/// Deletes the scene's generated outputs and rebuilds every available stage.
	/// </summary>
	public Task CleanCompileAsync() => StartAsync( null, CompileAction.CleanCompile );

	/// <summary>
	/// Clears all generated scene work, including disabled components, without running any bake jobs.
	/// </summary>
	public Task ClearAsync() => StartAsync( null, CompileAction.Clear );

	/// <summary>
	/// Clears the named stage's declared outputs and bake references, including those on disabled components.
	/// </summary>
	public Task ClearAsync( string group ) => StartAsync( new HashSet<string> { group }, CompileAction.Clear );

	enum CompileAction { Compile, CleanCompile, Clear }

	async Task StartAsync( IReadOnlySet<string> selected, CompileAction action )
	{
		if ( Running )
			return;

		Running = true;
		_cancel.Dispose();
		_cancel = new();
		_elapsed = FastTimer.StartNew();
		try
		{
			RefreshSources( action == CompileAction.CleanCompile );
			ClearResult();
			if ( Error is { } error )
				throw new InvalidOperationException( error );
			if ( _sources?.Asset is null || _unsaved || SceneEditorSession.Active.IsMounted )
				throw new InvalidOperationException( "Save the scene before compiling." );

			var jobs = _jobs.Where( x => selected is null || selected.Contains( x.Work.Group ) ).ToArray();
			RunningGroups = WorkGroups.Keys.Where( g => (selected is null || selected.Contains( g ))
				&& ((action != CompileAction.Clear && WorkGroups[g] > 0)
					|| ((action is CompileAction.Clear or CompileAction.CleanCompile) && ClearableGroups.Contains( g ))) ).ToHashSet();
			if ( action != CompileAction.Clear && !jobs.Any( job => job.CanRun ) )
			{
				_status = "Nothing to compile";
				Running = false;
				Line( _status );
				return;
			}

			_status = "Preparing";
			Fraction = -1;
			Line( action == CompileAction.Clear ? $"Clearing {Name}" : $"Compiling {Name}" );
			Cancel.ThrowIfCancellationRequested();
			var summary = await RunWork( jobs, action, selected );
			Finish( action == CompileAction.Clear ? "Cleared" : "Done", summary );
		}
		catch ( OperationCanceledException )
		{
			Finish( "Cancelled" );
		}
		catch ( Exception e )
		{
			Log.Error( e, "Compile Scene failed" );
			_failure = e.Message;
			Line( e.Message );
			Finish( "Failed" );
		}
		finally
		{
			if ( action == CompileAction.Clear )
				Refresh();
		}
	}

	public void RequestCancel()
	{
		if ( !Running || Cancelling )
			return;

		_cancel.Cancel();
		Notify();
	}

	[EditorEvent.Hotload]
	[Event( "app.exit" )]
	void OnEditorReset() => RequestCancel();

	internal void Phase( string title )
	{
		EndPhase();
		_phase = title;
		_phaseElapsed = FastTimer.StartNew();
		_status = title;
		Fraction = -1;
		Line( title );
	}

	void EndPhase()
	{
		if ( _phase is null )
			return;

		var duration = TimeSpan.FromMilliseconds( _phaseElapsed.ElapsedMilliSeconds );
		_stages.Add( new( _phase, duration ) );
		_lines.Add( $"{_phase,-26}{duration.TotalSeconds,6:n2}s" );
		_phase = null;
	}

	internal void Step( int current, int total )
	{
		Fraction = total > 0 ? (float)current / total : -1;
		Notify();
	}

	internal void Line( string text )
	{
		_lines.Add( text );
		Notify();
	}

	internal void Warn( string message, Component component = null )
	{
		var target = Scene.IsValid() && component.IsValid() ? Scene.Directory.FindComponentByGuid( component.Id ) : null;
		_warnings.Add( new( message, target ) );
		Line( message );
	}

	void Finish( string title, string[] summary = null )
	{
		if ( summary is null && IsCompiled && _sources?.Asset?.GetSourceFile( true ) is { Length: > 0 } sourcePath )
			IsCompiled = File.Exists( SceneResourceCompiler.GetBakePath( sourcePath ) );

		EndPhase();
		_status = title;
		Summary = summary;
		Fraction = 1;
		HasResult = true;
		_result = _sources?.Report;
		var duration = TimeSpan.FromMilliseconds( _elapsed.ElapsedMilliSeconds );
		if ( Statistics is not null )
		{
			Statistics.CompletedAt = DateTimeOffset.Now;
			Statistics.Duration = duration;
			Statistics.Stages = Array.AsReadOnly( _stages.ToArray() );
		}
		_lines.Add( $"{title} in {duration.TotalSeconds:n2}s" );
		Running = false;
		var name = Name;
		var detail = summary is not null ? string.Join( "\n", summary ) : Error ?? title;
		Notify();
		EditorEvent.Run( "scene.compile.finished", name, title, detail );
	}

	void Notify()
	{
		if ( _notifying )
			return;

		_notifying = true;
		try
		{
			Changed?.Invoke();
		}
		finally
		{
			_notifying = false;
		}
	}
}

public sealed record SceneCompileReport( string Name, int MeshCount, int PropCount, IReadOnlyList<SceneCompileSkip> Skipped );

public sealed record SceneCompileSkip( Component Component, string Label, SceneCompileSkipReason Reason );

public sealed record SceneCompileWarning( string Message, Component Component );
