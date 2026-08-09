namespace Pho.Domain.MathUtil
{
    /// <summary>
    /// Pure stand-ins for UnityEngine.Mathf, so domain logic never needs
    /// `Mathf.Clamp01`/`Vector3` and tempts loosening noEngineReferences.
    /// Ship this in M1, before anyone hits the wall -- if that flag comes
    /// off, the fast dotnet-test loop dies with it.
    /// </summary>
    public static class MathP
    {
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;

        public static float InverseLerp(float a, float b, float v)
        {
            if (a == b) return 0f;
            return Clamp01((v - a) / (b - a));
        }

        public static float Remap(float v, float inMin, float inMax, float outMin, float outMax)
        {
            var t = InverseLerp(inMin, inMax, v);
            return Lerp(outMin, outMax, t);
        }
    }
}
