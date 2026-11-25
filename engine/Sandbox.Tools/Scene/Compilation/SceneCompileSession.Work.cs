using Sandbox.Helpers;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Editor;

public sealed partial class SceneCompileSession
{
	readonly List<CompileJob> _jobs = new();
	SceneCompileWork _runtimeWork;

	internal readonly record struct CompileJob( ISceneCompile Owner, SceneCompileWork Work )
	{
		internal bool CanRun => Work.Run is not null && Owner is not Component { Active: false };

		// System instances get a fresh GUID on load. There is one instance per type in a scene.
		internal string Key => Owner is Component c ? c.Id.ToString() : Owner?.GetType().FullName;
	}

	/// <summary>
	/// Available stages and the number of runnable jobs in each. A stage with zero jobs can only be cleared.
	/// </summary>
	public IReadOnlyDictionary<string, int> WorkGroups { get; private set; } = new Dictionary<string, int>();

	/// <summary>
	/// Stages with a cleanup callback or generated files to remove.
	/// </summary>
	public IReadOnlySet<string> ClearableGroups { get; private set; } = new HashSet<string>();

	/// <summary>
	/// The groups chosen for the running or most recent compile.
	/// </summary>
	public IReadOnlySet<string> RunningGroups { get; private set; } = new HashSet<string>();

	internal static IEnumerable<CompileJob> DiscoverJobs( Scene scene, SceneCompileWork runtimeWork )
	{
		var owners = scene.Components.GetAll<ISceneCompile>( FindMode.EverythingInSelfAndDescendants )
			.Concat( scene.GetSystems().OfType<ISceneCompile>() );

		var jobs = owners.Select( owner => new CompileJob( owner, Describe( owner ) ) )
			.Where( job => job.Work is not null )
			.OrderBy( job => job.Work.Order )
			.ThenBy( job => job.Key, StringComparer.Ordinal );

		// The runtime scene snapshots the results of every other job, regardless of user-defined order.
		return jobs.Append( new CompileJob( null, runtimeWork ) );

		static SceneCompileWork Describe( ISceneCompile owner )
		{
			try
			{
				var work = owner.GetWork();
				if ( work is not null && string.IsNullOrWhiteSpace( work.Group ) )
					throw new InvalidOperationException( "The compile job needs a group name." );

				return work;
			}
			catch ( Exception e )
			{
				var name = owner is Component c ? $"{owner.GetType().Name} on '{c.GameObject.Name}'" : owner.GetType().FullName;
				throw new InvalidOperationException( $"Could not get compile work from {name}: {e.Message}", e );
			}
		}
	}

	void DiscoverWork( bool clean )
	{
		var asset = _sources?.Asset;
		var source = asset?.GetSourceFile( true );
		if ( string.IsNullOrEmpty( source ) ) return;

		_outputs = new( source );
		_runtimeWork = SceneCompiler.GetWork( _sources, _settings, this, _outputs, clean );
		foreach ( var job in DiscoverJobs( Scene, _runtimeWork ) )
		{
			_outputs.Observe( job.Work.Group, job.Work.Outputs );
			if ( job.CanRun || job.Work.Clear is not null )
				_jobs.Add( job );
		}

		var clearable = _outputs.Groups.ToHashSet();
		clearable.UnionWith( _jobs.Where( j => j.Work.Clear is not null ).Select( j => j.Work.Group ) );
		var groups = _jobs.GroupBy( j => j.Work.Group ).ToDictionary( g => g.Key, g => g.Count( job => job.CanRun ) );
		foreach ( var group in clearable )
			groups.TryAdd( group, 0 );
		ClearableGroups = clearable;
		WorkGroups = groups;
	}

	async Task<string[]> RunWork( CompileJob[] jobs, CompileAction action, IReadOnlySet<string> selected )
	{
		var editor = SceneEditorSession.Active;
		var asset = _sources.Asset;
		var path = asset.GetSourceFile( true );
		var clean = action is CompileAction.CleanCompile or CompileAction.Clear;
		var sourceHash = SceneResourceCompiler.GetSourceHash( path );
		var revision = editor.UndoSystem.DocumentRevision;

		if ( clean )
		{
			await EditorContentBuild.WaitForCurrentBuild( Cancel );
			CheckScene();
			CheckSource();
			Phase( selected is null ? "Clearing compiled outputs" : $"Clearing {string.Join( ", ", selected )}" );
			foreach ( var job in jobs )
			{
				CheckScene();
				job.Work.Clear?.Invoke( _outputs.CleanupFor( job.Work.Group ) );
			}

			CheckScene();
			CheckSource();
			await SaveBakeReferences();
			_outputs.Clear( selected, asset.Path );
			IsCompiled = HasCurrentBake( asset );

			if ( action == CompileAction.Clear )
				return LogOutputs();
		}

		foreach ( var job in jobs.Where( j => j.CanRun ) )
		{
			CheckScene();
			var work = job.Work;
			if ( ReferenceEquals( work, _runtimeWork ) )
			{
				CheckSource();
				await SaveBakeReferences();
			}

			Phase( work.Title );

			using var context = new CompileContext( this, clean, work.Group );
			await work.Run( context );
			CheckScene();
			_outputs.Observe( work.Group, work.Outputs );
		}

		CheckScene();
		CheckSource();
		await SaveBakeReferences();

		IsCompiled = HasCurrentBake( asset );
		return [.. Summary ?? [], .. LogOutputs()];

		string[] LogOutputs()
		{
			EndPhase();
			var outputs = OutputSummary( asset );
			Line( "Scene outputs on disk" );
			foreach ( var line in outputs ) Line( line );
			return outputs;
		}

		async Task SaveBakeReferences()
		{
			if ( !editor.HasUnsavedChanges )
				return;

			Phase( "Saving bake references" );
			await EditorContentBuild.WaitForCurrentBuild( Cancel );
			CheckScene();
			CheckSource();
			editor.SaveBakeReferences( revision );
			if ( editor.HasUnsavedChanges )
				throw new InvalidOperationException( "Could not save the scene's bake references." );

			sourceHash = SceneResourceCompiler.GetSourceHash( path );

			if ( selected is not null && !selected.Contains( SceneCompiler.Group ) && IsCompiled && !HasCurrentBake( asset ) )
				Warn( "Bake references changed. Recompile geometry and collision to update the runtime scene." );
		}

		void CheckSource()
		{
			if ( !sourceHash.SequenceEqual( SceneResourceCompiler.GetSourceHash( path ) ) )
				throw new InvalidOperationException( "The saved scene changed during compilation. Save and compile again." );
		}

		void CheckScene()
		{
			Cancel.ThrowIfCancellationRequested();
			if ( !Scene.IsValid() || editor != SceneEditorSession.Active || Game.IsPlaying || revision != editor.UndoSystem.DocumentRevision )
				throw new InvalidOperationException( "The scene changed during compilation. Save and compile again." );
		}
	}

	sealed class CompileContext( SceneCompileSession session, bool clean, string group ) : ICompileContext, IProgressSection
	{
		bool ICompileContext.IsClean => clean;
		IProgressSection ICompileContext.Progress => this;
		CancellationToken ICompileContext.CancellationToken => session.Cancel;
		void ICompileContext.Log( string message ) => session.Line( message );
		void ICompileContext.AddOutput( string relativePath ) => session._outputs.Observe( group, [relativePath] );

		double current;
		int lastLogged;
		string title;
		string subtitle;
		string IProgressSection.Icon { get; set; }
		string IProgressSection.Title
		{
			get => title;
			set
			{
				title = value;
				session._status = value ?? session._phase;
				session.Notify();
			}
		}

		string IProgressSection.Subtitle
		{
			get => subtitle;
			set
			{
				subtitle = value;
				session._status = value ?? title ?? session._phase;
				session.Notify();
			}
		}

		/// <inheritdoc/>
		public double TotalCount { get; set; }
		/// <inheritdoc/>
		public double Current
		{
			get => current;
			set
			{
				current = value;
				if ( TotalCount <= 0 ) return;
				session.Fraction = (float)Math.Clamp( value / TotalCount, 0, 1 );
				int tenth = (int)(session.Fraction * 10);
				if ( tenth > lastLogged && !string.IsNullOrWhiteSpace( subtitle ) )
					session.Line( subtitle );
				else
					session.Notify();
				lastLogged = tenth;
			}
		}

		CancellationToken IProgressSection.GetCancel() => session.Cancel;
		void IProgressSection.Cancel() => session.RequestCancel();
		/// <inheritdoc/>
		public void Dispose() { }
	}
}
