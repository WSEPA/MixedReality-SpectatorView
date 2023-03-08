// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;

namespace Microsoft.MixedReality.SpectatorView
{
    /// <summary>
    /// MonoBehaviour that reports tracking information for a HoloLens device.
    /// </summary>
    public class HoloLensTrackingObserver : TrackingObserver
    {
        /// <inheritdoc/>
        public override TrackingState TrackingState
        {
            get
            {
#if UNITY_WSA
                //if (UnityEngine.XR.WSA.WorldManager.state == UnityEngine.XR.WSA.PositionalLocatorState.Active)
                if (UnityEngine.XR.ARFoundation.ARSession.state == UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking)
                {
                    return TrackingState.Tracking;
                }

                return TrackingState.LostTracking;
#elif UNITY_EDITOR
                return TrackingState.Tracking;
#else
                return TrackingState.Unknown;
#endif
            }
        }
    }
}