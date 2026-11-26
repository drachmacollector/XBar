// XBar - GPL-3.0-or-later. Same pattern as the TranslucentTB-derived effect wrappers next to it.
#include "SaturationEffect.h"

HRESULT SaturationEffect::GetEffectId(GUID* id) noexcept
{
	if (id == nullptr) return E_INVALIDARG;
	*id = CLSID_D2D1Saturation;
	return S_OK;
}

HRESULT SaturationEffect::GetNamedPropertyMapping(LPCWSTR name, UINT* index, awge::GRAPHICS_EFFECT_PROPERTY_MAPPING* mapping) noexcept
{
	if (index == nullptr || mapping == nullptr) return E_INVALIDARG;
	if (std::wstring_view(name) == L"Saturation")
	{
		*index = D2D1_SATURATION_PROP_SATURATION;
		*mapping = awge::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT;
		return S_OK;
	}
	return E_INVALIDARG;
}

HRESULT SaturationEffect::GetPropertyCount(UINT* count) noexcept
{
	if (count == nullptr) return E_INVALIDARG;
	*count = 1;
	return S_OK;
}

HRESULT SaturationEffect::GetProperty(UINT index, ABI::Windows::Foundation::IPropertyValue** value) noexcept try
{
	if (value == nullptr) return E_INVALIDARG;
	if (index != D2D1_SATURATION_PROP_SATURATION) return E_BOUNDS;
	*value = wf::PropertyValue::CreateSingle(Saturation).as<ABI::Windows::Foundation::IPropertyValue>().detach();
	return S_OK;
}
catch (...)
{
	return winrt::to_hresult();
}

HRESULT SaturationEffect::GetSource(UINT index, awge::IGraphicsEffectSource** source) noexcept
{
	if (source == nullptr) return E_INVALIDARG;
	if (index != 0) return E_BOUNDS;
	winrt::copy_to_abi(Source, *reinterpret_cast<void**>(source));
	return S_OK;
}

HRESULT SaturationEffect::GetSourceCount(UINT* count) noexcept
{
	if (count == nullptr) return E_INVALIDARG;
	*count = 1;
	return S_OK;
}

winrt::hstring SaturationEffect::Name() { return m_name; }
void SaturationEffect::Name(winrt::hstring name) { m_name = name; }
