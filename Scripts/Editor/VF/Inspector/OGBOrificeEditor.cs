using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.UIElements;
using VF.Builder;
using VF.Menu;
using VF.Model;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;

namespace VF.Inspector {
    [CustomEditor(typeof(OGBOrifice), true)]
    public class OGBOrificeEditor : Editor {
        public override VisualElement CreateInspectorGUI() {
            var self = (OGBOrifice)target;

            var container = new VisualElement();
            
            container.Add(VRCFuryEditorUtils.Prop(serializedObject.FindProperty("name"), "Name Override"));
            container.Add(VRCFuryEditorUtils.Prop(serializedObject.FindProperty("addLight"), "Add DPS Light"));
            container.Add(VRCFuryEditorUtils.Prop(serializedObject.FindProperty("addMenuItem"), "Add Toggle to Menu?"));
            container.Add(VRCFuryEditorUtils.Prop(serializedObject.FindProperty("enableAuto"), "Include in Auto selection?"));
            container.Add(VRCFuryEditorUtils.WrappedLabel("Enable hand touch zone? (Auto will add only if child of Hips)"));
            container.Add(VRCFuryEditorUtils.Prop(serializedObject.FindProperty("enableHandTouchZone2")));
            container.Add(VRCFuryEditorUtils.WrappedLabel("Hand touch zone depth override in meters:\nNote, this zone is only used for hand touches, not penetration"));
            container.Add(VRCFuryEditorUtils.Prop(serializedObject.FindProperty("length")));

            var adv = new Foldout {
                text = "Advanced transform override",
                value = false,
            };
            container.Add(adv);
            adv.Add(VRCFuryEditorUtils.Prop(serializedObject.FindProperty("position"), "Position"));
            adv.Add(VRCFuryEditorUtils.Prop(serializedObject.FindProperty("rotation"), "Rotation"));

            container.Add(VRCFuryEditorUtils.WrappedLabel("Animations when penetrated:"));
            container.Add(VRCFuryEditorUtils.List(serializedObject.FindProperty("depthActions"), (i, prop) => {
                var c = new VisualElement();
                c.Add(VRCFuryEditorUtils.Info(
                    "If you provide an animation clip with more than 2 frames, the clip will run from start " +
                    "to end depending on penetration depth. Otherwise, it will animate from 'off' to 'on' depending on depth."));
                c.Add(VRCFuryEditorUtils.WrappedLabel("Penetrated state:"));
                c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("state")));
                c.Add(VRCFuryEditorUtils.WrappedLabel("Depth of minimum penetration in meters (can be slightly negative to trigger outside!):"));
                c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("minDepth")));
                c.Add(VRCFuryEditorUtils.WrappedLabel("Depth of maximum penetration in meters (0 for default):"));
                c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("maxDepth")));
                c.Add(VRCFuryEditorUtils.WrappedLabel("Enable animation for penetrators on this same avatar?"));
                c.Add(VRCFuryEditorUtils.Prop(prop.FindPropertyRelative("enableSelf")));
                return c;
            }));

            return container;
        }
        
        [DrawGizmo(GizmoType.Selected | GizmoType.Active | GizmoType.InSelectionHierarchy)]
        static void DrawGizmo(OGBOrifice orf, GizmoType gizmoType) {
            var autoInfo = GetInfoFromLightsOrComponent(orf);

            var (lightType, localPosition, localRotation) = autoInfo;
            var localForward = localRotation * Vector3.forward;
            // This is *90 because capsule length is actually "height", so we have to rotate it to make it a length
            var localCapsuleRotation = localRotation * Quaternion.Euler(90,0,0);
            
            var text = "Orifice Missing Light";
            if (lightType == OGBOrifice.AddLight.Hole) text = "Hole";
            if (lightType == OGBOrifice.AddLight.Ring) text = "Ring";

            var handTouchZoneSize = GetHandTouchZoneSize(orf);
            if (handTouchZoneSize != null) {
                var worldLength = handTouchZoneSize.Item1;
                var localLength = worldLength / orf.transform.lossyScale.x;
                var worldRadius = handTouchZoneSize.Item2;
                OGBPenetratorEditor.DrawCapsule(
                    orf.gameObject,
                    localPosition + localForward * -(localLength / 2),
                    localCapsuleRotation,
                    worldLength,
                    worldRadius
                );
                VRCFuryGizmoUtils.DrawText(
                    orf.transform.TransformPoint(localPosition + localForward * -(localLength / 2)),
                    "Hand Touch Zone\n(should be INSIDE)",
                    Color.red
                );
            }

            VRCFuryGizmoUtils.DrawSphere(
                orf.transform.TransformPoint(localPosition),
                0.03f,
                Color.green
            );
            VRCFuryGizmoUtils.DrawArrow(
                orf.transform.TransformPoint(localPosition),
                orf.transform.TransformPoint(localPosition + localForward * -0.1f / orf.transform.lossyScale.x),
                Color.green
            );
            VRCFuryGizmoUtils.DrawText(
                orf.transform.TransformPoint(localPosition),
                text + "\n(Arrow points INWARD)",
                Color.green
            );
        }
        
        public static Tuple<string,GameObject> Bake(OGBOrifice orifice, List<string> usedNames = null, bool onlySenders = false) {
            var obj = orifice.gameObject;
            OGBUtils.RemoveTPSSenders(obj);
            
            OGBUtils.AssertValidScale(obj, "orifice");

            var (lightType, localPosition, localRotation) = GetInfoFromLightsOrComponent(orifice);
            // This is *90 because capsule length is actually "height", so we have to rotate it to make it a length
            var capsuleRotation = Quaternion.Euler(90,0,0);

            var name = GetName(orifice);
            if (usedNames != null) name = OGBUtils.GetNextName(usedNames, name);

            Debug.Log("Baking OGB " + obj + " as " + name);

            var bakeRoot = new GameObject("BakedOGBOrifice");
            bakeRoot.transform.SetParent(orifice.transform, false);
            bakeRoot.transform.localPosition = localPosition;
            bakeRoot.transform.localRotation = localRotation;

            var senders = new GameObject("Senders");
            senders.transform.SetParent(bakeRoot.transform, false);

            // Senders
            OGBUtils.AddSender(senders, Vector3.zero, "Root", 0.01f, OGBUtils.CONTACT_ORF_MAIN);
            OGBUtils.AddSender(senders, Vector3.forward * 0.01f, "Front", 0.01f, OGBUtils.CONTACT_ORF_NORM);
            
            var paramPrefix = "OGB/Orf/" + name.Replace('/','_');

            if (onlySenders) {
                var info = new GameObject("Info");
                info.transform.SetParent(bakeRoot.transform, false);
                if (!string.IsNullOrWhiteSpace(orifice.name)) {
                    var nameObj = new GameObject("name=" + orifice.name);
                    nameObj.transform.SetParent(info.transform, false);
                }
            } else {
                // Receivers
                var handTouchZoneSize = GetHandTouchZoneSize(orifice);
                var receivers = new GameObject("Receivers");
                receivers.transform.SetParent(bakeRoot.transform, false);
                if (handTouchZoneSize != null) {
                    var oscDepth = handTouchZoneSize.Item1;
                    var closeRadius = handTouchZoneSize.Item2;
                    OGBUtils.AddReceiver(receivers, Vector3.forward * -oscDepth, paramPrefix + "/TouchSelf", "TouchSelf", oscDepth, OGBUtils.SelfContacts, allowOthers:false, localOnly:true);
                    OGBUtils.AddReceiver(receivers, Vector3.forward * -(oscDepth/2), paramPrefix + "/TouchSelfClose", "TouchSelfClose", closeRadius, OGBUtils.SelfContacts, allowOthers:false, localOnly:true, height: oscDepth, rotation: capsuleRotation, type: ContactReceiver.ReceiverType.Constant);
                    OGBUtils.AddReceiver(receivers, Vector3.forward * -oscDepth, paramPrefix + "/TouchOthers", "TouchOthers", oscDepth, OGBUtils.BodyContacts, allowSelf:false, localOnly:true);
                    OGBUtils.AddReceiver(receivers, Vector3.forward * -(oscDepth/2), paramPrefix + "/TouchOthersClose", "TouchOthersClose", closeRadius, OGBUtils.BodyContacts, allowSelf:false, localOnly:true, height: oscDepth, rotation: capsuleRotation, type: ContactReceiver.ReceiverType.Constant);
                    // Legacy non-OGB TPS penetrator detection
                    OGBUtils.AddReceiver(receivers, Vector3.forward * -oscDepth, paramPrefix + "/PenOthers", "PenOthers", oscDepth, new []{OGBUtils.CONTACT_PEN_MAIN}, allowSelf:false, localOnly:true);
                    OGBUtils.AddReceiver(receivers, Vector3.forward * -(oscDepth/2), paramPrefix + "/PenOthersClose", "PenOthersClose", closeRadius, new []{OGBUtils.CONTACT_PEN_MAIN}, allowSelf:false, localOnly:true, height: oscDepth, rotation: capsuleRotation, type: ContactReceiver.ReceiverType.Constant);
                    
                    var frotRadius = 0.1f;
                    var frotPos = 0.05f;
                    OGBUtils.AddReceiver(receivers, Vector3.forward * frotPos, paramPrefix + "/FrotOthers", "FrotOthers", frotRadius, new []{OGBUtils.CONTACT_ORF_MAIN}, allowSelf:false, localOnly:true);
                }
                
                OGBUtils.AddReceiver(receivers, Vector3.zero, paramPrefix + "/PenSelfNewRoot", "PenSelfNewRoot", 1f, new []{OGBUtils.CONTACT_PEN_ROOT}, allowOthers:false, localOnly:true);
                OGBUtils.AddReceiver(receivers, Vector3.zero, paramPrefix + "/PenSelfNewTip", "PenSelfNewTip", 1f, new []{OGBUtils.CONTACT_PEN_MAIN}, allowOthers:false, localOnly:true);
                OGBUtils.AddReceiver(receivers, Vector3.zero, paramPrefix + "/PenOthersNewRoot", "PenOthersNewRoot", 1f, new []{OGBUtils.CONTACT_PEN_ROOT}, allowSelf:false, localOnly:true);
                OGBUtils.AddReceiver(receivers, Vector3.zero, paramPrefix + "/PenOthersNewTip", "PenOthersNewTip", 1f, new []{OGBUtils.CONTACT_PEN_MAIN}, allowSelf:false, localOnly:true);
            }
            
            OGBUtils.AddVersionContacts(bakeRoot, paramPrefix, onlySenders, false);

            if (lightType != OGBOrifice.AddLight.None) {
                var lights = new GameObject("Lights");
                lights.transform.SetParent(bakeRoot.transform, false);

                ForEachPossibleLight(obj.transform, false, light => {
                    AvatarCleaner.RemoveComponent(light);
                });

                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) {
                    var main = new GameObject("Root");
                    main.transform.SetParent(lights.transform, false);
                    main.transform.localPosition = Vector3.zero;
                    var mainLight = main.AddComponent<Light>();
                    mainLight.type = LightType.Point;
                    mainLight.color = Color.black;
                    mainLight.range = lightType == OGBOrifice.AddLight.Ring ? 0.42f : 0.41f;
                    mainLight.shadows = LightShadows.None;
                    mainLight.renderMode = LightRenderMode.ForceVertex;

                    var front = new GameObject("Front");
                    front.transform.SetParent(lights.transform, false);
                    front.transform.localPosition = Vector3.forward * 0.01f / lights.transform.lossyScale.x;
                    var frontLight = front.AddComponent<Light>();
                    frontLight.type = LightType.Point;
                    frontLight.color = Color.black;
                    frontLight.range = 0.45f;
                    frontLight.shadows = LightShadows.None;
                    frontLight.renderMode = LightRenderMode.ForceVertex;
                }
            }

            return Tuple.Create(name, bakeRoot);
        }

        private static Tuple<float, float> GetHandTouchZoneSize(OGBOrifice orifice) {
            bool enableHandTouchZone = false;
            if (orifice.enableHandTouchZone2 == OGBOrifice.EnableTouchZone.On) {
                enableHandTouchZone = true;
            } else if (orifice.enableHandTouchZone2 == OGBOrifice.EnableTouchZone.Auto) {
                enableHandTouchZone = ShouldProbablyHaveTouchZone(orifice);
            }
            if (!enableHandTouchZone) {
                return null;
            }
            var length = orifice.length;
            if (length <= 0) length = 0.25f;
            var radius = length / 2.5f;
            return Tuple.Create(length, radius);
        }
        
        public static bool IsHole(Light light) {
            var rangeId = light.range % 0.1;
            return rangeId >= 0.005f && rangeId < 0.015f;
        }
        public static bool IsRing(Light light) {
            var rangeId = light.range % 0.1;
            return rangeId >= 0.015f && rangeId < 0.025f;
        }
        public static bool IsNormal(Light light) {
            var rangeId = light.range % 0.1;
            return rangeId >= 0.045f && rangeId <= 0.055f;
        }

        public static Tuple<OGBOrifice.AddLight, Vector3, Quaternion> GetInfoFromLightsOrComponent(OGBOrifice orf) {
            if (orf.addLight != OGBOrifice.AddLight.None) {
                var type = orf.addLight;
                if (type == OGBOrifice.AddLight.Auto) type = ShouldProbablyBeHole(orf) ? OGBOrifice.AddLight.Hole : OGBOrifice.AddLight.Ring;
                var position = orf.position;
                var rotation = Quaternion.Euler(orf.rotation);
                return Tuple.Create(type, position, rotation);
            }
            
            var lightInfo = GetInfoFromLights(orf.gameObject);
            if (lightInfo != null) {
                return lightInfo;
            }

            return Tuple.Create(OGBOrifice.AddLight.None, Vector3.zero, Quaternion.identity);
        }

        /**
         * Visit every light that could possibly be used for this orifice. This includes all children,
         * and single-deptch children of all parents.
         */
        public static void ForEachPossibleLight(Transform obj, bool directOnly, Action<Light> act) {
            var visited = new HashSet<Light>();
            void Visit(Light light) {
                if (visited.Contains(light)) return;
                visited.Add(light);
                if (!IsHole(light) && !IsRing(light) && !IsNormal(light)) return;
                act(light);
            }
            foreach (Transform child in obj) {
                foreach (var light in child.gameObject.GetComponents<Light>()) {
                    Visit(light);
                }
            }
            if (!directOnly) {
                foreach (var light in obj.GetComponentsInChildren<Light>(true)) {
                    Visit(light);
                }
            }
        }
        public static Tuple<OGBOrifice.AddLight, Vector3, Quaternion> GetInfoFromLights(GameObject obj, bool directOnly = false) {
            var isRing = false;
            Light main = null;
            Light normal = null;
            ForEachPossibleLight(obj.transform, directOnly, light => {
                if (main == null) {
                    if (IsHole(light)) {
                        main = light;
                    } else if (IsRing(light)) {
                        main = light;
                        isRing = true;
                    }
                }
                if (normal == null && IsNormal(light)) {
                    normal = light;
                }
            });

            if (main == null || normal == null) return null;

            var position = obj.transform.InverseTransformPoint(main.transform.position);
            var normalPosition = obj.transform.InverseTransformPoint(normal.transform.position);
            var forward = (normalPosition - position).normalized;
            var rotation = Quaternion.LookRotation(forward);

            return Tuple.Create(isRing ? OGBOrifice.AddLight.Ring : OGBOrifice.AddLight.Hole, position, rotation);
        }

        private static bool IsDirectChildOfHips(OGBOrifice orf) {
            return IsChildOfBone(orf, HumanBodyBones.Hips)
                && !IsChildOfBone(orf, HumanBodyBones.Chest)
                && !IsChildOfBone(orf, HumanBodyBones.Spine)
                && !IsChildOfBone(orf, HumanBodyBones.LeftUpperArm)
                && !IsChildOfBone(orf, HumanBodyBones.LeftUpperLeg)
                && !IsChildOfBone(orf, HumanBodyBones.RightUpperArm)
                && !IsChildOfBone(orf, HumanBodyBones.RightUpperLeg);
        }

        public static bool ShouldProbablyHaveTouchZone(OGBOrifice orf) {
            if (IsDirectChildOfHips(orf)) {
                var name = GetName(orf).ToLower();
                if (name.Contains("rubbing") || name.Contains("job")) {
                    return false;
                }
                return true;
            }
            return false;
        }
        
        public static bool ShouldProbablyBeHole(OGBOrifice orf) {
            if (IsChildOfBone(orf, HumanBodyBones.Head)) return true;
            return ShouldProbablyHaveTouchZone(orf);
        }

        private static bool IsChildOfBone(OGBOrifice orf, HumanBodyBones bone) {
            try {
                var avatarObject = orf.gameObject.GetComponentInParent<VRCAvatarDescriptor>()?.gameObject;
                if (!avatarObject) return false;
                var boneObj = VRCFArmatureUtils.FindBoneOnArmature(avatarObject, bone);
                return boneObj && IsChildOf(boneObj.transform, orf.transform);
            } catch (Exception e) {
                return false;
            }
        }

        private static string GetName(OGBOrifice orifice) {
            var name = orifice.name;
            if (string.IsNullOrWhiteSpace(name)) {
                name = orifice.gameObject.name;
            }
            return name;
        }

        private static bool IsChildOf(Transform parent, Transform child) {
            var alreadyChecked = new HashSet<Transform>();
            var current = child;
            while (current != null) {
                alreadyChecked.Add(current);
                if (current == parent) return true;
                var constraint = current.GetComponent<IConstraint>();
                if (constraint != null && constraint.sourceCount > 0) {
                    var source = constraint.GetSource(0).sourceTransform;
                    if (source != null && !alreadyChecked.Contains(source)) {
                        current = source;
                        continue;
                    }
                }
                current = current.parent;
            }
            return false;
        }
    }
}
