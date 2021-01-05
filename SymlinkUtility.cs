using UnityEditor;
using UnityEngine;
using System.Collections;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace Parabox
{
    /**
	 *	An editor utility for easily creating symlinks in your project.
	 *
	 *	Adds a Menu item under `Assets/Create/Folder (Symlink)`, and 
	 *	draws a small indicator in the Project view for folders that are
	 *	symlinks.
	 */
    [InitializeOnLoad]
    public static class SymlinkUtility
    {
        // FileAttributes that match a junction folder.
        const FileAttributes FOLDER_SYMLINK_ATTRIBS = FileAttributes.Directory | FileAttributes.ReparsePoint;

        // Style used to draw the symlink indicator in the project view.
        private static GUIStyle _symlinkMarkerStyle = null;
        private static GUIStyle symlinkMarkerStyle
        {
            get
            {
                if (_symlinkMarkerStyle == null)
                {
                    _symlinkMarkerStyle = new GUIStyle(EditorStyles.label);
                    _symlinkMarkerStyle.normal.textColor = new Color(.2f, .8f, .2f, .8f);
                    _symlinkMarkerStyle.alignment = TextAnchor.MiddleRight;
                }
                return _symlinkMarkerStyle;
            }
        }

        /**
		 *	Static constructor subscribes to projectWindowItemOnGUI delegate.
		 */
        static SymlinkUtility()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
        }

        /**
		 *	Draw a little indicator if folder is a symlink
		 */
        private static void OnProjectWindowItemGUI(string guid, Rect r)
        {
            try
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!string.IsNullOrEmpty(path))
                {
                    FileAttributes attribs = File.GetAttributes(path);

                    if ((attribs & FOLDER_SYMLINK_ATTRIBS) == FOLDER_SYMLINK_ATTRIBS)
                        GUI.Label(r, "<=>", symlinkMarkerStyle);
                }
            }
            catch { }
        }

        /**
		 *	Add a menu item in the Assets/Create category to add symlinks to directories.
		 */
        // Create an absolute symbolic link
        [MenuItem("Assets/Create/Folder (Absolute Symlink)", false, 20)]
        static void SymlinkAbsolute()
        {
            Symlink(true);
        }

        // Create a relative symbolic link
        [MenuItem("Assets/Create/Folder (Relative Symlink)", false, 21)]
        static void SymlinkRelative()
        {
            Symlink(false);
        }

        static void Symlink(bool absolute)
        {
            string sourceFolderPath = EditorUtility.OpenFolderPanel("Select Folder Source", "", "");

            // Cancelled dialog
            if (string.IsNullOrEmpty(sourceFolderPath))
                return;

            if (sourceFolderPath.Contains(Application.dataPath))
            {
                UnityEngine.Debug.LogWarning("Cannot create a symlink to folder in your project!");
                return;
            }

            string sourceFolderName = sourceFolderPath.Split(new char[] { '/', '\\' }).LastOrDefault();

            if (string.IsNullOrEmpty(sourceFolderName))
            {
                UnityEngine.Debug.LogWarning("Couldn't deduce the folder name?");
                return;
            }

            Object uobject = Selection.activeObject;

            string targetPath = uobject != null ? AssetDatabase.GetAssetPath(uobject) : null;

            if (string.IsNullOrEmpty(targetPath))
                targetPath = "Assets";

            FileAttributes attribs = File.GetAttributes(targetPath);

            if ((attribs & FileAttributes.Directory) != FileAttributes.Directory)
                targetPath = Path.GetDirectoryName(targetPath);

            // Get path to project.
            string pathToProject = Application.dataPath.Split(new string[1] { "/Assets" }, System.StringSplitOptions.None)[0];

            targetPath = string.Format("{0}/{1}/{2}", pathToProject, targetPath, sourceFolderName);

            if (Directory.Exists(targetPath))
            {
                UnityEngine.Debug.LogWarning(string.Format("A folder already exists at this location, aborting link.\n{0} -> {1}", sourceFolderPath, targetPath));
                return;
            }

            // Use absolute path or relative path?
            string sourcePath = absolute ? sourceFolderPath : GetRelativePath(sourceFolderPath, targetPath);
#if UNITY_EDITOR_WIN
			using (Process cmd = Process.Start("CMD.exe", string.Format("/C mklink /J \"{0}\" \"{1}\"", targetPath, sourcePath)))
            {
                cmd.WaitForExit();
            }
#elif UNITY_EDITOR_OSX
            // For some reason, OSX doesn't want to create a symlink with quotes around the paths, so escape the spaces instead.
            sourcePath = sourcePath.Replace(" ", "\\ ");
            targetPath = targetPath.Replace(" ", "\\ ");
            string command = string.Format("ln -s {0} {1}", sourcePath, targetPath);
            ExecuteBashCommand(command);
#elif UNITY_EDITOR_LINUX
            // Is Linux the same as OSX?
#endif

            //UnityEngine.Debug.Log(string.Format("Created symlink: {0} <=> {1}", targetPath, sourceFolderPath));

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        static string GetRelativePath(string sourcePath, string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                return sourcePath;
            }
            if (sourcePath == null)
            {
                sourcePath = string.Empty;
            }

            var splitOutput = outputPath.Split(new char[1] { Path.PathSeparator }, System.StringSplitOptions.RemoveEmptyEntries);
            var splitSource = sourcePath.Split(new char[1] { Path.PathSeparator }, System.StringSplitOptions.RemoveEmptyEntries);

            int max = Mathf.Min(splitOutput.Length, splitSource.Length);
            int i = 0;
            while (i < max)
            {
                if (splitOutput[i] != splitSource[i])
                {
                    break;
                }
                ++i;
            }
            int hopUpCount = splitOutput.Length - i - 1;
            int newSplitCount = hopUpCount + splitSource.Length - i;
            string[] newSplitTarget = new string[newSplitCount];
            int j = 0;
            for (; j < hopUpCount; ++j)
            {
                newSplitTarget[j] = "..";
            }
            for (max = newSplitTarget.Length; j < max; ++j, ++i)
            {
                newSplitTarget[j] = splitSource[i];
            }
            return string.Join(Path.PathSeparator.ToString(), newSplitTarget);
        }

        static void ExecuteBashCommand(string command)
        {
            command = command.Replace("\"", "\"\"");

            var proc = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"" + command + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            using (proc)
            {
                proc.Start();
                proc.WaitForExit();

                if (!proc.StandardError.EndOfStream)
                {
                    UnityEngine.Debug.LogError(proc.StandardError.ReadToEnd());
                }
            }
        }
    }
}
