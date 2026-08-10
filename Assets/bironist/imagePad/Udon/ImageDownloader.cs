

using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.Image;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace bironist.imagePad {
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ImageDownloader : UdonSharpBehaviour {
#if UNITY_ANDROID
        private bool isQuest = true;
#else
        private bool isQuest = false;
#endif
        [UdonSynced(UdonSyncMode.None)] private VRCUrl url;
        [SerializeField] private VRCUrlInputField urlInput;
        [SerializeField] private RectTransform rotateRoot;
        [SerializeField] private RectTransform image;
        [SerializeField] private UnityEngine.UI.RawImage rawImage;
        [SerializeField] private UnityEngine.UI.Text textInfo;
        [SerializeField] private UnityEngine.UI.Text textTime;

        [SerializeField] private Animator animator;
        [SerializeField] private VRCPickup pickup;

        [SerializeField] private AudioSource audio;
        [SerializeField] private AudioClip audioSuccess;
        [SerializeField] private AudioClip audioError;

        [SerializeField] private UnityEngine.UI.Image imageBack;
        [SerializeField] private Sprite spriteHome;
        [SerializeField] private Sprite spriteBack;

        private VRCImageDownloader imageDownloader;
        private Material material;
        private UdonBehaviour udon;
        private Texture2D tex;
        private IVRCImageDownload downloadTask;

        private float timeLeft = 1f;
        private int currentRotate = 0;//0=横 1=右90 2=天地返し 3=左90
        private int beforeRotate =0;
        private Quaternion targetRotate = Quaternion.identity;
        private Vector3 targetScale = Vector3.zero;
        private bool isUIView = false;
        private int timeCount = 1;
        private float interactTime = 0f;
        private bool isStart = false;
        private float lastLoadTime = 0f;

        void Start() {
            rawImage.enabled = false;
            imageDownloader = new VRCImageDownloader();
            udon = this.transform.GetComponent<UdonBehaviour>();
            tex = null;

            isStart = true;
        }

        public void OnURLChanged() {
            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);

            url = urlInput.GetUrl();
            urlInput.SetUrl(VRCUrl.Empty);
            RequestSerialization();
            DownloadImage();
        }

        public override void OnDeserialization() {
            DownloadImage();
        }

        public void DownloadPriset(VRCUrl urlPriset) {
            //連打対策
            if (Time.time - lastLoadTime<5f) {
                audio.PlayOneShot(audioError);
                return;
            }
            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
            lastLoadTime = Time.time;
            url = urlPriset;
            RequestSerialization();
            DownloadImage();
        }

        public void DownloadImage() {
            if (url.ToString().Length < 11) {
                CloseImage();
                return;
            }
            if (url.ToString().Substring(0, 4) == "http") {
                if (!isStart) Start();
                downloadTask = imageDownloader.DownloadImage(url, material, udon);
                animator.SetBool("InfoEnable", true);
            } else {
                CloseImage();
            }
        }

        public override void OnImageLoadSuccess(IVRCImageDownload result) {
            downloadTask = null;
            imageBack.sprite = spriteBack;
            isUIView = false;
            animator.SetBool("URLEnable", false);
            animator.SetBool("InfoEnable", true);
            SendCustomEventDelayedSeconds("DisableUIInfo", 3f);
            rawImage.enabled = true;
            tex = result.Result;
            rawImage.texture = tex;
            //ラージスクリーン
            this.transform.parent.GetComponent<imagePad>().UpdateTexture(tex);
            textInfo.text = string.Format($"Download Successful! ImageSize:{tex.width} x {tex.height}");
            audio.PlayOneShot(audioSuccess);
            image.localScale = Vector3.zero;
            PadRotate();
        }

        public override void OnImageLoadError(IVRCImageDownload result) {
            downloadTask = null;
            rawImage.enabled = false;
            imageBack.sprite = spriteHome;
            isUIView = false;
            animator.SetBool("URLEnable", false);
            animator.SetBool("InfoEnable", true);
            SendCustomEventDelayedSeconds("DisableUIInfo", 6f);
            textInfo.text =string.Format($"Download Failure! Error: {result.ErrorMessage}");
            tex = null;
            this.transform.parent.GetComponent<imagePad>().UpdateTexture(tex);
            audio.PlayOneShot(audioError);
        }

        public void ButtonClose() {
            SendCustomNetworkEvent(NetworkEventTarget.All, "CloseImage");
        }

        public void CloseImage() {
            rawImage.enabled = false;
            imageBack.sprite = spriteHome;
            isUIView = false;
            tex = null;
            this.transform.parent.GetComponent<imagePad>().UpdateTexture(tex);
            animator.SetBool("URLEnable", false);
            animator.SetBool("InfoEnable", false);
        }

        public Texture2D GetTexture() {
            return tex;
        }

        public void DisableUIInfo() {
            animator.SetBool("InfoEnable", false);
        }

        public void PadRotate() {
            if (tex == null) return;
            float texRatio = (float)tex.width / (float)tex.height;
            float screenRatio = 4f / 3f;
            switch(currentRotate){
                case 0:
                    screenRatio = 4f / 3f;
                    rotateRoot.sizeDelta = new Vector2(2048f, 1536f);
                    targetRotate = Quaternion.Euler(0f, 0f, 0f);
                    break;
                case 1:
                    screenRatio = 3f / 4f;
                    rotateRoot.sizeDelta = new Vector2(1536f,2048f);
                    targetRotate = Quaternion.Euler(0f, 0f, 90f);
                    break;
                case 2:
                    screenRatio = 4f / 3f;
                    rotateRoot.sizeDelta = new Vector2(2048f, 1536f);
                    targetRotate = Quaternion.Euler(0f, 0f, 180f);
                    break;
                case 3:
                    screenRatio = 3f / 4f;
                    rotateRoot.sizeDelta = new Vector2(1536f,2048f);
                    targetRotate = Quaternion.Euler(0f, 0f, -90f);
                    break;
            }
            if (texRatio > screenRatio) {
                //スクリーンより横長画像
                targetScale=new Vector3(1f, 1f / texRatio * screenRatio, 1f);
                //image.localScale = Vector3.zero;
            } else {
                //スクリーンより縦長画像
                targetScale = new Vector3(1f* texRatio / screenRatio, 1f, 1f);
                //image.localScale = Vector3.zero;
            }

            //Questだと何故かテクスチャがY軸判定してしまうのを補正する
            //処理はVRChat側が修正したようなのでもういらない？
            //if (isQuest) targetScale.y = -targetScale.y


        }
        public void ToggleUI() {
            if (Time.time - interactTime < 0.2f && interactTime != 0f) return;

            isUIView = !isUIView;
            if (isUIView) {
                if (downloadTask == null) urlInput.enabled = true;
                else urlInput.enabled = false;
            }
            animator.SetBool("URLEnable", isUIView);
            animator.SetBool("InfoEnable", isUIView);

            interactTime = Time.time;
        }
        public void ButtonScreenClick() {
            if (!Networking.LocalPlayer.IsUserInVR() && pickup.IsHeld) {
                //デスクトップでPickupUseするとスクリーンクリックイベントも同時に発生してしまう対策
            } else {
                ToggleUI();
            }
        }

        private void Update() {
            if (rotateRoot.localRotation != targetRotate) {
                rotateRoot.localRotation = Quaternion.Lerp(rotateRoot.localRotation, targetRotate, Time.deltaTime * 8f);
            }
            if (image.localScale != targetScale) {
                image.localScale = Vector3.Lerp(image.localScale, targetScale, Time.deltaTime * 8f);
            }

            //回転検出は0.5秒に1回
            timeLeft -= Time.deltaTime;
            if (timeLeft < 0f) {
                timeLeft = 0.5f;
                float zr = this.transform.rotation.eulerAngles.z;
                if (zr > 315f || zr <= 45f) currentRotate = 0;
                else if (zr > 45f && zr <= 135f) currentRotate = 1;
                else if (zr > 135f && zr <= 225f) currentRotate = 2;
                else if (zr > 225f && zr <= 315f) currentRotate = 3;

                if (beforeRotate != currentRotate) {
                    PadRotate();
                    beforeRotate = currentRotate;
                }
                //時計更新は10秒に1回で十分
                timeCount--;
                if (timeCount == 0) {
                    textTime.text = System.DateTime.Now.ToString("HH:mm\n") +
                    "<size=64>" + System.DateTime.Now.ToString("yyyy/MM/dd") +
                    " (" + System.DateTime.Now.DayOfWeek.ToString() + ")</size>";
                    timeCount = 20;
                }
            }

            if (downloadTask != null) {
                textInfo.text = downloadTask.State.ToString() +
                                " (" + (downloadTask.Progress * 100).ToString("F2") + ")";
            }
        }

        private void OnDestroy() {
            imageDownloader.Dispose();
        }
    }
}