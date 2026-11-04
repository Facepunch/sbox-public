namespace Sandbox.UI;

/// <summary>Ambient routing only for the original static Panel.Draw API.</summary>
internal static class LegacyPaint
{
	[ThreadStatic] static Painter.Context current;
	internal static Painter.Context Current => current is { IsPainting: true } ? current : throw new InvalidOperationException( "Panel.Draw can only be used during OnDraw." );

	internal ref struct Binding
	{
		readonly Painter.Context previous;
		internal Binding( Painter.Context context )
		{
			previous = current;
			current = context;
		}
		public void Dispose() => current = previous;
	}
}
