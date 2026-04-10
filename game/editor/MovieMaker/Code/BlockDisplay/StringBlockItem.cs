using Sandbox.MovieMaker;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

public sealed class StringBlockItem : PropertyBlockItem<string?>
{
	protected override void OnPaint()
	{
		base.OnPaint();

		if ( Block is IPaintHintBlock hintBlock )
		{
			foreach ( var hintRange in hintBlock.GetPaintHints( Block.TimeRange ) )
			{
				PaintRange( hintRange );
			}
		}
		else
		{
			PaintRange( Block.TimeRange );
		}
	}

	private void PaintRange( MovieTimeRange range )
	{
		if ( Block.GetValue( range.Start ) is not { } value ) return;

		PaintText( range, value );
	}
}
