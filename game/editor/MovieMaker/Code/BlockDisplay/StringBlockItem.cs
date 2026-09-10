using Sandbox.MovieMaker;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

public sealed class StringBlockItem : PropertyBlockItem<string?>
{
	protected override void OnPaint()
	{
		base.OnPaint();

		foreach ( var hintRange in GetPaintBlocks( Block.TimeRange ) )
		{
			PaintRange( hintRange );
		}
	}

	private void PaintRange( MovieTimeRange range )
	{
		if ( Block.GetValue( range.Start ) is not { } value ) return;

		PaintText( GetRect( range ), value );
	}
}
