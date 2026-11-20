namespace Sandbox;

/// <summary>
/// One part of the HUD (vitals, weapon buckets etc) painted into the shared HudCanvas
/// </summary>
public abstract class HudSection
{
	public HudCanvas Canvas { get; internal set; }

	/// <summary>
	/// Paint order, lowest first
	/// </summary>
	public virtual int Order => 0;

	public virtual void Tick() { }

	/// <summary>
	/// Paint this section
	/// </summary>
	public abstract void Paint( Painter painter );

	/// <summary>
	/// Screen rects the composite shader runs over. Everything painted (plus its glow) must sit inside them.
	/// </summary>
	public abstract void GetCompositeRegions( List<Rect> regions );

	/// <summary>
	/// The canvas's scale to the screen times the player's HUD scale. Size everything painted by this.
	/// </summary>
	protected float ScaleToScreen => Canvas.ScaleToScreen * GamePreferences.HudScale;

	protected Rect CanvasRect => Canvas.Box.Rect;

	protected Color Tint => Canvas.Layer.CurrentTint;
	protected float DigitGain => Canvas.Layer.DigitGain;
	protected float TextGain => Canvas.Layer.TextGain;
	protected float GlowRadius => Canvas.Layer.GlowRadius;

	protected internal Color PaintTint { get; set; } = Color.White;

	protected Color Tinted( Color color ) => color * PaintTint;

	protected Color Glowing( Color color ) => HudStyle.Brightened( color, DigitGain );
}
