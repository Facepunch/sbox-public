using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace NativeEngine;

[StructLayout( LayoutKind.Sequential, Pack = 4, Size = 20 )]
public struct CSamplerStateDesc
{
	public byte m_nFilterMode;
	public byte m_nMipLodBias;
	public byte m_nMipLodBiasSign;

	public byte m_nAddressU;
	public byte m_nAddressV;
	public byte m_nAddressW;

	public byte m_nAnisoExp;
	public byte m_nComparisonFunc;

	public byte m_nAllowGlobalMipBiasOverride;

	public byte m_nMinLod;
	public byte m_nMaxLod;

	public uint m_nBorderColor8Bit;

	// 2 bytes padding
	public ushort m_nPad;

	public CSamplerStateDesc( SamplerState samplerState )
	{
		m_nFilterMode = (byte)samplerState.Filter;
		m_nMipLodBias = (byte)samplerState.MipLodBias;
		m_nMipLodBiasSign = 0;

		m_nAddressU = (byte)samplerState.AddressModeU;
		m_nAddressV = (byte)samplerState.AddressModeV;
		m_nAddressW = (byte)samplerState.AddressModeW;

		m_nAnisoExp = (byte)samplerState.MaxAnisotropy;
		m_nComparisonFunc = 0;

		m_nAllowGlobalMipBiasOverride = 0;

		m_nMinLod = 0;
		m_nMaxLod = 15;

		m_nBorderColor8Bit = samplerState.BorderColor.ToColor32().RawInt;

		m_nPad = 0;
	}
}
