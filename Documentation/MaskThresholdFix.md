# Mask Threshold Fix for GPU/CPU Visual Consistency

## Problem
The GPU preview shader and CPU terrain painting had visual inconsistencies due to missing mask threshold adjustment in the CPU implementation. The GPU shader applies a threshold adjustment to mask values, but this was missing from the CPU jobs.

## Root Cause
The GPU shader's `SampleMaskAtlas2D` function in `BlendLayer.hlsl` applies a threshold adjustment:
```hlsl
float mask = saturate((maskValue - maskThreshold) / max(1e-5, 1.0 - maskThreshold));
```

However, the CPU's `SampleMaskAtlas` function in `TerrainJobsUtility.cs` was missing this adjustment.

## Solution
Added mask threshold support to the CPU implementation by:

### 1. Added MaskThreshold to RecipeData
**File:** `RecipeJobsUtility.cs`
- Added `public float MaskThreshold;` field to the `RecipeData` struct
- Initialized it to `0f` in the constructor to match GPU default

### 2. Updated SampleMaskAtlas Function
**File:** `TerrainJobsUtility.cs`
- Added `maskThreshold` parameter with default value `0f` to the main `SampleMaskAtlas` function
- Applied the same threshold adjustment formula as the GPU shader:
  ```csharp
  var thresholded = math.saturate((v - maskThreshold) / math.max(1e-5f, 1.0f - maskThreshold));
  return thresholded;
  ```
- Updated the backward-compatible overload to pass `0f` as the threshold

### 3. Updated CPU Jobs to Use MaskThreshold
**File:** `PaintSplatmapJob.cs`
- Modified the mask atlas sampling call to pass `Recipe.MaskThreshold`

**File:** `TerrainJobs.cs`
- Updated the `ModifyAlphamapsJob` to pass `Recipe.MaskThreshold` to `SampleMaskAtlas`

## Testing
Created a test window (`MaskThresholdTest.cs`) to verify:
1. The `SampleMaskAtlas` function correctly applies threshold adjustments
2. The `RecipeData` struct properly initializes with default threshold of 0

## Impact
- **Backward Compatible:** All changes maintain backward compatibility with default threshold of 0
- **Performance:** Minimal impact - just one additional float parameter and simple arithmetic
- **Consistency:** CPU and GPU now use identical mask threshold logic

## Files Modified
1. `RecipeJobsUtility.cs` - Added MaskThreshold field
2. `TerrainJobsUtility.cs` - Updated SampleMaskAtlas function
3. `PaintSplatmapJob.cs` - Pass threshold parameter
4. `TerrainJobs.cs` - Pass threshold parameter in ModifyAlphamapsJob
5. `MaskThresholdTest.cs` - Test utility (new file)

## Verification
The fix ensures that both GPU preview and CPU terrain painting use identical mask sampling logic, eliminating visual inconsistencies when the mask threshold is non-zero.