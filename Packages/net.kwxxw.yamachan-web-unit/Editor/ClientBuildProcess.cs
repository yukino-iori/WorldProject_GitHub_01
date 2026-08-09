using System.Collections.Generic;
using UdonSharp;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;

namespace Yamadev.YamachanWebUnit
{
    public class ClientBuildProcess : IProcessSceneWithReport
    {
        public int callbackOrder => -1;

        public void OnProcessScene(Scene scene, BuildReport report) => GenerateUrls();

        void GenerateUrls()
        {
            Client[] clients = Resources.FindObjectsOfTypeAll<Client>();

            foreach (Client client in clients)
            {
                if (!AssetDatabase.GetAssetOrScenePath(client).Contains(".unity")) continue;

                UdonSharpBehaviour udon = client.GetComponent<UdonSharpBehaviour>();
                int maxLength = (int)udon.GetProgramVariable("MaxLength");
                string api = (string)udon.GetProgramVariable("ApiBase");

                List<VRCUrl> urls = new List<VRCUrl>();
                for (int i = 0; i < maxLength * 256; i++)
                {
                    VRCUrl url = new VRCUrl($"rtsp://{api}/vrchat/set?i={i/256}&b={i%256}");
                    urls.Add(url);
                }
                udon.SetProgramVariable("Urls", urls.ToArray());
            }
        }
    }
}