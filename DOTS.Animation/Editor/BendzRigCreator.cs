#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using AnimationSystem.Editor;

namespace AnimationSystem.Editor
{
    class BendzRigCreator : UnityEditor.ProjectWindowCallback.EndNameEditAction
    {
        public GameObject SelectedGameObject;
        
        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            File.WriteAllText(pathName, ""); // Create an empty bendzrig file
            AssetDatabase.ImportAsset(pathName);

            var newAsset = AssetDatabase.LoadAssetAtPath<Object>(pathName);

            var importer = AssetImporter.GetAtPath(pathName) as BendzRigImporter;
            if (importer != null)
            {
                importer.SkeletalMesh = SelectedGameObject;
                importer.CollectRenderers();
                importer.CollectBones();
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }

            ProjectWindowUtil.ShowCreatedAsset(newAsset);
        }

        [MenuItem("Assets/Create/Bendz Rig", priority = 151)]
        static void CreateBendzRig()
        {
            var creator = ScriptableObject.CreateInstance<BendzRigCreator>();
            creator.SelectedGameObject = Selection.activeGameObject;

            string defaultName;
            if (Selection.activeGameObject == null)
            {
                defaultName = "New Bendz Rig.bendzrig";
            }
            else
            {
                defaultName = $"{Selection.activeGameObject.name}.bendzrig";
            }

            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, creator, defaultName, null, null);
        }
    }
}
#endif
