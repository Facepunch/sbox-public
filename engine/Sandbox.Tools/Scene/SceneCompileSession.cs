using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Sandbox;

namespace Editor;

/// <summary>
/// Owns scene compilation independently of the controls displaying its progress.
/// </summary>
public sealed class SceneCompileSession
{
	/// <summary>
	/// The shared editor compile job, independent of any toolbar, popup, or report window.
	/// </summary>
	public static SceneCompileSession Current { get; } = new();

	List<string> _lines = new();
	readonly List<SceneCompileStage> _stages = new();
	SceneCompilerSettings _settings = new();
	SceneCompiler.Sources _sources;
	SceneCompileReport _sourceReport;
	SceneCompileReport _resultReport;
	string _path;
	string _settingsError;
	string _scanError;
	string _failure;
	string _status;
	string _phase;
	bool _unsaved;
	bool _playing;
	bool _notifying;
	bool _savedCompilationDirty;
	CancellationTokenSource _cancel = new();
	FastTimer _elapsed;
	FastTimer _phaseElapsed;

	/// <summary>
	/// The selected source scene, pinned to the job's scene while compilation is running.
	/// </summary>
	public Scene Scene { get; private set; }

	/// <summary>
	/// The source scene's display name.
	/// </summary>
	public string Name { get; private set; } = "No scene";

	/// <summary>
	/// Source information for the latest result, or the current preview before a compile.
	/// Null when no sources are available.
	/// </summary>
	public SceneCompileReport Report => HasResult ? _resultReport : _sourceReport;

	/// <summary>
	/// Whether source information is available to display in a report.
	/// </summary>
	public bool HasSources => Report is not null;

	public bool HasCompileGeometry => _sources?.HasCompileGeometry == true;

	public bool HasCompilation { get; private set; }

	public bool NeedsCompilation => !HasCompilation || _savedCompilationDirty
		|| Scene?.Editor is SceneEditorSession { CompilationDirty: true };

	/// <summary>
	/// A settings, source-scan, or compile error, or null when none has been recorded.
	/// </summary>
	public string Error => _settingsError ?? _scanError ?? _failure;

	/// <summary>
	/// Whether the job is still running, including cancellation and cleanup.
	/// </summary>
	public bool Running { get; private set; }

	/// <summary>
	/// Whether cancellation has been requested and the job has not finished yet.
	/// </summary>
	public bool Cancelling => Running && _cancel.IsCancellationRequested;

	/// <summary>
	/// Whether this session contains a completed, failed, or cancelled result.
	/// </summary>
	public bool HasResult { get; private set; }

	/// <summary>
	/// The running phase or final result, or an empty string before starting.
	/// </summary>
	public string Status => Cancelling ? "Cancelling" : _status ?? "";

	/// <summary>
	/// Progress through the current phase, rather than an estimate of total compile time.
	/// Negative when the phase has no measurable total.
	/// </summary>
	public float Fraction { get; private set; }

	/// <summary>
	/// The retained compile log, in append order.
	/// </summary>
	public IReadOnlyList<string> Lines => _lines;

	/// <summary>
	/// Successful compilation's summary lines, or null when no summary was produced.
	/// </summary>
	public string[] Summary { get; private set; }

	/// <summary>
	/// Measurements for the displayed successful compile, or null when none are available.
	/// </summary>
	public SceneCompileStatistics Statistics { get; internal set; }

	/// <summary>
	/// The cancellation token observed by the compiler for the current job.
	/// </summary>
	internal CancellationToken Cancel => _cancel.Token;

	/// <summary>
	/// Raised when settings, source information, progress, or log output changes.
	/// Subscribers must unsubscribe when their views are destroyed.
	/// </summary>
	public event Action Changed;

	/// <summary>
	/// Whether the selected saved scene can start a compile with the current settings.
	/// </summary>
	public bool CanCompile => !Running && _sources?.Asset is not null
		&& _settingsError is null && _scanError is null && EligibilityError() is null;

	/// <summary>
	/// An extra aggregate's cost in fragments. Higher values favor fewer, larger aggregates.
	/// Changes are saved only when compiling.
	/// </summary>
	/// <exception cref="InvalidDataException">The value is not finite and positive.</exception>
	/// <exception cref="InvalidOperationException">A compile is running.</exception>
	public float AggregateCost
	{
		get => Settings.AggregateCost;
		set => Settings = Settings with { AggregateCost = value };
	}

	/// <summary>
	/// The maximum geometry chunk size before subdivision.
	/// Changes are saved only when compiling.
	/// </summary>
	/// <exception cref="InvalidDataException">The value is not finite and positive.</exception>
	/// <exception cref="InvalidOperationException">A compile is running.</exception>
	public float MaxChunkSize
	{
		get => Settings.MaxChunkSize;
		set => Settings = Settings with { MaxChunkSize = value };
	}

	/// <summary>
	/// Reset compile settings to built-in defaults without saving them.
	/// </summary>
	/// <exception cref="InvalidOperationException">A compile is running.</exception>
	public void ResetSettings() => Settings = new();

	internal SceneCompilerSettings Settings
	{
		get => _settings;
		set
		{
			if ( Running )
				throw new InvalidOperationException( "Cannot change settings while compiling." );

			ArgumentNullException.ThrowIfNull( value );
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
		if ( Running )
			return;

		var scene = SceneEditorSession.Active?.Scene;
		if ( scene != Scene || scene?.Source?.ResourcePath != _path
			|| (scene?.Editor?.HasUnsavedChanges ?? false) != _unsaved || Game.IsPlaying != _playing )
			Refresh();
	}

	internal void OnSceneEdited( Scene scene )
	{
		if ( scene != Scene )
			return;

		Refresh();
	}

	/// <summary>
	/// Rescan the active editor scene when idle. Preserve its draft settings and completed
	/// result unless the selected scene or source path has changed.
	/// </summary>
	public void Refresh()
	{
		if ( Running )
			return;

		RefreshSources();
		Notify();
	}

	void ClearResult()
	{
		_failure = null;
		_status = null;
		_phase = null;
		Summary = null;
		Statistics = null;
		HasResult = false;
		_resultReport = null;
		Fraction = 0;
		_lines = new();
		_stages.Clear();
	}

	void RefreshSources()
	{
		var scene = SceneEditorSession.Active?.Scene;
		var path = scene?.Source?.ResourcePath;
		if ( scene != Scene || path != _path )
		{
			Scene = scene;
			_path = path;
			_settingsError = null;
			HasCompilation = false;
			ClearResult();
			_settings = new();

			try
			{
				var asset = path is null ? null : AssetSystem.FindByPath( path );
				_settings = SceneCompilerSettings.Load( asset );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException )
			{
				Log.Error( e, "Could not load scene compile settings" );
				_settingsError = $"Could not load scene compile settings: {e.Message}";
			}
		}

		Name = string.IsNullOrEmpty( path ) ? scene?.Name ?? "No scene" : Path.GetFileNameWithoutExtension( path );
		_unsaved = scene?.Editor?.HasUnsavedChanges ?? false;
		_playing = Game.IsPlaying;
		_scanError = EligibilityError( requireSaved: false );
		_sources = null;
		_sourceReport = null;
		if ( _scanError is not null )
			return;

		try
		{
			_sources = SceneCompiler.Scan( Scene, out _scanError );
			HasCompilation = SceneCompileCache.HasCompilation( _sources?.Asset );
			_savedCompilationDirty = HasCompilation && SceneCompileCache.IsDirty( _sources.Asset );
			_sourceReport = _sources?.Report;
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException )
		{
			Log.Error( e, "Could not scan scene for compilation" );
			_scanError = $"Could not scan scene for compilation: {e.Message}";
		}
	}

	string EligibilityError( bool requireSaved = true )
	{
		if ( Game.IsPlaying )
			return "Stop playing before compiling the scene.";
		if ( !Scene.IsValid() )
			return "No scene is open.";
		if ( SceneEditorSession.Active is not { IsPrefabSession: false } active || active.Scene != Scene )
			return "Open a scene rather than a prefab to compile.";
		if ( Scene.Source?.ResourcePath != _path )
			return "The scene moved. Refresh before compiling it.";
		if ( requireSaved && string.IsNullOrEmpty( _path ) )
			return "Save the scene before compiling it.";
		if ( requireSaved && (Scene.Editor is null || Scene.Editor.HasUnsavedChanges) )
			return "Save the scene, then use Scene > Compile Scene. Unsaved changes cannot be compiled.";

		return null;
	}

	/// <summary>
	/// Refresh the source scan and compile the active scene. Errors and cancellation are
	/// retained in the session; invalid startup input also requests an error report.
	/// Every request rebuilds the saved scene, even when its inputs and settings have not changed.
	/// </summary>
	/// <returns>A task that completes after the job and its cleanup finish.</returns>
	public async Task StartAsync()
	{
		if ( Running )
		{
			Line( Cancelling ? "Cancellation is already requested." : "A scene compile is already running." );
			return;
		}

		// Lock before notifications or pumping can re-enter through another compile control.
		Running = true;
		_cancel.Dispose();
		_cancel = new();
		_elapsed = FastTimer.StartNew();
		var enteredCompiler = false;

		try
		{
			RefreshSources();
			var nothingToCompile = _settingsError is null && _scanError is null && _sources is { HasCompileGeometry: false };
			ClearResult();
			_status = nothingToCompile ? "Nothing to compile" : "Preparing";
			Fraction = nothingToCompile ? 0 : -1;

			if ( Error is { } error )
				throw new InvalidOperationException( error );
			if ( EligibilityError() is { } eligibilityError )
				throw new InvalidOperationException( eligibilityError );
			if ( _sources?.Asset is null )
				throw new InvalidOperationException( "There is no saved scene to compile." );

			if ( nothingToCompile )
			{
				SceneCompileCache.ClearCompilation( _sources.Asset, Scene.Id );
				HasCompilation = false;
				_lines.Add( _status );
				Running = false;
				Notify();
				return;
			}

			_settings.Validate();
			_lines.Add( $"{_sourceReport.MeshCount} meshes, {_sourceReport.PropCount} props to compile" );
			Notify();
			Cancel.ThrowIfCancellationRequested();
			enteredCompiler = true;
			var result = await SceneCompiler.Compile( _sources, _settings, this );
			HasCompilation = true;
			_savedCompilationDirty = SceneCompileCache.IsDirty( _sources.Asset );
			Finish( "Done", result );
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

			if ( !enteredCompiler )
				EditorEvent.Run( "scene.compile.show-report", "Report" );
		}
	}

	/// <summary>
	/// Request cancellation without marking the job complete before its cleanup finishes.
	/// </summary>
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

	/// <summary>
	/// Begin a compiler phase, recording the elapsed time of the previous phase.
	/// </summary>
	/// <param name="title">The phase's display name.</param>
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
		_stages.Add( new SceneCompileStage( _phase, duration ) );
		_lines.Add( $"{_phase,-26}{duration.TotalSeconds,6:n2}s" );
		_phase = null;
	}

	/// <summary>
	/// Update progress through the current compiler phase.
	/// </summary>
	/// <param name="current">The number of completed items.</param>
	/// <param name="total">The number of items in the phase, or zero when unknown.</param>
	internal void Step( int current, int total )
	{
		Fraction = total > 0 ? (float)current / total : -1;
		Notify();
	}

	/// <summary>
	/// Append a line to the retained compile log and notify its views.
	/// </summary>
	/// <param name="text">The text to append.</param>
	internal void Line( string text )
	{
		_lines.Add( text );
		Notify();
	}

	/// <summary>
	/// Finalize the job after compiler cleanup and emit its completion notification once.
	/// </summary>
	/// <param name="title">The result: Done, Failed, or Cancelled.</param>
	/// <param name="summary">Summary lines produced by a successful compile.</param>
	void Finish( string title, string[] summary = null )
	{
		if ( !Running )
			return;

		EndPhase();
		_status = title;
		Summary = summary;
		Fraction = 1;
		HasResult = true;
		_resultReport = _sourceReport;
		var duration = TimeSpan.FromMilliseconds( _elapsed.ElapsedMilliSeconds );
		if ( summary is not null && Statistics is not null )
		{
			Statistics.CompletedAt = DateTimeOffset.Now;
			Statistics.Duration = duration;
			Statistics.Stages = Array.AsReadOnly( _stages.ToArray() );
		}
		else
		{
			Statistics = null;
		}
		_lines.Add( $"{title} in {duration.TotalSeconds:n2}s" );
		Running = false;

		var name = Name;
		var detail = title switch
		{
			"Done" => string.Join( "\n", summary ?? [] ),
			"Failed" => Error ?? "Scene compilation failed.",
			_ => "The previous compiled scene is unchanged."
		};
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

/// <summary>
/// Read-only source information for compile reports, independent of compiler internals.
/// </summary>
/// <param name="Name">The source scene's display name.</param>
/// <param name="MeshCount">The number of eligible mesh components.</param>
/// <param name="PropCount">The number of eligible prop renderers.</param>
/// <param name="Skipped">Components excluded from compilation and their reasons.</param>
public sealed record SceneCompileReport( string Name, int MeshCount, int PropCount, IReadOnlyList<SceneCompileSkip> Skipped );

/// <summary>
/// A component left unchanged by compilation and the reason it was skipped.
/// </summary>
/// <param name="Component">The original source component, which may later be destroyed.</param>
/// <param name="Label">The category of source geometry.</param>
/// <param name="Reason">Why the component was excluded from compilation.</param>
public sealed record SceneCompileSkip( Component Component, string Label, SceneCompileSkipReason Reason );
