
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace Rasta.FlipCamera
{
    public class EventDispatcher : UdonSharpBehaviour
    {
        [SerializeField] UdonBehaviour target;

        [SerializeField] bool pickupHandler = false;
        [SerializeField] bool pickupUseDownHandler = false;
        [SerializeField] bool pickupUseUpHandler = false;
        [SerializeField] bool dropHandler = false;

        public override void OnPickup()
        {
            if (pickupHandler)
                target.SendCustomEvent("HandlePickup");
        }

        public override void OnPickupUseDown()
        {
            if (pickupUseDownHandler)
                target.SendCustomEvent("HandlePickupUseDown");
        }

        public override void OnPickupUseUp()
        {
            if (pickupUseUpHandler)
                target.SendCustomEvent("HandlePickupUseUp");
        }

        public override void OnDrop()
        {
            if (dropHandler)
                target.SendCustomEvent("HandleDrop");
        }
    }
}
