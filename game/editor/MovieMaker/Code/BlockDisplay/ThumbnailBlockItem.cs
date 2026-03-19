
namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

public abstract class ThumbnailBlockItem<T> : PropertyBlockItem<T>
{
	protected abstract Pixmap? GetThumbnail();
	protected virtual string? GetLabel() => null;

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( GetThumbnail() is { } thumb )
		{
			Paint.Draw( LocalRect.Contain( Height ), thumb, 0.5f );
		}

		if ( GetLabel() is { } label )
		{
			PaintText( TimeRange, label );
		}
	}
}

public sealed class ResourceBlockItem<T> : ThumbnailBlockItem<T>
	where T : Resource
{
	protected override string? GetLabel() => Block.GetValue( Block.TimeRange.Start ) is { ResourceName: { } name }
		? name
		: null;

	protected override Pixmap? GetThumbnail() => Block.GetValue( Block.TimeRange.Start ) is { ResourcePath: { } path }
		? AssetSystem.FindByPath( path )?.GetAssetThumb()
		: null;
}
