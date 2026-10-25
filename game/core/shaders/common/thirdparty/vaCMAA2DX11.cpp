///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2018, Intel Corporation
//
// Licensed under the Apache License, Version 2.0 ( the "License" );
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// Author(s):  Filip Strugar (filip.strugar@intel.com)
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#include "Core/vaCoreIncludes.h"

#include "Rendering/vaRenderingIncludes.h"
#include "Rendering/DirectX/vaRenderDeviceContextDX11.h"
#include "Rendering/Shaders/vaSharedTypes.h"
#include "Rendering/DirectX/vaTextureDX11.h"

#include "vaCMAA2.h"

namespace VertexAsylum
{
    class vaCMAA2DX11 : public vaCMAA2
    {
        VA_RENDERING_MODULE_MAKE_FRIENDS( );
    private:

        ////////////////////////////////////////////////////////////////////////////////////
        // SHADERS
        //
        // Main shaders
        vaAutoRMI<vaComputeShader>      m_CSEdgesColor2x2;
        vaAutoRMI<vaComputeShader>      m_CSProcessCandidates;
        vaAutoRMI<vaComputeShader>      m_CSDeferredColorApply2x2;
        //
        // Helper shaders for DispatchIndirect
        vaAutoRMI<vaComputeShader>      m_CSComputeDispatchArgs;
        //
        // Debugging view shader
        vaAutoRMI<vaComputeShader>      m_CSDebugDrawEdges;
        //
        ////////////////////////////////////////////////////////////////////////////////////

        ////////////////////////////////////////////////////////////////////////////////////
        // SHADER SETTINGS
        int                             m_textureResolutionX    = 0;
        int                             m_textureResolutionY    = 0;
        int                             m_textureSampleCount    = 0;
        //
        bool                            m_extraSharpness        = false;
        int                             m_qualityPreset         = -1;
        ////////////////////////////////////////////////////////////////////////////////////

        // previous call's external inputs - used to figure out if we need to re-create dependencies (not using weak ptrs because no way to distinguish between null and expired - I think at least?)
        shared_ptr<vaTexture>           m_externalInOutColor;
        shared_ptr<vaTexture>           m_externalOptionalInLuma; 
        shared_ptr<vaTexture>           m_externalInColorMS;
        shared_ptr<vaTexture>           m_externalInColorMSComplexityMask;

        ////////////////////////////////////////////////////////////////////////////////////
        // IN/OUT BUFFER VIEWS
        ID3D11ShaderResourceView *      m_inoutColorReadonlySRV                 = nullptr;
        ID3D11UnorderedAccessView *     m_inoutColorWriteonlyUAV                = nullptr;
        //
        ID3D11ShaderResourceView *      m_inLumaReadonlySRV                     = nullptr;
        //
        ID3D11ShaderResourceView *      m_inColorMSReadonlySRV                  = nullptr;
        ID3D11ShaderResourceView *      m_inColorMSComplexityMaskReadonlySRV    = nullptr;
        //
        ////////////////////////////////////////////////////////////////////////////////////

        ////////////////////////////////////////////////////////////////////////////////////
        // used only for .Gather - can be avoided for slight loss of perf
        ID3D11SamplerState *            m_pointSampler                          = nullptr;
        ////////////////////////////////////////////////////////////////////////////////////

        ////////////////////////////////////////////////////////////////////////////////////
        // WORKING BUFFERS
        //
        // This texture stores the edges output by EdgeColor2x2CS
        shared_ptr<vaTexture>           m_workingEdges                          = nullptr;
        //
        // This buffer stores potential shapes for further processing, filled in EdgesColor2x2CS and read/used by ProcessCandidatesCS; each element is a pixel location encoded as (pixelPos.x << 16) | pixelPos.y
        ID3D11UnorderedAccessView *     m_workingShapeCandidatesUAV             = nullptr;
        //
        // This buffer stores a list of pixel coordinates (locations) that contain one or more anti-aliased color values generated in ProcessCanidatesCS; coordinates are in 2x2 quad locations (instead of simple per-pixel) for memory usage reasons; this is used used by DeferredColorApply2x2CS
        ID3D11UnorderedAccessView *     m_workingDeferredBlendLocationListUAV   = nullptr;
        //
        // This buffer contains per-location linked lists with the actual anti-aliased color values.
        ID3D11UnorderedAccessView *     m_workingDeferredBlendItemListUAV       = nullptr;
        //
        // This buffer contains per-location linked list heads (pointing to 'workingDeferredBlendItemList') (to add to confusion, it's all in 2x2-sized chunks to reduce memory usage)
        shared_ptr<vaTexture>           m_workingDeferredBlendItemListHeads     = nullptr;
        //
        // Global counters & info for setting up DispatchIndirect
        ID3D11Buffer *                  m_workingControlBuffer                  = nullptr;
        ID3D11UnorderedAccessView *     m_workingControlBufferUAV               = nullptr;
        // DispatchIndirect/ExecuteIndirect buffer
        ID3D11Buffer *                  g_workingExecuteIndirectBuffer          = nullptr;
        ID3D11UnorderedAccessView *     g_workingExecuteIndirectBufferUAV       = nullptr;
        //
        ////////////////////////////////////////////////////////////////////////////////////

    protected:
        explicit vaCMAA2DX11( const vaRenderingModuleParams & params );
        ~vaCMAA2DX11( );

    private:
        virtual vaDrawResultFlags       Draw( vaRenderDeviceContext & deviceContext, const shared_ptr<vaTexture> & inoutColor, const shared_ptr<vaTexture> & optionalInLuma ) override;
        virtual vaDrawResultFlags       DrawMS( vaRenderDeviceContext & deviceContext, const shared_ptr<vaTexture> & inoutColor, const shared_ptr<vaTexture> & inColorMS, const shared_ptr<vaTexture> & inColorMSComplexityMask ) override;
        virtual void                    CleanupTemporaryResources( ) override;

    private:
        bool                            UpdateResources( vaRenderDeviceContext & deviceContext, const shared_ptr<vaTexture> & inoutColor, const shared_ptr<vaTexture> & optionalInLuma = nullptr, const shared_ptr<vaTexture> & inColorMS = nullptr, const shared_ptr<vaTexture> & inColorMSComplexityMask = nullptr );
        void                            Reset( );

    private:
        vaDrawResultFlags               Execute( vaRenderDeviceContext & deviceContext );
    };

}

using namespace VertexAsylum;

static const bool c_useTypedUAVStores = false;

vaCMAA2DX11::vaCMAA2DX11( const vaRenderingModuleParams & params ) : vaCMAA2( params ), 
        m_CSEdgesColor2x2( params.RenderDevice ),
        m_CSProcessCandidates( params.RenderDevice ),
        m_CSDeferredColorApply2x2( params.RenderDevice ),
        m_CSComputeDispatchArgs( params.RenderDevice ),
        m_CSDebugDrawEdges( params.RenderDevice )
{
    params; // unreferenced

    Reset( );

    ID3D11Device * device = params.RenderDevice.SafeCast<vaRenderDeviceDX11*>( )->GetPlatformDevice();
    HRESULT hr;
    {
        CD3D11_SAMPLER_DESC desc = CD3D11_SAMPLER_DESC( CD3D11_DEFAULT() );

        desc.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
        desc.AddressU = desc.AddressV = desc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        V( device->CreateSamplerState( &desc, &m_pointSampler ) );
     }
}

vaCMAA2DX11::~vaCMAA2DX11( )
{
    CleanupTemporaryResources();
    SAFE_RELEASE( m_pointSampler );
}

void vaCMAA2DX11::Reset( )
{
    m_textureResolutionX            = 0;
    m_textureResolutionY            = 0;
    m_textureSampleCount            = 0;
}


// helper
static HRESULT CreateBufferAndViews( ID3D11Device *pDevice, const D3D11_BUFFER_DESC &BuffDesc, D3D11_SUBRESOURCE_DATA *pInitData, ID3D11Buffer**ppBuffer, ID3D11ShaderResourceView **ppSRV, ID3D11UnorderedAccessView **ppUAV, UINT UAVFlags /*= 0*/ )
{
    HRESULT hr;

    ID3D11Buffer * dummyBuffer = nullptr;
    if( ppBuffer == nullptr ) 
        ppBuffer = &dummyBuffer;

    V_RETURN( pDevice->CreateBuffer( &BuffDesc, pInitData, ppBuffer ) );

    if( ppSRV )
    {
        D3D11_SHADER_RESOURCE_VIEW_DESC SRVDesc;
        ZeroMemory( &SRVDesc, sizeof( SRVDesc ) );
        SRVDesc.Format = (BuffDesc.MiscFlags & D3D11_RESOURCE_MISC_BUFFER_STRUCTURED)?(DXGI_FORMAT_UNKNOWN):(DXGI_FORMAT_R32_UINT);
        SRVDesc.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
        SRVDesc.Buffer.FirstElement = 0;
        SRVDesc.Buffer.NumElements = BuffDesc.ByteWidth / BuffDesc.StructureByteStride;
        V_RETURN( pDevice->CreateShaderResourceView( *ppBuffer, &SRVDesc, ppSRV ) );
    }

    if( ppUAV )
    {
        D3D11_UNORDERED_ACCESS_VIEW_DESC UAVDesc;
        UAVDesc.Format = (BuffDesc.MiscFlags & D3D11_RESOURCE_MISC_BUFFER_STRUCTURED)?(DXGI_FORMAT_UNKNOWN):(DXGI_FORMAT_R32_UINT);
        if( (UAVFlags & D3D11_BUFFER_UAV_FLAG_RAW) )
            UAVDesc.Format = DXGI_FORMAT_R32_TYPELESS;
        UAVDesc.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
        UAVDesc.Buffer.FirstElement = 0;
        UAVDesc.Buffer.NumElements = BuffDesc.ByteWidth / BuffDesc.StructureByteStride;
        UAVDesc.Buffer.Flags = UAVFlags;
        V_RETURN( pDevice->CreateUnorderedAccessView( *ppBuffer, &UAVDesc, ppUAV ) );
    }

    SAFE_RELEASE( dummyBuffer );

    return S_OK;
}

static bool CheckUAVTypedStoreFormatSupport( ID3D11Device* device, DXGI_FORMAT format )
{
    D3D11_FEATURE_DATA_FORMAT_SUPPORT   capsFormatSupport   = { format };
    D3D11_FEATURE_DATA_FORMAT_SUPPORT2  capsFormatSupport2  = { format };
    HRESULT hr;
    hr = device->CheckFeatureSupport( D3D11_FEATURE_FORMAT_SUPPORT, &capsFormatSupport, sizeof( capsFormatSupport ) );
    assert( SUCCEEDED( hr ) );
    hr = device->CheckFeatureSupport( D3D11_FEATURE_FORMAT_SUPPORT2, &capsFormatSupport2, sizeof( capsFormatSupport2 ) );
    assert( SUCCEEDED( hr ) );

    bool typed_unordered_access_view    = (capsFormatSupport.OutFormatSupport & D3D11_FORMAT_SUPPORT_TYPED_UNORDERED_ACCESS_VIEW) != 0;     // Format can be used for an unordered access view.

    bool uav_typed_store                = ( capsFormatSupport2.OutFormatSupport2 & D3D11_FORMAT_SUPPORT2_UAV_TYPED_STORE ) != 0;            // Format supports a typed store.

    return typed_unordered_access_view && uav_typed_store;
}

static ID3D11UnorderedAccessView * CreateUnorderedAccessView( ID3D11Resource * resource, DXGI_FORMAT format = DXGI_FORMAT_UNKNOWN,        int mipSliceMin = 0, int arraySliceMin = 0, int arraySliceCount = -1 )
{
    ID3D11UnorderedAccessView * ret = NULL;

    D3D11_UNORDERED_ACCESS_VIEW_DESC desc;
    D3D11_UAV_DIMENSION dimension = D3D11_UAV_DIMENSION_UNKNOWN;

    ID3D11Texture2D * texture2D = NULL;
    if( SUCCEEDED( resource->QueryInterface( IID_ID3D11Texture2D, (void**)&texture2D ) ) )
    {
        D3D11_TEXTURE2D_DESC descTex2D;
        texture2D->GetDesc( &descTex2D );

        if( arraySliceCount == -1 )
            arraySliceCount = descTex2D.ArraySize - arraySliceMin;

        assert( mipSliceMin >= 0 && (UINT)mipSliceMin < descTex2D.MipLevels );
        assert( arraySliceMin >= 0 && (UINT)arraySliceMin < descTex2D.ArraySize );
        assert( arraySliceMin + arraySliceCount > 0 && (UINT)arraySliceMin + arraySliceCount <= descTex2D.ArraySize );

        dimension = ( descTex2D.ArraySize == 1 ) ? ( D3D11_UAV_DIMENSION_TEXTURE2D ) : ( D3D11_UAV_DIMENSION_TEXTURE2DARRAY );

        desc = CD3D11_UNORDERED_ACCESS_VIEW_DESC( texture2D, dimension, format );

        if( dimension == D3D11_UAV_DIMENSION_TEXTURE2D )
        {
            desc.Texture2D.MipSlice             = mipSliceMin;
            assert( arraySliceMin == 0 );
        }
        else if( dimension == D3D11_UAV_DIMENSION_TEXTURE2DARRAY )
        {
            desc.Texture2DArray.MipSlice        = mipSliceMin;
            desc.Texture2DArray.FirstArraySlice = arraySliceMin;
            desc.Texture2DArray.ArraySize       = arraySliceCount;
        }
        else { assert( false ); }

        SAFE_RELEASE( texture2D );
    }
    else
    {
        assert( false );
        return nullptr;
    }

    ID3D11Device * device = nullptr;
    resource->GetDevice(&device);
    device->Release(); // yeah, ugly - we know at minimum texture will guarantee device persistence but still...
    if( SUCCEEDED( device->CreateUnorderedAccessView( resource, &desc, &ret ) ) )
    {
        return ret;
    }
    else
    {
        assert( false );
        return NULL;
    }
}

void vaCMAA2DX11::CleanupTemporaryResources( )
{
    SAFE_RELEASE( m_workingShapeCandidatesUAV );
    SAFE_RELEASE( m_workingDeferredBlendLocationListUAV );
    SAFE_RELEASE( m_workingDeferredBlendItemListUAV );
    SAFE_RELEASE( m_workingControlBuffer );
    SAFE_RELEASE( m_workingControlBufferUAV );
    SAFE_RELEASE( g_workingExecuteIndirectBuffer );
    SAFE_RELEASE( g_workingExecuteIndirectBufferUAV );
    SAFE_RELEASE( m_inoutColorReadonlySRV   );
    SAFE_RELEASE( m_inoutColorWriteonlyUAV  );
    SAFE_RELEASE( m_inLumaReadonlySRV );
    SAFE_RELEASE( m_inColorMSReadonlySRV  );
    SAFE_RELEASE( m_inColorMSComplexityMaskReadonlySRV  );

    m_workingEdges                      = nullptr;
    m_workingDeferredBlendItemListHeads = nullptr;

    m_externalInOutColor                = nullptr;
    m_externalOptionalInLuma            = nullptr;
    m_externalInColorMS                 = nullptr;
    m_externalInColorMSComplexityMask   = nullptr;

    Reset();
}

bool vaCMAA2DX11::UpdateResources( vaRenderDeviceContext & deviceContext, const shared_ptr<vaTexture> & inoutColor, const shared_ptr<vaTexture> & optionalInLuma, const shared_ptr<vaTexture> & inColorMS, const shared_ptr<vaTexture> & inColorMSComplexityMask )
{
    deviceContext;
    int resX = inoutColor->GetSizeX();
    int resY = inoutColor->GetSizeY();

    bool staticSettingsChanged = m_qualityPreset != m_settings.QualityPreset;
    staticSettingsChanged |= m_extraSharpness != m_settings.ExtraSharpness;

    // all is fine, no need to update anything (unless I made a mistake somewhere in which case yikes!)
    if( m_externalInOutColor == inoutColor && m_externalOptionalInLuma == optionalInLuma && m_externalInColorMS == inColorMS && m_externalInColorMSComplexityMask == inColorMSComplexityMask
        && !staticSettingsChanged )
    {
        assert( (inColorMS == nullptr) == (m_textureSampleCount == 1) );
        return true;
    }

    CleanupTemporaryResources();

    // track the external inputs so we can re-create
    m_externalInOutColor                = inoutColor;
    m_externalOptionalInLuma            = optionalInLuma;
    m_externalInColorMS                 = inColorMS;
    m_externalInColorMSComplexityMask   = inColorMSComplexityMask;

    m_qualityPreset                     = m_settings.QualityPreset;
    m_extraSharpness                    = m_settings.ExtraSharpness;
    m_textureResolutionX                = inoutColor->GetSizeX();
    m_textureResolutionY                = inoutColor->GetSizeY();
    m_textureSampleCount                = 1;

    if( inColorMS != nullptr )
    {
        assert( m_inColorMSReadonlySRV == nullptr );
        m_inColorMSReadonlySRV        = inColorMS->SafeCast<vaTextureDX11*>( )->GetSRV( );
        m_inColorMSReadonlySRV->AddRef();
        if( inColorMSComplexityMask != nullptr )
        {
            m_inColorMSComplexityMaskReadonlySRV = inColorMSComplexityMask->SafeCast<vaTextureDX11*>( )->GetSRV( );
            m_inColorMSComplexityMaskReadonlySRV->AddRef();
        }

        m_textureSampleCount = inColorMS->GetArrayCount();
        assert( m_textureResolutionX == inColorMS->GetSizeX() );
        assert( m_textureResolutionY == inColorMS->GetSizeY() );
    }

    if( optionalInLuma != nullptr )
    {
        assert( m_inLumaReadonlySRV == nullptr );
        m_inLumaReadonlySRV = optionalInLuma->SafeCast<vaTextureDX11*>( )->GetSRV( );
        m_inLumaReadonlySRV->AddRef();
    }

#if !defined(CMAA_PACK_SINGLE_SAMPLE_EDGE_TO_HALF_WIDTH) || !defined(CMAA2_CS_INPUT_KERNEL_SIZE_X) || !defined(CMAA2_CS_INPUT_KERNEL_SIZE_Y)
#error Forgot to include CMAA2.hlsl?
#endif        

    vector< pair< string, string > > shaderMacros;

    shaderMacros.push_back( { "CMAA2_STATIC_QUALITY_PRESET", vaStringTools::Format("%d", m_qualityPreset) } );
    shaderMacros.push_back( { "CMAA2_EXTRA_SHARPNESS", vaStringTools::Format("%d", (m_extraSharpness)?(1):(0) ) } );

    if( m_textureSampleCount != 1 )
        shaderMacros.push_back( { "CMAA_MSAA_SAMPLE_COUNT", vaStringTools::Format("%d", m_textureSampleCount) } );

    ID3D11Device * d3d11Device = GetRenderDevice().SafeCast<vaRenderDeviceDX11*>( )->GetPlatformDevice();

    // support for various color format combinations
    {
        m_inoutColorReadonlySRV  = inoutColor->SafeCast<vaTextureDX11*>( )->GetSRV();
        m_inoutColorReadonlySRV->AddRef();
        
        D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc;
        m_inoutColorReadonlySRV->GetDesc( &srvDesc );

        DXGI_FORMAT srvFormat = srvDesc.Format;
        DXGI_FORMAT srvFormatStrippedSRGB = DXGIFormatFromVA( vaResourceFormatHelpers::StripSRGB( VAFormatFromDXGI( srvDesc.Format ) ) );

        bool validInputOutputs = false;
        // Assume we don't support typed UAV store-s for our combination of inputs/outputs - reset if we do
        bool convertToSRGBOnOutput  = vaResourceFormatHelpers::IsSRGB( inoutColor->GetSRVFormat( ) );

        assert( m_inoutColorWriteonlyUAV == nullptr );

        bool hdrFormat = vaResourceFormatHelpers::IsFloat( inoutColor->GetSRVFormat( ) );;
        bool uavStoreTyped = false;
        bool uavStoreTypesUnormFloat = false;

        // if we support direct writes to this format - excellent, just create an UAV on it and Bob's your uncle
        if( CheckUAVTypedStoreFormatSupport( d3d11Device, srvFormat ) )
        {
            m_inoutColorWriteonlyUAV = CreateUnorderedAccessView( inoutColor->SafeCast<vaTextureDX11*>( )->GetTexture2D() );
            assert( m_inoutColorWriteonlyUAV != nullptr );   // check DX errors for clues
            validInputOutputs = m_inoutColorWriteonlyUAV != nullptr;
            convertToSRGBOnOutput = false;      // no conversion will be needed as the GPU supports direct typed UAV store

            uavStoreTyped = true;
            uavStoreTypesUnormFloat = !vaResourceFormatHelpers::IsFloat( inoutColor->GetSRVFormat( ) );
        }
        // maybe just sRGB UAV store is not supported?
        else if( CheckUAVTypedStoreFormatSupport( d3d11Device, srvFormatStrippedSRGB ) )
        {
            m_inoutColorWriteonlyUAV = CreateUnorderedAccessView( inoutColor->SafeCast<vaTextureDX11*>( )->GetTexture2D(), srvFormatStrippedSRGB );
            assert( m_inoutColorWriteonlyUAV != nullptr ); // check DX errors for clues
            validInputOutputs = m_inoutColorWriteonlyUAV != nullptr;
            uavStoreTyped = true;
            uavStoreTypesUnormFloat = !vaResourceFormatHelpers::IsFloat( inoutColor->GetSRVFormat( ) );
        }
        // ok we have to encode manually
        else
        {
            m_inoutColorWriteonlyUAV = CreateUnorderedAccessView( inoutColor->SafeCast<vaTextureDX11*>( )->GetTexture2D(), DXGI_FORMAT_R32_UINT );
            assert( m_inoutColorWriteonlyUAV != nullptr ); // check DX errors for clues
            validInputOutputs = m_inoutColorWriteonlyUAV != nullptr;
            if( validInputOutputs )
            {
                // the need for pre-store sRGB conversion already accounted for above by 'convertToSRGBOnOutput'
                switch( srvFormatStrippedSRGB )
                {
                case DXGI_FORMAT_R8G8B8A8_UNORM:    shaderMacros.push_back( std::pair<std::string, std::string>( "CMAA2_UAV_STORE_UNTYPED_FORMAT", "1" ) );     break;
                case DXGI_FORMAT_R10G10B10A2_UNORM: shaderMacros.push_back( std::pair<std::string, std::string>( "CMAA2_UAV_STORE_UNTYPED_FORMAT", "2" ) );     break;
                default: { assert( false ); validInputOutputs = false; } // add support
                }
            }
        }

        // force manual conversion to sRGB before write
        shaderMacros.push_back( std::pair<std::string, std::string>( "CMAA2_UAV_STORE_TYPED", ( uavStoreTyped ) ? ( "1" ) : ( "0" ) ) );
        shaderMacros.push_back( std::pair<std::string, std::string>( "CMAA2_UAV_STORE_TYPED_UNORM_FLOAT", ( uavStoreTypesUnormFloat ) ? ( "1" ) : ( "0" ) ) );
        shaderMacros.push_back( std::pair<std::string, std::string>( "CMAA2_UAV_STORE_CONVERT_TO_SRGB", ( convertToSRGBOnOutput ) ? ( "1" ) : ( "0" ) ) );
        shaderMacros.push_back( std::pair<std::string, std::string>( "CMAA2_SUPPORT_HDR_COLOR_RANGE", ( hdrFormat ) ? ( "1" ) : ( "0" ) ) );

        if( !validInputOutputs )
        {
            assert( false );
            CleanupTemporaryResources();
            return false;
        }
    }

    if( optionalInLuma != nullptr )
        shaderMacros.push_back( std::pair<std::string, std::string>( "CMAA2_EDGE_DETECTION_LUMA_PATH", "2" ) );

    // create all temporary storage buffers
    {
        vaResourceFormat edgesFormat;
        int edgesResX = resX;
#if CMAA_PACK_SINGLE_SAMPLE_EDGE_TO_HALF_WIDTH // adds more ALU but reduces memory use for edges by half by packing two 4 bit edge info into one R8_UINT texel - helps on all HW except at really low res
        if( m_textureSampleCount == 1 ) edgesResX = ( edgesResX + 1 ) / 2;
#endif // CMAA_PACK_SINGLE_SAMPLE_EDGE_TO_HALF_WIDTH

        switch( m_textureSampleCount )
        {
        case( 1 ): edgesFormat = vaResourceFormat::R8_UINT;   break;
        case( 2 ): edgesFormat = vaResourceFormat::R8_UINT;   break;
        case( 4 ): edgesFormat = vaResourceFormat::R16_UINT;   break;
        case( 8 ): edgesFormat = vaResourceFormat::R32_UINT;   break;
        default: assert( false ); edgesFormat = vaResourceFormat::Unknown;
        }

        m_workingEdges = vaTexture::Create2D( GetRenderDevice( ), edgesFormat, edgesResX, resY, 1, 1, 1, vaResourceBindSupportFlags::UnorderedAccess );

        m_workingDeferredBlendItemListHeads = vaTexture::Create2D( GetRenderDevice( ), vaResourceFormat::R32_UINT, ( resX + 1 ) / 2, ( resY + 1 ) / 2, 1, 1, 1, vaResourceBindSupportFlags::UnorderedAccess );

        HRESULT hr;

#if 0
        // completely safe version even with very high noise
        int requiredCandidatePixels = resX * resY * m_textureSampleCount;                   // enough in all cases
        int requiredDeferredColorApplyBuffer = resX * resY * m_textureSampleCount;          // enough in all cases
        int requiredListHeadsPixels = ( resX * resY + 3 ) / 4;                                // enough in all cases - this covers the whole screen since list is in 2x2 quads
#else
        // 99.99% safe version that uses less memory but will start running out of storage in extreme cases (and start ignoring edges in a non-deterministic way)
        // on an average scene at ULTRA preset only 1/4 of below is used but we leave 4x margin for extreme cases like full screen dense foliage
        int requiredCandidatePixels = resX * resY / 4 * m_textureSampleCount;
        int requiredDeferredColorApplyBuffer = resX * resY / 2 * m_textureSampleCount;
        int requiredListHeadsPixels = ( resX * resY + 3 ) / 6;
#endif

        // Create buffer for storing a list of all pixel candidates to process (potential AA shapes, both simple and complex)
        {
            CD3D11_BUFFER_DESC cdesc( requiredCandidatePixels * sizeof( UINT ), D3D11_BIND_UNORDERED_ACCESS, D3D11_USAGE_DEFAULT, 0, D3D11_RESOURCE_MISC_BUFFER_STRUCTURED, sizeof( UINT ) );
            V( CreateBufferAndViews( d3d11Device, cdesc, nullptr, nullptr, nullptr, &m_workingShapeCandidatesUAV, 0 ) );
        }

        // Create buffer for storing linked list of all output values to blend
        {
            CD3D11_BUFFER_DESC cdesc( requiredDeferredColorApplyBuffer * sizeof( UINT ) * 2, D3D11_BIND_UNORDERED_ACCESS, D3D11_USAGE_DEFAULT, 0, D3D11_RESOURCE_MISC_BUFFER_STRUCTURED, sizeof( UINT ) * 2 );
            V( CreateBufferAndViews( d3d11Device, cdesc, nullptr, nullptr, nullptr, &m_workingDeferredBlendItemListUAV, 0 ) );
        }

        // Create buffer for storing a list of coordinates of linked list heads quads, to allow for combined processing in the last step
        {
            CD3D11_BUFFER_DESC cdesc( requiredListHeadsPixels * sizeof( UINT ), D3D11_BIND_UNORDERED_ACCESS, D3D11_USAGE_DEFAULT, 0, D3D11_RESOURCE_MISC_BUFFER_STRUCTURED, sizeof( UINT ) );
            V( CreateBufferAndViews( d3d11Device, cdesc, nullptr, nullptr, nullptr, &m_workingDeferredBlendLocationListUAV, 0 ) );
        }

        // Control buffer (always the same size, doesn't need re-creating but oh well)
        {
            CD3D11_BUFFER_DESC cdesc( 16 * sizeof( UINT ), D3D11_BIND_UNORDERED_ACCESS, D3D11_USAGE_DEFAULT, 0, D3D11_RESOURCE_MISC_BUFFER_ALLOW_RAW_VIEWS, sizeof( UINT ) );
            UINT initData[16] = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            D3D11_SUBRESOURCE_DATA srd;
            srd.pSysMem = initData; srd.SysMemPitch = srd.SysMemSlicePitch = 0;
            V( CreateBufferAndViews( d3d11Device, cdesc, &srd, &m_workingControlBuffer, nullptr, &m_workingControlBufferUAV, D3D11_BUFFER_UAV_FLAG_RAW ) );
        }
        // Control buffer (always the same size, doesn't need re-creating but oh well)
        {
            CD3D11_BUFFER_DESC cdesc( 4 * sizeof( UINT ), D3D11_BIND_UNORDERED_ACCESS, D3D11_USAGE_DEFAULT, 0, D3D11_RESOURCE_MISC_BUFFER_ALLOW_RAW_VIEWS | D3D11_RESOURCE_MISC_DRAWINDIRECT_ARGS, sizeof( UINT ) );
            V( CreateBufferAndViews( d3d11Device, cdesc, nullptr, &g_workingExecuteIndirectBuffer, nullptr, &g_workingExecuteIndirectBufferUAV, D3D11_BUFFER_UAV_FLAG_RAW ) );
        }
    }

    // Update shaders to match input/output format permutations based on support
    {
#if 0
        // We can check feature support for this but there's no point really since DX should fall back to 32 if min16float not supported anyway
        D3D11_FEATURE_DATA_SHADER_MIN_PRECISION_SUPPORT capsMinPrecisionSupport;
        hr = device->CheckFeatureSupport( D3D11_FEATURE_SHADER_MIN_PRECISION_SUPPORT, &capsMinPrecisionSupport, sizeof( capsMinPrecisionSupport ) );
        if( capsMinPrecisionSupport.AllOtherShaderStagesMinPrecision != 0 )
            shaderMacros.push_back( std::pair<std::string, std::string>( "CMAA2_USE_HALF_FLOAT_PRECISION", "1" ) );
        else
            shaderMacros.push_back( std::pair<std::string, std::string>( "CMAA2_USE_HALF_FLOAT_PRECISION", "0" ) );
#endif

        m_CSEdgesColor2x2->CreateShaderFromFile( L"CMAA2/CMAA2.hlsl", "cs_5_0", "EdgesColor2x2CS", shaderMacros, false );
        m_CSProcessCandidates->CreateShaderFromFile( L"CMAA2/CMAA2.hlsl", "cs_5_0", "ProcessCandidatesCS", shaderMacros, false );
        m_CSDeferredColorApply2x2->CreateShaderFromFile( L"CMAA2/CMAA2.hlsl", "cs_5_0", "DeferredColorApply2x2CS", shaderMacros, false );
        m_CSComputeDispatchArgs->CreateShaderFromFile( L"CMAA2/CMAA2.hlsl", "cs_5_0", "ComputeDispatchArgsCS", shaderMacros, false );
        m_CSDebugDrawEdges->CreateShaderFromFile( L"CMAA2/CMAA2.hlsl", "cs_5_0", "DebugDrawEdgesCS", shaderMacros, false );
    }

    return true;
}

vaDrawResultFlags vaCMAA2DX11::DrawMS( vaRenderDeviceContext & deviceContext, const shared_ptr<vaTexture> & inoutColor, const shared_ptr<vaTexture> & inColorMS, const shared_ptr<vaTexture> & inColorMSComplexityMask )
{
    vaRenderDeviceContext::RenderOutputsState rtState = deviceContext.GetOutputs( );

    deviceContext.SetRenderTarget( nullptr, nullptr, false );

    if( !UpdateResources( deviceContext, inoutColor, nullptr, inColorMS, inColorMSComplexityMask ) )
    {
        assert( false );
        return vaDrawResultFlags::UnspecifiedError;
    }

    vaDrawResultFlags renderResults = Execute( deviceContext );

    // restore previous RTs
    deviceContext.SetOutputs( rtState );

    return renderResults;
}

vaDrawResultFlags vaCMAA2DX11::Draw( vaRenderDeviceContext & deviceContext, const shared_ptr<vaTexture> & inoutColor, const shared_ptr<vaTexture> & optionalInLuma )
{
    vaRenderDeviceContext::RenderOutputsState rtState = deviceContext.GetOutputs( );

    deviceContext.SetRenderTarget( nullptr, nullptr, false );

    if( !UpdateResources( deviceContext, inoutColor, optionalInLuma, nullptr, nullptr ) )
    {
        assert( false );
        return vaDrawResultFlags::UnspecifiedError;
    }

    vaDrawResultFlags renderResults = Execute( deviceContext );

    // restore previous RTs
    deviceContext.SetOutputs( rtState );
    
    return renderResults;
}

vaDrawResultFlags vaCMAA2DX11::Execute( vaRenderDeviceContext & deviceContext )
{
    ID3D11DeviceContext * dx11Context = vaSaferStaticCast< vaRenderDeviceContextDX11 * >( &deviceContext )->GetDXContext( );

    // make sure shaders are not still compiling
    m_CSEdgesColor2x2->WaitFinishIfBackgroundCreateActive();
    m_CSProcessCandidates->WaitFinishIfBackgroundCreateActive();
    m_CSDeferredColorApply2x2->WaitFinishIfBackgroundCreateActive();
    m_CSComputeDispatchArgs->WaitFinishIfBackgroundCreateActive();

    ID3D11ComputeShader * shaderEdgesColor2x2          = m_CSEdgesColor2x2         ->SafeCast<vaComputeShaderDX11*>()->GetShader();
    ID3D11ComputeShader * shaderProcessCandidates      = m_CSProcessCandidates     ->SafeCast<vaComputeShaderDX11*>()->GetShader();
    ID3D11ComputeShader * shaderDeferredColorApply2x2  = m_CSDeferredColorApply2x2 ->SafeCast<vaComputeShaderDX11*>()->GetShader();
    ID3D11ComputeShader * shaderComputeDispatchArgs    = m_CSComputeDispatchArgs   ->SafeCast<vaComputeShaderDX11*>()->GetShader();
    ID3D11ComputeShader * shaderDebugDrawEdges         = m_CSDebugDrawEdges        ->SafeCast<vaComputeShaderDX11*>()->GetShader();
    if( shaderEdgesColor2x2 == nullptr || shaderProcessCandidates  == nullptr || shaderDeferredColorApply2x2  == nullptr || shaderComputeDispatchArgs  == nullptr || shaderDebugDrawEdges  == nullptr )
        {   /*VA_WARN( "CMAA2: Not all shaders compiled, can't run" );*/ return vaDrawResultFlags::ShadersStillCompiling; }

    // UpdateConstants( deviceContext );

    dx11Context->CSSetSamplers( 0, 1, &m_pointSampler );

    ID3D11UnorderedAccessView * UAVs[]                      = { nullptr, m_workingEdges->SafeCast<vaTextureDX11*>( )->GetUAV( ), m_workingShapeCandidatesUAV, m_workingDeferredBlendLocationListUAV, m_workingDeferredBlendItemListUAV, m_workingDeferredBlendItemListHeads->SafeCast<vaTextureDX11*>( )->GetUAV( ), m_workingControlBufferUAV, g_workingExecuteIndirectBufferUAV };
    ID3D11UnorderedAccessView * nullUAVs[_countof( UAVs )]  = { nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr };

    // Warning: the input SRV >must< be in UNORM_SRGB format if the resource is sRGB
    ID3D11ShaderResourceView * SRVs[]                       = { nullptr, nullptr, nullptr, m_inLumaReadonlySRV };
    ID3D11ShaderResourceView * nullSRVs[_countof( SRVs )]   = { nullptr, nullptr, nullptr, nullptr };

    // multisample surface case
    if( m_inColorMSReadonlySRV != nullptr )
    {
        // we read from the MS surface...
        SRVs[2] = m_inColorMSReadonlySRV;
        // ...and write all resolved pixels to output buffer (and then later write anti-aliased ones again on top of them)
        UAVs[0] = m_inoutColorWriteonlyUAV;

        // also set multisample complexity mask SRV, if provided
        SRVs[1] = m_inColorMSComplexityMaskReadonlySRV;
    }
    else
    {
        SRVs[0] = m_inoutColorReadonlySRV;
    }

    dx11Context->CSSetUnorderedAccessViews( 0, _countof(UAVs), UAVs, nullptr );
    dx11Context->CSSetShaderResources( 0, _countof( SRVs ), SRVs );

    // first pass edge detect
    {
        VA_SCOPE_CPUGPU_TIMER( DetectEdges2x2, deviceContext );
        int csOutputKernelSizeX = CMAA2_CS_INPUT_KERNEL_SIZE_X - 2; // m_csInputKernelSizeX - 2;
        int csOutputKernelSizeY = CMAA2_CS_INPUT_KERNEL_SIZE_Y - 2; // m_csInputKernelSizeY - 2;
        int threadGroupCountX   = ( m_textureResolutionX + csOutputKernelSizeX * 2 - 1 ) / (csOutputKernelSizeX * 2);
        int threadGroupCountY   = ( m_textureResolutionY + csOutputKernelSizeY * 2 - 1 ) / (csOutputKernelSizeY * 2);
        dx11Context->CSSetShader( shaderEdgesColor2x2, nullptr, 0 );
        dx11Context->Dispatch( threadGroupCountX, threadGroupCountY, 1 );
    }

    // Set up for the first DispatchIndirect
    {
        VA_SCOPE_CPUGPU_TIMER( ComputeDispatchArgs1CS, deviceContext );
        dx11Context->CSSetShader( shaderComputeDispatchArgs, nullptr, 0 );
        dx11Context->Dispatch( 2, 1, 1 );
    }

    // Process shape candidates DispatchIndirect
    {
        VA_SCOPE_CPUGPU_TIMER( ProcessCandidates, deviceContext );
        dx11Context->CSSetShader( shaderProcessCandidates, nullptr, 0 );
        dx11Context->DispatchIndirect( g_workingExecuteIndirectBuffer, 0 );
    }

    // Set up for the second DispatchIndirect
    {
        VA_SCOPE_CPUGPU_TIMER( ComputeDispatchArgs2CS, deviceContext );
        dx11Context->CSSetShader( shaderComputeDispatchArgs, nullptr, 0 );
        dx11Context->Dispatch( 1, 2, 1 );
    }

    SRVs[0] = nullptr;                                                      // remove input colors
    if( m_textureSampleCount != 1 )
        SRVs[2] = m_inColorMSReadonlySRV;                                           // special case for MSAA - needs input MSAA colors for correct resolve in case of only partially touched pixels
    dx11Context->CSSetShaderResources( 0, _countof( SRVs ), SRVs );

    UAVs[0] = m_inoutColorWriteonlyUAV;                                     // set output colors
    dx11Context->CSSetUnorderedAccessViews( 0, _countof( UAVs ), UAVs, nullptr );

    // Resolve & apply blended colors
    {
        VA_SCOPE_CPUGPU_TIMER( DeferredColorApply, deviceContext );
        dx11Context->CSSetShader( shaderDeferredColorApply2x2, nullptr, 0 );
        dx11Context->DispatchIndirect( g_workingExecuteIndirectBuffer, 0 );
    }

    // DEBUGGING
    if( m_debugShowEdges )
    {
        VA_SCOPE_CPUGPU_TIMER( DebugDrawEdges, deviceContext );

        int tgcX = ( m_textureResolutionX + 16 - 1 ) / 16;
        int tgcY = ( m_textureResolutionY + 16 - 1 ) / 16;

        dx11Context->CSSetShaderResources( 0, _countof( SRVs ), SRVs );
        dx11Context->CSSetShader( shaderDebugDrawEdges, nullptr, 0 );
        dx11Context->Dispatch( tgcX, tgcY, 1 );
    }

    // Reset API states
    dx11Context->CSSetShader( nullptr, nullptr, 0 );
    dx11Context->CSSetUnorderedAccessViews( 0, _countof(nullUAVs), nullUAVs, nullptr );

    dx11Context->CSSetShaderResources( 0, _countof(SRVs), nullSRVs );

    ID3D11SamplerState * samps[1] = {nullptr};
    dx11Context->CSSetSamplers( 0, 1, samps );

    return vaDrawResultFlags::None;
}


void RegisterCMAA2DX11( )
{
    VA_RENDERING_MODULE_REGISTER( vaRenderDeviceDX11, vaCMAA2, vaCMAA2DX11 );
}