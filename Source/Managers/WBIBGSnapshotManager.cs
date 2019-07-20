using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;
using System.Text.RegularExpressions;
using Expansions.Serenity;

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
    public class WBIBGSnapshotManager : PartModule
    {
        #region Constants
        const string NO_SERVOS_MESSAGE = "Cannot take a snapshot, add servos to the controller track first.";
        const string SERVOS_LOCKED_MESSAGE = "Servos locked.";
        const string SERVOS_UNLOCKED_MESSAGE = "Servos unlocked.";
        const string SNAPSHOT_TAKEN_MESSAGE = "Snapshot taken.";
        public const string SNAPSHOT_NAME_NODE = "SNAPSHOT_NAME";
        public const string NAME_FIELD = "name";
        public const string INDEX_FIELD = "index";
        #endregion

        #region Fields
        #endregion

        #region Housekeeping
        ModuleRoboticController roboticController;
        int currentSnapshotIndex;
        WBIServoGUI managerWindow;
        List<ConfigNode> snapshotNodes;
        WBIBGSnapshotController[] controllers;
        #endregion

        #region Overrides
        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            roboticController = this.part.FindModuleImplementing<ModuleRoboticController>();

            if (snapshotNodes == null)
                snapshotNodes = new List<ConfigNode>();

            managerWindow = new WBIServoGUI();
            managerWindow.roboticController = roboticController;
            managerWindow.PlaySnapshot = PlaySnapshot;
            managerWindow.AddSnapshot = AddSnapshot;
            managerWindow.DeleteSnapshot = DeleteSnapshot;
            managerWindow.UpdateSnapshotAt = UpdateSnapshotAt;
            managerWindow.LockServos = LockServos;
            managerWindow.UnlockServos = UnlockServos;
            managerWindow.DrawServoControls = DrawServoControls;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            if (snapshotNodes == null)
                snapshotNodes = new List<ConfigNode>();

            if (node.HasNode(SNAPSHOT_NAME_NODE))
            {
                ConfigNode[] nodes = node.GetNodes(SNAPSHOT_NAME_NODE);
                for (int index = 0; index < nodes.Length; index++)
                    snapshotNodes.Add(nodes[index]);
            }

            currentSnapshotIndex = snapshotNodes.Count - 1;
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            int count = snapshotNodes.Count;
            for (int index = 0; index < count; index++)
                node.AddNode(snapshotNodes[index]);
        }
        #endregion

        #region KSP PAW Events
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Open Snapshot Manager")]
        public void OpenSnapshotManager()
        {
            controllers = findControllers();
            if (controllers != null)
            {
                int panelHeight = 0;
                for (int index = 0; index < controllers.Length; index++)
                    panelHeight += controllers[index].GetPanelHeight();

                if (panelHeight > managerWindow.maxWindowHeight)
                    panelHeight = managerWindow.maxWindowHeight;

                managerWindow.panelHeight = panelHeight;
                managerWindow.WindowTitle = roboticController.displayName;
                managerWindow.snapshots = snapshotNodes;
                managerWindow.SetVisible(true);
            }
        }
        #endregion

        #region API
        protected int AddSnapshot()
        {
            if (snapshotNodes.Count == 0)
                currentSnapshotIndex = 0;
            else
                currentSnapshotIndex += 1;

            //Add index and snapshot name to the list
            int snapshotID = currentSnapshotIndex + 1;
            ConfigNode snapshotNode = new ConfigNode(SNAPSHOT_NAME_NODE);
            snapshotNode.AddValue(NAME_FIELD, "Snapshot" + snapshotID.ToString());
            snapshotNode.AddValue(INDEX_FIELD, currentSnapshotIndex);

            snapshotNodes.Add(snapshotNode);

            for (int index = 0; index < controllers.Length; index++)
                controllers[index].AddSnapshot();

            ScreenMessages.PostScreenMessage(SNAPSHOT_TAKEN_MESSAGE, 5.0f, ScreenMessageStyle.UPPER_CENTER);

            return currentSnapshotIndex;
        }

        protected void UpdateSnapshotAt(int snapshotIndex, string snapshotName)
        {
            if (snapshotIndex < 0 || snapshotIndex > snapshotNodes.Count)
                return;

            ConfigNode node = snapshotNodes[snapshotIndex];
            node.SetValue(NAME_FIELD, snapshotName);
            snapshotNodes[snapshotIndex] = node;

            for (int index = 0; index < controllers.Length; index++)
                controllers[index].UpdateSnapshot(snapshotIndex);

            ScreenMessages.PostScreenMessage(SNAPSHOT_TAKEN_MESSAGE, 5.0f, ScreenMessageStyle.UPPER_CENTER);
        }

        protected void DeleteSnapshot(int index)
        {
            if (index < 0 || index > snapshotNodes.Count)
                return;

            snapshotNodes.RemoveAt(index);
        }

        protected void PlaySnapshot(int snapshotIndex)
        {
            for (int index = 0; index < controllers.Length; index++)
                controllers[index].PlaySnapshot(snapshotIndex);
        }

        protected void LockServos()
        {
            for (int index = 0; index < controllers.Length; index++)
                controllers[index].SetServoLock(true);

            ScreenMessages.PostScreenMessage(SERVOS_LOCKED_MESSAGE, 5.0f, ScreenMessageStyle.UPPER_CENTER);
        }

        protected void UnlockServos()
        {
            for (int index = 0; index < controllers.Length; index++)
                controllers[index].SetServoLock(false);

            ScreenMessages.PostScreenMessage(SERVOS_UNLOCKED_MESSAGE, 5.0f, ScreenMessageStyle.UPPER_CENTER);
        }

        protected void DrawServoControls()
        {
            for (int index = 0; index < controllers.Length; index++)
                controllers[index].DrawControls();
        }
        #endregion

        #region Heleprs
        protected WBIBGSnapshotController[] findControllers()
        {
            List<WBIBGSnapshotController> assignedControllers = new List<WBIBGSnapshotController>();
            WBIBGSnapshotController snapshotController;
            ControlledAxis[] controlledAxes = roboticController.ControlledAxes.ToArray();

            for (int index = 0; index < controlledAxes.Length; index++)
            {
                snapshotController = controlledAxes[index].Part.FindModuleImplementing<WBIBGSnapshotController>();
                if (snapshotController != null)
                {
                    snapshotController.partNickName = controlledAxes[index].PartNickName;
                    assignedControllers.Add(snapshotController);
                }
            }
            if (assignedControllers.Count > 0)
            {
                return assignedControllers.ToArray();
            }
            else
            {
                ScreenMessages.PostScreenMessage(NO_SERVOS_MESSAGE, 5.0f, ScreenMessageStyle.UPPER_CENTER);
                return null;
            }
        }
        #endregion
    }
}
