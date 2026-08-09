
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Video.Components.AVPro;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;
using System.Text;

namespace Yamadev.YamachanWebUnit
{
    [RequireComponent(typeof(VRCAVProVideoPlayer))]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class Client : UdonSharpBehaviour
    {
        public int SendTimes = 2;
        public int MaxLength = 100;
        public string ApiBase = "api.yamachan.moe";
        public VRCUrl[] Urls;

        VRCAVProVideoPlayer _player;

        bool _isLoading = false;
        VRCUrl[] _queue = new VRCUrl[0];
        int _current = 0;
        VRCUrl _requestApi;
        float _lastRequestTime = 0f;
        Receiver _receiver;

        public bool IsLoading => _isLoading || _requestApi != null;

        void Start()
        {
            _player = GetComponent<VRCAVProVideoPlayer>();
        }

        void Update() {
            if (!_isLoading) return;

            if (Time.time - _lastRequestTime < 0.2f) return;

            if (_current == _queue.Length)
            {
                _isLoading = false;
                _current = 0;

                if (_requestApi.Equals(VRCUrl.Empty))
                {
                    _receiver.OnRequestSuccess(null);
                    clear();
                    return;
                }

                SendCustomEventDelayedSeconds(nameof(StartDownload), 0.3f);
                return;
            }

            _lastRequestTime = Time.time;
            VRCUrl url = _queue[_current];
            _current++;

            for (int i = 0; i < SendTimes; i++)
            {
                _player.LoadURL(url);
            }
        }

        public void StartDownload()
        {
            VRCStringDownloader.LoadUrl(_requestApi, (IUdonEventReceiver)this);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            _receiver.OnRequestSuccess(result);
            clear();
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            _receiver.OnRequestError();
            clear();
        }

        void clear()
        {
            _queue = new VRCUrl[0];
            _requestApi = null;
            _receiver = null;
        }

        public bool Request(VRCUrl req, string data, Receiver target)
        {
            if (IsLoading)
            {
                _receiver.OnRequestError();
                return false;
            }

            byte[] byteData = Encoding.UTF8.GetBytes(data);
            if (byteData.Length > MaxLength )
            {
                _receiver.OnRequestError();
                return false;
            }

            _queue = new VRCUrl[byteData.Length];
            for (int i = 0; i < byteData.Length; i++)
            {
                VRCUrl targetUrl = Urls[i * 256 + byteData[i]];
                _queue[i] = targetUrl;
            }

            _requestApi = req;
            _receiver = target;
            _isLoading = true;
            _current = 0;

            return true;
        }

        public int GetPercentage() {
            if (!_isLoading) return 100;

            return Mathf.RoundToInt((float)_current / (float)_queue.Length * 100);
        }
    }
}