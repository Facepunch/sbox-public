/**********************************************************************
Copyright (c) 2021 Advanced Micro Devices, Inc. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
********************************************************************/

#ifndef FFX_DNSR_REFLECTIONS_CONFIG
#define FFX_DNSR_REFLECTIONS_CONFIG

#define FFX_DNSR_REFLECTIONS_GAUSSIAN_K 3.0
#define FFX_DNSR_REFLECTIONS_RADIANCE_WEIGHT_BIAS 0.6
#define FFX_DNSR_REFLECTIONS_RADIANCE_WEIGHT_VARIANCE_K 0.1
#define FFX_DNSR_REFLECTIONS_AVG_RADIANCE_LUMINANCE_WEIGHT 0.3
#define FFX_DNSR_REFLECTIONS_PREFILTER_VARIANCE_WEIGHT 4.4
#define FFX_DNSR_REFLECTIONS_REPROJECT_SURFACE_DISCARD_VARIANCE_WEIGHT 1.5
#define FFX_DNSR_REFLECTIONS_PREFILTER_VARIANCE_BIAS 0.1
#define FFX_DNSR_REFLECTIONS_PREFILTER_NORMAL_SIGMA 512.0
#define FFX_DNSR_REFLECTIONS_PREFILTER_DEPTH_SIGMA 4.0
#define FFX_DNSR_REFLECTIONS_DISOCCLUSION_NORMAL_WEIGHT 1.4
#define FFX_DNSR_REFLECTIONS_DISOCCLUSION_DEPTH_WEIGHT 1.0
#define FFX_DNSR_REFLECTIONS_DISOCCLUSION_THRESHOLD 0.9
#define FFX_DNSR_REFLECTIONS_REPROJECTION_NORMAL_SIMILARITY_THRESHOLD 0.999f
#define FFX_DNSR_REFLECTIONS_SAMPLES_FOR_ROUGHNESS(r) (1.0 - exp(-r * 100.0))

#endif // FFX_DNSR_REFLECTIONS_CONFIG