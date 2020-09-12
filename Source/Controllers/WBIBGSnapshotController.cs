using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;
using System.Text.RegularExpressions;
using Expansions.Serenity;
using Highlighting;

/*
Source code copyright 2018, by Michael Billard (Angel-125)
License: GNU General Public License Version 3
License URL: http://www.gnu.org/licenses/
Wild Blue Industries is trademarked by Michael Billard and may be used for non-commercial purposes. All other rights reserved.
Note that Wild Blue Industries is a ficticious entity 
created for entertainment purposes. It is in no way meant to represent a real entity.
Any similarity to a real entity is purely coincidental.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
namespace ServoController
{
    #region Enums
    /// <summary>
    /// Types of servos that the controller supports
    /// </summary>
    public enum BGServoTypes
    {
        /// <summary>
        /// Supports ModuleRoboticServoHinge
        /// </summary>
        hinge,

        /// <summary>
        /// Supports ModuleRoboticServoPiston
        /// </summary>
        piston,

        /// <summary>
        /// Supports ModuleRoboticServoRotor
        /// </summary>
        rotor,

        /// <summary>
        /// Supports ModuleRoboticRotationServo
        /// </summary>
        servo
    }

    /// <summary>
    /// Types of snapshots to play/record
    /// </summary>
    public enum BGSnapshotTypes
    {
        /// <summary>
        /// The start animation snapshot
        /// </summary>
        snapshotStartAnimation = 0,

        /// <summary>
        /// The end animation snapshot
        /// </summary>
        snapshotEndAnimation = 1,

        /// <summary>
        /// The custom snapshot
        /// </summary>
        snapshotCustomAnimation = 2
    }
    #endregion

    /// <summary>
    /// This helper class can take position snapshots and play them back.
    /// </summary>
    public class WBIBGSnapshotController: PartModule
    {
        #region Constants
        public const string SNAPSHOT_NODE = "SNAPSHOT";
        const string TARGET_ANGLE = "targetAngle";
        const string HINGE_DAMPING = "hingeDamping";
        const string TRAVERSE_VELOCITY = "traverseVelocity";
        const string INVERT_DIRECTION = "inverted";
        const string SERVO_IS_LOCKED = "servoIsLocked";
        const string SOFT_MIN_ANGLE = "softMinAngle";
        const string SOFT_MAX_ANGLE = "softMaxAngle";
        const string ALLOW_FULL_ROTATION = "allowFullRotation";
        const string RPM_LIMIT = "rpmLimit";
        const string BRAKE_PERCENTAGE = "brakePercentage";
        const string RATCHETED = "ratcheted";
        const string ROTATE_COUNTERCLOCKWISE = "rotateCounterClockwise";
        const string PISTON_DAMPING = "pistonDamping";
        const string TARGET_EXTENSION = "targetExtension";
        const int kPanelHeight = 190;

        public const string START_ANIMATION = "Animation Start";
        public const string END_ANIMATION = "Animation End";
        public const string CUSTOM_SNAPSHOT = "Custom";
        #endregion

        #region Housekeeping
        #endregion

        #region Fields
        /// <summary>
        /// Type of servo to support.
        /// </summary>
        [KSPField]
        public string type;

        [KSPField(isPersistant = true)]
        public bool autoLock = true;

        [KSPField]
        public string servoEffectName = string.Empty;
        #endregion

        #region Housekeeping
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Servo ID")]
        public string partNickName = string.Empty;

        BGServoTypes servoType;
        BaseServo servo = null;
        ModuleRoboticServoHinge hingeServo;
        ModuleRoboticServoPiston pistonServo;
        ModuleRoboticServoRotor rotorServo;
        ModuleRoboticRotationServo rotationServo;

        List<ConfigNode> snapshots = new List<ConfigNode>();

        protected string targetValueText = "";

        Vector2 scrollVector = new Vector2();
        GUILayoutOption[] panelOptions = new GUILayoutOption[] { GUILayout.Height(kPanelHeight) };
        public static GUILayoutOption[] iconOptions = new GUILayoutOption[] { GUILayout.Width(24), GUILayout.Height(24) };
        string unitsText = "15";
        bool inverted;
        bool counterClockwiseDirection;
        bool isPlayingSnapshot = false;
        bool isHighlighted = false;
        #endregion

        #region Overrides
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            if (node.HasNode(SNAPSHOT_NODE))
            {
                ConfigNode[] nodes = node.GetNodes(SNAPSHOT_NODE);
                for (int index = 0; index < nodes.Length; index++)
                {
                    snapshots.Add(nodes[index]);
                }
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            int count = snapshots.Count;
            for (int index = 0; index < count; index++)
                node.AddNode(snapshots[index]);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            servoType = (BGServoTypes)Enum.Parse(typeof(BGServoTypes), type);
            switch (servoType)
            {
                case BGServoTypes.hinge:
                    hingeServo = this.part.FindModuleImplementing<ModuleRoboticServoHinge>();
                    if (hingeServo != null)
                        servo = hingeServo;
                    break;

                case BGServoTypes.piston:
                    pistonServo = this.part.FindModuleImplementing<ModuleRoboticServoPiston>();
                    if (pistonServo != null)
                        servo = pistonServo;
                    break;

                case BGServoTypes.rotor:
                    rotorServo = this.part.FindModuleImplementing<ModuleRoboticServoRotor>();
                    if (rotorServo != null)
                        servo = rotorServo;
                    break;

                case BGServoTypes.servo:
                    rotationServo = this.part.FindModuleImplementing<ModuleRoboticRotationServo>();
                    if (rotationServo != null)
                        servo = rotationServo;
                    break;
            }
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            checkAutolockAfterPlaying();
            resetLockedTargetAngle();
        }

        #endregion

        #region KSP Events and Actions
        #endregion

        #region API
        public void AddSnapshot()
        {
            snapshots.Add(takeSnapshot());
        }

        public void UpdateSnapshot(int snapshotIndex)
        {
            if (snapshotIndex < 0 || snapshotIndex > snapshots.Count - 1)
                return;

            snapshots[snapshotIndex] = takeSnapshot();
        }

        /// <summary>
        /// Plays the snapshot if it exists.
        /// </summary>
        /// <param name="snapshotIndex">The index of the snapshot to play.</param>
        public void PlaySnapshot(int snapshotIndex)
        {
            if (snapshotIndex >= 0 && snapshotIndex <= snapshots.Count - 1)
            {
                ConfigNode node = snapshots[snapshotIndex];
                if (node.values.Count == 0)
                    return;

                isPlayingSnapshot = true;
                SetServoLock(false);

                switch (servoType)
                {
                    case BGServoTypes.hinge:
                        playHingeSnapshot(node);
                        break;

                    case BGServoTypes.piston:
                        playPistonSnapshot(node);
                        break;

                    case BGServoTypes.rotor:
                        playRotorSnapshot(node);
                        break;

                    case BGServoTypes.servo:
                        playRotationSnapshot(node);
                        break;
                }
            }
        }
        public void SetAutolock(bool autoLockOn)
        {
            autoLock = autoLockOn;
        }

        public bool IsJointUnlocked()
        {
            if (servo == null)
                return false;

            return servo.IsJointUnlocked();
        }

        public void SetServoLock(bool isLocked)
        {
            if (servo == null)
                return;

            if (isLocked && !servo.servoIsLocked)
                servo.EngageServoLock();
            else if (!isLocked && servo.servoIsLocked)
                servo.DisengageServoLock();
        }

        public void ReturnHome()
        {
            if (servo == null)
                return;
        }

        public int GetPanelHeight()
        {
            if (rotorServo != null)
            {
                int panelHeight = 240;

                return panelHeight;
            }
            else
            {
                panelOptions = new GUILayoutOption[] { GUILayout.Height(kPanelHeight) };
                return kPanelHeight;
            }
        }

        public void DrawControls()
        {
            GUILayout.BeginVertical();

            GUILayout.BeginScrollView(scrollVector, panelOptions);

            GUILayout.BeginHorizontal();
            //Name
            GUILayout.Label("<b><color=white>" + partNickName + "</color></b>");

            // Status icons
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (IsJointUnlocked())
                    GUILayout.Label(WBIServoGUI.unlockIcon, iconOptions);
                else
                    GUILayout.Label(WBIServoGUI.lockIcon, iconOptions);
            }
            //Highlight
            if (isHighlighted && GUILayout.Button(WBIServoGUI.lightOnIcon, iconOptions))
            {
                isHighlighted = false;
                this.part.Highlight(false);
            }
            else if (!isHighlighted && GUILayout.Button(WBIServoGUI.lightOffIcon, iconOptions))
            {
                isHighlighted = true;
                this.part.Highlight(Highlighter.colorPartEditorActionHighlight);
            }

            GUILayout.EndHorizontal();

            switch (servoType)
            {
                case BGServoTypes.hinge:
                    drawServoHingeControls();
                    break;

                case BGServoTypes.piston:
                    drawPistonControls();
                    break;

                case BGServoTypes.rotor:
                    drawRotorControls();
                    break;

                case BGServoTypes.servo:
                    drawServoHingeControls();
                    break;
            }

            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }
        #endregion

        #region Helpers
        public ConfigNode takeSnapshot()
        {
            //Create the snapshot
            ConfigNode node = null;
            switch (servoType)
            {
                case BGServoTypes.hinge:
                    node = takeHingeSnapshot();
                    break;

                case BGServoTypes.piston:
                    node = takePistonSnapshot();
                    break;

                case BGServoTypes.rotor:
                    node = takeRotorSnapshot();
                    break;

                case BGServoTypes.servo:
                    node = takeRotationSnapshot();
                    break;
            }
            return node;
        }

        protected void resetLockedTargetAngle()
        {
            if (IsJointUnlocked())
                return;

            switch (servoType)
            {
                case BGServoTypes.hinge:
                    if (Mathf.Abs(hingeServo.targetAngle) != 0)
                    {
                        float targetRatio = Mathf.Abs(hingeServo.currentAngle) / Mathf.Abs(hingeServo.targetAngle);
                        if (targetRatio < 0.9999f || targetRatio > 1.0001)
                            hingeServo.targetAngle = hingeServo.currentAngle;
                    }
                    break;

                case BGServoTypes.piston:
                    if (Mathf.Abs(pistonServo.targetExtension) != 0)
                    {
                        float targetRatio = Mathf.Abs(pistonServo.currentExtension) / Mathf.Abs(pistonServo.targetExtension);
                        if (targetRatio < 0.9999f || targetRatio > 1.0001)
                            pistonServo.targetExtension = pistonServo.currentExtension;
                    }
                    break;

                case BGServoTypes.rotor:
                    servo = rotorServo;
                    break;

                case BGServoTypes.servo:
                    if (Mathf.Abs(rotationServo.targetAngle) != 0)
                    {
                        float targetRatio = Mathf.Abs(rotationServo.currentAngle) / Mathf.Abs(rotationServo.targetAngle);
                        if (targetRatio < 0.9999f || targetRatio > 1.0001)
                            rotationServo.targetAngle = rotationServo.currentAngle;
                    }
                    break;
            }
        }

        protected void checkAutolockAfterPlaying()
        {
            if (!isPlayingSnapshot || !autoLock)
                return;

            switch (servoType)
            {
                case BGServoTypes.hinge:
                    if (Mathf.Abs(hingeServo.currentAngle) / Mathf.Abs(hingeServo.targetAngle) <= 0.1f)
                        SetServoLock(true);
                    isPlayingSnapshot = hingeServo.IsJointUnlocked();
                    break;

                case BGServoTypes.piston:
                    if (Mathf.Abs(pistonServo.currentExtension) / Mathf.Abs(pistonServo.targetExtension) <= 0.1f)
                        SetServoLock(true);
                    isPlayingSnapshot = pistonServo.IsJointUnlocked();
                    break;

                case BGServoTypes.rotor:
                    servo = rotorServo;
                    break;

                case BGServoTypes.servo:
                    if (Mathf.Abs(rotationServo.currentAngle) / Mathf.Abs(rotationServo.targetAngle) <= 0.1f)
                        SetServoLock(true);
                    isPlayingSnapshot = rotationServo.IsJointUnlocked();
                    break;
            }

            if (isPlayingSnapshot)
                this.part.Effect(servoEffectName, 1.0f);
            else
                this.part.Effect(servoEffectName, -1.0f);
        }

        protected void drawPistonControls()
        {
            if (pistonServo == null)
                return;

            drawResourceConsumption(pistonServo);

            float value;
            float currentExtension = pistonServo.currentExtension;
            bool lockServo = true;
            GUILayout.Label(string.Format("<color=white><b>Current Extension: </b>{0:f2}</color>", currentExtension));

            //Rotation speed
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=white>Velocity:</color>");

            float velocity = pistonServo.traverseVelocity;
            unitsText = velocity.ToString();
            unitsText = GUILayout.TextField(unitsText);
            Vector2 softMinMaxExtension = Vector2.zero;
            if (float.TryParse(unitsText, out velocity))
            {
                softMinMaxExtension = pistonServo.softMinMaxExtension;

                if (velocity >= 0.05 && velocity <= 5)
                    pistonServo.traverseVelocity = velocity;
            }

            GUILayout.Label("<color=white>m/s</color>");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            //Minimum rotation
            if (GUILayout.Button(WBIServoGUI.minIcon, WBIServoGUI.buttonOptions))
            {
                lockServo = false;
                pistonServo.Fields["targetExtension"].SetValue(softMinMaxExtension.x, pistonServo);
                pistonServo.currentExtension = softMinMaxExtension.x;
            }

            ///Retract
            if (GUILayout.RepeatButton(WBIServoGUI.backIcon, WBIServoGUI.buttonOptions))
            {
                lockServo = false;
                SetServoLock(false);
                currentExtension -= velocity * TimeWarp.fixedDeltaTime;
                if (currentExtension < softMinMaxExtension.x)
                    currentExtension = softMinMaxExtension.x;

                pistonServo.Fields["targetExtension"].SetValue(currentExtension, pistonServo);
                pistonServo.currentExtension = currentExtension;
            }

            //Extend
            if (GUILayout.RepeatButton(WBIServoGUI.forwardIcon, WBIServoGUI.buttonOptions))
            {
                lockServo = false;
                SetServoLock(false);
                currentExtension += velocity * TimeWarp.fixedDeltaTime;
                if (currentExtension > softMinMaxExtension.y)
                    currentExtension = softMinMaxExtension.y;

                pistonServo.Fields["targetExtension"].SetValue(currentExtension, pistonServo);
                pistonServo.currentExtension = currentExtension;
            }

            //Max rotation
            if (GUILayout.Button(WBIServoGUI.maxIcon, WBIServoGUI.buttonOptions))
            {
                lockServo = false;
                SetServoLock(false);
                pistonServo.Fields["targetExtension"].SetValue(softMinMaxExtension.y, pistonServo);
                pistonServo.currentExtension = softMinMaxExtension.y;
            }

            //Manual lock buttons
            if (!autoLock)
            {
                if (GUILayout.Button(WBIServoGUI.lockIcon, WBIServoGUI.buttonOptions))
                {
                    lockServo = false;
                    SetServoLock(true);
                }
                if (GUILayout.Button(WBIServoGUI.unlockIcon, WBIServoGUI.buttonOptions))
                {
                    lockServo = false;
                    SetServoLock(false);
                }
            }

            GUILayout.EndHorizontal();

            //Specific target position
            GUILayout.BeginHorizontal();

            GUILayout.Label("<color=white>Target position:</color>");
            targetValueText = GUILayout.TextField(targetValueText);

            //Make sure we're in bounds
            if (float.TryParse(targetValueText, out value))
            {
                if (value < softMinMaxExtension.x)
                    value = softMinMaxExtension.x;
                else if (value > softMinMaxExtension.y)
                    value = softMinMaxExtension.y;
            }
            else
            {
                value = currentExtension;
            }

            if (GUILayout.Button("Set"))
            {
                lockServo = false;
                SetServoLock(false);
                pistonServo.Fields["targetExtension"].SetValue(softMinMaxExtension.y, pistonServo);
                pistonServo.currentExtension = softMinMaxExtension.y;
            }

            if (lockServo && autoLock && !isPlayingSnapshot && HighLogic.LoadedSceneIsFlight)
                SetServoLock(true);

            GUILayout.EndHorizontal();
        }

        protected void drawRotorControls()
        {
            if (rotorServo == null)
                return;

            drawResourceConsumption(rotorServo);

            //RPM
//            GUILayout.Label(string.Format("<color=white><b>Current RPM: </b>{0:f2}</color>", rotorServo.currentRPM)); ;
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("<color=white><b>RPM Limit: </b>{0:f2}</color>", rotorServo.rpmLimit)); ;

            float rpmLimit = rotorServo.rpmLimit;
            unitsText = rpmLimit.ToString();
            unitsText = GUILayout.TextField(unitsText);
            Vector2 softMinMaxAngles = Vector2.zero;
            if (float.TryParse(unitsText, out rpmLimit))
            {
                if (rpmLimit >= 0 && rpmLimit <= 460)
                    rotorServo.rpmLimit = rpmLimit;
            }
            GUILayout.EndHorizontal();

            //RPM slider
            rpmLimit = GUILayout.HorizontalSlider(rpmLimit, 0, 460);
            if (!rpmLimit.Equals(rotorServo.rpmLimit))
            {
                rotorServo.Fields["rpmLimit"].SetValue(rpmLimit, rotorServo);
                rotorServo.rpmLimit = rpmLimit;
            }

            //Torque
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("<color=white><b>Torque: </b>{0:f2}</color>", rotorServo.servoMotorLimit)); ;

            float servoMotorLimit = rotorServo.servoMotorLimit;
            unitsText = servoMotorLimit.ToString();
            unitsText = GUILayout.TextField(unitsText);
            if (float.TryParse(unitsText, out rpmLimit))
            {
                if (servoMotorLimit >= 0 && servoMotorLimit <= 100)
                    rotorServo.servoMotorLimit = servoMotorLimit;
            }
            GUILayout.EndHorizontal();

            //Torque slider
            servoMotorLimit = GUILayout.HorizontalSlider(servoMotorLimit, 0, 100);
            if (!servoMotorLimit.Equals(rotorServo.servoMotorLimit))
            {
                rotorServo.Fields["servoMotorLimit"].SetValue(servoMotorLimit, rotorServo);
                rotorServo.servoMotorLimit = servoMotorLimit;
            }

            //Rotate clockwise
            counterClockwiseDirection = GUILayout.Toggle(counterClockwiseDirection, "Rotate clockwise");
            if (counterClockwiseDirection != rotorServo.rotateCounterClockwise)
            {
                rotorServo.Fields["rotateCounterClockwise"].SetValue(counterClockwiseDirection, rotorServo);
                rotorServo.rotateCounterClockwise = counterClockwiseDirection;
            }

            //Invert Direction
            inverted = GUILayout.Toggle(inverted, "Invert Direction");
            if (inverted != rotorServo.inverted)
            {
                rotorServo.Fields["inverted"].SetValue(inverted, rotorServo);
                rotorServo.inverted = inverted;
            }
        }

        protected void drawServoHingeControls()
        {
            float setAngle;
            float currentAngle = 0;
            float targetAngle = 0;
            bool lockServo = true;
            bool soundPlaying = false;

            if (hingeServo != null)
            {
                drawResourceConsumption(hingeServo);
                currentAngle = hingeServo.currentAngle;
                targetAngle = hingeServo.targetAngle;
            }
            else if (rotationServo != null)
            {
                drawResourceConsumption(rotationServo);
                currentAngle = rotationServo.currentAngle;
                targetAngle = rotationServo.targetAngle;
            }
            GUILayout.Label(string.Format("<color=white><b>Angle (cur/tgt): </b>{0:f2}/{1:f2}</color>", currentAngle, targetAngle));

            //Rotation speed
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=white>Rotation Rate:</color>");

            float rotationDegPerSec = 0;
            if (hingeServo != null)
                rotationDegPerSec = hingeServo.traverseVelocity;
            else if (rotationServo != null)
                rotationDegPerSec = rotationServo.traverseVelocity;
            unitsText = rotationDegPerSec.ToString();
            unitsText = GUILayout.TextField(unitsText);
            Vector2 softMinMaxAngles = Vector2.zero;
            if (float.TryParse(unitsText, out rotationDegPerSec))
            {
                if (hingeServo != null)
                {
                    softMinMaxAngles = hingeServo.softMinMaxAngles;

                    if (rotationDegPerSec >= 1f && rotationDegPerSec <= 180f)
                        hingeServo.traverseVelocity = rotationDegPerSec;
                }
                else if (rotationServo != null)
                {
                    softMinMaxAngles = rotationServo.softMinMaxAngles;

                    if (rotationDegPerSec >= 1f && rotationDegPerSec <= 180f)
                        rotationServo.traverseVelocity = rotationDegPerSec;
                }
            }

            GUILayout.Label("<color=white>deg/s</color>");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            //Minimum rotation
            if (GUILayout.Button(WBIServoGUI.minIcon, WBIServoGUI.buttonOptions))
            {
                lockServo = false;
                if (hingeServo != null)
                    hingeServo.SetMinimumAngle();
                else if (rotationServo != null)
                    rotationServo.SetMinimumAngle();
            }

            ///Rotate down
            if (GUILayout.RepeatButton(WBIServoGUI.counterClockwiseIcon, WBIServoGUI.buttonOptions))
            {
                currentAngle -= rotationDegPerSec * TimeWarp.fixedDeltaTime;
                if (currentAngle < softMinMaxAngles.x)
                    currentAngle = softMinMaxAngles.x;

                if (hingeServo != null)
                {
                    SetServoLock(false);
                    lockServo = false;
                    hingeServo.Fields["targetAngle"].SetValue(currentAngle, hingeServo);
                    hingeServo.currentAngle = currentAngle;
                }
                else if (rotationServo != null)
                {
                    SetServoLock(false);
                    lockServo = false;
                    rotationServo.Fields["targetAngle"].SetValue(currentAngle, rotationServo);
                    rotationServo.currentAngle = currentAngle;
                }
                this.part.Effect(servoEffectName, 1.0f);
                soundPlaying = true;
            }

            //Rotate up
            if (GUILayout.RepeatButton(WBIServoGUI.clockwiseIcon, WBIServoGUI.buttonOptions))
            {
                currentAngle += rotationDegPerSec * TimeWarp.fixedDeltaTime;
                if (currentAngle > softMinMaxAngles.y)
                    currentAngle = softMinMaxAngles.y;

                if (hingeServo != null)
                {
                    SetServoLock(false);
                    lockServo = false;
                    hingeServo.Fields["targetAngle"].SetValue(currentAngle, hingeServo);
                    hingeServo.currentAngle = currentAngle;
                }
                else if (rotationServo != null)
                {
                    SetServoLock(false);
                    lockServo = false;
                    rotationServo.Fields["targetAngle"].SetValue(currentAngle, rotationServo);
                    rotationServo.currentAngle = currentAngle;
                }
                this.part.Effect(servoEffectName, 1.0f);
                soundPlaying = true;
            }

            //Max rotation
            if (GUILayout.Button(WBIServoGUI.maxIcon, WBIServoGUI.buttonOptions))
            {
                lockServo = false;
                SetServoLock(false);
                if (hingeServo != null)
                    hingeServo.SetMaximumAngle();
                else if (rotationServo != null)
                    rotationServo.SetMaximumAngle();
            }

            //Manual lock buttons
            if (!autoLock)
            {
                if (GUILayout.Button(WBIServoGUI.lockIcon, WBIServoGUI.buttonOptions))
                {
                    lockServo = false;
                    SetServoLock(true);
                }
                if (GUILayout.Button(WBIServoGUI.unlockIcon, WBIServoGUI.buttonOptions))
                {
                    lockServo = false;
                    SetServoLock(false);
                }
            }

            GUILayout.EndHorizontal();

            //Specific target angle
            GUILayout.BeginHorizontal();

            GUILayout.Label("<color=white>Target Angle:</color>");
            targetValueText = GUILayout.TextField(targetValueText);

            //Make sure we're in bounds
            if (float.TryParse(targetValueText, out setAngle))
            {
                if (setAngle < softMinMaxAngles.x)
                    setAngle = softMinMaxAngles.x;
                else if (setAngle > softMinMaxAngles.y)
                    setAngle = softMinMaxAngles.y;
            }
            else
            {
                setAngle = currentAngle;
            }

            if (GUILayout.Button("Set"))
            {
                if (hingeServo != null)
                {
                    lockServo = false;
                    SetServoLock(false);
                    hingeServo.Fields["targetAngle"].SetValue(setAngle, hingeServo);
                    hingeServo.currentAngle = setAngle;
                }
                else if (rotationServo != null)
                {
                    lockServo = false;
                    SetServoLock(false);
                    rotationServo.Fields["targetAngle"].SetValue(setAngle, rotationServo);
                    rotationServo.currentAngle = setAngle;
                }
            }

            // Lock servo
            if (lockServo && autoLock && !isPlayingSnapshot && HighLogic.LoadedSceneIsFlight)
            {
                SetServoLock(true);
            }

            // Mute sound
            if (!soundPlaying && !isPlayingSnapshot && HighLogic.LoadedSceneIsFlight)
                this.part.Effect(servoEffectName, -1.0f);

            GUILayout.EndHorizontal();
        }

        protected void drawResourceConsumption(PartModule partModule)
        {
            ModuleResource[] inputResources = partModule.resHandler.inputResources.ToArray();
            StringBuilder builder = new StringBuilder();

            for (int index = 0; index < inputResources.Length; index++)
            {
                builder.Append(inputResources[index].resourceDef.displayName + ": ");
                builder.Append(string.Format("{0:f2}", inputResources[index].currentRequest / TimeWarp.fixedDeltaTime));
                builder.Append("u/sec,");
            }

            string consumedResources = builder.ToString();
            consumedResources = consumedResources.Substring(0, consumedResources.Length - 1);
            GUILayout.Label(string.Format("<color=white>" + consumedResources + "</color>"));
        }

        protected ConfigNode takeRotationSnapshot()
        {
            ConfigNode node = new ConfigNode(SNAPSHOT_NODE);

            node.AddValue(ALLOW_FULL_ROTATION, rotationServo.allowFullRotation);
            node.AddValue(TARGET_ANGLE, rotationServo.targetAngle);
            node.AddValue(INVERT_DIRECTION, rotationServo.inverted);
            node.AddValue(TRAVERSE_VELOCITY, rotationServo.traverseVelocity);
            node.AddValue(HINGE_DAMPING, rotationServo.hingeDamping);

            return node;
        }

        protected void playRotationSnapshot(ConfigNode node)
        {
            bool boolValue;
            bool.TryParse(node.GetValue(ALLOW_FULL_ROTATION), out boolValue);
            rotationServo.Fields["allowFullRotation"].SetValue(boolValue, rotationServo);

            float value;
            float.TryParse(node.GetValue(TARGET_ANGLE), out value);
            rotationServo.Fields["targetAngle"].SetValue(value, rotationServo);
            rotationServo.currentAngle = value;

            bool.TryParse(node.GetValue(INVERT_DIRECTION), out boolValue);
            rotationServo.Fields["inverted"].SetValue(boolValue, rotationServo);

            float.TryParse(node.GetValue(TRAVERSE_VELOCITY), out value);
            rotationServo.Fields["traverseVelocity"].SetValue(value, rotationServo);

            float.TryParse(node.GetValue(HINGE_DAMPING), out value);
            rotationServo.Fields["hingeDamping"].SetValue(value, rotationServo);
        }

        protected ConfigNode takeRotorSnapshot()
        {
            ConfigNode node = new ConfigNode(SNAPSHOT_NODE);

            node.AddValue(RPM_LIMIT, rotorServo.rpmLimit);
            node.AddValue(BRAKE_PERCENTAGE, rotorServo.brakePercentage);
            node.AddValue(RATCHETED, rotorServo.ratcheted);
            node.AddValue(INVERT_DIRECTION, rotorServo.inverted);
            node.AddValue(ROTATE_COUNTERCLOCKWISE, rotorServo.rotateCounterClockwise);

            return node;
        }

        protected void playRotorSnapshot(ConfigNode node)
        {
            bool boolValue;
            float value;

            float.TryParse(RPM_LIMIT, out value);
            rotorServo.Fields["rpmLimit"].SetValue(value, rotorServo);

            float.TryParse(BRAKE_PERCENTAGE, out value);
            rotorServo.Fields["brakePercentage"].SetValue(value, rotorServo);

            bool.TryParse(RATCHETED, out boolValue);
            rotorServo.Fields["ratcheted"].SetValue(boolValue, rotorServo);

            bool.TryParse(INVERT_DIRECTION, out boolValue);
            rotorServo.Fields["inverted"].SetValue(boolValue, rotorServo);

            bool.TryParse(ROTATE_COUNTERCLOCKWISE, out boolValue);
            rotorServo.Fields["rotateCounterClockwise"].SetValue(boolValue, rotorServo);
        }

        protected ConfigNode takePistonSnapshot()
        {
            ConfigNode node = new ConfigNode(SNAPSHOT_NODE);

            node.AddValue(SOFT_MIN_ANGLE, pistonServo.softMinMaxExtension.x);
            node.AddValue(SOFT_MAX_ANGLE, pistonServo.softMinMaxExtension.y);
            node.AddValue(PISTON_DAMPING, pistonServo.pistonDamping);
            node.AddValue(TRAVERSE_VELOCITY, pistonServo.traverseVelocity);
            node.AddValue(TARGET_EXTENSION, pistonServo.targetExtension);

            return node;
        }

        protected void playPistonSnapshot(ConfigNode node)
        {
            Vector2 angles = new Vector2();
            float value;

            float.TryParse(node.GetValue(SOFT_MIN_ANGLE), out value);
            angles.x = value;
            float.TryParse(node.GetValue(SOFT_MAX_ANGLE), out value);
            angles.y = value;
            pistonServo.Fields["softMinMaxExtension"].SetValue(angles, pistonServo);

            float.TryParse(node.GetValue(PISTON_DAMPING), out value);
            pistonServo.Fields["pistonDamping"].SetValue(value, pistonServo);

            float.TryParse(node.GetValue(TRAVERSE_VELOCITY), out value);
            pistonServo.Fields["traverseVelocity"].SetValue(value, pistonServo);

            float.TryParse(node.GetValue(TARGET_EXTENSION), out value);
            pistonServo.Fields["targetExtension"].SetValue(value, pistonServo);
            pistonServo.currentExtension = value;
        }

        protected ConfigNode takeHingeSnapshot()
        {
            ConfigNode node = new ConfigNode(SNAPSHOT_NODE);

            node.AddValue(SOFT_MIN_ANGLE, hingeServo.softMinMaxAngles.x);
            node.AddValue(SOFT_MAX_ANGLE, hingeServo.softMinMaxAngles.y);
            node.AddValue(TARGET_ANGLE, hingeServo.targetAngle);
            node.AddValue(TRAVERSE_VELOCITY, hingeServo.traverseVelocity);
            node.AddValue(HINGE_DAMPING, hingeServo.hingeDamping);

            return node;
        }

        protected void playHingeSnapshot(ConfigNode node)
        {
            Vector2 angles = new Vector2();
            float value;
            float.TryParse(node.GetValue(SOFT_MIN_ANGLE), out value);
            angles.x = value;
            float.TryParse(node.GetValue(SOFT_MAX_ANGLE), out value);
            angles.y = value;
            hingeServo.Fields["softMinMaxAngles"].SetValue(angles, hingeServo);

            float.TryParse(node.GetValue(TARGET_ANGLE), out value);
            hingeServo.Fields["targetAngle"].SetValue(value, hingeServo);
            hingeServo.currentAngle = value;

            float.TryParse(node.GetValue(TRAVERSE_VELOCITY), out value);
            hingeServo.Fields["traverseVelocity"].SetValue(value, hingeServo);

            float.TryParse(node.GetValue(HINGE_DAMPING), out value);
            hingeServo.Fields["hingeDamping"].SetValue(value, hingeServo);
        }
        #endregion
    }
}
