using System.Text.Json.Nodes;

namespace Sandbox;

[Expose]
public class RenderOptions : IJsonPopulator, IEquatable<RenderOptions>, ICloneable
{
	Action onDirty;

	internal RenderOptions( Action onDirty )
	{
		this.onDirty = onDirty;
	}

	/// <summary>
	/// Regular game rendering layers
	/// </summary>
	public bool Game
	{
		get;
		set { field = value; onDirty?.InvokeWithWarning(); }
	} = true;

	/// <summary>
	/// Rendered above everything else
	/// </summary>
	public bool Overlay
	{
		get;
		set { field = value; onDirty?.InvokeWithWarning(); }
	}

	/// <summary>
	/// Rendererd during bloom
	/// </summary>
	public bool Bloom
	{
		get;
		set { field = value; onDirty?.InvokeWithWarning(); }
	}

	/// <summary>
	/// Rendered after the UI is rendered
	/// </summary>
	public bool AfterUI
	{
		get;
		set { field = value; onDirty?.InvokeWithWarning(); }
	}

	/// <summary>
	/// Apply these options to a SceneObject
	/// </summary>
	public void Apply( SceneObject obj )
	{
		Assert.IsValid( obj );

		obj.Flags.SetFlag( Rendering.SceneObjectFlags.ExcludeGameLayer, !Game && !Overlay );
		obj.Flags.SetFlag( Rendering.SceneObjectFlags.GameOverlayLayer, Overlay );
		obj.Flags.SetFlag( Rendering.SceneObjectFlags.EffectsBloomLayer, Bloom );
		obj.Flags.SetFlag( Rendering.SceneObjectFlags.UIOverlayLayer, AfterUI );
	}

	internal RenderOptions Clone()
	{
		RenderOptions cloned = new( null )
		{
			Game = Game,
			Overlay = Overlay,
			Bloom = Bloom,
			AfterUI = AfterUI
		};
		return cloned;
	}

	object ICloneable.Clone() => Clone();

	public override bool Equals( object obj ) => Equals( obj as RenderOptions );
	public virtual bool Equals( RenderOptions obj )
	{
		return Game == obj.Game && AfterUI == obj.AfterUI && Bloom == obj.Bloom && Overlay == obj.Overlay;
	}

	public override int GetHashCode() => HashCode.Combine( Game, AfterUI, Bloom, Overlay );

	JsonNode IJsonPopulator.Serialize()
	{
		var jso = new JsonObject();
		jso["GameLayer"] = Game;
		jso["OverlayLayer"] = Overlay;
		jso["BloomLayer"] = Bloom;
		jso["AfterUILayer"] = AfterUI;

		return jso;
	}

	void IJsonPopulator.Deserialize( JsonNode node )
	{
		if ( node is not JsonObject jso )
			return;

		Game = jso["GameLayer"]?.GetValue<bool>() ?? true;
		Overlay = jso["OverlayLayer"]?.GetValue<bool>() ?? false;
		Bloom = jso["BloomLayer"]?.GetValue<bool>() ?? false;
		AfterUI = jso["AfterUILayer"]?.GetValue<bool>() ?? false;
	}
}
