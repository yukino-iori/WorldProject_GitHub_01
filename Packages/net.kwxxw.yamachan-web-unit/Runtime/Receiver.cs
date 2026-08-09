
using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon;

namespace Yamadev.YamachanWebUnit
{
    public abstract class Receiver : UdonSharpBehaviour
    {
        public virtual void OnRequestSuccess(IVRCStringDownload result) { }
        public virtual void OnRequestError() { }
    }
}