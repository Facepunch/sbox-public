namespace Basalt.Backend;

/// <summary>
/// Object attributes (<see cref="SetObjectAttributes"/>): an object's own attributes and its lightmap's or light probe's,
/// which its draws read over the pass's - the object's first, then its lighting's, then the context's. Nothing is copied
/// into the context's attributes, so the pass state stays what it was and material objects keep their resolved pass
/// bindings: a draw with another object's attributes changes only the bindings, constants and combos those attributes
/// set (see <see cref="MaterialLayout.For"/>). See docs/managed/basalt-materials.md.
/// </summary>
internal sealed partial class BackendContext
{
	BackendParameters objectLayer, lightingLayer;
	int objectLayerVersion, lightingLayerVersion;

	/// <summary>Which object attributes draws read now: 0 for none, else new each time they change.</summary>
	long layerEpoch;
	long layerCounter;

	/// <summary>The combos the object attributes set, as a key (interned), so variant lookups can be shared.</summary>
	string layerCombos = "";

	public override void SetObjectAttributes( ShaderParameters objectAttributes, ShaderParameters lightmap = null )
	{
		var o = objectAttributes as BackendParameters;
		var l = lightmap as BackendParameters;
		var ov = o?.Version ?? 0;
		var lv = l?.Version ?? 0;
		if ( ReferenceEquals( o, objectLayer ) && ReferenceEquals( l, lightingLayer ) && ov == objectLayerVersion && lv == lightingLayerVersion ) return;

		(objectLayer, lightingLayer, objectLayerVersion, lightingLayerVersion) = (o, l, ov, lv);
		if ( o is null && l is null )
		{
			layerEpoch = 0;
			layerCombos = "";
			return;
		}

		layerEpoch = ++layerCounter;
		if ( !device.MaterialObjects ) return;

		// The names they set, so layouts know what objects change; and their combos, for the variant key
		var cache = device.MaterialCache;
		var objectCombos = o is null ? "" : ObjectLayer.Of( o, cache ).Combos;
		var lightingCombos = l is null ? "" : ObjectLayer.Of( l, cache ).Combos;
		layerCombos = objectCombos.Length == 0 ? lightingCombos : lightingCombos.Length == 0 ? objectCombos : string.Intern( objectCombos + "|" + lightingCombos );
	}

	/// <summary>An attribute the object attributes set, and which of them set it; null when neither does.</summary>
	object LayerValue( StringToken name, out BackendParameters from )
	{
		from = null;
		if ( layerEpoch == 0 ) return null;
		if ( objectLayer?.Get( name ) is { } own )
		{
			from = objectLayer;
			return own;
		}

		if ( lightingLayer?.Get( name ) is { } lighting )
		{
			from = lightingLayer;
			return lighting;
		}

		return null;
	}

	/// <summary>An attribute as a draw reads it: the object's, its lighting's, else this context's.</summary>
	object Lookup( StringToken name ) => LayerValue( name, out _ ) ?? attributes.Get( name );

	/// <summary>A combo as a draw reads it: the object's, its lighting's, else this context's.</summary>
	int? LookupCombo( StringToken name )
	{
		if ( layerEpoch != 0 && (objectLayer?.GetCombo( name ) ?? lightingLayer?.GetCombo( name )) is int set ) return set;
		return attributes.GetCombo( name );
	}
}
