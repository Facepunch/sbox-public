using System;
using System.IO;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class Conversion
	{
		public void ConvertImagePlane( GameObject obj, Transform plane, string map, string path, float opacity )
		{
			obj.Flags |= GameObjectFlags.EditorOnly;
			// Hammer's quad spans XY. A fixed sprite spans its local Y/Z axes.
			var size = plane.Scale;
			var up = size.y < 0 ? Vector3.Right : Vector3.Left;
			var normal = (size.x < 0) != (size.y < 0) ? Vector3.Down : Vector3.Up;
			obj.WorldTransform = new Transform( plane.Position, plane.Rotation * Rotation.LookAt( normal, up ) );
			var renderer = obj.Components.Create<SpriteRenderer>();
			renderer.Billboard = SpriteRenderer.BillboardMode.None;
			renderer.Color = Color.White.WithAlpha( opacity );
			SetPlaneImage( renderer, size, map, path );
		}

		void SetPlaneImage( SpriteRenderer renderer, Vector3 size, string map, string path )
		{
			var texture = string.IsNullOrWhiteSpace( path ) ? null : Texture.Load( ResolvePlaneImage( map, path ) );
			if ( texture is null || texture.IsError )
				Warnings.Add( $"Image plane '{renderer.GameObject.Name}' could not load '{path}'." );

			// SpriteRenderer fits the texture to its longest side. Preserve the authored plane dimensions.
			float aspect = texture is { Width: > 0, Height: > 0 } ? (float)texture.Width / texture.Height : 1;
			renderer.Size = new Vector2( MathF.Abs( size.x ) / MathF.Min( aspect, 1 ), MathF.Abs( size.y ) * MathF.Max( aspect, 1 ) );
			renderer.Sprite = new Sprite { EmbeddedResource = new() { ResourceCompiler = "embed" } };
			renderer.Sprite.Animations[0].Frames[0].Texture = texture;
		}

		readonly Dictionary<string, string> copiedImages = new( StringComparer.OrdinalIgnoreCase );

		string ResolvePlaneImage( string map, string path )
		{
			var absolute = Path.IsPathFullyQualified( path );
			if ( !absolute && (Path.IsPathRooted( path ) || path.Split( '/', '\\' ).Contains( ".." ) || path.Contains( ':' )) )
				return path;

			var source = absolute ? Path.GetFullPath( path ) : VmapReader.LocalReferencePath( map, path ) ?? FileSystem.Content.GetFullPath( path );
			if ( !File.Exists( source ) ) return path;
			var extension = Path.GetExtension( source );
			var direct = Sandbox.TextureLoader.Image.Extensions.Contains( extension );
			if ( direct )
			{
				var relative = absolute ? FileSystem.Content.GetRelativePath( source ) : path;
				if ( relative is not null && FileSystem.Content.GetFullPath( relative ) is { } mounted &&
					string.Equals( source, Path.GetFullPath( mounted ), StringComparison.OrdinalIgnoreCase ) )
					return relative;
			}

			if ( copiedImages.TryGetValue( source, out var asset ) ) return asset;
			// Both extensions use the same JPEG decoder. Retain the original compressed bytes.
			if ( extension.Equals( ".jpeg", StringComparison.OrdinalIgnoreCase ) )
			{
				direct = true;
				extension = ".jpg";
			}

			var image = direct ? null : ReadPlanePixmap( source, extension );
			if ( !direct && (image is null || image.Width == 0) ) return path;

			var output = DependencyPath( $"{copiedImages.Count}/{Path.GetFileName( source )}", "images", direct ? extension : ".png" );
			if ( direct )
			{
				using var input = File.OpenRead( source );
				using var file = CreateOutput( output );
				input.CopyTo( file );
			}
			else
			{
				using ( CreateOutput( output ) ) { }
				if ( !image.SavePng( output ) ) throw new IOException( $"Could not write image plane texture '{output}'." );
			}

			asset = Path.GetRelativePath( assetsPath, output ).NormalizeFilename( false, false );
			copiedImages.Add( source, asset );
			return asset;
		}

		static Pixmap ReadPlanePixmap( string source, string extension )
		{
			if ( !extension.Equals( ".pfm", StringComparison.OrdinalIgnoreCase ) ) return Pixmap.FromFile( source );

			// Hammer uploads float maps as BGRA8 using the float bitmap's linear conversion.
			using var bitmap = EditorUtility.LoadBitmap( source );
			if ( bitmap?.EncodeTo( ImageFormat.BGRA8888 ) is not { Length: > 0 } pixels ) return null;
			var image = new Pixmap( bitmap.Width, bitmap.Height );
			return image.UpdateFromPixels( pixels, bitmap.Width, bitmap.Height ) ? image : null;
		}
	}
}
