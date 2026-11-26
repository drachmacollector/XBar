// Adapted from TranslucentTB (https://github.com/TranslucentTB/TranslucentTB), GPL-3.0.
// Copyright (c) TranslucentTB contributors. Used in XBar under GPL-3.0-or-later.
#include "XamlBlurBrush.h"
#include "CompositeEffect.h"
#include "FloodEffect.h"
#include "GaussianBlurEffect.h"
#include "SaturationEffect.h"

XamlBlurBrush::XamlBlurBrush(wuc::Compositor compositor, float blurAmount, wfn::float4 tint, float saturation) :
	m_compositor(std::move(compositor)),
	m_blurAmount(blurAmount),
	m_tint(tint),
	m_saturation(saturation)
{ }

void XamlBlurBrush::OnConnected()
{
	if (!CompositionBrush())
	{
		auto backdropBrush = m_compositor.CreateBackdropBrush();

		auto blurEffect = winrt::make_self<GaussianBlurEffect>();
		blurEffect->Source = wuc::CompositionEffectSourceParameter(L"backdrop");
		blurEffect->BlurAmount = m_blurAmount;

		auto floodEffect = winrt::make_self<FloodEffect>();
		floodEffect->Color = m_tint;

		auto compositeEffect = winrt::make_self<CompositeEffect>();
		if (m_saturation != 1.0f)
		{
			// backdrop -> blur -> saturate (liquid glass), then the tint on top
			auto satEffect = winrt::make_self<SaturationEffect>();
			satEffect->Source = *blurEffect;
			satEffect->Saturation = m_saturation;
			compositeEffect->Sources.push_back(*satEffect);
		}
		else
		{
			compositeEffect->Sources.push_back(*blurEffect);
		}
		compositeEffect->Sources.push_back(*floodEffect);
		compositeEffect->Mode = D2D1_COMPOSITE_MODE_SOURCE_OVER;

		auto factory = m_compositor.CreateEffectFactory(*compositeEffect);
		auto blurBrush = factory.CreateBrush();
		blurBrush.SetSourceParameter(L"backdrop", backdropBrush);

		CompositionBrush(blurBrush);
	}
}

void XamlBlurBrush::OnDisconnected()
{
	if (const auto brush = CompositionBrush())
	{
		brush.Close();
		CompositionBrush(nullptr);
	}
}
