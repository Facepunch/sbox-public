using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sandbox;

namespace Editor;

/// <summary>
/// Compiles a scene's static mesh geometry into the source asset's generated runtime representation.
/// The editable scene is never rewritten by this compiler.
/// </summary>
internal static partial class SceneCompiler
{
	/// <summary>
	/// Each run gets its own generation so failed or cancelled compilations leave the previous one intact.
	/// </summary>
	static string OutputFolder;

	/// <summary>
	/// The immutable recipe captured at the start of this compile. Window edits cannot change it.
	/// </summary>
	internal static SceneCompilerSettings Settings { get; private set; } = new();

	/// <summary>
	/// True while a compile is in flight. A compile is spread over frames and works out of shared
	/// state, so a second one starting on top of the first would trample it.
	/// </summary>
	static bool _running;

	/// <summary>
	/// Something in the scene we're leaving alone, and why. Kept as the component itself so the
	/// window can take you to it.
	/// </summary>
	internal readonly record struct Skip( Component Component, string Label, string Reason );

	/// <summary>
	/// What a compile is going to work on, worked out up front so it can be shown before anything
	/// is built.
	/// </summary>
	internal sealed class Sources
	{
		public Scene Scene { get; init; }
		public Asset Asset { get; init; }
		public SceneFolder Folder { get; init; }
		public string Name { get; init; }
		public MeshComponent[] Meshes { get; init; }
		public ModelRenderer[] Props { get; init; }
		public List<Skip> Skipped { get; init; }
		public bool HasCompileGeometry { get; init; }
	}

	[Menu( "Editor", "Scene/View Compile Report", "list", Priority = 1002 )]
	public static void ViewCompileReport()
	{
		var session = SceneCompileSession.Current;
		session.Refresh();
		EditorEvent.Run( "scene.compile.show-report", session.HasResult ? session.CreateReportSnapshot() : session, "Report" );
	}

	/// <summary>
	/// Everything the active scene has to compile, and why the rest is being left alone. Null with a
	/// reason when the scene can't be compiled at all.
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
		if ( hasCompileGeometry && (scene.Editor is null || scene.Editor.HasUnsavedChanges) )
		{
			error = "Save the scene, then use Scene > Compile Scene. Unsaved changes cannot be compiled.";
			return null;
		}

		var asset = scene.Source is null ? null : AssetSystem.FindByPath( scene.Source.ResourcePath );
		if ( hasCompileGeometry && asset is null )
		{
			error = "Save the scene before compiling it.";
			return null;
		}

		var folder = asset is not null && scene.Editor?.HasUnsavedChanges == false ? scene.Editor.GetSceneFolder() : null;
		if ( hasCompileGeometry && folder is null )
		{
			error = "This scene has nowhere to write its compiled resources.";
			return null;
		}

		var skipped = new List<Skip>();

		return new Sources
		{
			Scene = scene,
			Asset = asset,
			Folder = folder,
			Name = asset is null ? scene.Name : Path.GetFileNameWithoutExtension( asset.AbsolutePath ),
			Meshes = Gather<MeshComponent>( sources, skipped ),
			Props = Gather<ModelRenderer>( sources, skipped ),
			Skipped = skipped,
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
		Settings = settings;
		var generation = Guid.NewGuid().ToString( "N" );
		var sourcePath = sources.Asset.GetSourceFile( true );
		_running = true;
		OutputFolder = $"/compiled/{generation}";
		string[] summary = null;

		try
		{
			summary = await Run( sources, session, generation );
		}
		finally
		{
			try
			{
				if ( summary is null )
					SceneCompileCache.DiscardGeneration( sourcePath, generation );
			}
			finally
			{
				_running = false;
				OutputFolder = null;
			}
		}

		return summary;
	}

	static async Task<string[]> Run( Sources sources, SceneCompileSession session, string generation )
	{
		var scene = sources.Scene;
		var sourceAsset = sources.Asset;
		var sceneFolder = sources.Folder;
		var meshes = sources.Meshes;
		var props = sources.Props;

		if ( !scene.IsValid() )
			throw new OperationCanceledException( "The source scene was closed." );

		if ( Game.IsPlaying || scene.Editor is SceneEditorSession { IsPrefabSession: true } )
			throw new InvalidOperationException( "Stop playing and open a scene rather than a prefab before compiling." );

		if ( scene.Editor is null || scene.Editor.HasUnsavedChanges )
			throw new InvalidOperationException( "Save the scene, then use Scene > Compile Scene. Unsaved changes cannot be compiled." );

		session.Cancel.ThrowIfCancellationRequested();
		session.Phase( "Reading scene data" );
		await Task.Delay( 1, session.Cancel );
		var snapshot = SceneCompileCache.Capture( sourceAsset );
		var sourcePath = sourceAsset.GetSourceFile( true );
		// TimeSince/TimeUntil serialize against the running clock, so take both snapshots at one reading.
		var sourceTime = scene.TimeNow;
		var sourceFile = scene.CreateSceneFile( sourceTime );
		var jsonOptions = new JsonSerializerOptions( JsonSerializerOptions.Default ) { MaxDepth = 512 };
		var sourceJson = sourceFile.Serialize().ToJsonString( jsonOptions );
		var sourceBlob = sourceFile.BinaryData?.ToArray() ?? [];
		SceneCompileCache.BeginGeneration( sourceAsset, generation );

		void RequireUnchanged()
		{
			session.Cancel.ThrowIfCancellationRequested();
			if ( !scene.IsValid() )
				throw new OperationCanceledException( "The source scene was closed." );

			if ( Game.IsPlaying )
				throw new InvalidOperationException( "Play mode started while compiling. Stop playing, then compile again." );

			if ( scene.Editor is null || scene.Editor.HasUnsavedChanges )
				throw new InvalidOperationException( "The scene was edited while compiling. Save the scene, then use Scene > Compile Scene again." );

			if ( !string.Equals( sourcePath, sourceAsset.GetSourceFile( true ), StringComparison.OrdinalIgnoreCase ) )
				throw new InvalidOperationException( "The scene moved while compiling. Use Scene > Compile Scene again at its new location." );

			var current = scene.CreateSceneFile( sourceTime );
			var currentJson = current.Serialize();
			if ( currentJson.ToJsonString( jsonOptions ) != sourceJson )
			{
				var change = FirstDifference( JsonNode.Parse( sourceJson, documentOptions: new JsonDocumentOptions { MaxDepth = 512 } ), currentJson, "Scene" );
				throw new InvalidOperationException( $"The scene changed while compiling without being edited: {change}. A component running in the editor is probably rewriting that property." );
			}

			if ( !(current.BinaryData ?? []).AsSpan().SequenceEqual( sourceBlob ) )
				throw new InvalidOperationException( "The scene's binary data changed while compiling. Save the scene, then use Scene > Compile Scene again." );
		}

		var frame = FastTimer.StartNew();

		// Pumping the editor costs more than most of the work between two steps, so we only do it
		// on a frame's cadence rather than for every item.
		async Task Step( int current, int total )
		{
			session.Cancel.ThrowIfCancellationRequested();
			if ( !scene.IsValid() )
				throw new OperationCanceledException( "The source scene was closed." );

			if ( frame.ElapsedMilliSeconds < 30 )
				return;

			session.Step( current, total );

			await Task.Delay( 1 );
			session.Cancel.ThrowIfCancellationRequested();
			if ( !scene.IsValid() )
				throw new OperationCanceledException( "The source scene was closed." );

			frame = FastTimer.StartNew();
		}

		session.Phase( "Compiling geometry" );
		await Task.Delay( 1 );
		session.Cancel.ThrowIfCancellationRequested();
		if ( !scene.IsValid() )
			throw new OperationCanceledException( "The source scene was closed." );

		var processed = new HashSet<Guid>();
		var plan = await Plan( meshes, props, processed, Step, session.Cancel );

		if ( plan is null )
			return null;

		var plans = plan.Aggregates;
		var statistics = new SceneCompileStatistics();

		session.Phase( "Building models" );

		var fragments = new AggregateFragmentInfo[plans.Length][];
		var vmdls = new byte[plans.Length][];

		for ( int i = 0; i < plans.Length; i++ )
		{
			var build = Build( plans[i] );

			fragments[i] = build.Fragments;
			vmdls[i] = build.Model.SaveToVmdl();
			if ( !plans[i].Translucent )
				statistics.FragmentCount += build.Fragments.Length;

			foreach ( var chunk in plans[i].Chunks )
			{
				statistics.VertexCount += chunk.Vertices.Length;
				statistics.TriangleCount += chunk.Indices.Length / 3;
			}

			await Step( i + 1, plans.Length );
		}

		session.Phase( "Building collision" );
		await Task.Delay( 1 );

		var physics = await BuildCollision( plan.Collision, plan.Shapes, Step );

		session.Phase( "Writing resources" );
		await Task.Delay( 1 );

		RequireUnchanged();
		SceneCompileCache.RequireUnchanged( sourceAsset, snapshot );

		var models = new Model[plans.Length];

		for ( int i = 0; i < plans.Length; i++ )
		{
			models[i] = Model.Load( Write( sceneFolder, $"{OutputFolder}/aggregate_{i}.vmdl_c", vmdls[i] ) );
			if ( !models[i].IsValid() || models[i].IsError )
				throw new InvalidOperationException( $"Could not load compiled aggregate model {i}." );

			await Step( i + 1, plans.Length );
		}

		var collision = new PhysicsGroupDescription[physics.Count];

		for ( int i = 0; i < physics.Count; i++ )
		{
			collision[i] = PhysicsGroupDescription.Load( Write( sceneFolder, $"{OutputFolder}/collision_{i}.vphys_c", physics[i].Data ) );
			if ( collision[i] is null )
				throw new InvalidOperationException( $"Could not load compiled collision resource {i}." );

			await Step( i + 1, physics.Count );
		}

		session.Phase( "Cloning scene" );
		await Task.Delay( 1 );

		// An editor scene, not a game one - loading a scene file into a game scene additively pulls
		// in the project's system scene and network spawns everything, and all of that would end up
		// saved into the compile.
		var compiled = Scene.CreateEditorScene();
		var converted = 0;
		SceneFile file = null;

		try
		{
			var leftovers = new HashSet<Guid>();

			using ( compiled.Push() )
			{
				sourceFile.ActionGraphCache.Clear();
				if ( !compiled.Load( sourceFile ) )
					throw new InvalidOperationException( "Could not load the editable scene for compilation." );

				foreach ( var mesh in compiled.Components.GetAll<MeshComponent>( FindMode.EverythingInSelfAndDescendants ) )
				{
					if ( !processed.Contains( mesh.Id ) )
					{
						leftovers.Add( mesh.Id );
					}
				}
			}

			if ( leftovers.Count > 0 )
			{
				session.Phase( $"Converting {leftovers.Count} meshes" );
				await Task.Delay( 1 );

				converted = await ConvertMeshes( compiled, leftovers, sceneFolder, statistics, Step );
				processed.UnionWith( leftovers );
			}

			session.Phase( "Stripping compiled geometry" );
			await Task.Delay( 1 );

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
				if ( plans.Length > 0 || collision.Length > 0 )
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
						model.Model = models[i];
						model.Tint = plans[i].Tint;

						continue;
					}

					go.Name = $"Aggregate {i}";

					var renderer = go.AddComponent<AggregateRenderer>();
					renderer.Model = models[i];
					renderer.Tint = plans[i].Tint;
					renderer.Fragments = fragments[i].ToList();
				}

				for ( int i = 0; i < collision.Length; i++ )
				{
					var go = compiled.CreateObject();
					go.Name = $"Collision {i}";
					go.SetParent( root );
					go.Flags |= GameObjectFlags.Hidden;
					ApplyTags( go, physics[i].Tags );

					var collider = go.AddComponent<PhysicsCollider>();
					collider.Physics = collision[i];
					collider.Static = true;
				}

				if ( compiled.Components.GetAll<MeshComponent>( FindMode.EverythingInSelfAndDescendants ).FirstOrDefault() is { } remainingMesh )
					throw new InvalidOperationException( $"Cannot publish the compiled scene: mesh '{remainingMesh.GameObject.Name}' was not converted. Compiled scenes cannot contain MeshComponents." );

				file = new SceneFile();
				compiled.ToSceneFile( file );
				file.Id = sourceFile.Id;
			}

			session.Phase( "Writing runtime scene" );
			SceneCompileCache.Publish( sourceAsset, generation, file, snapshot, Settings, RequireUnchanged );
			Settings.SaveDefaults();
			session.Statistics = statistics;

			var translucent = plans.Count( x => x.Translucent );
			var aggregateCount = plans.Length - translucent;
			var summary = new List<string> { $"{aggregateCount:n0} {(aggregateCount == 1 ? "aggregate" : "aggregates")}" };

			if ( translucent > 0 ) summary.Add( $"{translucent:n0} translucent {(translucent == 1 ? "model" : "models")}" );
			if ( converted > 0 ) summary.Add( $"{converted:n0} converted {(converted == 1 ? "mesh" : "meshes")}" );
			if ( collision.Length > 0 ) summary.Add( $"{collision.Length:n0} collision {(collision.Length == 1 ? "group" : "groups")}" );

			return [.. summary];
		}
		finally
		{
			compiled.Destroy();
		}
	}

	/// <summary>
	/// Path to the first place two scene serializations disagree.
	/// </summary>
	static string FirstDifference( JsonNode before, JsonNode after, string path )
	{
		if ( JsonNode.DeepEquals( before, after ) )
			return null;

		if ( before is JsonObject a && after is JsonObject b )
		{
			foreach ( var key in a.Select( x => x.Key ).Union( b.Select( x => x.Key ) ) )
			{
				a.TryGetPropertyValue( key, out var x );
				b.TryGetPropertyValue( key, out var y );
				if ( FirstDifference( x, y, $"{path}.{key}" ) is { } found )
					return found;
			}
		}
		else if ( before is JsonArray l && after is JsonArray r && l.Count == r.Count )
		{
			for ( int i = 0; i < l.Count; i++ )
			{
				var name = l[i] is JsonObject o && (o["Name"] ?? o["__type"]) is JsonValue label && label.TryGetValue<string>( out var text ) ? text : null;
				if ( FirstDifference( l[i], r[i], name is null ? $"{path}[{i}]" : $"{path}[{name}]" ) is { } found )
					return found;
			}
		}

		return $"{path} went from {Describe( before )} to {Describe( after )}";

		static string Describe( JsonNode node )
		{
			var text = node?.ToJsonString() ?? "nothing";
			return text.Length <= 80 ? text : text[..77] + "...";
		}
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

	/// <summary>
	/// Everything in the scene we can compile, noting what we're leaving alone and why.
	/// </summary>
	static T[] Gather<T>( IEnumerable<Source> sources, List<Skip> skipped ) where T : Component
	{
		var found = new List<T>();

		foreach ( var source in sources )
		{
			if ( source.Component is not T component || !component.Active )
				continue;

			if ( source.SkipReason is not { } reason )
			{
				found.Add( component );
				continue;
			}

			skipped.Add( new Skip( component, source.Label, reason ) );
		}

		return [.. found];
	}

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
