namespace Sandbox.PanelGallery;

/// <summary>
/// Invalidates animated drawing when the shared clock advances.
/// </summary>
internal abstract class AnimatedDrawPanel( Func<float> getTime ) : Panel
{
	float _lastTickTime = float.NaN;

	public override void Tick()
	{
		base.Tick();
		var time = getTime();
		if ( time == _lastTickTime ) return;
		_lastTickTime = time;
	}

	public sealed override void OnDraw( Painter painter ) => DrawFrame( painter, getTime() );

	protected abstract void DrawFrame( Painter painter, float time );
}
