using UnityEngine;

namespace PicoBridge.UI
{
    /// <summary>
    /// Keeps a world-space Canvas in front of the headset using head direction as gaze.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public class HeadGazeCanvasFollower : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private bool followHeadGaze = true;
        [SerializeField] private float distance = 1.2f;
        [SerializeField] private float verticalOffset;
        [SerializeField] private float followSpeed = 10f;
        [SerializeField] private bool keepWorldUp = true;

        public bool FollowHeadGaze
        {
            get => followHeadGaze;
            set => followHeadGaze = value;
        }

        private void LateUpdate()
        {
            if (!followHeadGaze)
                return;

            var headTransform = ResolveHead();
            if (headTransform == null)
                return;

            float clampedDistance = Mathf.Max(0.1f, distance);
            Vector3 targetPosition =
                headTransform.position +
                headTransform.forward * clampedDistance +
                headTransform.up * verticalOffset;

            Vector3 forward = targetPosition - headTransform.position;
            if (forward.sqrMagnitude < 0.0001f)
                forward = headTransform.forward;

            Vector3 up = keepWorldUp ? Vector3.up : headTransform.up;
            Quaternion targetRotation = Quaternion.LookRotation(forward.normalized, up);

            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, followSpeed) * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
        }

        private Transform ResolveHead()
        {
            if (head != null)
                return head;

            var mainCamera = UnityEngine.Camera.main;
            if (mainCamera == null)
                return null;

            head = mainCamera.transform;
            return head;
        }

        private void OnValidate()
        {
            distance = Mathf.Max(0.1f, distance);
            followSpeed = Mathf.Max(0.01f, followSpeed);
        }
    }
}
