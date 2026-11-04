//
// Some experiments with reducing texture repition from:
// https://iquilezles.org/articles/texturerepetition/
//

struct NoTileUVs
{
    float4 ofa;
    float4 ofb;
    float4 ofc;
    float4 ofd;
    float2 uva;
    float2 uvb;
    float2 uvc;
    float2 uvd;
    float2 b;
    float2 ddxa;
    float2 ddxb;
    float2 ddxc;
    float2 ddxd;
    float2 ddya;
    float2 ddyb;
    float2 ddyc;
    float2 ddyd;
};

float4 hash4( float2 p ) { return frac(sin(float4( 1.0+dot(p,float2(37.0,17.0)),
                                                2.0+dot(p,float2(11.0,47.0)),
                                                3.0+dot(p,float2(41.0,29.0)),
                                                4.0+dot(p,float2(23.0,31.0))))*103.0); }

float3 textureNoTile2( Texture2D tex, SamplerState sampler, in float2 uv, float v )
{
    float2 p = floor( uv );
    float2 f = frac( uv );
    
    // derivatives (for correct mipmapping)
    float2 addx = ddx( uv );
    float2 addy = ddy( uv );
    
    float3 va = float3( 0, 0, 0 );
    float w1 = 0.0;
    float w2 = 0.0;
    for( int j=-1; j<=1; j++ )
    for( int i=-1; i<=1; i++ )
    {
        float2 g = float2( float(i),float(j) );
        float4 o = hash4( p + g );
        float2 r = g - f + o.xy;
        float d = dot(r,r);
        float w = exp(-5.0*d );
        float3 c = tex.SampleGrad( sampler,  uv + v*o.zw, addx, addy ).xyz;
        va += w*c;
        w1 += w;
        w2 += w*w;
    }
    
    // normal averaging --> lowers contrasts
    return va/w1;

    // contrast preserving average
    float mean = 0.3;// textureGrad( samp, uv, ddx*16.0, ddy*16.0 ).x;
    float3 res = mean + (va-w1*mean)/sqrt(w2);
    return lerp( va/w1, res, v );
}

NoTileUVs textureNoTileCalcUVs( in float2 uv )
{
    NoTileUVs ntuvs;
    float2 iuv = floor( uv );
    float2 fuv = frac( uv );

    // generate per-tile transform
    ntuvs.ofa = hash4( iuv + float2(0,0) );
    ntuvs.ofb = hash4( iuv + float2(1,0) );
    ntuvs.ofc = hash4( iuv + float2(0,1) );
    ntuvs.ofd = hash4( iuv + float2(1,1) );

    float2 uvddx = ddx( uv );
    float2 uvddy = ddy( uv );

    // transform per-tile uvs
    ntuvs.ofa.zw = sign(ntuvs.ofa.zw-0.5);
    ntuvs.ofb.zw = sign(ntuvs.ofb.zw-0.5);
    ntuvs.ofc.zw = sign(ntuvs.ofc.zw-0.5);
    ntuvs.ofd.zw = sign(ntuvs.ofd.zw-0.5);

    // uv's, and derivatives (for correct mipmapping)
    ntuvs.uva = uv*ntuvs.ofa.zw + ntuvs.ofa.xy; ntuvs.ddxa = uvddx*ntuvs.ofa.zw; ntuvs.ddya = uvddy*ntuvs.ofa.zw;
    ntuvs.uvb = uv*ntuvs.ofb.zw + ntuvs.ofb.xy; ntuvs.ddxb = uvddx*ntuvs.ofb.zw; ntuvs.ddyb = uvddy*ntuvs.ofb.zw;
    ntuvs.uvc = uv*ntuvs.ofc.zw + ntuvs.ofc.xy; ntuvs.ddxc = uvddx*ntuvs.ofc.zw; ntuvs.ddyc = uvddy*ntuvs.ofc.zw;
    ntuvs.uvd = uv*ntuvs.ofd.zw + ntuvs.ofd.xy; ntuvs.ddxd = uvddx*ntuvs.ofd.zw; ntuvs.ddyd = uvddy*ntuvs.ofd.zw;

    // fetch and blend
    ntuvs.b = smoothstep(0.25, 0.75, fuv);

    return ntuvs;
}

float4 textureNoTile( Texture2D tex, SamplerState sampler, in NoTileUVs ntuvs )
{
    // Use modified UVs to sample a texture
    return lerp( lerp( tex.SampleGrad( sampler, ntuvs.uva, ntuvs.ddxa, ntuvs.ddya ),
                    tex.SampleGrad( sampler, ntuvs.uvb, ntuvs.ddxb, ntuvs.ddyb ), ntuvs.b.x ),
                lerp( tex.SampleGrad( sampler, ntuvs.uvc, ntuvs.ddxc, ntuvs.ddyc ),
                    tex.SampleGrad( sampler, ntuvs.uvd, ntuvs.ddxd, ntuvs.ddyd ), ntuvs.b.x ), ntuvs.b.y );
}