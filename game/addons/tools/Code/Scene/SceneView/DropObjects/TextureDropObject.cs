using Sandbox.Resources;
using System.Threading;

namespace Editor;

[DropObject( "texture", "vtex", "vtex_c", "png", "jpg", "jpeg", "tga", "ies", "webp" )]
partial class TextureDropObject : BaseDropObject
{
	Texture texture;
	float aspect = 1f;

	protected override async Task Initialize( string dragData, CancellationToken token )
	{
		Asset asset = await InstallAsset( dragData, token );

		if ( asset is null )
			return;

		if ( token.IsCancellationRequested )
			return;

		PackageStatus = "Loading Texture";
		if ( asset.Path.EndsWith( "vtex" ) || asset.Path.EndsWith( "vtex_c" ) )
		{
			texture = asset.LoadResource<Texture>();
		}
		else
		{
			var generator = new ImageFileGenerator
			{
				FilePath = asset.RelativePath
			};

			texture = await generator.CreateAsync( ResourceGenerator.Options.Default, token );
		}
		PackageStatus = null;

		if ( !texture.IsValid() || texture == Texture.Invalid )
			throw new System.Exception( $"Failed to load texture from asset '{asset.Path}'" );

		aspect = (float)texture.Height / texture.Width;
		if ( texture.HasAnimatedSequences ) aspect = 0f;
	}

	public override void OnUpdate()
	{
		using var scope = Gizmo.Scope( "DropObject", traceTransform );

		Gizmo.Draw.Color = Color.White;
		if ( texture is not null && aspect != 0 )
		{
			Gizmo.Draw.Sprite( Vector3.Zero, new Vector2( 10f, 10f * aspect ), texture, true );
		}
		else
		{
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
			Gizmo.Draw.Sprite( Bounds.Center, 16, "materials/gizmo/downloads.png" );
		}

		if ( !string.IsNullOrWhiteSpace( PackageStatus ) )
		{
			Gizmo.Draw.Text( PackageStatus, new Transform( Bounds.Center ), "Inter", 14 * Application.DpiScale );
		}
	}

	public override async Task OnDrop()
	{
		await WaitForLoad();

		if ( texture is null )
			return;

		using var scene = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Drop Texture" ).WithGameObjectCreations().Push() )
		{
			GameObject = new GameObject();
			GameObject.Name = texture.ResourceName;
			GameObject.WorldTransform = traceTransform;

			var spriteComponent = GameObject.Components.GetOrCreate<SpriteRenderer>();
			spriteComponent.Sprite = CreateSpriteFromTexture( texture );

			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( GameObject );
		}
	}

	Sprite CreateSpriteFromTexture( Texture texture )
	{
		var sprite = EditorTypeLibrary.Create<Sprite>( "Sprite" );
		sprite.Animations = [
			new Sprite.Animation()
			{
				Name = "Default",
				Frames = [ new Sprite.Frame { Texture = texture } ]
			}
		];

		sprite.EmbeddedResource = new()
		{
			ResourceCompiler = "embed",
			Data = sprite?.EmbeddedResource?.Data ?? new()
		};

		return sprite;
	}
}
