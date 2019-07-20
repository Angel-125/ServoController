using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;
using System.Text.RegularExpressions;
using Expansions.Serenity;

namespace ServoController
{
    public class WBIBGTrackingServo: ModuleRoboticRotationServo
    {
        #region Constants
        const string kVesselIDNone = "NONE";
        const string kTrackingNone = "Nothing";
        const string kNotEnoughResource = "Insufficient ";
        const string kBlockedBy = "Blocked by ";
        const string kTracking = "Tracking: ";
        const string kSearching = "Searching for ";
        const string kLOS = " (LOS)";
        const float kMessageDuration = 3.0f;
        const float kOverheadAquisitionAngle = 5.0f;
        const float kAcquisitionRotationThresholdAngle = 5.0f;
        #endregion

        #region Fields
        /// <summary>
        /// Flag to enable/disable debug fields.
        /// </summary>
        [KSPField]
        public bool debugEnabled = false;

        /// <summary>
        /// Name of the rotation transform
        /// </summary>
        [KSPField]
        public string rotationTransformName = string.Empty;

        /// <summary>
        /// Fixed reference relative to the rotation transform. Make sure z-axis is facing the same direction.
        /// </summary>
        [KSPField]
        public string referenceRotationTransformName = string.Empty;

        /// Flag to indicate whether or not tracking is enabled.
        /// </summary>
        [KSPField(isPersistant = true, guiName = "Tracking Systems", guiActive = true)]
        [UI_Toggle(enabledText = "On", disabledText = "Off")]
        public bool enableTracking = false;

        /// <summary>
        /// Flag to indicate whether or not we can track random objects.
        /// </summary>
        [KSPField]
        public bool canTrackRandomObjects = true;

        /// <summary>
        /// Flag to indicate whether or not we are currently tracking random objects.
        /// If the vessel has one or more tracking controllers and they're all tracking random objects, then the first controller will select the random target and all other
        /// controllers will track the same target.
        /// </summary>
        [KSPField(isPersistant = true, guiName = "Track random objects", guiActive = true)]
        [UI_Toggle(enabledText = "Yes", disabledText = "No")]
        public bool trackRandomObjects = false;

        /// <summary>
        /// Current tracking status
        /// </summary>
        [KSPField(guiActive = true, guiName = "Tracking")]
        public string trackingStatus = string.Empty;

        /// <summary>
        /// How long in seconds to track a random target.
        /// </summary>
        [KSPField]
        public double randomTrackDuration = 0;

        /// <summary>
        /// Flag to indicate if we can track player-selected targets.
        /// </summary>
        [KSPField]
        public bool canTrackPlayerTargets = true;

        /// <summary>
        /// Angle to rotate the servo to.
        /// </summary>
        [KSPField(guiName = "Target rotation")]
        public float targetRotationAngle;
        #endregion

        #region Housekeeping
        public Vessel targetVessel;
        public CelestialBody targetBody;
        public Transform targetTransform;
        public string targetName;
        bool targetObjectSet;

        Transform rotationReferenceTransform;
        Transform rotationTransform;

        double randomTrackStart = -1f;
        double losCooldownStart;
        #endregion

        #region PAW Events
        /// <summary>
        /// Clears the current target and readies the tracking controller to track something else.
        /// This event isn't enabled unless the controller can track random targets, and it is currently tracking random targets.
        /// </summary>
        [KSPEvent(guiName = "Track something else", guiActive = true)]
        public void ClearTarget()
        {
            FlightGlobals.fetch.SetVesselTarget((ITargetable)null, false);
            targetBody = null;
            targetTransform = null;
        }
        #endregion

        #region Overrides
        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            Fields["targetRotationAngle"].guiActive = debugEnabled;

            //Find our rotation transform.
            rotationTransform = this.part.FindModelTransform(rotationTransformName);

            //This is needed to calculate the reference angle between a fixed point and rotationTransform.
            rotationReferenceTransform = this.part.FindModelTransform(referenceRotationTransformName);
        }

        protected override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (!HighLogic.LoadedSceneIsFlight)
                return;
            if (!enableTracking)
                return;

            //Update GUI
            Fields["trackRandomObjects"].guiActive = !targetObjectSet && canTrackRandomObjects;
            Events["ClearTarget"].active = trackRandomObjects && !targetObjectSet && canTrackRandomObjects;

            /*
            //Check to see if we've moved into the rotation threshold.
            if (Mathf.Abs(targetAngle) <= kAcquisitionRotationThresholdAngle)
            {
                trackingStatus = targetName;
                losCooldownStart = Planetarium.GetUniversalTime();

                //If we will only track for a limited time, then check our expiration timer
                if (randomTrackDuration > 0)
                {
                    if (randomTrackStart == -1f)
                    {
                        randomTrackStart = Planetarium.GetUniversalTime();
                    }
                    else if (Planetarium.GetUniversalTime() - randomTrackStart >= randomTrackDuration)
                    {
                        targetTransform = null;
                        randomTrackStart = -1f;
                    }
                }
            }
            */

            //If the servo is moving then wait until it's done.
            if (IsMoving())
                return;

            //Update the tracking target
            bool hasTargetToTrack = UpdateTarget();

            //If we have no target to track then we're done.
            if (!hasTargetToTrack)
                return;

            //Calculate and set target rotation
            Vector3 inversePosition;
            if (rotationTransform != null && rotationReferenceTransform != null)
            {
                inversePosition = rotationTransform.InverseTransformPoint(targetTransform.position);
                targetRotationAngle = Mathf.Atan2(inversePosition.y, inversePosition.z) * Mathf.Rad2Deg;

                //Set the target angle
                Fields["targetAngle"].SetValue(targetRotationAngle, this);
            }
        }
        #endregion

        #region Helpers
        /// <summary>
        /// Forces the controller to pick a target to track. In order of preference:
        /// 1) A player-selected target.
        /// 2) A target vessel specified by the targetVessel variable.
        /// 3) A target celestial body specified by the targetBody variable.
        /// 4) A random target if trackRandomTargets is set to true.
        /// </summary>
        /// <returns></returns>
        public bool UpdateTarget()
        {
            ITargetable targetObject = this.part.vessel.targetObject;

            //chek for unset
            if (targetObject == null && targetObjectSet)
            {
                targetTransform = null;
                targetObjectSet = false;
            }

            //If we already have a target to track then we're good to go.
            if (targetTransform != null && targetObject == null)
                return true;

            //First check to see if the vessel has selected a target.
            if (targetObject != null && canTrackPlayerTargets)
            {
                targetTransform = targetObject.GetTransform();
                trackingStatus = targetObject.GetDisplayName().Replace("^N", "");
                targetName = trackingStatus;
                targetObjectSet = true;
                return true;
            }

            //Next check to see if we have a target vessel
            if (targetVessel != null)
            {
                targetTransform = targetVessel.vesselTransform;
                trackingStatus = targetVessel.vesselName;
                targetName = trackingStatus;
                return true;
            }

            //Now check target planet
            if (targetBody != null)
            {
                targetTransform = targetBody.scaledBody.transform;
                trackingStatus = targetBody.displayName.Replace("^N", "");
                targetName = trackingStatus;
                return true;
            }

            //Lastly, if random tracking is enabled and we don't have a target, then randomly select one from the unloaded vessels list.
            if (trackRandomObjects && targetTransform == null)
            {
                //get the tracking controllers on the vessel
                List<WBIBGTrackingServo> trackingControllers = this.part.vessel.FindPartModulesImplementing<WBIBGTrackingServo>();

                //If we aren't the first controller, then we're done.
                if (trackingControllers[0] != this)
                {
                    trackingStatus = trackingControllers[0].trackingStatus;
                    targetTransform = trackingControllers[0].targetTransform;
                    targetName = trackingStatus;
                    return false;
                }

                //Find a random vessel to track
                int vesselCount = FlightGlobals.VesselsUnloaded.Count;
                Vessel trackedVessel;
                for (int index = 0; index < vesselCount; index++)
                {
                    trackedVessel = FlightGlobals.VesselsUnloaded[UnityEngine.Random.Range(0, vesselCount - 1)];

                    //If we find a vessel we're interested in, tell all the other tracking controllers.
                    if (trackedVessel.vesselType != VesselType.Flag &&
                        trackedVessel.vesselType != VesselType.EVA &&
                        trackedVessel.vesselType != VesselType.Unknown)
                    {
                        int controllerCount = trackingControllers.Count;
                        for (int controllerIndex = 0; controllerIndex < controllerCount; controllerIndex++)
                        {
                            if (!trackingControllers[controllerIndex].trackRandomObjects)
                                continue;

                            trackingControllers[controllerIndex].targetTransform = trackedVessel.vesselTransform;
                            trackingControllers[controllerIndex].trackingStatus = trackedVessel.vesselName;
                            trackingControllers[controllerIndex].targetName = trackedVessel.vesselName;
                        }
                        return true;
                    }
                }
            }

            //Nothing to track
            trackingStatus = kTrackingNone;
            targetName = trackingStatus;
            return false;
        }
        #endregion
    }
}
