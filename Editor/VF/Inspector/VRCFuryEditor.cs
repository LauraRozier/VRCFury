using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VF.Builder;
using VF.Feature;
using VF.Feature.Base;
using VF.Model;
using VF.Model.Feature;

namespace VF.Inspector {
    

[CustomEditor(typeof(VRCFury), true)]
public class VRCFuryEditor : Editor {
    public override VisualElement CreateInspectorGUI() {
        var self = (VRCFury)target;

        if (self.failedToLoad) {
            return VRCFuryEditorUtils.Error(
                "The VRCFury component failed to load. It's likely that your VRCFury is out of date." +
                " Please try Tools -> VRCFury -> Update VRCFury. If this doesn't help, let us know on the " +
                " discord at https://vrcfury.com/discord");
        }

        self.config.Upgrade();
        serializedObject.Update();

        var container = new VisualElement();

        var features = serializedObject.FindProperty("config.features");
        if (features == null) {
            container.Add(new Label("Feature list is missing? This is a bug."));
        } else {
            var disabled = PrefabUtility.IsPartOfPrefabInstance(self);
            container.Add(CreateOverrideLabel(features));
            if (disabled) {
                // We prevent users from adding overrides on prefabs, because it does weird things (at least in unity 2019)
                // when you apply modifications to an object that lives within a SerializedReference. Some properties not overridden
                // will just be thrown out randomly, and unity will dump a bunch of errors.
                var baseFury = PrefabUtility.GetCorrespondingObjectFromOriginalSource(self);
                container.Add(CreatePrefabInstanceLabel(baseFury));
            }
            var featureList = VRCFuryEditorUtils.List(features, 
                renderElement: (i, prop) => renderFeature(self.config.features[i], prop, self.gameObject),
                onPlus: () => OnPlus(features, self.gameObject),
                onEmpty: () => {
                    var c = new VisualElement();
                    VRCFuryEditorUtils.Padding(c, 10);
                    var l = new Label {
                        text = "You haven't added any VRCFury features yet.",
                        style = {
                            unityTextAlign = TextAnchor.MiddleCenter
                        }
                    };
                    c.Add(l);
                    var l2 = new Label {
                        text = "Click the + to add your first one!",
                        style = {
                            unityTextAlign = TextAnchor.MiddleCenter
                        }
                    };
                    c.Add(l2);
                    return c;
                }
            );
            container.Add(featureList);
            if (disabled) featureList.SetEnabled(false);
        }

        var pointingToAvatar = self.gameObject.GetComponent<VRCAvatarDescriptor>() != null;
        if (pointingToAvatar) {
            var box = new Box();
            box.style.marginTop = box.style.marginBottom = 10;
            container.Add(box);

            var label = new Label(
                "VRCFury only applies changes to a temporary copy of your avatar while uploading." + 
                " If you wish to verify these changes in your editor, clicking this button will generate" +
                " a clone object with the VRCFury changes applied.");
            VRCFuryEditorUtils.Padding(box, 5);
            label.style.whiteSpace = WhiteSpace.Normal;
            box.Add(label);

            var genButton = new Button(() => {
                var builder = new VRCFuryBuilder();
                builder.TestRun(self.gameObject);
            }) {
                style = {
                    marginTop = 5
                },
                text = "Build a Test Copy"
            };
            box.Add(genButton);
        }

        return container;
    }

    private VisualElement CreateOverrideLabel(SerializedProperty prop) {
        var overrideLabel = VRCFuryEditorUtils.Error(
            "The VRCFury features in this prefab are overridden on this instance. Please revert them!" +
            " If you apply, it may corrupt data in the changed features.");
        overrideLabel.style.display = DisplayStyle.None;

        double lastCheck = 0;
        void CheckOverride() {
            if (this == null) return; // The editor was deleted
            var now = EditorApplication.timeSinceStartup;
            if (lastCheck < now - 0.5) {
                lastCheck = now;
                overrideLabel.style.display = prop.prefabOverride ? DisplayStyle.Flex : DisplayStyle.None;
            }
            EditorApplication.delayCall += CheckOverride;
        }
        CheckOverride();

        return overrideLabel;
    }
    
    private VisualElement CreatePrefabInstanceLabel(VRCFury parent) {
        var label = new Button(() => AssetDatabase.OpenAsset(parent)) {
            text = "You are viewing a prefab instance\nClick here to edit VRCFury on the base prefab",
            style = {
                paddingTop = 5,
                paddingBottom = 5,
                unityTextAlign = TextAnchor.MiddleCenter,
                whiteSpace = WhiteSpace.Normal,
                borderTopLeftRadius = 5,
                borderTopRightRadius = 5,
                borderBottomLeftRadius = 0,
                borderBottomRightRadius = 0,
                marginTop = 5,
                marginLeft = 20,
                marginRight = 20,
                borderTopWidth = 1,
                borderLeftWidth = 1,
                borderRightWidth = 1,
                borderBottomWidth = 0
            }
        };
        VRCFuryEditorUtils.Padding(label, 5);
        VRCFuryEditorUtils.BorderColor(label, Color.black);
        return label;
    }

    private VisualElement renderFeature(FeatureModel model, SerializedProperty prop, GameObject gameObject) {
        return FeatureFinder.RenderFeatureEditor(prop, model, gameObject);
    }

    private void OnPlus(SerializedProperty listProp, GameObject gameObject) {
        var menu = new GenericMenu();
        foreach (var feature in FeatureFinder.GetAllFeaturesForMenu(gameObject)) {
            var editorInst = (FeatureBuilder) Activator.CreateInstance(feature.Value);
            var title = editorInst.GetEditorTitle();
            if (title != null) {
                menu.AddItem(new GUIContent(title), false, () => {
                    var modelInst = Activator.CreateInstance(feature.Key);
                    VRCFuryEditorUtils.AddToList(listProp, entry => entry.managedReferenceValue = modelInst);
                });
            }
        }
        menu.ShowAsContext();
    }
    
    [DrawGizmo(GizmoType.Selected | GizmoType.Active | GizmoType.InSelectionHierarchy)]
    static void DrawGizmo(VRCFury vf, GizmoType gizmoType) {
        foreach (var g in vf.config.features.Select(f => f as Gizmo).Where(f => f != null)) {
            var q = Quaternion.Euler(g.rotation);
            Vector3 getPoint(Vector3 input) {
                return vf.transform.TransformPoint(q * input);
            }

            var worldPos = getPoint(Vector3.zero);

            if (g.arrowLength > 0) {
                var tip = getPoint(new Vector3(0, 0, g.arrowLength));
                VRCFuryGizmoUtils.DrawArrow(worldPos, tip, Color.red);
            }

            if (!string.IsNullOrWhiteSpace(g.text)) {
                VRCFuryGizmoUtils.DrawText(worldPos, g.text, Color.white);
            }

            if (g.sphereRadius > 0) {
                VRCFuryGizmoUtils.DrawSphere(worldPos, g.sphereRadius * vf.transform.lossyScale.x, Color.red);
            }
        }
    }
}

}
