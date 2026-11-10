

//
// Blend Modes (https://web.dev/learn/css/blend-modes/)
// I only filled in what I needed. A job for someone else - garry
//
DynamicCombo( D_BLENDMODE, 0..3, Sys( ALL ) );

// Alpha Blend (standard)
#if D_BLENDMODE == 0
    RenderState( BlendEnable, true );
    RenderState( SrcBlend, SRC_ALPHA );
    RenderState( DstBlend, INV_SRC_ALPHA );
    RenderState( BlendOp, ADD );
    RenderState( SrcBlendAlpha, ONE );
    RenderState( DstBlendAlpha, INV_SRC_ALPHA );
    RenderState( BlendOpAlpha, ADD );

// Multiply
#elif D_BLENDMODE == 1
    RenderState( BlendEnable, true );
    RenderState( SrcBlend, DEST_COLOR );
    RenderState( DstBlend, ZERO );
    RenderState( BlendOp, ADD );
    RenderState( SrcBlendAlpha, ONE );
    RenderState( DstBlendAlpha, ZERO );
    RenderState( BlendOpAlpha, ADD );

// Lighten / Additive
#elif D_BLENDMODE == 2
    RenderState( BlendEnable, true );
    RenderState( SrcBlend, SRC_ALPHA );
    RenderState( DstBlend, ONE );
    RenderState( BlendOp, ADD );
    RenderState( SrcBlendAlpha, ONE );
    RenderState( DstBlendAlpha, ONE );
    RenderState( BlendOpAlpha, ADD );

// Premultiplied Alpha
#elif D_BLENDMODE == 3
    RenderState( BlendEnable, true );
    RenderState( SrcBlend, ONE );
    RenderState( DstBlend, INV_SRC_ALPHA );
    RenderState( BlendOp, ADD );
    RenderState( SrcBlendAlpha, ONE );
    RenderState( DstBlendAlpha, INV_SRC_ALPHA );
    RenderState( BlendOpAlpha, ADD );
#endif