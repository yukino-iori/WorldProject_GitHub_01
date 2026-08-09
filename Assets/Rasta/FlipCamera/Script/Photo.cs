
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;
using UnityEngine.Animations;
using Rasta.RenderTextureSyncerV2;

namespace Rasta.FlipCamera
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class Photo : UdonSharpBehaviour
    {
        [SerializeField] private FlipCamera cameraController;
        [SerializeField] private GameObject followTarget;
        [SerializeField] private GameObject photoObj;
        [SerializeField] private Animator photoAnimator;
        [SerializeField] private Text timeText;
        [SerializeField] private RTS rts;
        [SerializeField] Text debugText;

        [UdonSynced, FieldChangeCallback(nameof(SyncedPhotoState))] private int _syncedPhotoState = -1;
        [UdonSynced, FieldChangeCallback(nameof(SyncedUnixTime))] private long _syncedUnixTime = 0;
        [UdonSynced, FieldChangeCallback(nameof(SyncedWatermarkAngle))] private int _syncedWatermarkAngle = 0;

        private Collider photoCollider;
        private ParentConstraint parentConstraint;
        private Renderer photoRenderer;

        bool isFollow = false;
        bool isActive = false;

        private bool isInited = false;
        private bool needUpdateSyncedVariables = false;


        private void Start()
        {
            GameObject photoParent = this.gameObject.transform.parent.gameObject;
            photoCollider = photoParent.GetComponent<Collider>();
            parentConstraint = photoParent.GetComponent<ParentConstraint>();
            photoRenderer = photoObj.GetComponent<Renderer>();

            photoObj.SetActive(false);
            photoCollider.enabled = false;

            if (Networking.IsOwner(this.gameObject))
            {
                SyncedWatermarkAngle = 0;
                if (SyncedPhotoState == -1)
                    SyncedPhotoState = 0;
            }

            isInited = true;

            if (needUpdateSyncedVariables)
            {
                SyncedPhotoState = SyncedPhotoState;
                SyncedWatermarkAngle = SyncedWatermarkAngle;
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(this.gameObject))
                RequestSerialization();
        }

        public void HandlePickup()
        {
            if (!Networking.IsOwner(this.gameObject))
                Networking.SetOwner(Networking.LocalPlayer, this.gameObject);

            SyncedPhotoState = 3;
        }

        public void HandleDrop()
        {
        }

        public void OnRTSWaitStartDownload()
        {
            photoAnimator.SetInteger("state", 1);
        }

        public void OnRTSDownloading()
        {
            float progress = rts.GetDownloadProgress();
            photoAnimator.Play("DownloadProgress", 1, progress);
            photoAnimator.SetInteger("state", 2);
        }

        public void OnRTSFinishDownload()
        {
            photoAnimator.SetInteger("state", 0);
        }

        public void OnRTSDownloadError()
        {
            photoAnimator.SetInteger("state", 3);
        }

        public void OnRTSClearData()
        {
            photoAnimator.SetInteger("state", 0);
        }

        public int SyncedPhotoState
        {
            set
            {
                bool isChanged = _syncedPhotoState != value;
                _syncedPhotoState = value;

                if (!isInited)
                {
                    needUpdateSyncedVariables = true;
                    return;
                }

                if (_syncedPhotoState == 0)
                {
                    isFollow = false;
                    isActive = false;
                }
                else if (_syncedPhotoState == 1)
                {
                    isFollow = true;
                    isActive = false;
                }
                else if (_syncedPhotoState == 2)
                {
                    isFollow = true;
                    isActive = true;
                }
                else if (_syncedPhotoState == 3)
                {
                    isFollow = false;
                    isActive = true;
                }

                parentConstraint.constraintActive = isFollow;
                photoObj.SetActive(isActive);
                photoCollider.GetComponent<Collider>().enabled = isActive;

                if (!isActive)
                {
                    photoAnimator.SetInteger("state", 0);
                }

                if (isChanged)
                {
                    cameraController.UpdateRemainingPhotoCount();
                }

                if (Networking.IsOwner(this.gameObject))
                    RequestSerialization();

            }
            get => _syncedPhotoState;
        }

        public long SyncedUnixTime
        {
            set
            {
                _syncedUnixTime = value;

                DateTime dt = DateTimeOffset.FromUnixTimeSeconds(_syncedUnixTime).ToLocalTime().DateTime;
                timeText.text = dt.ToString("yyyy/MM/dd HH:mm");

                if (Networking.IsOwner(this.gameObject))
                    RequestSerialization();
            }
            get => _syncedUnixTime;
        }

        public int SyncedWatermarkAngle
        {
            set
            {
                _syncedWatermarkAngle = value;

                if (!isInited)
                {
                    needUpdateSyncedVariables = true;
                    return;
                }

                photoRenderer.materials[0].SetInt("_WatermarkAngle", _syncedWatermarkAngle);

                if (Networking.IsOwner(this.gameObject))
                    RequestSerialization();
            }
            get => _syncedWatermarkAngle;
        }
    }
}