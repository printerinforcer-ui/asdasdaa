using UnityEngine;
using System;

namespace ZexQoLMenu
{
    /// <summary>
    /// Low-impact animated background system for the UI menu.
    /// Features lightweight particle-like animations using simple math without allocations.
    /// Can be toggled off entirely by users to save RAM and processing.
    /// </summary>
    public partial class Plugin
    {
        // ============================================================
        // ANIMATED BACKGROUND CONFIG
        // ============================================================
        private bool backgroundAnimationEnabled = true;
        private int backgroundAnimationType = 0; // 0 = Subtle Pulse, 1 = Gradient Shift, 2 = Noise Flow
        private float backgroundAnimationSpeed = 1f;
        private float backgroundAnimationIntensity = 0.3f;

        // Configuration references (will be set in Plugin.Configuration.cs)
        private BepInEx.Configuration.ConfigEntry<bool> configBackgroundAnimationEnabled;
        private BepInEx.Configuration.ConfigEntry<int> configBackgroundAnimationType;
        private BepInEx.Configuration.ConfigEntry<float> configBackgroundAnimationSpeed;
        private BepInEx.Configuration.ConfigEntry<float> configBackgroundAnimationIntensity;

        // Animation state (reused each frame, no allocations)
        private float animationTimer = 0f;
        private float[] noiseCache = new float[4]; // Small cache for perlin-like values
        private int noiseCacheIndex = 0;

        /// <summary>
        /// Initialize animated background configuration.
        /// Call this from Plugin.Configuration.cs in the configuration setup.
        /// </summary>
        public void InitializeBackgroundAnimationConfig()
        {
            try
            {
                configBackgroundAnimationEnabled = Config.Bind(
                    "Background Animation",
                    "Enable Background Animation",
                    true,
                    "Enable/disable animated background to save performance");

                configBackgroundAnimationType = Config.Bind(
                    "Background Animation",
                    "Animation Type",
                    0,
                    "0 = Subtle Pulse, 1 = Gradient Shift, 2 = Noise Flow");

                configBackgroundAnimationSpeed = Config.Bind(
                    "Background Animation",
                    "Animation Speed",
                    1f,
                    new BepInEx.Configuration.ConfigDescription(
                        "Animation speed multiplier (0.1 to 3.0)",
                        new BepInEx.Configuration.AcceptableValueRange<float>(0.1f, 3f)));

                configBackgroundAnimationIntensity = Config.Bind(
                    "Background Animation",
                    "Animation Intensity",
                    0.3f,
                    new BepInEx.Configuration.ConfigDescription(
                        "How pronounced the animation is (0.1 to 1.0)",
                        new BepInEx.Configuration.AcceptableValueRange<float>(0.1f, 1f)));

                backgroundAnimationEnabled = configBackgroundAnimationEnabled.Value;
                backgroundAnimationType = configBackgroundAnimationType.Value;
                backgroundAnimationSpeed = configBackgroundAnimationSpeed.Value;
                backgroundAnimationIntensity = configBackgroundAnimationIntensity.Value;
            }
            catch { }
        }

        /// <summary>
        /// Update animation state. Call this from Update() with Time.unscaledDeltaTime.
        /// Extremely lightweight—only processes if enabled.
        /// </summary>
        private void TickBackgroundAnimation(float dt)
        {
            if (!backgroundAnimationEnabled || dt <= 0f)
                return;

            // Reload config values if they changed
            if (configBackgroundAnimationEnabled != null && configBackgroundAnimationEnabled.Value != backgroundAnimationEnabled)
                backgroundAnimationEnabled = configBackgroundAnimationEnabled.Value;
            if (configBackgroundAnimationType != null)
                backgroundAnimationType = Mathf.Clamp(configBackgroundAnimationType.Value, 0, 2);
            if (configBackgroundAnimationSpeed != null)
                backgroundAnimationSpeed = Mathf.Clamp(configBackgroundAnimationSpeed.Value, 0.1f, 3f);
            if (configBackgroundAnimationIntensity != null)
                backgroundAnimationIntensity = Mathf.Clamp(configBackgroundAnimationIntensity.Value, 0.1f, 1f);

            // Advance timer (wraps every ~100 seconds to prevent float precision issues)
            animationTimer += dt * backgroundAnimationSpeed;
            if (animationTimer > 100f)
                animationTimer -= 100f;
        }

        /// <summary>
        /// Draw animated background overlay. Replaces or enhances DrawMenuBackground().
        /// Call this from DrawMainWindow() instead of the original DrawMenuBackground().
        /// </summary>
        private void DrawAnimatedMenuBackground()
        {
            Color prev = GUI.color;
            Color tint = Color.HSVToRGB(backgroundHue, MenuSat(0.5f), 0.15f);

            if (!backgroundAnimationEnabled)
            {
                // No animation—just draw the static tint
                tint.a = backgroundOpacity;
                GUI.color = tint;
                GUI.Box(new Rect(0f, 0f, menuRect.width, menuRect.height), "");
                GUI.color = prev;
                return;
            }

            // Apply animation effect to opacity/color
            float animAlpha = backgroundOpacity;
            Color animColor = tint;

            switch (backgroundAnimationType)
            {
                case 0: // Subtle Pulse
                    animAlpha = ApplyPulseAnimation(backgroundOpacity);
                    break;
                case 1: // Gradient Shift
                    animColor = ApplyGradientShiftAnimation(tint);
                    break;
                case 2: // Noise Flow
                    animColor = ApplyNoiseFlowAnimation(tint);
                    break;
            }

            animColor.a = animAlpha;
            GUI.color = animColor;
            GUI.Box(new Rect(0f, 0f, menuRect.width, menuRect.height), "");
            GUI.color = prev;
        }

        /// <summary>
        /// Subtle pulse: gently varies opacity in and out.
        /// </summary>
        private float ApplyPulseAnimation(float baseAlpha)
        {
            float pulse = Mathf.Sin(animationTimer * 2f * Mathf.PI) * 0.5f + 0.5f; // 0 to 1
            float variation = Mathf.Lerp(1f - backgroundAnimationIntensity, 1f, pulse);
            return baseAlpha * variation;
        }

        /// <summary>
        /// Hue shift: cycles the background hue smoothly in real-time.
        /// </summary>
        private Color ApplyGradientShiftAnimation(Color baseColor)
        {
            float hueShift = Mathf.Repeat(animationTimer * 0.1f, 1f) * backgroundAnimationIntensity;
            float newHue = Mathf.Repeat(backgroundHue + hueShift, 1f);
            Color animated = Color.HSVToRGB(newHue, MenuSat(0.5f), 0.15f);
            animated.a = baseColor.a;
            return animated;
        }

        /// <summary>
        /// Noise flow: creates a subtle shimmer effect using simple sine waves.
        /// No texture needed—purely mathematical.
        /// </summary>
        private Color ApplyNoiseFlowAnimation(Color baseColor)
        {
            // Use multiple sine waves at different frequencies to create organic motion
            float noise1 = Mathf.Sin(animationTimer * 0.5f + 0f) * 0.5f + 0.5f;
            float noise2 = Mathf.Sin(animationTimer * 0.7f + 1f) * 0.5f + 0.5f;
            float noise3 = Mathf.Sin(animationTimer * 0.3f + 2f) * 0.5f + 0.5f;

            // Blend the noises
            float noiseValue = (noise1 + noise2 + noise3) / 3f;

            // Apply intensity
            float variation = Mathf.Lerp(1f - backgroundAnimationIntensity * 0.5f, 1f, noiseValue);

            // Modulate saturation slightly for visual interest
            float newSat = MenuSat(0.5f) * variation;
            Color animated = Color.HSVToRGB(backgroundHue, newSat, 0.15f);
            animated.a = baseColor.a;
            return animated;
        }
    }
}
