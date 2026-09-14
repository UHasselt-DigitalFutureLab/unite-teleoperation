using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Base type for an operator-side visual source that can be arranged by an
    /// operator presentation implementation.
    /// </summary>
    public abstract class OperatorPresentationSource : MonoBehaviour
    {
        [SerializeField]
        private string sourceId;

        public string SourceId =>
            string.IsNullOrWhiteSpace(sourceId) ? string.Empty : sourceId.Trim();

        internal void SetPresented(bool isPresented)
        {
            OnPresentedChanged(isPresented);
        }

        /// <summary>
        /// Allows a source to enable or disable its own local presentation
        /// objects, such as a camera, RawImage, material, or haptic device.
        /// </summary>
        protected virtual void OnPresentedChanged(bool isPresented)
        {
        }
    }
}
