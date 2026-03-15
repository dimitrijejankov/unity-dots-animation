#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using AnimationSystem;

namespace AnimationSystem.Editor
{
    [CustomEditor(typeof(BendzRigImporter))]
    public class BendzRigImporterEditor : ScriptedImporterEditor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            // Add standard importer UI
            var refreshButton = new Button(() =>
            {
                var importer = target as AssetImporter;
                importer.SaveAndReimport();
            })
            {
                text = "Refresh",
                style =
                {
                    marginBottom = 5,
                    marginTop = 5
                }
            };
            root.Add(refreshButton);

            // Add the Settings field at the top
            var settingsField = new PropertyField(serializedObject.FindProperty("Settings"), "Animation Settings")
            {
                style =
                {
                    marginBottom = 10,
                    marginTop = 5
                }
            };
            root.Add(settingsField);

            // Standard importer fields
            root.Add(new PropertyField(serializedObject.FindProperty("SkeletalMesh")));
            root.Add(new PropertyField(serializedObject.FindProperty("Bones")));
            root.Add(new PropertyField(serializedObject.FindProperty("Animations")));
            root.Add(new PropertyField(serializedObject.FindProperty("BoneNames")));
            root.Add(new PropertyField(serializedObject.FindProperty("AnimationNames")));

            // Add Virtual Bones field
            var virtualBonesField = new PropertyField(serializedObject.FindProperty("VirtualBones"), "Virtual IK Bones");
            root.Add(virtualBonesField);

            // Add Extra Curve Names
            var extraCurvesField = new PropertyField(serializedObject.FindProperty("ExtraCurveNames"), "Extra Curves (Attributes)");
            root.Add(extraCurvesField);

            // Add Curve Bindings (Read-only)
            var curveBindingsField = new PropertyField(serializedObject.FindProperty("CurveBindings"), "Packed Curves (Read-only)");
            curveBindingsField.SetEnabled(false);
            root.Add(curveBindingsField);

            root.Bind(serializedObject);

            // Add Apply/Revert buttons (Required for ScriptedImporterEditor)
            root.Add(new IMGUIContainer(ApplyRevertGUI));

            return root;
        }
    }
}
#endif
