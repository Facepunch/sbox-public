// The MIT License
// Copyright © 2021 Inigo Quilez
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// https://iquilezles.org

// https://iquilezles.org/articles/distfunctions2d/

#ifndef HUD_SDF_HLSLI
#define HUD_SDF_HLSLI

float SdfLine( float2 coord, float2 a, float2 b )
{
    float2 c = coord - a;
    float2 d = b - a;
    return c.x * d.y - c.y * d.x;
}

float SdfBox( float2 coord, float2 size )
{
    float2 q = abs( coord ) - size;
    return length( max( 0, q ) ) + min( 0, max( q.x, q.y ) );
}

// Helpers

float2 InverseLerp( float2 a, float2 b, float2 v )
{
    return ( v - a ) / ( b - a );
}

float2 Remap( float2 iMin, float2 iMax, float2 oMin, float2 oMax, float2 v )
{
    float2 t = InverseLerp( iMin, iMax, v );
    return lerp( oMin, oMax, t );
}

#endif