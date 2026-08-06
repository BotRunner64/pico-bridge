using System;
using System.Collections;
using Unity.WebRTC;
using UnityEngine;
using PicoBridge.Network;

namespace PicoBridge.Camera
{
    public class WebRtcCameraReceiver : MonoBehaviour
    {
        private RTCPeerConnection _peer;
        private VideoStreamTrack _videoTrack;
        private Texture _texture;
        private PicoTcpClient _tcp;
        private Coroutine _updateCoroutine;
        private Coroutine _resetCoroutine;
        private Coroutine _statsCoroutine;
        private string _status = "Idle";
        private int _frameCount;
        private bool _ignorePeerStateChanges;
        private float _previewStartedAt = -1f;
        private float _disconnectedAt = -1f;
        private float _lastFrameIntervalMs;
        private int _packetsLost;
        private uint _framesDropped;
        private uint _pliCount;
        private uint _nackCount;
        private float _receiveFps;
        private float _jitterMs;
        private float _receiveBitrateMbps;
        private string _videoCodec = string.Empty;
        private ulong _lastBytesReceived;
        private float _lastStatsSampleAt = -1f;
        private float _lastStatsLogAt = -1f;

        public Texture Texture => _texture;
        public string Status => _status;
        public int FrameCount => _frameCount;
        public float LastFrameIntervalMs => _lastFrameIntervalMs;
        public int PacketsLost => _packetsLost;
        public uint FramesDropped => _framesDropped;
        public uint PliCount => _pliCount;
        public uint NackCount => _nackCount;
        public float ReceiveFps => _receiveFps;
        public float JitterMs => _jitterMs;
        public float ReceiveBitrateMbps => _receiveBitrateMbps;
        public string VideoCodec => _videoCodec;
        public bool IsActive => _peer != null;
        public bool HasVideoSignal => _texture != null && _frameCount > 0;
        public bool ShouldRetry
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                bool initialTimedOut = !HasVideoSignal && _previewStartedAt > 0f && now - _previewStartedAt >= InitialPreviewTimeout;
                bool disconnectedTimedOut = _disconnectedAt > 0f && now - _disconnectedAt >= DisconnectedRetryTimeout;
                return initialTimedOut || disconnectedTimedOut;
            }
        }

        private const float InitialPreviewTimeout = 10f;
        private const float DisconnectedRetryTimeout = 12f;
        private const float StatsPollIntervalSeconds = 2f;
        private const float StatsLogIntervalSeconds = 10f;

        public void StartPreview(PicoTcpClient tcp, int width, int height, int fps, int bitrate)
        {
            StopPreview();
            _tcp = tcp;
            if (_tcp == null || _tcp.State != SocketState.Working)
            {
                _status = "TCP disconnected";
                return;
            }

            _status = "Requesting WebRTC offer";
            _previewStartedAt = Time.realtimeSinceStartup;
            _disconnectedAt = -1f;
            ResetInboundStats();
            EnsureWebRtcUpdateLoop();
            string cameraJson = $"{{\"codec\":\"webrtc\",\"source\":\"frames\",\"width\":{width},\"height\":{height},\"fps\":{fps},\"bitrate\":{bitrate}}}";
            _tcp.SendFunction("StartReceivePcCamera", cameraJson);
            Debug.Log("[WebRtcCameraReceiver] Sent StartReceivePcCamera codec=webrtc");
        }

        public void StopPreview()
        {
            bool wasActive = _peer != null;
            if (wasActive && _tcp != null && _tcp.State == SocketState.Working)
                _tcp.SendFunction("StopReceivePcCamera", "\"\"");

            ResetPeer(clearSignal: true);
            _status = "Idle";
            _previewStartedAt = -1f;
            _disconnectedAt = -1f;
        }

        private void ResetPeer(bool clearSignal)
        {
            if (_statsCoroutine != null)
            {
                StopCoroutine(_statsCoroutine);
                _statsCoroutine = null;
            }

            if (_resetCoroutine != null)
            {
                StopCoroutine(_resetCoroutine);
                _resetCoroutine = null;
            }

            _videoTrack?.Dispose();
            _videoTrack = null;
            if (clearSignal)
                _texture = null;
            var peer = _peer;
            _peer = null;
            if (peer != null)
            {
                _ignorePeerStateChanges = true;
                try
                {
                    peer.Close();
                    peer.Dispose();
                }
                finally
                {
                    _ignorePeerStateChanges = false;
                }
            }
            if (clearSignal)
                ResetInboundStats();
        }

        public void HandleFunction(string functionName, string json)
        {
            if (functionName == "WebRtcOffer")
                StartCoroutine(HandleOffer(json));
            else if (functionName == "WebRtcIceCandidate")
                HandleRemoteIce(json);
        }

        private IEnumerator HandleOffer(string json)
        {
            EnsureWebRtcUpdateLoop();
            ResetPeer(clearSignal: false);
            ResetInboundStats();
            CreatePeer();
            string sdp = ExtractString(json, "sdp");
            string type = ExtractString(json, "type");
            if (string.IsNullOrEmpty(sdp))
            {
                _status = "Bad WebRTC offer";
                Debug.LogWarning("[WebRtcCameraReceiver] WebRtcOffer missing SDP");
                yield break;
            }

            _status = "Applying offer";
            var offer = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
            var remoteOp = _peer.SetRemoteDescription(ref offer);
            yield return remoteOp;
            if (remoteOp.IsError)
            {
                _status = "Offer failed";
                Debug.LogError($"[WebRtcCameraReceiver] SetRemoteDescription failed: {remoteOp.Error.message}");
                yield break;
            }

            var answerOp = _peer.CreateAnswer();
            yield return answerOp;
            if (answerOp.IsError)
            {
                _status = "Answer failed";
                Debug.LogError($"[WebRtcCameraReceiver] CreateAnswer failed: {answerOp.Error.message}");
                yield break;
            }

            var answer = answerOp.Desc;
            var localOp = _peer.SetLocalDescription(ref answer);
            yield return localOp;
            if (localOp.IsError)
            {
                _status = "Local answer failed";
                Debug.LogError($"[WebRtcCameraReceiver] SetLocalDescription failed: {localOp.Error.message}");
                yield break;
            }

            _tcp.SendFunction("WebRtcAnswer", $"{{\"type\":\"answer\",\"sdp\":{QuoteJson(answer.sdp)}}}");
            _status = string.IsNullOrEmpty(type) ? "Answer sent" : $"Answer sent ({type})";
            Debug.Log("[WebRtcCameraReceiver] WebRTC answer sent");
        }

        private void CreatePeer()
        {
            if (_peer != null)
                return;

            var configuration = new RTCConfiguration
            {
                iceServers = Array.Empty<RTCIceServer>()
            };
            _peer = new RTCPeerConnection(ref configuration);

            _peer.OnIceCandidate = candidate =>
            {
                if (candidate == null || _tcp == null)
                    return;
                string payload = "{" +
                    $"\"candidate\":{QuoteJson(candidate.Candidate)}," +
                    $"\"sdpMid\":{QuoteJson(candidate.SdpMid)}," +
                    $"\"sdpMLineIndex\":{candidate.SdpMLineIndex}" +
                    "}";
                _tcp.SendFunction("WebRtcIceCandidate", payload);
            };

            _peer.OnConnectionStateChange = state =>
            {
                if (_ignorePeerStateChanges)
                    return;

                _status = $"WebRTC {state}";
                if (state == RTCPeerConnectionState.Disconnected)
                {
                    if (_disconnectedAt < 0f)
                        _disconnectedAt = Time.realtimeSinceStartup;
                }
                else
                {
                    _disconnectedAt = -1f;
                }

                if (state == RTCPeerConnectionState.Failed ||
                    state == RTCPeerConnectionState.Closed)
                {
                    SchedulePeerReset("Preview interrupted", clearSignal: false);
                }

                Debug.Log($"[WebRtcCameraReceiver] Connection state: {state}");
            };

            _peer.OnTrack = e =>
            {
                if (e.Track is VideoStreamTrack track)
                {
                    _videoTrack = track;
                    _videoTrack.OnVideoReceived += texture =>
                    {
                        _disconnectedAt = -1f;
                        _texture = texture;
                        if (_frameCount == 0)
                            _frameCount = 1;
                        _status = "Preview active";
                    };
                    StartStatsPolling(_peer);
                    _status = "Video track received";
                    Debug.Log("[WebRtcCameraReceiver] Video track received");
                }
            };
        }

        private void StartStatsPolling(RTCPeerConnection peer)
        {
            if (_statsCoroutine != null)
                StopCoroutine(_statsCoroutine);
            _statsCoroutine = peer != null ? StartCoroutine(PollInboundStats(peer)) : null;
        }

        private IEnumerator PollInboundStats(RTCPeerConnection peer)
        {
            var wait = new WaitForSecondsRealtime(StatsPollIntervalSeconds);
            while (_peer == peer)
            {
                yield return wait;
                if (_peer != peer)
                    break;

                RTCStatsReportAsyncOperation operation;
                try
                {
                    operation = peer.GetStats();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[WebRtcCameraReceiver] GetStats failed: {exception.Message}");
                    continue;
                }

                yield return operation;
                if (operation.IsError)
                {
                    Debug.LogWarning($"[WebRtcCameraReceiver] GetStats failed: {operation.Error.message}");
                    continue;
                }

                var report = operation.Value;
                if (report == null)
                    continue;

                try
                {
                    if (_peer == peer)
                        ApplyInboundStats(report);
                }
                finally
                {
                    report.Dispose();
                }
            }

            if (_peer == peer)
                _statsCoroutine = null;
        }

        private void ApplyInboundStats(RTCStatsReport report)
        {
            RTCInboundRTPStreamStats inbound = null;
            foreach (var stat in report.Stats.Values)
            {
                if (stat is RTCInboundRTPStreamStats candidate && candidate.kind == "video")
                {
                    inbound = candidate;
                    break;
                }
            }
            if (inbound == null)
                return;

            float now = Time.realtimeSinceStartup;
            int packetsLost = Mathf.Max(0, inbound.packetsLost);
            int lostDelta = Mathf.Max(0, packetsLost - _packetsLost);
            uint droppedDelta = inbound.framesDropped >= _framesDropped
                ? inbound.framesDropped - _framesDropped
                : 0;
            uint pliDelta = inbound.pliCount >= _pliCount
                ? inbound.pliCount - _pliCount
                : 0;
            uint nackDelta = inbound.nackCount >= _nackCount
                ? inbound.nackCount - _nackCount
                : 0;

            if (_lastStatsSampleAt >= 0f && now > _lastStatsSampleAt &&
                inbound.bytesReceived >= _lastBytesReceived)
            {
                _receiveBitrateMbps = (inbound.bytesReceived - _lastBytesReceived) * 8f /
                    (now - _lastStatsSampleAt) / 1_000_000f;
            }

            _lastBytesReceived = inbound.bytesReceived;
            _lastStatsSampleAt = now;
            _packetsLost = packetsLost;
            _framesDropped = inbound.framesDropped;
            _pliCount = inbound.pliCount;
            _nackCount = inbound.nackCount;
            _receiveFps = Mathf.Max(0f, (float)inbound.framesPerSecond);
            _jitterMs = Mathf.Max(0f, (float)inbound.jitter * 1000f);
            _lastFrameIntervalMs = _receiveFps > 0.01f ? 1000f / _receiveFps : 0f;
            if (inbound.framesDecoded > 0)
            {
                _frameCount = inbound.framesDecoded > int.MaxValue
                    ? int.MaxValue
                    : (int)inbound.framesDecoded;
            }
            _videoCodec = ResolveVideoCodec(report, inbound.codecId);
            _status = "Preview active";

            if (lostDelta > 0 || droppedDelta > 0 || pliDelta > 0 || nackDelta > 0)
            {
                Debug.LogWarning(
                    "[WebRtcCameraReceiver] Video degradation: " +
                    $"lost +{lostDelta}, dropped +{droppedDelta}, " +
                    $"PLI +{pliDelta}, NACK +{nackDelta}");
            }

            if (_lastStatsLogAt < 0f || now - _lastStatsLogAt >= StatsLogIntervalSeconds)
            {
                _lastStatsLogAt = now;
                Debug.Log(
                    "[WebRtcCameraReceiver] Receive stats: " +
                    $"codec={_videoCodec} fps={_receiveFps:0.0} " +
                    $"bitrate={_receiveBitrateMbps:0.00}Mbps " +
                    $"decoded={_frameCount} dropped={_framesDropped} " +
                    $"lost={_packetsLost} jitter={_jitterMs:0.0}ms " +
                    $"PLI={_pliCount} NACK={_nackCount}");
            }
        }

        private static string ResolveVideoCodec(RTCStatsReport report, string codecId)
        {
            if (!string.IsNullOrEmpty(codecId) &&
                report.Stats.TryGetValue(codecId, out var stat) &&
                stat is RTCCodecStats codec &&
                !string.IsNullOrEmpty(codec.mimeType))
                return codec.mimeType;
            return "unknown";
        }

        private void ResetInboundStats()
        {
            _frameCount = 0;
            _lastFrameIntervalMs = 0f;
            _packetsLost = 0;
            _framesDropped = 0;
            _pliCount = 0;
            _nackCount = 0;
            _receiveFps = 0f;
            _jitterMs = 0f;
            _receiveBitrateMbps = 0f;
            _videoCodec = string.Empty;
            _lastBytesReceived = 0;
            _lastStatsSampleAt = -1f;
            _lastStatsLogAt = -1f;
        }

        private void HandleRemoteIce(string json)
        {
            if (_peer == null)
                return;
            string candidate = ExtractString(json, "candidate");
            if (string.IsNullOrEmpty(candidate))
                return;
            var init = new RTCIceCandidateInit
            {
                candidate = candidate,
                sdpMid = ExtractString(json, "sdpMid"),
                sdpMLineIndex = ExtractNullableInt(json, "sdpMLineIndex")
            };
            _peer.AddIceCandidate(new RTCIceCandidate(init));
        }

        private void SchedulePeerReset(string status, bool clearSignal)
        {
            if (_resetCoroutine != null)
                return;

            _resetCoroutine = StartCoroutine(ResetPeerNextFrame(status, clearSignal));
        }

        private IEnumerator ResetPeerNextFrame(string status, bool clearSignal)
        {
            yield return null;
            _resetCoroutine = null;
            ResetPeer(clearSignal);
            _status = status;
        }

        private void EnsureWebRtcUpdateLoop()
        {
            if (_updateCoroutine == null)
                _updateCoroutine = StartCoroutine(WebRTC.Update());
        }

        private static string ExtractString(string json, string key)
        {
            string needle = $"\"{key}\"";
            int keyIndex = json.IndexOf(needle, StringComparison.Ordinal);
            if (keyIndex < 0) return string.Empty;
            int colon = json.IndexOf(':', keyIndex + needle.Length);
            if (colon < 0) return string.Empty;
            int start = json.IndexOf('"', colon + 1);
            if (start < 0) return string.Empty;
            var result = new System.Text.StringBuilder();
            bool escape = false;
            for (int i = start + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (escape)
                {
                    switch (c)
                    {
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case '\\': result.Append('\\'); break;
                        case '"': result.Append('"'); break;
                        default: result.Append(c); break;
                    }
                    escape = false;
                }
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    return result.ToString();
                else
                    result.Append(c);
            }
            return string.Empty;
        }

        private static int? ExtractNullableInt(string json, string key)
        {
            string needle = $"\"{key}\"";
            int keyIndex = json.IndexOf(needle, StringComparison.Ordinal);
            if (keyIndex < 0) return null;
            int colon = json.IndexOf(':', keyIndex + needle.Length);
            if (colon < 0) return null;
            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            int end = start;
            while (end < json.Length && char.IsDigit(json[end])) end++;
            if (end == start) return null;
            return int.TryParse(json.Substring(start, end - start), out int value) ? value : null;
        }

        private static string QuoteJson(string value)
        {
            if (value == null) return "null";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        private void OnDestroy()
        {
            StopPreview();
        }
    }
}
