using System;
using System.IO;

namespace Editor;

/// <summary>
/// Recovery copies of unsaved scenes, written when the editor's Borealis.Gpu device is lost, before it exits: each beside
/// its scene (never over it), or in the project's Assets for one never saved. Only the scene's JSON, nothing that needs
/// the GPU.
/// </summary>
internal static class SceneRecovery
{
	/// <summary>Where a copy of <paramref name="original"/> goes: beside it, named for <paramref name="when"/>, and never an existing file.</summary>
	internal static string RecoveryPath( string original, DateTime when )
	{
		var folder = Path.GetDirectoryName( original ) ?? "";
		var name = Path.GetFileNameWithoutExtension( original );
		var extension = Path.GetExtension( original );
		var path = Path.Combine( folder, $"{name}.recovered-{when:yyyyMMdd-HHmmss}{extension}" );
		for ( int n = 2; File.Exists( path ); n++ )
			path = Path.Combine( folder, $"{name}.recovered-{when:yyyyMMdd-HHmmss}-{n}{extension}" );

		return path;
	}

	/// <summary>Write a copy of each editor session's scene that has unsaved changes; says where each went.</summary>
	internal static List<string> SaveCopies()
	{
		var saved = new List<string>();
		var when = DateTime.Now;
		foreach ( var session in SceneEditorSession.All.ToArray() )
		{
			if ( session is GameEditorSession || !session.HasUnsavedChanges || session.Scene is null ) continue;

			try
			{
				var scene = session.Scene;
				var isPrefab = scene is PrefabScene;
				var original = scene.Source is not null && AssetSystem.FindByPath( scene.Source.ResourcePath ) is Asset asset
					? asset.AbsolutePath
					: Path.Combine( Project.Current.GetAssetsPath(), $"untitled.{(isPrefab ? "prefab" : "scene")}" );

				GameResource resource = scene is PrefabScene prefab ? prefab.ToPrefabFile() : scene.CreateSceneFile();
				var path = RecoveryPath( original, when );
				File.WriteAllText( path, resource.Serialize().ToJsonString( Json.options ) );
				saved.Add( path );
				Log.Warning( $"Saved a recovery copy of {scene.Name} to {path}" );
			}
			catch ( Exception e )
			{
				Log.Error( e, $"Couldn't save a recovery copy of {session.Scene?.Name}" );
			}
		}

		return saved;
	}
}
