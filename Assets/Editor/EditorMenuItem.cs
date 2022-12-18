using UnityEditor;
using UnityEngine;

namespace Pixelism {

    public static class EditorMenuItem {

        [MenuItem("Pixelism/Open PersistentDataPath")]
        private static void OpenPersistentDataPath() {
            System.Diagnostics.Process.Start(Application.persistentDataPath);
        }
    }
}
