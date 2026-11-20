/// <summary>
/// Which renderers an outline effect is drawing around, and in what colour. Worked out once a frame, the first time someone asks,
/// for things that can't be outlined by the effect and draw the highlight themselves, like <see cref="TextEntity"/>.
/// </summary>
public sealed class HighlightedRenderers : GameObjectSystem<HighlightedRenderers>
{
	readonly Dictionary<Renderer, Color> _colors = new();
	ulong _frame = ulong.MaxValue;

	public HighlightedRenderers( Scene scene ) : base( scene )
	{
	}

	/// <summary>
	/// The colour of the first active outline drawing around <paramref name="renderer"/>, if any is.
	/// </summary>
	public bool TryGet( Renderer renderer, out Color color )
	{
		if ( _frame != Application.FrameCount )
			Rebuild();

		return _colors.TryGetValue( renderer, out color );
	}

	void Rebuild()
	{
		_frame = Application.FrameCount;
		_colors.Clear();

		foreach ( var outline in Scene.GetAllComponents<HighlightOutline>() )
		{
			if ( !outline.Active ) continue;

			foreach ( var target in outline.GetOutlineTargets() )
			{
				if ( target.IsValid() )
					_colors.TryAdd( target, outline.Color );
			}
		}
	}
}
