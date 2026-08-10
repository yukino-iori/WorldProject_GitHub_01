
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace bironist.imagePad {

    public class imagePadHolder : UdonSharpBehaviour {
#if UNITY_ANDROID
        private bool isQuest = true;
#else
        private bool isQuest = false;
#endif
        [SerializeField, Header("タブレットが無いときは画面を隠す")] private bool isHideScreen;
        private bool isMount;
        private imagePad imagePad;

        [SerializeField] private RectTransform image;
        [SerializeField] private UnityEngine.UI.RawImage rawImage;
        [SerializeField] private GameObject panelBackGround;
        [SerializeField] private AudioSource audio;
        [SerializeField] private AudioClip audioMount;
        private Vector3 targetScale = Vector3.zero;

        void Start() {
            if (isHideScreen) panelBackGround.SetActive(false);
            SetTexture(null);
        }

        void OnTriggerEnter(Collider other) {
            imagePad ip = other.transform.GetComponent<imagePad>();
            if (ip != null && !isMount) {
                //imagePadである
                if (isHideScreen) panelBackGround.SetActive(true);
                imagePad = ip;
                imagePad.HoldTablet(this.transform);
                audio.PlayOneShot(audioMount);
                isMount = true;
            }
        }
        void OnTriggerExit(Collider other) {
            imagePad ip = other.transform.GetComponent<imagePad>();
            if (isMount && imagePad==ip) {
                if (isHideScreen) panelBackGround.SetActive(false);
                imagePad.ReleaseTablet();
                SetTexture(null);
                imagePad = null;
                isMount = false;
            }
        }

        public void SetTexture(Texture2D tex) {
            image.localScale = Vector3.zero;

            if (tex == null) {
                targetScale = Vector3.zero;
                //image.localScale = Vector3.zero;
                //rawImage.color = new Color(1f, 1f, 1f, 0f);
            } else {
                float texRatio = (float)tex.width / (float)tex.height;
                float screenRatio = image.rect.width / image.rect.height;// 4f / 3f;
                if (texRatio > screenRatio) {
                    //スクリーンより横長画像
                    targetScale = new Vector3(1f, 1f / texRatio * screenRatio, 1f);
                } else {
                    //スクリーンより縦長画像
                    targetScale = new Vector3(1f * texRatio / screenRatio, 1f, 1f);
                }

                //Questだと何故かテクスチャがY軸判定してしまうのを補正する
                //処理はVRChat側が修正したようなのでもういらない？
                //if (isQuest) targetScale.y = -targetScale.y;
                //rawImage.color = new Color(1f, 1f, 1f, 1f);
                rawImage.texture = tex;
            }
        }
        private void Update() {
            if (image.localScale != targetScale) {
                image.localScale = Vector3.Lerp(image.localScale, targetScale, Time.deltaTime * 8f);
            }
        }
    }
}