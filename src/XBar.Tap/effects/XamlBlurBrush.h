// Adapted from TranslucentTB (https://github.com/TranslucentTB/TranslucentTB), GPL-3.0.
// Copyright (c) TranslucentTB contributors. Used in XBar under GPL-3.0-or-later.
#pragma once
#include "winrt.hpp"
#include "undefgetcurrenttime.h"
#include <winrt/Windows.Foundation.Numerics.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Xaml.Media.h>

class XamlBlurBrush : public wux::Media::XamlCompositionBrushBaseT<XamlBlurBrush>
{
public:
	XamlBlurBrush(wuc::Compositor compositor, float blurAmount, wfn::float4 tint);

	void OnConnected();
	void OnDisconnected();

private:
	wuc::Compositor m_compositor;
	float m_blurAmount;
	wfn::float4 m_tint;
};
