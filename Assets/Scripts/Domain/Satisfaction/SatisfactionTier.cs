namespace Pho.Domain.Satisfaction
{
    /// <summary>
    /// GDD §12 tier bands over Score0to100: Angry 0-30, Unhappy 31-50,
    /// Satisfied 51-70, Happy 71-90, Fan 91-100 (inclusive upper bound per
    /// band; a score of exactly 30 is Angry, exactly 90 is Happy).
    /// </summary>
    public enum SatisfactionTier
    {
        Angry, Unhappy, Satisfied, Happy, Fan
    }
}
