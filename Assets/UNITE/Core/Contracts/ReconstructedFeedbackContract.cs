namespace Unite.Core
{
    /// <summary>
    /// Base type for study-defined operator-side information produced by state
    /// reconstruction. Received transport payloads are not implicitly promoted
    /// to this contract. Implementations preserve their observation metadata.
    /// </summary>
    public abstract class ReconstructedFeedbackContract
    {
    }
}
