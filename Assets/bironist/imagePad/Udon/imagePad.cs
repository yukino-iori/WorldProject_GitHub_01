
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace bironist.imagePad {
    public class imagePad : UdonSharpBehaviour {
        [SerializeField, Header("プリセット機能を使う")] private bool isPreset = false;

        [SerializeField, Header("プリセット1のタイトル")] private string titlePreset1 = "Preset 1";
        [SerializeField, Header("プリセット1のURL")] private VRCUrl urlPreset1;
        [SerializeField, Header("プリセット2のタイトル")] private string titlePreset2 = "Preset 2";
        [SerializeField, Header("プリセット2のURL")] private VRCUrl urlPreset2;
        [SerializeField, Header("プリセット3のタイトル")] private string titlePreset3 = "Preset 3";
        [SerializeField, Header("プリセット3のURL")] private VRCUrl urlPreset3;

        [SerializeField] private ImageDownloader id;
        [SerializeField] private GameObject panelPreset;
        [SerializeField] private GameObject buttonPreset1;
        [SerializeField] private GameObject buttonPreset2;
        [SerializeField] private GameObject buttonPreset3;
        [SerializeField] private VRC_Pickup pickup;

        private imagePadHolder iph;

        void Start() {
            if (isPreset) {
                panelPreset.SetActive(true);
                buttonPreset1.transform.Find("Text").GetComponent<UnityEngine.UI.Text>().text = titlePreset1;
                buttonPreset2.transform.Find("Text").GetComponent<UnityEngine.UI.Text>().text = titlePreset2;
                buttonPreset3.transform.Find("Text").GetComponent<UnityEngine.UI.Text>().text = titlePreset3;
            } else {
                panelPreset.SetActive(false);
                buttonPreset1.SetActive(false);
                buttonPreset2.SetActive(false);
                buttonPreset3.SetActive(false);
            }
        }

        public void ButtonPriset1() {
            id.DownloadPriset(urlPreset1);
        }
        public void ButtonPriset2() {
            id.DownloadPriset(urlPreset2);
        }
        public void ButtonPriset3() {
            id.DownloadPriset(urlPreset3);
        }
        public void HoldTablet(Transform target) {
            pickup.Drop();
            iph = target.GetComponent<imagePadHolder>();
            if (iph != null) iph.SetTexture(id.GetTexture());
            this.transform.SetPositionAndRotation(target.position, target.rotation);
        }
        public void ReleaseTablet() {
            iph = null;
        }
        public void UpdateTexture(Texture2D tex) {
            if (iph != null) {
                iph.SetTexture(tex);
            }
        }

        public override void OnPickupUseDown() {
            id.ToggleUI();
        }
    }
}