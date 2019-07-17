// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Assuming assembly reference 'System.Numerics.Vectors, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a' used by 'Windows.Foundation.UniversalApiContract' matches
// identity 'System.Numerics.Vectors, Version=4.1.1.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a' of 'System.Numerics.Vectors', you may need to supply runtime policy
#pragma warning disable 1701

// Enable this preprocessor directive in your player settings as needed.
#if QRCODESTRACKER_BINARY_AVAILABLE

using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.MixedReality.SpatialAlignment;

#if WINDOWS_UWP
using Windows.Perception.Spatial;
using Windows.Perception.Spatial.Preview;
using Microsoft.MixedReality.PhotoCapture;
#endif

using QRCodesTrackerPlugin;
namespace Microsoft.MixedReality.SpectatorView
{
    public static class QRCodeEventArgs
    {
        public static QRCodeEventArgs<TData> Create<TData>(TData data)
        {
            return new QRCodeEventArgs<TData>(data);
        }
    }

    [Serializable]
    public class QRCodeEventArgs<TData> : EventArgs
    {
        public TData Data { get; private set; }

        public QRCodeEventArgs(TData data)
        {
            Data = data;
        }
    }

    public class QRCodesManager : MonoBehaviour
    {
        [Tooltip("Determines if the QR codes scanner should be automatically started.")]
        public bool AutoStartQRTracking = false;

        public bool IsTrackerRunning { get; private set; }
        public QRTrackerStartResult StartResult { get; private set; }
        public bool DebugLogging { get; set; }

        public event EventHandler<QRCodeEventArgs<QRCodesTrackerPlugin.QRCode>> QRCodeAdded;
        public event EventHandler<QRCodeEventArgs<QRCodesTrackerPlugin.QRCode>> QRCodeUpdated;
        public event EventHandler<QRCodeEventArgs<QRCodesTrackerPlugin.QRCode>> QRCodeRemoved;

        private SortedDictionary<System.Guid, QRCodesTrackerPlugin.QRCode> qrCodesList = new SortedDictionary<System.Guid, QRCodesTrackerPlugin.QRCode>();
        private QRTracker qrTracker;
        private object lockObj = new object();
        private Task<QRTrackerStartResult> startTrackerTask = null;
        private CancellationTokenSource startTrackerCTS = null;
        private Task stopTrackerTask = null;

        private static QRCodesManager qrCodesManager;
        public static QRCodesManager FindOrCreateQRCodesManager(GameObject gameObject)
        {
            if (qrCodesManager != null)
                return qrCodesManager;

            qrCodesManager = FindObjectOfType<QRCodesManager>();
            if (qrCodesManager != null)
                return qrCodesManager;

            Debug.Log("QRCodesManager created in scene");
            qrCodesManager = gameObject.AddComponent<QRCodesManager>();
            return qrCodesManager;
        }

        /// <summary>
        /// Tries to obtain the QRCode location in Unity Space.
        /// The position component of the location matrix will be at the top left of the QRCode
        /// The orientation of the location matrix will reflect the following axii:
        /// x axis: horizontal with the QRCode.
        /// y axis: positive direction down the QRCode.
        /// z axis: positive direction outward from the QRCode.
        /// Note: This function should be called from the main thread
        /// </summary>
        /// <param name="id">QRCode guid id</param>
        /// <param name="location">Output location for the QRCode in Unity Space</param>
        /// <returns>returns true if the QRCode was located</returns>
        public bool TryGetLocationForQRCode(Guid id, out Matrix4x4 location)
        {
            location = Matrix4x4.identity;

#if WINDOWS_UWP
            try
            {
                var coordinateSystem = SpatialGraphInteropPreview.CreateCoordinateSystemForNode(id);
                return TryGetLocationForQRCode(coordinateSystem, out location);
            }
            catch (Exception e)
            {
                Debug.LogError($"Exception thrown creating coordinate system for qr code id: {id.ToString()}, {e.ToString()}");
                return false;
            }

#else
            Debug.LogError($"Failed to create coordinate system for qr code id: {id.ToString()}");
            return false;
#endif
        }

#if WINDOWS_UWP
        /// <summary>
        /// Tries to obtain the QRCode location in Unity Space.
        /// The position component of the location matrix will be at the top left of the QRCode
        /// The orientation of the location matrix will reflect the following axii:
        /// x axis: horizontal with the QRCode.
        /// y axis: positive direction down the QRCode.
        /// z axis: positive direction outward from the QRCode.
        /// /// Note: This function should be called from the main thread
        /// </summary>
        /// <param name="coordinateSystem">QRCode SpatialCoordinateSystem</param>
        /// <param name="location">Output location for the QRCode in Unity Space</param>
        /// <returns>returns true if the QRCode was located</returns>
        public bool TryGetLocationForQRCode(SpatialCoordinateSystem coordinateSystem, out Matrix4x4 location)
        {
            location = Matrix4x4.identity;
            if (coordinateSystem != null)
            {
                try
                {
                    WinRTExtensions.GetSpatialCoordinateSystem(UnityEngine.XR.WSA.WorldManager.GetNativeISpatialCoordinateSystemPtr(), out SpatialCoordinateSystem appSpatialCoordinateSystem);
                    if (appSpatialCoordinateSystem != null)
                    {
                        // Get the relative transform from the unity origin
                        System.Numerics.Matrix4x4? relativePose = coordinateSystem.TryGetTransformTo(appSpatialCoordinateSystem);
                        if (relativePose != null)
                        {
                            System.Numerics.Matrix4x4 newMatrix = relativePose.Value;

                            // Platform coordinates are all right handed and unity uses left handed matrices. so we convert the matrix
                            // from rhs-rhs to lhs-lhs
                            // Convert from right to left coordinate system
                            newMatrix.M13 = -newMatrix.M13;
                            newMatrix.M23 = -newMatrix.M23;
                            newMatrix.M43 = -newMatrix.M43;

                            newMatrix.M31 = -newMatrix.M31;
                            newMatrix.M32 = -newMatrix.M32;
                            newMatrix.M34 = -newMatrix.M34;

                            System.Numerics.Vector3 winrtScale;
                            System.Numerics.Quaternion winrtRotation;
                            System.Numerics.Vector3 winrtTranslation;
                            System.Numerics.Matrix4x4.Decompose(newMatrix, out winrtScale, out winrtRotation, out winrtTranslation);

                            var translation = new Vector3(winrtTranslation.X, winrtTranslation.Y, winrtTranslation.Z);
                            var rotation = new Quaternion(winrtRotation.X, winrtRotation.Y, winrtRotation.Z, winrtRotation.W);
                            location = Matrix4x4.TRS(translation, rotation, Vector3.one);

                            return true;
                        }
                        else
                        {
                            Debug.LogWarning("QRCode location unknown or not yet available.");
                            return false;
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Failed to obtain coordinate system for application");
                        return false;
                    }
                }
                catch(Exception e)
                {
                    Debug.LogWarning($"Note: TryGetLocationForQRCode needs to be called from main thread: {e}");
                    return false;
                }
            }
            else
            {
                Debug.LogWarning("Failed to obtain coordinate system for QRCode");
                return false;
            }
        }
#endif // WINDOWS_UWP

        public Guid GetIdForQRCode(string qrCodeData)
        {
            lock (qrCodesList)
            {
                foreach (var ite in qrCodesList)
                {
                    if (ite.Value.Code == qrCodeData)
                    {
                        return ite.Key;
                    }
                }
            }
            return new Guid();
        }

        public IList<QRCodesTrackerPlugin.QRCode> GetList()
        {
            lock (qrCodesList)
            {
                return new List<QRCodesTrackerPlugin.QRCode>(qrCodesList.Values);
            }
        }

        private void Awake()
        {
            IsTrackerRunning = false;
        }

        protected async void Start()
        {
            if (AutoStartQRTracking)
            {
                await StartQRTrackingAsync();
            }
        }

        public Task<QRTrackerStartResult> StartQRTrackingAsync(CancellationToken cancellationToken = default)
        {
            lock (lockObj)
            {
                if (startTrackerTask != null)
                {
                    DebugLog("Returning existing start tracker task");
                    return startTrackerTask;
                }

                stopTrackerTask = null;
                startTrackerCTS = new CancellationTokenSource();

                var hybridCTS = CancellationTokenSource.CreateLinkedTokenSource(startTrackerCTS.Token, cancellationToken);
                DebugLog("Kicking off a new start tracker task");
                return startTrackerTask = Task.Run(() => StartQRTrackingAsyncImpl(hybridCTS.Token), hybridCTS.Token);
            }
        }

        private async Task<QRTrackerStartResult> StartQRTrackingAsyncImpl(CancellationToken token)
        {
#if WINDOWS_UWP
            DebugLog("Requesting webcam capability");
            Windows.Security.Authorization.AppCapabilityAccess.AppCapability cap = Windows.Security.Authorization.AppCapabilityAccess.AppCapability.Create("webcam");
            var accessTask = cap.RequestAccessAsync().AsTask();
            var completedTask = await Task.WhenAny(accessTask, Task.Delay(-1, token));
            var accessStatus = (accessTask == completedTask) ? accessTask.Result : Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.DeniedBySystem;
            if (accessStatus != Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed)
            {
                DebugLog("Failed to obtain webcam capability for qr code tracking");
            }
            else
            {
                DebugLog("Webcam capability granted.");
            }
#endif

            // Note: If the QRTracker is created prior to obtaining the webcam capability, initialization will fail.
            if (qrTracker == null)
            {
                DebugLog("Creating qr tracker");
                qrTracker = new QRTracker();
                qrTracker.Added += QrTracker_Added;
                qrTracker.Updated += QrTracker_Updated;
                qrTracker.Removed += QrTracker_Removed;
            }

            if (!token.IsCancellationRequested &&
                !IsTrackerRunning)
            {
                StartResult = qrTracker.Start();
                if (StartResult == QRTrackerStartResult.Success)
                {
                    DebugLog("Successfully started qr tracker");
                    IsTrackerRunning = true;
                }
                else
                {
                    Debug.LogWarning("Failed to start qr tracker: " + StartResult.ToString());
                }
            }

            return await Task.FromResult(StartResult);
        }

        public Task StopQRTrackingAsync()
        {
            lock (lockObj)
            {
                if (startTrackerTask == null)
                {
                    DebugLog("StopQRTrackerAsync was called when no start task had been created.");
                    return Task.CompletedTask;
                }

                if (stopTrackerTask != null)
                {
                    DebugLog("StopQRTrackerAsync was called when already stopping tracking.");
                    return stopTrackerTask;
                }

                startTrackerCTS.Cancel();
                startTrackerCTS.Dispose();
                startTrackerCTS = null;

                DebugLog("Stop tracker task created.");
                return stopTrackerTask = Task.Run(() => StopQRTrackingAsyncImpl(startTrackerTask));
            }
        }


        private async Task StopQRTrackingAsyncImpl(Task previousTask)
        {
            await previousTask.IgnoreCancellation();

            if (qrTracker != null &&
                IsTrackerRunning)
            {
                qrTracker.Stop();
                IsTrackerRunning = false;
                StartResult = QRTrackerStartResult.DeviceNotConnected;
                DebugLog("QR tracker was stopped.");

                lock (qrCodesList)
                {
                    qrCodesList.Clear();
                    DebugLog("QR Code list was cleared when stopping.");
                }
            }

            lock (lockObj)
            {
                startTrackerTask = null;
                DebugLog("Start tracker task was set back to null.");
            }
        }

        private void QrTracker_Removed(QRCodeRemovedEventArgs args)
        {
            lock (qrCodesList)
            {
                qrCodesList.Remove(args.Code.Id);
            }

            Debug.Log("QR Code Lost: " + args.Code.Code);
            QRCodeRemoved?.Invoke(this, QRCodeEventArgs.Create(args.Code));
        }

        private void QrTracker_Updated(QRCodeUpdatedEventArgs args)
        {
            lock (qrCodesList)
            {
                if (!qrCodesList.ContainsKey(args.Code.Id))
                {
                    Debug.LogWarning("QRCode updated that was not previously being observed: " + args.Code.Code);
                }

                qrCodesList[args.Code.Id] = args.Code;
            }

            QRCodeUpdated?.Invoke(this, QRCodeEventArgs.Create(args.Code));
        }

        private void QrTracker_Added(QRCodeAddedEventArgs args)
        {
            lock (qrCodesList)
            {
                qrCodesList[args.Code.Id] = args.Code;
            }

            Debug.Log("QR Code Added: " + args.Code.Code);
            QRCodeAdded?.Invoke(this, QRCodeEventArgs.Create(args.Code));
        }

        private void DebugLog(string message)
        {
            if (DebugLogging)
            {
                Debug.Log($"QRCodesManager: {message}");
            }
        }
    }
}
#endif // QRCODESTRACKER_BINARY_AVAILABLE