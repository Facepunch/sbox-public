using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Editor;

public sealed class SceneCompileSession
{
	public static SceneCompileSession Current { get; } = new();

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
	public string Status => Cancelling ? "Cancelling" : _status;
	string _status = "";
	public float Fraction { get; private set; }
	public IReadOnlyList<string> Lines => _lines;
	public IReadOnlyList<SceneCompileWarning> Warnings => _warnings;
	public string[] Summary { get; private set; }
	public SceneCompileStatistics Statistics { get; internal set; }
	internal CancellationToken Cancel => _cancel.Token;
	public event Action Changed;

	public bool CanCompile => !Running && !Game.IsPlaying && _sources?.Asset is not null
		&& _scanError is null && _settingsError is null
		&& SceneEditorSession.Active is { IsPrefabSession: false, HasUnsavedChanges: false } editor
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

	[EditorEvent.Frame]
	void FollowActiveScene()
	{
		var scene = SceneEditorSession.Active?.Scene;
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

	void RefreshSources()
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
		_scanError = null;
		if ( Game.IsPlaying || editor is not { IsPrefabSession: false } || !scene.IsValid() )
		{
			_scanError = Game.IsPlaying ? "Stop playing before compiling." : "Open a scene to compile.";
			return;
		}

		try
		{
			_sources = SceneCompiler.Scan( scene, out _scanError );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException )
		{
			Log.Error( e, "Could not scan scene for compilation" );
			_scanError = e.Message;
		}
	}

	public async Task StartAsync()
	{
		if ( Running )
			return;

		Running = true;
		_cancel.Dispose();
		_cancel = new();
		_elapsed = FastTimer.StartNew();
		try
		{
			RefreshSources();
			ClearResult();
			if ( Error is { } error )
				throw new InvalidOperationException( error );
			if ( _sources?.Asset is null || _unsaved )
				throw new InvalidOperationException( "Save the scene before compiling." );

			if ( !HasCompileGeometry )
			{
				_status = "Nothing to compile";
				Running = false;
				Line( _status );
				return;
			}

			_status = "Preparing";
			Fraction = -1;
			Line( $"{Report.MeshCount} meshes, {Report.PropCount} props to compile" );
			Cancel.ThrowIfCancellationRequested();
			var summary = await SceneCompiler.Compile( _sources, _settings, this );
			Finish( "Done", summary );
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
		Notify();
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
