using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Sandbox;

namespace Editor;

/// <summary>
/// Compiles a scene's static mesh geometry into the source asset's generated runtime representation.
/// The editable scene is never rewritten by this compiler.
/// </summary>
internal static partial class SceneCompiler
{
	static bool _running;

	/// <summary>
	/// What a compile is going to work on, worked out up front so it can be shown before anything
	/// is built.
	/// </summary>
	internal sealed class Sources
	{
		public Scene Scene { get; init; }
		public Asset Asset { get; init; }
		public SceneCompileReport Report { get; init; }
		public bool HasCompileGeometry { get; init; }
	}

	[Menu( "Editor", "Scene/View Compile Report", "list", Priority = 1002 )]
	public static void ViewCompileReport() => EditorEvent.Run( "scene.compile.show-report", "Report" );

	/// <summary>
	/// Everything the active scene has to compile, and why the rest is being left alone.
	/// Reports remain available when saving is required before compilation.
	/// </summary>
	internal static Sources Scan( Scene scene, out string error )
	{
		error = null;

		if ( !scene.IsValid() )
		{
			error = "No scene is open.";
			return null;
		}

		var sources = DiscoverSources( scene ).ToArray();
		var hasCompileGeometry = sources.Any( x => x.NeedsCompilation );
		var asset = scene.Source is null ? null : AssetSystem.FindByPath( scene.Source.ResourcePath );

		if ( hasCompileGeometry && (scene.Editor is null || scene.Editor.HasUnsavedChanges) )
		{
			error = "Save the scene, then use Scene > Compile Scene. Unsaved changes cannot be compiled.";
		}
		else if ( hasCompileGeometry && asset is null )
		{
			error = "Save the scene before compiling it.";
		}
		else if ( asset is not null && string.IsNullOrEmpty( asset.GetSourceFile( true ) ) )
		{
			error = "Save a local copy of this scene before compiling it.";
		}

		var skipped = sources
			.Where( x => x.Component.Active && x.SkipReason != SceneCompileSkipReason.None )
			.Select( x => new SceneCompileSkip( x.Component, x.Label, x.SkipReason ) )
			.ToArray();

		return new Sources
		{
			Scene = scene,
			Asset = asset,
			Report = new SceneCompileReport(
				asset is null ? scene.Name : Path.GetFileNameWithoutExtension( asset.AbsolutePath ),
				Gather<MeshComponent>( sources ).Count(), Gather<ModelRenderer>( sources ).Count(), Array.AsReadOnly( skipped ) ),
			HasCompileGeometry = hasCompileGeometry,
		};
	}

	/// <summary>
	/// Compile geometry in the editor, yielding between steps while preserving native thread affinity.
	/// </summary>
	internal static async Task<string[]> Compile( Sources sources, SceneCompilerSettings settings, SceneCompileSession session )
	{
		if ( _running )
			throw new InvalidOperationException( "A scene compile is already running." );

		ArgumentNullException.ThrowIfNull( settings );
		settings.Validate();
		var generation = Guid.NewGuid().ToString( "N" );
		var sourcePath = sources.Asset.GetSourceFile( true );
		if ( string.IsNullOrEmpty( sourcePath ) )
			throw new InvalidOperationException( "Save a local copy of this scene before compiling it." );

		_running = true;
		string[] result = null;
		Scene compiled = null;

		try
		{
			var scene = sources.Scene;
			if ( !scene.IsValid() || Game.IsPlaying || scene.Editor is SceneEditorSession { IsPrefabSession: true } )
				throw new InvalidOperationException( "Stop playing and open a scene rather than a prefab before compiling." );

			if ( scene.Editor is not SceneEditorSession editor || editor.HasUnsavedChanges )
				throw new InvalidOperationException( "Save the scene, then use Scene > Compile Scene. Unsaved changes cannot be compiled." );

			var editVersion = editor.EditVersion;
			session.Cancel.ThrowIfCancellationRequested();
			var sceneFolder = scene.Editor.GetSceneFolder()
				?? throw new InvalidOperationException( "This scene has nowhere to write its compiled resources." );
			session.Phase( "Copying scene" );
			var sourceFile = scene.CreateSceneFile();

			// A game scene would also load the project's system scene and network spawns.
			compiled = Scene.CreateEditorScene();
			using ( compiled.Push() )
			{
				sourceFile.ActionGraphCache.Clear();
				if ( !compiled.Load( sourceFile ) )
					throw new InvalidOperationException( "Could not load the editable scene for compilation." );
			}

			SceneCompileCache.BeginGeneration( sources.Asset, generation );
			result = await Run( sources.Asset, sceneFolder, compiled, sourceFile.Id, sourcePath, settings, session, generation );
			var dirty = !scene.IsValid() || editor.EditVersion != editVersion;
			SceneCompileCache.WriteSetting( sources.Asset, SceneCompileCache.DirtyProperty, JsonValue.Create( dirty ) );
			editor.CompilationDirty = dirty;
		}
		finally
		{
			try
			{
				compiled?.Destroy();
			}
			finally
			{
				try
				{
					if ( result is null )
						SceneCompileCache.DiscardGeneration( sourcePath, generation );
				}
				finally
				{
					_running = false;
				}
			}
		}

		return result;
	}

	static async Task<string[]> Run( Asset sourceAsset, SceneFolder sceneFolder, Scene compiled, Guid sceneId, string sourcePath,
		SceneCompilerSettings settings, SceneCompileSession session, string generation )
	{
		var outputFolder = $"/compiled/{generation}";
		var resourceFolder = $"{System.IO.Path.ChangeExtension( sourceAsset.Path, null )}_scene_data{outputFolder}";
		var discovered = DiscoverSources( compiled ).ToArray();
		var meshes = Gather<MeshComponent>( discovered ).ToArray();
		var props = Gather<ModelRenderer>( discovered ).ToArray();

		var frame = FastTimer.StartNew();

		// Pumping the editor costs more than most of the work between two steps, so we only do it
		// on a frame's cadence rather than for every item.
		async Task Step( int current, int total )
		{
			session.Cancel.ThrowIfCancellationRequested();

			if ( frame.ElapsedMilliSeconds < 30 )
				return;

			session.Step( current, total );

			await Task.Delay( 1, session.Cancel );

			frame = FastTimer.StartNew();
		}

		session.Phase( "Compiling geometry" );
		await Task.Delay( 1, session.Cancel );

		var processed = new HashSet<Guid>();
		var plan = await Plan( meshes, props, processed, settings, Step, session.Cancel );

		var plans = plan.Aggregates;
		var statistics = new SceneCompileStatistics();

		session.Phase( "Building models" );

		var aggregates = new (Model Model, List<AggregateFragmentInfo> Fragments)[plans.Length];

		for ( int i = 0; i < plans.Length; i++ )
		{
			var (model, fragments) = Build( plans[i], $"{resourceFolder}/aggregate_{i}.vmdl", statistics );

			model = WriteModel( sceneFolder, $"{outputFolder}/aggregate_{i}.vmdl_c", model );
			if ( !model.IsValid() || model.IsError )
				throw new InvalidOperationException( $"Could not load compiled aggregate model {i}." );

			aggregates[i] = (model, fragments);
			await Step( i + 1, plans.Length );
		}

		session.Phase( "Building collision" );
		await Task.Delay( 1, session.Cancel );

		var collision = await BuildCollision( plan.Collision, plan.Shapes, sceneFolder, outputFolder, Step );

		var converted = 0;
		SceneFile file = null;

		var leftovers = compiled.Components.GetAll<MeshComponent>( FindMode.EverythingInSelfAndDescendants )
			.Where( mesh => !processed.Contains( mesh.Id ) )
			.ToArray();

		if ( leftovers.Length > 0 )
		{
			session.Phase( $"Converting {leftovers.Length} meshes" );
			await Task.Delay( 1, session.Cancel );

			converted = await ConvertMeshes( compiled, leftovers, sceneFolder, outputFolder, resourceFolder, statistics, Step );
			processed.UnionWith( leftovers.Select( mesh => mesh.Id ) );
		}

		session.Phase( "Stripping compiled geometry" );
		await Task.Delay( 1, session.Cancel );

		using ( compiled.Push() )
		{
			// Unlink affected prefabs before stripping their source
			// components so those components cannot return when the prefab expands again.
			foreach ( var go in compiled.Children.ToArray() )
			{
				Unlink( go, processed );
			}

			StripCompiled( compiled, processed );

			session.Phase( "Building objects" );

			GameObject root = null;

			// Nothing under here is meant to be touched by hand - the next compile throws it all
			// away and builds it again, so keep it out of the hierarchy and out of selection.
			if ( plans.Length > 0 || collision.Count > 0 )
			{
				root = compiled.CreateObject();
				root.Name = "World";
				root.IsStatic = true;
				root.Flags |= GameObjectFlags.Hidden;
			}

			for ( int i = 0; i < plans.Length; i++ )
			{
				var go = compiled.CreateObject();
				go.SetParent( root );
				go.Flags |= GameObjectFlags.Hidden;
				ApplyTags( go, plans[i].Tags );

				// Aggregates are an opaque path, so translucent geometry is compiled into a model
				// and drawn like any other model instead.
				if ( plans[i].Translucent )
				{
					go.Name = $"Translucent {i}";
					go.LocalTransform = plans[i].Transform;

					var model = go.AddComponent<ModelRenderer>();
					model.Model = aggregates[i].Model;
					model.Tint = plans[i].Tint;

					continue;
				}

				go.Name = $"Aggregate {i}";

				var renderer = go.AddComponent<AggregateRenderer>();
				renderer.Model = aggregates[i].Model;
				renderer.Tint = plans[i].Tint;
				renderer.Fragments = aggregates[i].Fragments;
			}

			for ( int i = 0; i < collision.Count; i++ )
			{
				var go = compiled.CreateObject();
				go.Name = $"Collision {i}";
				go.SetParent( root );
				go.Flags |= GameObjectFlags.Hidden;
				ApplyTags( go, collision[i].Tags );

				var collider = go.AddComponent<PhysicsCollider>();
				collider.Physics = collision[i].Physics;
				collider.Static = true;
			}

			if ( compiled.Components.GetAll<MeshComponent>( FindMode.EverythingInSelfAndDescendants ).FirstOrDefault() is { } remainingMesh )
				throw new InvalidOperationException( $"Cannot publish the compiled scene: mesh '{remainingMesh.GameObject.Name}' was not converted. Compiled scenes cannot contain MeshComponents." );

			file = new SceneFile();
			compiled.ToSceneFile( file );
			file.Id = sceneId;
		}

		session.Phase( "Writing runtime scene" );
		SceneCompileCache.Publish( sourceAsset, sourcePath, generation, file, settings, session.Cancel );
		settings.SaveDefaults();
		session.Statistics = statistics;

		var translucent = plans.Count( x => x.Translucent );
		var aggregateCount = plans.Length - translucent;
		var summary = new List<string> { $"{aggregateCount:n0} {(aggregateCount == 1 ? "aggregate" : "aggregates")}" };

		if ( translucent > 0 ) summary.Add( $"{translucent:n0} translucent {(translucent == 1 ? "model" : "models")}" );
		if ( converted > 0 ) summary.Add( $"{converted:n0} converted {(converted == 1 ? "mesh" : "meshes")}" );
		if ( collision.Count > 0 ) summary.Add( $"{collision.Count:n0} collision {(collision.Count == 1 ? "group" : "groups")}" );

		return [.. summary];
	}

	/// <summary>
	/// Write a generated resource into this run's private generation.
	/// </summary>
	static string Write( SceneFolder folder, string path, byte[] data )
	{
		var written = folder.WriteFile( path, data );

		// The resource system wants the source name, not the compiled one.
		var name = written.EndsWith( "_c" ) ? written[..^2] : written;

		NativeEngine.g_pResourceSystem.ReloadResource( name );

		return written;
	}

	static Model WriteModel( SceneFolder folder, string path, Model model )
	{
		var name = Resource.FixPath( Write( folder, path, model.SaveToVmdl() ) );
		return Model.FromNative( NativeGlue.Resources.GetModel( name, Guid.Empty ), name: name );
	}

	static IEnumerable<T> Gather<T>( IEnumerable<Source> sources ) where T : Component => sources
		.Where( x => x.SkipReason == SceneCompileSkipReason.None )
		.Select( x => x.Component )
		.OfType<T>()
		.Where( x => x.Active );

	/// <summary>
	/// Break every prefab instance holding compiled geometry, so the components we're about to strip
	/// stay stripped. Breaking an instance promotes the ones nested in it, so we go back through
	/// its children once it's loose.
	/// </summary>
	static bool Unlink( GameObject go, HashSet<Guid> processed )
	{
		var holds = Holds( go, processed );

		foreach ( var child in go.Children.ToArray() )
		{
			holds |= Unlink( child, processed );
		}

		if ( !holds || !go.IsOutermostPrefabInstanceRoot )
			return holds;

		go.BreakFromPrefab();

		foreach ( var child in go.Children.ToArray() )
		{
			Unlink( child, processed );
		}

		return true;
	}

	static bool Holds( GameObject go, HashSet<Guid> processed )
	{
		foreach ( var component in go.Components.GetAll( FindMode.EverythingInSelf ) )
		{
			if ( processed.Contains( component.Id ) )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Put back the tags the compiled geometry inherited before it left its old parents.
	/// </summary>
	static void ApplyTags( GameObject go, string tags )
	{
		if ( string.IsNullOrEmpty( tags ) )
			return;

		go.Tags.Add( tags.Split( ',' ) );
	}

	/// <summary>
	/// Remove everything we compiled from the hierarchy, taking an object with it when that was all it
	/// had. Children go first, so an object left holding nothing after its compiled children left goes
	/// too. Components are matched by id, which survives the round trip through the scene file.
	/// Returns whether anything under here was removed.
	/// </summary>
	static bool StripCompiled( GameObject go, HashSet<Guid> processed )
	{
		var stripped = false;

		foreach ( var child in go.Children.ToArray() )
		{
			stripped |= StripCompiled( child, processed );
		}

		foreach ( var component in go.Components.GetAll( FindMode.EverythingInSelf ).ToArray() )
		{
			if ( !processed.Contains( component.Id ) )
				continue;

			component.Destroy();
			stripped = true;
		}

		// Objects that were already empty are the author's, so only clear up after ourselves
		if ( stripped && go is not Scene && go.Components.Count == 0 && go.Children.Count == 0 )
			go.DestroyImmediate();

		return stripped;
	}
}
