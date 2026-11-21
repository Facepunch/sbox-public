using System;
using System.IO;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	// Hammer takes the first visible, enabled reference in the shallowest prefab world.
	// Choose after placement so prefab overrides and hidden parents are already resolved.
	internal static void SelectActiveSkybox( Scene scene, IEnumerable<Skybox3D> references )
	{
		Skybox3D selected = null;
		int bestDepth = int.MaxValue;
		foreach ( var skybox in references )
		{
			skybox.Enabled = false;
			if ( !skybox.GameObject.Enabled ) continue;
			int depth = 0;
			var parent = skybox.GameObject.Parent;
			for ( ; parent != scene; parent = parent.Parent )
			{
				if ( !parent.Enabled || parent.Flags.Contains( GameObjectFlags.EditorOnly ) ) break;
				if ( parent.IsPrefabInstanceRoot ) depth++;
			}
			if ( parent != scene || depth >= bestDepth ) continue;
			bestDepth = depth;
			selected = skybox;
		}
		if ( selected is not null ) selected.Enabled = true;
	}

	internal static bool SkyboxEnabled( Dictionary<string, string> keys ) =>
		HammerInteger( keys.GetValueOrDefault( "StartDisabled" ), 0 ) == 0 && HammerInteger( keys.GetValueOrDefault( "enabled" ), 1 ) == 1;

	static Skybox3D SkyboxComponent( GameObject reference ) =>
		reference.Children.FirstOrDefault( child => child.Name == "3D Skybox" )?.Components.Get<Skybox3D>( FindMode.EverythingInSelf );

	// CMapGameDataNode uses atoi on present values, including empty strings.
	static int HammerInteger( string value, int fallback )
	{
		if ( value is null ) return fallback;
		var text = value.AsSpan().TrimStart();
		int end = text.StartsWith( "+" ) || text.StartsWith( "-" ) ? 1 : 0;
		int start = end;
		while ( end < text.Length && char.IsAsciiDigit( text[end] ) ) end++;
		if ( end == start ) return 0;
		return int.TryParse( text[..end], System.Globalization.CultureInfo.InvariantCulture, out var result )
			? result : text[0] == '-' ? int.MinValue : int.MaxValue;
	}

	sealed partial class Conversion
	{
		public Dictionary<string, SceneFile> Skyboxes { get; } = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<(SourceNode World, Rotation Rotation), (SceneFile Scene, float Scale)> _skyboxSources = new();
		readonly Dictionary<SceneFile, (SourceNode World, string Path)> _skyboxReferences = new();
		readonly HashSet<SourceNode> _buildingSkyboxes = new();

		public void FinalizeSkyboxes( Scene scene )
		{
			if ( Skyboxes.Count == 0 ) return;
			foreach ( var component in scene.Components.GetAll<Skybox3D>( FindMode.EverythingInSelfAndDescendants ) )
			{
				// A linked prefab carries the scene baked at its definition's rotation.
				// Reuse or bake the sky for its final placement before saving the instance patch.
				if ( component.SkyboxScene is { } file && _skyboxReferences.TryGetValue( file, out var source ) )
					PlaceSkybox( component, source.World, source.Path );
			}
			SelectActiveSkybox( scene, scene.Components.GetAll<Skybox3D>( FindMode.EverythingInSelfAndDescendants )
				.Where( c => c.SkyboxScene is { } file && _skyboxReferences.ContainsKey( file ) ) );
		}

		string DependencyPath( string assetPath, string folder, string extension )
		{
			var directory = Path.Combine( Path.GetDirectoryName( scenePath ), Path.GetFileNameWithoutExtension( scenePath ) + "_" + folder );
			var output = Path.GetFullPath( Path.Combine( directory, Path.ChangeExtension( assetPath, extension ) ) );
			var relative = Path.GetRelativePath( directory, output );
			if ( Path.IsPathRooted( assetPath ) || relative == ".." || relative.StartsWith( ".." + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
				throw new InvalidDataException( $"Cannot place '{assetPath}' inside the generated assets folder." );
			CheckOutputPath( output );
			return output;
		}

		public void AttachSkybox( GameObject reference, SourceNode world, string path, Dictionary<string, string> keys )
		{
			var component = SkyboxComponent( reference );
			if ( world?.IsSkyboxWorld != true )
			{
				if ( component is not null ) component.SkyboxScene = null;
				if ( world is not null ) Warnings.Add( $"Map '{path}' is not marked as a 3D skybox." );
				else if ( !string.IsNullOrWhiteSpace( path ) ) Warnings.Add( $"Skybox '{path}' could not be resolved." );
				return;
			}

			component ??= new GameObject( reference, false, "3D Skybox" ).Components.Create<Skybox3D>();
			PlaceSkybox( component, world, path );
			component.GameObject.Enabled = SkyboxEnabled( keys );
		}

		void PlaceSkybox( Skybox3D component, SourceNode world, string path )
		{
			var reference = component.GameObject.Parent;
			var key = (world, reference.WorldRotation);
			if ( !_skyboxSources.TryGetValue( key, out var skybox ) )
			{
				if ( !_buildingSkyboxes.Add( world ) ) throw new InvalidDataException( $"Recursive VMAP skybox reference to '{path}'." );
				var scene = CreateScene();
				using ( scene.Push() )
				{
					new WorldConversion( this, world, scene, insideSkybox: true ).Convert();
					var camera = FindCamera( world );
					skybox.Scale = camera?.Entity.GetValue( "scale", 1f ) ?? 1f;
					var origin = camera?.Transform.Position ?? Vector3.Zero;
					// Hammer views the sky from the sky_camera, in the skybox_reference's axes.
					// Move the scene roots into that frame without changing their mesh data.
					var basis = new Transform( reference.WorldRotation * -origin, reference.WorldRotation );
					foreach ( var root in scene.Children ) root.WorldTransform = basis.ToWorld( root.WorldTransform );
					skybox.Scene = scene.CreateSceneFile();
				}
				var output = DependencyPath( path, "skyboxes", ".scene" );
				for ( int suffix = Skyboxes.Count; Skyboxes.ContainsKey( output ); suffix++ )
					output = DependencyPath( path, "skyboxes", $".{suffix}.scene" );
				var assetPath = Path.GetRelativePath( assetsPath, output ).NormalizeFilename( false, false );
				skybox.Scene.RegisterWeakResourceId( assetPath );
				skybox.Scene.Register( assetPath );
				Skyboxes.Add( output, skybox.Scene );
				_skyboxSources.Add( key, skybox );
				_skyboxReferences.Add( skybox.Scene, (world, path) );
				_buildingSkyboxes.Remove( world );
			}

			component.GameObject.WorldTransform = new Transform( reference.WorldPosition, Rotation.Identity, skybox.Scale );
			component.SkyboxScene = skybox.Scene;
		}

		static SourceNode FindCamera( SourceNode node )
		{
			if ( node.Hidden || node.EditorOnly || node.HiddenWorldLayer ) return null;
			if ( node.Kind == NodeKind.Entity && node.Entity.TypeName == "sky_camera" ) return node;
			foreach ( var child in node.Children )
				if ( FindCamera( child ) is { } camera ) return camera;
			return null;
		}
	}
}
