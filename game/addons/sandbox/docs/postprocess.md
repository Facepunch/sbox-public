# Adding a spawn menu effect

Put your script and shader together under `Assets/postprocess/<name>/`. Add `[PostProcessMenu]` directly to a concrete `BasePostProcess` class. Sandbox discovers it on load or hotload and shows it in the spawn menu’s **Effects** tab and in the camera inspector.

## Grayscale example

Save this as `Assets/postprocess/grayscale/Grayscale.csx`. The title and group place it under **Light & Color → Grayscale**. Give the class a unique, stable `[ClassName]` so saved cameras can find it. Each editable `[Property]` becomes a control, and its initializer sets the default.

```csharp
using Sandbox.Rendering;

/// <summary>
/// Fades the scene to grayscale.
/// </summary>
[PostProcessMenu, Title( "Grayscale" ), ClassName( "grayscale" )]
[Group( "#effects.group.light_color" )]
public sealed class Grayscale : BasePostProcess
{
    /// <summary>
    /// How much color to remove.
    /// </summary>
    [Property, Range( 0, 1 )]
    public float Amount { get; set; } = 1.0f;

    CommandList _pass;

    /// <summary>
    /// Draws the grayscale pass over the camera image.
    /// </summary>
    public override void Render()
    {
        if ( Amount <= 0 ) return;

        Attributes.Set( "Amount", Amount );
        _pass ??= PostProcessPass.FromShader( "postprocess/grayscale/grayscale.shader", Attributes );
        InsertCommandList( _pass, Stage.AfterPostProcess, 200, "Grayscale" );
    }
}
```

Copy the [Comic Print shader](../Assets/postprocess/comicprint/comicprint.shader) to `grayscale.shader`, change its header description to `Grayscale`, and replace its `PS` block with the one below. The pass supplies the camera image as `ColorBuffer`. The `Amount` attribute name matches the script.

```hlsl
PS
{
    #include "postprocess/common.hlsl"

    Texture2D SceneColor < Attribute( "ColorBuffer" ); SrgbRead( true ); >;
    float Amount < Attribute( "Amount" ); Default( 1 ); >;

    float4 MainPs( PixelInput input ) : SV_Target0
    {
        float2 uv = CalculateViewportUv( input.position.xy );
        float4 scene = SceneColor.Sample( g_sTrilinearClamp, uv );
        float gray = GetLuminance( scene.rgb );
        return float4( lerp( scene.rgb, gray.xxx, saturate( Amount ) ), scene.a );
    }
}
```

Open **Effects → Light & Color**, enable **Grayscale**, and drag **Amount** between full color and black and white. When publishing, include both the `.csx` and the shader in the project’s resource rules.

For more examples, [Comic Print](../Assets/postprocess/comicprint/ComicPrint.csx) exposes numeric sliders, and [1-Bit Dither](../Assets/postprocess/onebitdither/OneBitDither.csx) exposes two color pickers.
