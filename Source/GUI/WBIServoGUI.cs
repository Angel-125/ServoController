/*
Source code copyrighgt 2019, by Michael Billard (Angel-125)
License: GNU General Public License Version 3
License URL: http://www.gnu.org/licenses/
If you want to use this code, give me a shout on the KSP forums! :)
Wild Blue Industries is trademarked by Michael Billard and may be used for non-commercial purposes. All other rights reserved.
Note that Wild Blue Industries is a ficticious entity 
created for entertainment purposes. It is in no way meant to represent a real entity.
Any similarity to a real entity is purely coincidental.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System.Collections.Generic;
using UnityEngine;
using Expansions.Serenity;

namespace ServoController
{
    internal enum EEditStates
    {
        None,
        MoveUp,
        MoveDown,
        Delete,
        EditMode
    }

    #region Delegates
    public delegate void PlaySnapshotDelegate(int index);
    public delegate int AddSnapshotDelegate();
    public delegate void DeleteSnapshotDelegate(int index);
    public delegate void UpdateSnapshotAtDelegate(int index, string snapshotName);
    public delegate void LockServosDelegate();
    public delegate void UnlockServosDelegate();
    public delegate void DrawServoControlsDelegate();
    #endregion

    public class WBIServoGUI : Window<WBIServoGUI>
    {
        #region Constants
        public static Texture recordIcon = null;
        public static Texture cameraIcon = null;
        public static Texture playIcon = null;
        public static Texture deleteIcon = null;
        public static Texture stopIcon = null;
        public static Texture minIcon = null;
        public static Texture maxIcon = null;
        public static Texture forwardIcon = null;
        public static Texture backIcon = null;
        public static Texture saveIcon = null;
        public static Texture lockIcon = null;
        public static Texture unlockIcon = null;
        public static GUILayoutOption[] buttonOptions = new GUILayoutOption[] { GUILayout.Width(32), GUILayout.Height(32) };
        #endregion

        #region Housekeeping
        public ModuleRoboticController roboticController;
        public int maxWindowHeight = 600;
        public WBIBGSnapshotManager servoManager;
        public List<ConfigNode> snapshots = new List<ConfigNode>();
        public List<string> snapshotNames = new List<string>();
        public int panelHeight;

        Vector2 scrollPos;
        Vector2 playbookScrollPos;
        GUILayoutOption[] sequencePanelOptions = new GUILayoutOption[] { GUILayout.Width(150) };
        GUILayoutOption[] servoPanelOptions = new GUILayoutOption[] { GUILayout.Width(275) };
        #endregion

        #region Delegate Methods
        public PlaySnapshotDelegate PlaySnapshot;
        public AddSnapshotDelegate AddSnapshot;
        public DeleteSnapshotDelegate DeleteSnapshot;
        public UpdateSnapshotAtDelegate UpdateSnapshotAt;
        public LockServosDelegate LockServos;
        public UnlockServosDelegate UnlockServos;
        public DrawServoControlsDelegate DrawServoControls;
        #endregion

        #region Constructors
        public WBIServoGUI(string title = "", int height = 400, int width = 500) :
            base(title, width, height)
        {
            scrollPos = new Vector2(0, 0);
            playbookScrollPos = new Vector2(0, 0);

        }
        #endregion

        public override void SetVisible(bool newValue)
        {
            base.SetVisible(newValue);

            //Grab the textures if needed.
            if (cameraIcon == null)
            {
                string baseIconURL = "WildBlueIndustries/ServoController/Icons/";
                ConfigNode[] settingsNodes = GameDatabase.Instance.GetConfigNodes("ServoController");
                if (settingsNodes != null && settingsNodes.Length > 0)
                    baseIconURL = settingsNodes[0].GetValue("iconsFolder");
                recordIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Record", false);
                cameraIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Camera", false);
                playIcon = GameDatabase.Instance.GetTexture(baseIconURL + "PlayIcon", false);
                deleteIcon = GameDatabase.Instance.GetTexture(baseIconURL + "TrashCan", false);
                saveIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Save", false);
                stopIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Stop", false);
                minIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Min", false);
                maxIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Max", false);
                forwardIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Forward", false);
                backIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Backward", false);
                lockIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Lock", false);
                unlockIcon = GameDatabase.Instance.GetTexture(baseIconURL + "Unlock", false);
            }

            if (newValue)
            {
                windowPos.height = panelHeight + 60;

                int count = snapshots.Count;
                for (int index = 0; index < count; index++)
                    snapshotNames.Add(snapshots[index].GetValue(WBIBGSnapshotManager.NAME_FIELD));
            }
        }
        protected override void DrawWindowContents(int windowId)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();

            WindowTitle = roboticController.displayName;

            //Draw snapshot playbook
            drawPlaybook();

            //Draw servo controllers
            drawServoControllers();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        protected void drawPlaybook()
        {
            int deleteIndex = -1;
            string snapshotName;
            int snapshotIndex;

            GUILayout.BeginVertical();

            GUILayout.Label("<b><color=white>Snapshots</color></b>");

            GUILayout.BeginHorizontal();

            //Add a new snapshot
            if (GUILayout.Button(cameraIcon, buttonOptions))
            {
                snapshotIndex = AddSnapshot();
                snapshotNames.Add(snapshots[snapshotIndex].GetValue(WBIBGSnapshotManager.NAME_FIELD));
            }

            //Lock servos
            if (GUILayout.Button(lockIcon, buttonOptions))
                LockServos();

            //Unlock servos
            if (GUILayout.Button(unlockIcon, buttonOptions))
                UnlockServos();

            GUILayout.EndHorizontal();

            //list of recorded snapshots
            playbookScrollPos = GUILayout.BeginScrollView(playbookScrollPos);

            int totalSnapshots = snapshots.Count;
            if (totalSnapshots >= 1)
            {
                for (int index = 0; index < totalSnapshots; index++)
                {
                    snapshotName = snapshotNames[index];
                    snapshotName = GUILayout.TextField(snapshotName);
                    snapshotNames[index] = snapshotName;

                    GUILayout.BeginHorizontal();

                    //Play snapshot
                    if (GUILayout.Button(playIcon, buttonOptions))
                        PlaySnapshot(index);

                    //Save snapshot
                    if (GUILayout.Button(cameraIcon, buttonOptions))
                        UpdateSnapshotAt(index, snapshotName);

                    //Delete snapshot
                    if (GUILayout.Button(deleteIcon, buttonOptions))
                        deleteIndex = index;

                    GUILayout.EndHorizontal();
                }

                //Do we have a sequence marked for death?
                if (deleteIndex != -1)
                {
                    snapshots.RemoveAt(deleteIndex);
                    snapshotNames.RemoveAt(deleteIndex);
                    deleteIndex = -1;
                }
            }

            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }

        protected void drawServoControllers()
        {
            scrollPos = GUILayout.BeginScrollView(scrollPos, servoPanelOptions);

            DrawServoControls();

            GUILayout.EndScrollView();
        }
    }
}
