using Sandbox.UI;

namespace Sandbox;

internal partial class PainterBatcher
{
	internal readonly record struct Spatial( Matrix Transform, int ScissorIndex, int TransformIndex );

	internal void Resolve( in Painter.BoxDescriptor desc, in Matrix localTransform, int clipIndex, out UICssBoxBatched.BoxInstance gpu )
	{
		UICssBoxBatched.BoxInstance.From( desc, out gpu );
		var spatial = Destination.ResolveSpatial( this, localTransform );
		var backgroundImage = desc.HasImage ? desc.BackgroundImage : null;
		var borderImage = desc.BorderImage.Texture;

		if ( desc.HasTextMask )
		{
			desc.TextMask.MarkUsed();
			RetainTexture( desc.TextMask, true );
			gpu.TextMaskIndex = desc.TextMask.Index;
		}

		var rect = new Rect( gpu.Rect.x, gpu.Rect.y, gpu.Rect.z, gpu.Rect.w );
		bool? isOnScreen = null;
		if ( clipIndex >= 0 && (backgroundImage?.ParentObject is VideoPlayer || borderImage?.ParentObject is VideoPlayer) )
		{
			isOnScreen = OverlapsScissor( rect, spatial.Transform, Destination.Scissor );
			var inverse = Destination.Transform.Inverted;
			for ( int index = clipIndex; index >= 0 && isOnScreen == true; index = DrawClips[index].Parent )
			{
				var clip = DrawClips[index];
				isOnScreen = clip.Rect.Width > 0 && clip.Rect.Height > 0 && OverlapsScissor( rect, spatial.Transform,
					Painter.Scissoring.Single( clip.Rect, clip.Radii, inverse * clip.Matrix ) );
			}
		}
		if ( backgroundImage is not null )
		{
			var presented = MarkPresented( backgroundImage, rect, spatial.Transform, Destination.Scissor, ref isOnScreen, Destination.PlaybackPaused );
			RetainTexture( backgroundImage, presented );
			gpu.TextureIndex = backgroundImage.Index > 0 ? backgroundImage.Index : Texture.Transparent.Index;
		}
		if ( borderImage is not null )
		{
			var presented = MarkPresented( borderImage, rect, spatial.Transform, Destination.Scissor, ref isOnScreen );
			RetainTexture( borderImage, presented );
			gpu.BorderImageIndex = borderImage.Index > 0 ? borderImage.Index : Texture.Transparent.Index;
		}

		// Gradient, shape, transform and scissor indices are resolved afresh each frame.
		if ( backgroundImage is null && desc.HasGradient )
			gpu.TextureIndex = -GetOrAddGradient( in desc.BackgroundGradient ) - 1;
		gpu.ShapeIndex = desc.ShapeIndex ?? (desc.PathData is null ? GetOrAddShape( desc.BorderShapeData ) : GetOrAddPath( desc.PathData ));
		ResolveSpatial( ref gpu, clipIndex, spatial );
	}

	internal void Resolve( in Painter.ShadowDescriptor desc, in Matrix localTransform, int clipIndex, out UICssBoxBatched.BoxInstance gpu )
	{
		gpu = UICssBoxBatched.BoxInstance.FromShadow( desc );
		var spatial = Destination.ResolveSpatial( this, localTransform );
		ResolveSpatial( ref gpu, clipIndex, spatial );
		gpu.InverseScissorIndex = GetOrAddScissor( Painter.Scissoring.Single( desc.Rect, desc.Radii, spatial.Transform.Inverted, invert: !desc.Inset ) );
	}

	internal void Resolve( in Painter.OutlineDescriptor desc, in Matrix localTransform, int clipIndex, out UICssBoxBatched.BoxInstance gpu )
	{
		gpu = UICssBoxBatched.BoxInstance.FromOutline( desc );
		ResolveSpatial( ref gpu, clipIndex, Destination.ResolveSpatial( this, localTransform ) );
	}

	void ResolveSpatial( ref UICssBoxBatched.BoxInstance gpu, int clipIndex, in Spatial spatial )
	{
		gpu.ScissorIndex = clipIndex < 0 ? spatial.ScissorIndex : GetOrAddDrawClip( clipIndex, Destination.Transform, spatial.ScissorIndex );
		gpu.TransformIndex = spatial.TransformIndex;
	}

	internal static bool MarkPresented( Texture texture, Rect rect, Matrix transform, in Painter.Scissoring scissor, ref bool? isOnScreen, bool? playbackPaused = null )
	{
		var player = texture.ParentObject as VideoPlayer;
		if ( player is null )
		{
			texture.MarkUsed();
			return true;
		}

		var presented = playbackPaused switch
		{
			true => false,
			false => true,
			_ => isOnScreen ??= OverlapsScissor( rect, transform, scissor )
		};

		player.TrackPresentation( presented );
		if ( presented ) texture.MarkUsed();
		return presented;
	}

	internal static bool OverlapsScissor( Rect rect, Matrix transform, in Painter.Scissoring scissor )
	{
		for ( int i = 0; i < scissor.Count; i++ )
		{
			ref readonly var clip = ref scissor.Clips[i];
			if ( !(transform * clip.Matrix).Transform( rect ).Overlaps( clip.Rect ) ) return false;
		}

		return true;
	}
}
