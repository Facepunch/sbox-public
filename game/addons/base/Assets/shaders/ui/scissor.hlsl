int HasScissoring <Default( 0.0 ); Attribute( "HasScissor" );>;
float4 ScissorRect < Default4( 0.0, 0.0, 0.0, 0.0 ); Attribute( "ScissorRect" ); >;
float4 ScissorCornerRadius < Default4( 0.0, 0.0, 0.0, 0.0 ); Attribute( "ScissorCornerRadius" ); >;
float4x4 ScissorTransformMat < Attribute( "ScissorTransformMat" ); >; 

// Clip anything inside this, only used for box-shadow, either combo or only doing it in ui_cssshadow would optimize if needed
int HasInverseScissoring <Default( 0.0 ); Attribute( "HasInverseScissor" );>;
float4 InverseScissorRect < Default4( 0.0, 0.0, 0.0, 0.0 ); Attribute( "InverseScissorRect" ); >;
float4 InverseScissorCornerRadius < Default4( 0.0, 0.0, 0.0, 0.0 ); Attribute( "InverseScissorCornerRadius" ); >;
float4x4 InverseScissorTransformMat < Attribute( "InverseScissorTransformMat" ); >; 

//
// I think there's cases we'd want some sort of scissor stack or stencil, I can think of a few ways to do this..
// But only want to do it if it's actually needed / limiting people
//

float2 GetWorldPixelPosition( PS_INPUT i )
{
    float2 vPos = ( BoxSize ) * ( i.vTexCoord.xy );
    vPos += BoxPosition;

    return vPos;
}

bool IsOutsideBox( float2 vPos, float4 vRect, float4 vRadius, float4x4 matTransform )
{
    // transform everything else, so the scissor rect is just aabb
    vPos = mul( matTransform, float4( vPos, 0, 1 ) ).xy;

    // rounded corners
    float2 tl = float2( vRect.x + vRadius.x, vRect.y + vRadius.x );
    float2 tr = float2( vRect.z - vRadius.y, vRect.y + vRadius.y );
    float2 bl = float2( vRect.x + vRadius.z, vRect.w - vRadius.z );
    float2 br = float2( vRect.z - vRadius.w, vRect.w - vRadius.w );

    // outside of basic rect or outside of rounded corners
    return  ( vPos.x < vRect.x || vPos.x > vRect.z || vPos.y > vRect.w || vPos.y < vRect.y ) ||
            ( length( vPos - tl ) > vRadius.x && vPos.x < tl.x && vPos.y < tl.y ) ||
            ( length( vPos - tr ) > vRadius.y && vPos.x > tr.x && vPos.y < tr.y ) ||
            ( length( vPos - bl ) > vRadius.z && vPos.x < bl.x && vPos.y > bl.y ) ||
            ( length( vPos - br ) > vRadius.w && vPos.x > br.x && vPos.y > br.y );
}

float4x4 TransformMat < Attribute( "TransformMat" ); >; 

void SoftwareScissoring( PS_INPUT i )
{
#if D_WORLDPANEL
    // For world panels, calculate local position from UV and then transform (matches screen panel behaviour where clipping happens after transforms)
    float2 localPos = ( BoxSize ) * ( i.vTexCoord.xy ) + BoxPosition;
    float2 pixelPos = mul( TransformMat, float4( localPos, 0, 1 ) ).xy;
#else
    float2 pixelPos = i.vPositionPanelSpace.xy;
#endif

    bool bShouldClip = IsOutsideBox( pixelPos, ScissorRect, ScissorCornerRadius, ScissorTransformMat );
    if ( HasInverseScissoring )
    {
        bShouldClip = bShouldClip || !IsOutsideBox( pixelPos, InverseScissorRect, InverseScissorCornerRadius, InverseScissorTransformMat );
    }

    clip( bShouldClip ? -1 : 1 );
}