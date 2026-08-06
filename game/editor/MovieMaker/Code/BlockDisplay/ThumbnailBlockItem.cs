
using Sandbox.MovieMaker;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

public abstract class ThumbnailBlockItem<T> : PropertyBlockItem<T>
{
	protected abstract Pixmap? GetThumbnail( MovieTimeRange timeRange );
	protected virtual string? GetLabel( MovieTimeRange timeRange ) => null;

	protected override void OnPaint()
	{
		base.OnPaint();

		foreach ( var range in GetPaintBlocks( TimeRange ) )
		{
			var rect = GetRect( range );

			if ( GetThumbnail( range ) is { } thumb )
			{
				Paint.Draw( rect.Contain( Height ), thumb, 0.5f );
			}

			if ( GetLabel( range ) is { } label )
			{
				PaintText( rect, label );
			}
		}
	}
}

public sealed class ResourceBlockItem<T> : ThumbnailBlockItem<T>
	where T : Resource
{
	protected override string? GetLabel( MovieTimeRange timeRange ) => Block.GetValue( timeRange.Start ) is { ResourceName: { } name }
		? name
		: null;

	protected override Pixmap? GetThumbnail( MovieTimeRange timeRange ) => Block.GetValue( timeRange.Start ) is { ResourcePath: { } path }
		? AssetSystem.FindByPath( path )?.GetAssetThumb()
		: null;
}
