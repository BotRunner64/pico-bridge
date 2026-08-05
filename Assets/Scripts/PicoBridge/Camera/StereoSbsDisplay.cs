using UnityEngine;

namespace PicoBridge.Camera
{
    /// <summary>
    /// Binds a received side-by-side WebRTC texture to a scene-authored stereo screen.
    /// The renderer and its hierarchy are created by an editor tool, never at runtime.
    /// </summary>
    public sealed class StereoSbsDisplay : MonoBehaviour
    {
        [SerializeField] private PicoBridgeManager manager;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private UnityEngine.Camera displayCamera;
        [SerializeField] private bool swapEyes;
        [SerializeField] private bool flipY;

        [Header("Uncalibrated SBS Fallback")]
        [SerializeField, Range(30f, 150f)] private float fallbackHorizontalFovDegrees = 90f;

        [Header("Comfort")]
        [SerializeField, Range(0f, 0.1f)] private float edgeFeather = 0.025f;

        private MaterialPropertyBlock _properties;

        private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int SwapEyesId = Shader.PropertyToID("_SwapEyes");
        private static readonly int FlipYId = Shader.PropertyToID("_FlipY");
        private static readonly int SourceIntrinsicsId = Shader.PropertyToID("_SourceIntrinsics");
        private static readonly int LeftEyeProjectionId = Shader.PropertyToID("_LeftEyeProjection");
        private static readonly int RightEyeProjectionId = Shader.PropertyToID("_RightEyeProjection");
        private static readonly int EdgeFeatherId = Shader.PropertyToID("_EdgeFeather");

        private void Awake()
        {
            if (manager == null)
                manager = FindObjectOfType<PicoBridgeManager>();
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();
            if (displayCamera == null)
                displayCamera = GetComponentInParent<UnityEngine.Camera>();

            _properties = new MaterialPropertyBlock();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            var receiver = manager != null ? manager.WebRtcCamera : null;
            var texture = receiver != null ? receiver.Texture : null;
            bool visible = manager != null &&
                manager.IsPcVideoStereoSbs &&
                receiver != null &&
                receiver.HasVideoSignal &&
                texture != null;

            SetVisible(visible);
            if (!visible || targetRenderer == null)
                return;

            targetRenderer.GetPropertyBlock(_properties);
            _properties.SetTexture(MainTextureId, texture);
            _properties.SetFloat(SwapEyesId, swapEyes ? 1f : 0f);
            _properties.SetFloat(FlipYId, flipY ? 1f : 0f);
            _properties.SetVector(SourceIntrinsicsId, ResolveSourceIntrinsics(texture));
            SetEyeProjectionProperties();
            _properties.SetFloat(EdgeFeatherId, edgeFeather);
            targetRenderer.SetPropertyBlock(_properties);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (targetRenderer != null && targetRenderer.enabled != visible)
                targetRenderer.enabled = visible;
        }

        private Vector4 ResolveSourceIntrinsics(Texture texture)
        {
            if (manager != null && manager.HasPcStereoIntrinsics)
                return manager.PcStereoIntrinsics;

            float horizontalFov = Mathf.Clamp(fallbackHorizontalFovDegrees, 30f, 150f);
            float fxNormalized = 0.5f / Mathf.Tan(horizontalFov * Mathf.Deg2Rad * 0.5f);
            float eyeWidth = Mathf.Max(1f, texture.width * 0.5f);
            float eyeHeight = Mathf.Max(1f, texture.height);
            float fyNormalized = fxNormalized * eyeWidth / eyeHeight;
            return new Vector4(fxNormalized, fyNormalized, 0.5f, 0.5f);
        }

        private void SetEyeProjectionProperties()
        {
            Matrix4x4 leftProjection = Matrix4x4.identity;
            Matrix4x4 rightProjection = Matrix4x4.identity;
            if (displayCamera != null)
            {
                leftProjection = displayCamera.projectionMatrix;
                rightProjection = leftProjection;
                if (displayCamera.stereoEnabled)
                {
                    leftProjection = displayCamera.GetStereoProjectionMatrix(
                        UnityEngine.Camera.StereoscopicEye.Left);
                    rightProjection = displayCamera.GetStereoProjectionMatrix(
                        UnityEngine.Camera.StereoscopicEye.Right);
                }
            }

            _properties.SetVector(LeftEyeProjectionId, ToProjectionParameters(leftProjection));
            _properties.SetVector(RightEyeProjectionId, ToProjectionParameters(rightProjection));
        }

        private static Vector4 ToProjectionParameters(Matrix4x4 projection)
        {
            float horizontalScale = Mathf.Abs(projection.m00) > 0.0001f ? projection.m00 : 1f;
            float verticalScale = Mathf.Abs(projection.m11) > 0.0001f ? projection.m11 : 1f;
            return new Vector4(horizontalScale, projection.m02, verticalScale, projection.m12);
        }
    }
}
