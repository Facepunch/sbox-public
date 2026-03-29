namespace NativeEngine;

internal enum LightSourceShape_t
{
	Sphere = 0,     // m_flLightSourceDim0 is light source radius
	Capsule = 1,    // m_flLightSourceDim0 is light source radius,	m_flLightSourceDim1 is light source length
	Rectangle = 2   // m_flLightSourceDim0 is light source width,	m_flLightSourceDim1 is light source height
}
