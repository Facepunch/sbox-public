using Sandbox.Rendering;

namespace Sandbox;

public sealed partial class SceneSpriteSystem
{
	readonly record struct TextGroupKey( ulong RenderGroup, TextRenderer.BillboardMode Billboard );
	sealed class TextGroup
	{
		internal readonly List<TextRenderer> Members = [];
		internal SpriteBatchSceneObject.SpriteData[] Sprites = new SpriteBatchSceneObject.SpriteData[16];
		internal TextBatch Batch;
		internal BBox Bounds;
	}

	readonly Dictionary<TextGroupKey, TextGroup> _textBatches = [];
	readonly List<TextGroupKey> _unusedTextGroups = [];

	// Uses the same group-level translucent sorting as other sprite batches.
	internal sealed class TextBatch : SceneCustomObject
	{
		readonly SpriteBatchSceneObject sprites;
		internal ulong Frame { get; private set; } = ulong.MaxValue;

		internal TextBatch( Scene scene ) : base( scene.SceneWorld )
		{
			sprites = new SpriteBatchSceneObject( scene ) { Sorted = true, RenderingEnabled = false };
		}

		internal void Upload( SpriteBatchSceneObject.SpriteData[] data, int count, BBox bounds )
		{
			sprites.RegisterSprite( Guid.Empty, data, 0, count, 0, bounds );
			sprites.UploadOnHost();
			Bounds = bounds;
			Frame = Application.FrameCount;
		}

		internal void Destroy()
		{
			sprites.Destroy();
			Delete();
		}

		internal void Invalidate() => Frame = ulong.MaxValue;

		internal bool CanRender => this.IsValid() && Frame == Application.FrameCount
			&& Graphics.CurrentView is Graphics.NativeFrameView
			&& !Graphics.Attributes.GetBool( "WorldPanelLighting" ) && !Graphics.Attributes.GetBool( "UIGammaOutput" );

		public override void RenderSceneObject()
		{
			if ( CanRender ) sprites.RenderSceneObject();
		}
	}

	private void UpdateTextRenderers()
	{
		foreach ( var group in _textBatches.Values )
		{
			group.Batch?.Invalidate();
			group.Members.Clear();
		}

		foreach ( var text in Scene.Query<TextRenderer>() )
		{
			text.SetBatch( null );
			if ( !text.TryGetTextSprite( out var sprite, out var bounds ) ) continue;

			var key = new TextGroupKey( GetRenderGroupKey( text, (GameTags)text.Tags, text.RenderOptions ), text.Billboard );
			if ( !_textBatches.TryGetValue( key, out var group ) )
				_textBatches[key] = group = new TextGroup();

			group.Bounds = group.Members.Count == 0 ? bounds : group.Bounds.AddBBox( bounds );
			if ( group.Sprites.Length == group.Members.Count ) Array.Resize( ref group.Sprites, group.Sprites.Length * 2 );
			group.Sprites[group.Members.Count] = sprite;
			group.Members.Add( text );
		}

		_unusedTextGroups.Clear();
		foreach ( var (key, group) in _textBatches )
		{
			if ( group.Members.Count > 1 )
			{
				if ( group.Batch is null )
				{
					var first = group.Members[0];
					group.Batch = new TextBatch( Scene );
					group.Batch.Flags.IsTranslucent = true;
					group.Batch.Tags.SetFrom( first.GameObject.Tags );
					first.RenderOptions.Apply( group.Batch );
				}
				group.Batch.Upload( group.Sprites, group.Members.Count, group.Bounds );
				foreach ( var text in group.Members ) text.SetBatch( group.Batch );
			}
			else if ( group.Members.Count == 0 )
			{
				group.Batch?.Destroy();
				_unusedTextGroups.Add( key );
			}
		}

		foreach ( var key in _unusedTextGroups ) _textBatches.Remove( key );
	}
}
