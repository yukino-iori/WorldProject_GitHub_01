using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using System;
using VRC.SDK3.Components;
using VRC.SDK3.StringLoading;
using System.Security.Policy;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Image;
using Rasta.RenderTextureSyncerV2;

namespace Rasta.FlipCamera
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class FlipCamera : UdonSharpBehaviour
    {
        [SerializeField] private bool enableRenderTextureSyncer = true;
        [SerializeField] private bool enableFlip = true;
        [Space(20)]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private RawImage cameraDisplay;
        [SerializeField] private GameObject photosParent;
        [SerializeField] private Animator animator;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Text countText;

        [UdonSynced, FieldChangeCallback(nameof(SyncedStreamingRTIndex))] private int _syncedStreamingRTIndex = int.MinValue;
        [UdonSynced, FieldChangeCallback(nameof(SyncedIsSleep))] private bool _syncedIsSleep = true;

        private GameObject[] photos;
        private VRCPickup[] photosVRCPickup;
        private RenderTexture[] RTs;
        private Photo[] photoControllers;
        private DateTime sleepTime = new DateTime();
        private RTS[] RTSs;
        private VRCPickup vrcPickup;
        private bool _isFlip = false;
        private bool _isPrintout = false;
        private int isSyncWatermarkAngle = 0;
        private RawImage watermark;
        private RectTransform watermarkRectTransform;
        private Texture watermark_H;
        private Texture watermark_V;
        private int befWatermarkAngle = -1;
        private DateTime shotTime;

        private int remainingPhotoCount = -1;
        private bool isInited = false;
        private bool needUpdateSyncedVariables = false;

        void Start()
        {
            photos = new GameObject[photosParent.transform.childCount];
            photoControllers = new Photo[photos.Length];
            photosVRCPickup = new VRCPickup[photos.Length];
            RTs = new RenderTexture[photos.Length + 1];
            RTSs = new RTS[photos.Length];
            vrcPickup = this.gameObject.transform.parent.gameObject.GetComponent<VRCPickup>();
            watermark = cameraDisplay.transform.parent.Find("Watermark/WatermarkDisplay").gameObject.GetComponent<RawImage>();
            watermarkRectTransform = watermark.gameObject.GetComponent<RectTransform>();

            for (int i = 0; i < photos.Length; i++)
            {
                photos[i] = photosParent.transform.GetChild(i).gameObject;
                photoControllers[i] = photos[i].transform.Find("Controller").GetComponent<Photo>();
                photosVRCPickup[i] = photos[i].GetComponent<VRCPickup>();

                RTs[i] = new RenderTexture(1600, 1200, 16, RenderTextureFormat.ARGB32);
                photos[i].transform.Find("Photo").GetComponent<Renderer>().materials[0].mainTexture = RTs[i];

                RTSs[i] = photos[i].transform.Find("Controller/RenderTextureSyncer").GetComponent<RTS>();
                RTSs[i].TargetRenderTexture = RTs[i];
                //RTSs[i].EventTriggerTarget = photoControllers[i].GetComponent<UdonBehaviour>();
            }
            RTs[RTs.Length - 1] = new RenderTexture(1600, 1200, 16, RenderTextureFormat.ARGB32);

            targetCamera.targetTexture = RTs[RTs.Length - 1];
            cameraDisplay.texture = RTs[RTs.Length - 1];

            Material photoMat = photos[0].transform.Find("Photo").GetComponent<Renderer>().materials[0];
            if (photoMat.GetTexture("_WatermarkTexHorizontal"))
            {
                watermark_H = photoMat.GetTexture("_WatermarkTexHorizontal");
                isSyncWatermarkAngle = isSyncWatermarkAngle | 1;
            }
            if (photoMat.GetTexture("_WatermarkTexVertical"))
            {
                watermark_V = photoMat.GetTexture("_WatermarkTexVertical");
                isSyncWatermarkAngle = isSyncWatermarkAngle | 2;
            }
            if (isSyncWatermarkAngle > 0)
            {
                Color wcol = watermark.color;
                wcol.a = 1;
                watermark.color = wcol;
            }

            if (Networking.IsOwner(this.gameObject))
            {
                SyncedStreamingRTIndex = 0;
            }

            if (remainingPhotoCount == -1)
                SetRemainingPhotoCount(photos.Length);

            shotTime = DateTime.Now.AddSeconds(-1);

            isInited = true;

            // 同期変数の初期化処理
            if (needUpdateSyncedVariables)
                SyncedStreamingRTIndex = SyncedStreamingRTIndex;

            UpdateRemainingPhotoCount();
        }

        private void Update()
        {
            if (vrcPickup.IsHeld)
            {
                int watermarkAngle = getWatermarkAngle();
                if (watermarkAngle != -1 && watermarkAngle != befWatermarkAngle)
                {
                    befWatermarkAngle = watermarkAngle;

                    watermarkRectTransform.localRotation = Quaternion.Euler(0, 0, 90 * watermarkAngle);

                    if (watermarkAngle % 2 == 0)
                    {
                        watermarkRectTransform.localScale = Vector3.one;
                        watermark.texture = watermark_H;
                    }
                    else
                    {
                        watermarkRectTransform.localScale = new Vector3(3/4.0f, 4/3.0f, 1.0f);
                        watermark.texture = watermark_V;
                    }
                }


                if (enableFlip)
                {
                    Vector3 headPos = vrcPickup.currentPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                    Vector3 cameraRelative = this.gameObject.transform.InverseTransformPoint(headPos);
                    if (cameraRelative.z > 0)
                    {
                        IsFlip = true;
                    }
                    else
                    {
                        IsFlip = false;
                    }
                }
            }
            else
            {
                if (!SyncedIsSleep && DateTime.Now > sleepTime)
                {
                    if (Networking.IsOwner(this.gameObject))
                    {
                        SyncedIsSleep = true;
                    }
                }
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (player.isLocal)
            {
                SyncedStreamingRTIndex = SyncedStreamingRTIndex;
                SyncedIsSleep = SyncedIsSleep;
            }
        }

        public void HandlePickup()
        {
            if (!Networking.IsOwner(this.gameObject))
                Networking.SetOwner(Networking.LocalPlayer, this.gameObject);

            SyncedIsSleep = false;

            // 最初にstate1が1枚もないため、Pickup時に初期化
            if (SyncedStreamingRTIndex >= 0)
            {
                if (!Networking.IsOwner(photoControllers[SyncedStreamingRTIndex].gameObject))
                    Networking.SetOwner(Networking.LocalPlayer, photoControllers[SyncedStreamingRTIndex].gameObject);
                photoControllers[SyncedStreamingRTIndex].SyncedPhotoState = 1;
            }

            // カメラに追従している写真のOwnerを移動
            int[] indexes = new int[2] { findPhotoStateIndex(1), findPhotoStateIndex(2) };
            foreach (int i in indexes)
            {
                if (i != -1)
                {
                    if (!Networking.IsOwner(photos[i]))
                        Networking.SetOwner(Networking.LocalPlayer, photos[i]);
                    if (!Networking.IsOwner(photoControllers[i].gameObject))
                        Networking.SetOwner(Networking.LocalPlayer, photoControllers[i].gameObject);
                }
            }
        }

        public void HandlePickupUseDown()
        {
            if (DateTime.Now < shotTime.AddSeconds(1))
                return;
            shotTime = DateTime.Now;

            // 写真が取れる状態か
            if (findPhotoStateIndex(2) == -1 && SyncedStreamingRTIndex >= 0)
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ShotSound));

                // ストリーミングしている写真を出力
                photoControllers[SyncedStreamingRTIndex].SyncedPhotoState = 2;

                // 撮影時間を記録
                photoControllers[SyncedStreamingRTIndex].SyncedUnixTime = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();

                // ウォーターマークの向きを計算
                int watermarkAngle = getWatermarkAngle();
                if (watermarkAngle != -1)
                {
                    photoControllers[SyncedStreamingRTIndex].SyncedWatermarkAngle = watermarkAngle;
                }

                // RenderTextureSyncer
                if (enableRenderTextureSyncer)
                {
                    RTSs[SyncedStreamingRTIndex].StoreAllPlayers();
                }

                // RenderTextureの切り替え
                SyncedStreamingRTIndex = findPhotoStateIndex(0);

                // 切り替えた写真のStateを変更(非表示、追従) 
                if (SyncedStreamingRTIndex >= 0)
                {
                    photoControllers[SyncedStreamingRTIndex].SyncedPhotoState = 1;
                }
            }
        }

        private int getWatermarkAngle()
        {
            if (isSyncWatermarkAngle == 0)
            {
                return -1;
            }
            else
            {
                int watermarkAngle = 0;
                Transform tf = this.gameObject.transform;
                if (Mathf.Abs(tf.up.y) >= Mathf.Abs(tf.right.y) && isSyncWatermarkAngle != 2)
                {
                    if (tf.up.y >= 0)
                        watermarkAngle = 0;
                    else
                        watermarkAngle = 2;
                }
                else if (isSyncWatermarkAngle != 1)
                {
                    if (tf.right.y >= 0)
                        watermarkAngle = 3;
                    else
                        watermarkAngle = 1;
                }

                return watermarkAngle;
            }
        }

        public void HandleDrop()
        {
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SetSleepTime));
        }

        private void SetRemainingPhotoCount(int remainingPhotoCount)
        {
            this.remainingPhotoCount = remainingPhotoCount;
            countText.text = remainingPhotoCount.ToString() + "/" + photos.Length.ToString();
        }

        public void SetSleepTime()
        {
            sleepTime = DateTime.Now.AddSeconds(10);
        }

        public int SyncedStreamingRTIndex
        {
            set
            {
                _syncedStreamingRTIndex = value;

                if (!isInited)
                {
                    needUpdateSyncedVariables = true;
                    return;
                }

                UpdateStreamingRTIndex();

                if (Networking.IsOwner(this.gameObject))
                    RequestSerialization();
            }
            get => _syncedStreamingRTIndex;
        }

        private void UpdateStreamingRTIndex()
        {
            // RenderTextureの適用先を変更
            if (SyncedStreamingRTIndex >= 0)
            {
                targetCamera.targetTexture = RTs[SyncedStreamingRTIndex];
                cameraDisplay.texture = RTs[SyncedStreamingRTIndex];
            }
            else if (SyncedStreamingRTIndex == -1)
            {
                // 残り枚数がない場合は予備のRenderTextureを表示
                targetCamera.targetTexture = RTs[RTs.Length - 1];
                cameraDisplay.texture = RTs[RTs.Length - 1];
            }
        }

        private bool IsFlip
        {
            set
            {
                _isFlip = value;
                animator.SetBool("flip", value);
            }
            get => _isFlip;
        }

        public bool SyncedIsSleep
        {
            set
            {
                _syncedIsSleep = value;

                targetCamera.enabled = !_syncedIsSleep;

                animator.SetBool("sleep", _syncedIsSleep);
                if (_syncedIsSleep && enableFlip)
                {
                    IsFlip = false;
                }

                if (Networking.IsOwner(this.gameObject))
                    RequestSerialization();
            }
            get => _syncedIsSleep;
        }


        public bool IsPrintout
        {
            set
            {
                if (_isPrintout != value)
                {
                    _isPrintout = value;

                    if (_isPrintout)
                        animator.Play("Printout");
                    else
                        animator.Play("ResetPrintout");
                }
            }
            get => _isPrintout;
        }

        private void OnTriggerEnter(Collider other)
        {
            int index = Array.IndexOf(photos, other.gameObject);
            if (index != -1)
            {
                // カメラに入れた写真を持ってる人のみ実行
                if (Networking.IsOwner(photoControllers[index].gameObject) && photosVRCPickup[index].currentPlayer == Networking.LocalPlayer)
                {
                    photoControllers[index].SyncedPhotoState = 0;
                    photos[index].GetComponent<VRC_Pickup>().Drop();
                    if (enableRenderTextureSyncer)
                    {
                        RTSs[index].SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, nameof(RTS.Clear));
                    }
                }
            }
        }

        public void UpdateRemainingPhotoCount()
        {
            if (!isInited) return;

            SetRemainingPhotoCount(countPhotoState(0) + countPhotoState(1));

            if (Networking.IsOwner(this.gameObject) && SyncedStreamingRTIndex == -1 && remainingPhotoCount > 0)
            {
                if (countPhotoState(1) == 0)
                {
                    SyncedStreamingRTIndex = findPhotoStateIndex(0);
                    if (!Networking.IsOwner(photos[SyncedStreamingRTIndex]))
                        Networking.SetOwner(Networking.LocalPlayer, photos[SyncedStreamingRTIndex]);
                    if (!Networking.IsOwner(photoControllers[SyncedStreamingRTIndex].gameObject))
                        Networking.SetOwner(Networking.LocalPlayer, photoControllers[SyncedStreamingRTIndex].gameObject);
                    photoControllers[SyncedStreamingRTIndex].SyncedPhotoState = 1;
                }
            }

            IsPrintout = countPhotoState(2) != 0;
        }

        private int countPhotoState(int targetState)
        {
            int count = 0;
            for (int i = 0; i < photoControllers.Length; i++)
            {
                int state = photoControllers[i].SyncedPhotoState;
                if (state == targetState)
                {
                    count++;
                }
            }
            return count;
        }

        private int findPhotoStateIndex(int targetState)
        {
            int index = -1;
            for (int i = 0; i < photoControllers.Length; i++)
            {
                int state = photoControllers[i].SyncedPhotoState;
                if (state == targetState)
                {
                    index = i;
                    break;
                }
            }
            return index;
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(this.gameObject))
                RequestSerialization();
        }

        public void ShotSound()
        {
            audioSource.PlayOneShot(audioSource.clip);
        }
    }
}