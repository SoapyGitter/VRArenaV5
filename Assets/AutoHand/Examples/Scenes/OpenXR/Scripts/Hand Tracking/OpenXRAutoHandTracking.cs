using Autohand.Demo;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Autohand {
    public enum AxisEnum {
        right,
        up,
        forward,
        left,
        down,
        back
    }

    [System.Serializable]
    public struct HandPoseOffset {
        public XRHandJointID jointID;
        public Vector3 localPositionOffset;
        public Vector3 localEularRotationOffset;
    }

    [RequireComponent(typeof(XRHandTrackingEvents))]
    public class OpenXRAutoHandTracking : MonoBehaviour {
        [Header("Hand Settings")]
        public Hand hand;
        public OpenXRHandControllerLink controllerLink;
        public AxisEnum upAxis = AxisEnum.up;
        public AxisEnum forwardAxis = AxisEnum.right;
        public Vector3 handOffset = Vector3.zero;
        public Vector3 handRotationOffset = Vector3.zero;
        public float handPoseSmoothingSpeed = 0.5f;
        public List<HandPoseOffset> handPoseOffsets = new List<HandPoseOffset>();

        [Header("Follow Settings")]
        public float followPositionSmoothing = 1f;
        public float followRotationSmoothing = 1f;

        [Header("Gizmos")]
        public bool drawGizmos = true;
        public Color gizmoColor = Color.white;

        FingerPoseData[] _currentHandTrackingPose = new FingerPoseData[5];
        public FingerPoseData[] currentHandTrackingPose { get { return _currentHandTrackingPose; } }

        FingerPoseData[] _currentTargetPose = new FingerPoseData[5];
        public FingerPoseData[] currentTargetPose { get { return _currentTargetPose; } }

        public bool handTrackingActive { get; private set; }
        public bool controllerTrackingActive { get; private set; }

        XRHandTrackingEvents xrHandTrackingEvents;

        bool jointMapInitialized = false;
        XRHandJointID[] _jointIDMap;
        XRHandJointID[] jointIDMap {
            get {
                if(!jointMapInitialized) {
                    _jointIDMap = new XRHandJointID[20];
                    _jointIDMap[(int)FingerEnum.index * 4 + (int)FingerJointEnum.tip] = XRHandJointID.IndexTip;
                    _jointIDMap[(int)FingerEnum.index * 4 + (int)FingerJointEnum.distal] = XRHandJointID.IndexDistal;
                    _jointIDMap[(int)FingerEnum.index * 4 + (int)FingerJointEnum.middle] = XRHandJointID.IndexIntermediate;
                    _jointIDMap[(int)FingerEnum.index * 4 + (int)FingerJointEnum.knuckle] = XRHandJointID.IndexProximal;

                    _jointIDMap[(int)FingerEnum.middle * 4 + (int)FingerJointEnum.tip] = XRHandJointID.MiddleTip;
                    _jointIDMap[(int)FingerEnum.middle * 4 + (int)FingerJointEnum.distal] = XRHandJointID.MiddleDistal;
                    _jointIDMap[(int)FingerEnum.middle * 4 + (int)FingerJointEnum.middle] = XRHandJointID.MiddleIntermediate;
                    _jointIDMap[(int)FingerEnum.middle * 4 + (int)FingerJointEnum.knuckle] = XRHandJointID.MiddleProximal;

                    _jointIDMap[(int)FingerEnum.ring * 4 + (int)FingerJointEnum.tip] = XRHandJointID.RingTip;
                    _jointIDMap[(int)FingerEnum.ring * 4 + (int)FingerJointEnum.distal] = XRHandJointID.RingDistal;
                    _jointIDMap[(int)FingerEnum.ring * 4 + (int)FingerJointEnum.middle] = XRHandJointID.RingIntermediate;
                    _jointIDMap[(int)FingerEnum.ring * 4 + (int)FingerJointEnum.knuckle] = XRHandJointID.RingProximal;

                    _jointIDMap[(int)FingerEnum.pinky * 4 + (int)FingerJointEnum.tip] = XRHandJointID.LittleTip;
                    _jointIDMap[(int)FingerEnum.pinky * 4 + (int)FingerJointEnum.distal] = XRHandJointID.LittleDistal;
                    _jointIDMap[(int)FingerEnum.pinky * 4 + (int)FingerJointEnum.middle] = XRHandJointID.LittleIntermediate;
                    _jointIDMap[(int)FingerEnum.pinky * 4 + (int)FingerJointEnum.knuckle] = XRHandJointID.LittleProximal;

                    _jointIDMap[(int)FingerEnum.thumb * 4 + (int)FingerJointEnum.tip] = XRHandJointID.ThumbTip;
                    _jointIDMap[(int)FingerEnum.thumb * 4 + (int)FingerJointEnum.distal] = XRHandJointID.ThumbDistal;
                    _jointIDMap[(int)FingerEnum.thumb * 4 + (int)FingerJointEnum.middle] = XRHandJointID.ThumbProximal;
                    _jointIDMap[(int)FingerEnum.thumb * 4 + (int)FingerJointEnum.knuckle] = XRHandJointID.ThumbMetacarpal;

                    jointMapInitialized = true;
                }
                return _jointIDMap;
            }
        }

        bool handTrackingSkeletonInitialized = false;
        Dictionary<XRHandJointID, Transform> _skeletonMap;
        Dictionary<XRHandJointID, Transform> skeletonMap {
            get {
                if(!handTrackingSkeletonInitialized) {
                    _skeletonMap = new Dictionary<XRHandJointID, Transform>();
                    var wrist = new GameObject("Wrist").transform;
                    var indexProximal = new GameObject("IndexProximal").transform;
                    var indexIntermediate = new GameObject("IndexIntermediate").transform;
                    var indexDistal = new GameObject("IndexDistal").transform;
                    var indexTip = new GameObject("IndexTip").transform;

                    var middleProximal = new GameObject("MiddleProximal").transform;
                    var middleIntermediate = new GameObject("MiddleIntermediate").transform;
                    var middleDistal = new GameObject("MiddleDistal").transform;
                    var middleTip = new GameObject("MiddleTip").transform;

                    var ringProximal = new GameObject("RingProximal").transform;
                    var ringIntermediate = new GameObject("RingIntermediate").transform;
                    var ringDistal = new GameObject("RingDistal").transform;
                    var ringTip = new GameObject("RingTip").transform;

                    var littleProximal = new GameObject("LittleProximal").transform;
                    var littleIntermediate = new GameObject("LittleIntermediate").transform;
                    var littleDistal = new GameObject("LittleDistal").transform;
                    var littleTip = new GameObject("LittleTip").transform;

                    var thumbMetacarpal = new GameObject("ThumbMetacarpal").transform;
                    var thumbProximal = new GameObject("ThumbProximal").transform;
                    var thumbDistal = new GameObject("ThumbDistal").transform;
                    var thumbTip = new GameObject("ThumbTip").transform;

                    indexProximal.SetParent(wrist);
                    indexIntermediate.SetParent(indexProximal);
                    indexDistal.SetParent(indexIntermediate);
                    indexTip.SetParent(indexDistal);

                    middleProximal.SetParent(wrist);
                    middleIntermediate.SetParent(middleProximal);
                    middleDistal.SetParent(middleIntermediate);
                    middleTip.SetParent(middleDistal);

                    ringProximal.SetParent(wrist);
                    ringIntermediate.SetParent(ringProximal);
                    ringDistal.SetParent(ringIntermediate);
                    ringTip.SetParent(ringDistal);

                    littleProximal.SetParent(wrist);
                    littleIntermediate.SetParent(littleProximal);
                    littleDistal.SetParent(littleIntermediate);
                    littleTip.SetParent(littleDistal);

                    thumbMetacarpal.SetParent(wrist);
                    thumbProximal.SetParent(thumbMetacarpal);
                    thumbDistal.SetParent(thumbProximal);
                    thumbTip.SetParent(thumbDistal);

                    _skeletonMap.Add(XRHandJointID.Wrist, wrist);
                    _skeletonMap.Add(XRHandJointID.IndexProximal, indexProximal);
                    _skeletonMap.Add(XRHandJointID.IndexIntermediate, indexIntermediate);
                    _skeletonMap.Add(XRHandJointID.IndexDistal, indexDistal);
                    _skeletonMap.Add(XRHandJointID.IndexTip, indexTip);

                    _skeletonMap.Add(XRHandJointID.MiddleProximal, middleProximal);
                    _skeletonMap.Add(XRHandJointID.MiddleIntermediate, middleIntermediate);
                    _skeletonMap.Add(XRHandJointID.MiddleDistal, middleDistal);
                    _skeletonMap.Add(XRHandJointID.MiddleTip, middleTip);

                    _skeletonMap.Add(XRHandJointID.RingProximal, ringProximal);
                    _skeletonMap.Add(XRHandJointID.RingIntermediate, ringIntermediate);
                    _skeletonMap.Add(XRHandJointID.RingDistal, ringDistal);
                    _skeletonMap.Add(XRHandJointID.RingTip, ringTip);

                    _skeletonMap.Add(XRHandJointID.LittleProximal, littleProximal);
                    _skeletonMap.Add(XRHandJointID.LittleIntermediate, littleIntermediate);
                    _skeletonMap.Add(XRHandJointID.LittleDistal, littleDistal);
                    _skeletonMap.Add(XRHandJointID.LittleTip, littleTip);

                    _skeletonMap.Add(XRHandJointID.ThumbMetacarpal, thumbMetacarpal);
                    _skeletonMap.Add(XRHandJointID.ThumbProximal, thumbProximal);
                    _skeletonMap.Add(XRHandJointID.ThumbDistal, thumbDistal);
                    _skeletonMap.Add(XRHandJointID.ThumbTip, thumbTip);

                    handTrackingSkeletonInitialized = true;
                }
                return _skeletonMap;
            }
        }

        Transform handControllerFollow = null;

        Transform _handTrackingFollowOffset = null;
        public Transform handTrackingFollowOffset {
            get {
                if(_handTrackingFollowOffset == null) {
                    _handTrackingFollowOffset = new GameObject("HandFollowTrackingOffset").transform;
                    _handTrackingFollowOffset.parent = handTrackingFollow;
                    _handTrackingFollowOffset.localPosition = Vector3.zero;
                    _handTrackingFollowOffset.localRotation = Quaternion.identity;
                }
                return _handTrackingFollowOffset;
            }
        }

        Transform _handTrackingFollow = null;
        public Transform handTrackingFollow {
            get {
                if(_handTrackingFollow == null) {
                    _handTrackingFollow = new GameObject("HandFollowTracking").transform;
                    _handTrackingFollow.parent = hand.transform.parent;
                    _handTrackingFollow.localPosition = Vector3.zero;
                    _handTrackingFollow.localRotation = Quaternion.identity;
                    if(handControllerFollow == null) {
                        if(hand.follow != null)
                            handControllerFollow = hand.follow;
                        else
                            handControllerFollow = handTrackingFollowOffset;
                    }
                    hand.follow = handTrackingFollowOffset;
                }
                return _handTrackingFollow;
            }
        }

        public XRHandJointID GetHandJointID(FingerEnum fingerType, FingerJointEnum fingerJoint) => jointIDMap[(int)fingerType * 4 + (int)fingerJoint];

        public Transform GetHandTransform(XRHandJointID jointID) {
            switch(jointID) {
                case XRHandJointID.IndexProximal: return GetFinger(FingerEnum.index).knuckleJoint;
                case XRHandJointID.IndexIntermediate: return GetFinger(FingerEnum.index).middleJoint;
                case XRHandJointID.IndexDistal: return GetFinger(FingerEnum.index).distalJoint;
                case XRHandJointID.IndexTip: return GetFinger(FingerEnum.index).tip;
                case XRHandJointID.MiddleProximal: return GetFinger(FingerEnum.middle).knuckleJoint;
                case XRHandJointID.MiddleIntermediate: return GetFinger(FingerEnum.middle).middleJoint;
                case XRHandJointID.MiddleDistal: return GetFinger(FingerEnum.middle).distalJoint;
                case XRHandJointID.MiddleTip: return GetFinger(FingerEnum.middle).tip;
                case XRHandJointID.RingProximal: return GetFinger(FingerEnum.ring).knuckleJoint;
                case XRHandJointID.RingIntermediate: return GetFinger(FingerEnum.ring).middleJoint;
                case XRHandJointID.RingDistal: return GetFinger(FingerEnum.ring).distalJoint;
                case XRHandJointID.RingTip: return GetFinger(FingerEnum.ring).tip;
                case XRHandJointID.LittleProximal: return GetFinger(FingerEnum.pinky).knuckleJoint;
                case XRHandJointID.LittleIntermediate: return GetFinger(FingerEnum.pinky).middleJoint;
                case XRHandJointID.LittleDistal: return GetFinger(FingerEnum.pinky).distalJoint;
                case XRHandJointID.LittleTip: return GetFinger(FingerEnum.pinky).tip;
                case XRHandJointID.ThumbMetacarpal: return GetFinger(FingerEnum.thumb).knuckleJoint;
                case XRHandJointID.ThumbProximal: return GetFinger(FingerEnum.thumb).middleJoint;
                case XRHandJointID.ThumbDistal: return GetFinger(FingerEnum.thumb).distalJoint;
                case XRHandJointID.ThumbTip: return GetFinger(FingerEnum.thumb).tip;
            }
            return null;
        }

        Finger GetFinger(FingerEnum fingerType) {
            for(int i = 0; i < hand.fingers.Length; i++) {
                if(hand.fingers[i].fingerType == fingerType)
                    return hand.fingers[i];
            }
            return null;
        }

        Dictionary<XRHandJointID, Pose> handPoseOffsetDictionary;

        protected virtual void OnEnable() {
            xrHandTrackingEvents = GetComponent<XRHandTrackingEvents>();
            if(hand != null)
                xrHandTrackingEvents.handedness = hand.left ? Handedness.Left : Handedness.Right;

            hand.follow = handTrackingFollowOffset;

            for(int i = 0; i < _currentHandTrackingPose.Length; i++)
                _currentHandTrackingPose[i] = new FingerPoseData(hand, hand.fingers[i]);

            for(int i = 0; i < _currentTargetPose.Length; i++)
                _currentTargetPose[i] = new FingerPoseData(hand, hand.fingers[i]);

            if(controllerLink == null) {
                if(!hand.CanGetComponent(out controllerLink))
                    controllerLink = hand.gameObject.GetComponentInChildren<OpenXRHandControllerLink>();
            }

            handPoseOffsetDictionary = new Dictionary<XRHandJointID, Pose>();
            foreach(var poseOffset in handPoseOffsets) {
                var jointTransform = GetHandTransform(poseOffset.jointID);
                if(jointTransform != null) {
                    var basePos = jointTransform.localPosition;
                    var offset = new Pose(
                        basePos + poseOffset.localPositionOffset,
                        Quaternion.Euler(poseOffset.localEularRotationOffset)
                    );
                    handPoseOffsetDictionary.Add(poseOffset.jointID, offset);
                }
            }

            xrHandTrackingEvents.jointsUpdated.AddListener(UpdateSkeletonTransform);
        }

        protected virtual void OnDisable() {
            handPoseOffsetDictionary.Clear(); 
            xrHandTrackingEvents.jointsUpdated.RemoveListener(UpdateSkeletonTransform);

            hand.enableIK = true;
            hand.follow = handControllerFollow;
            if(controllerLink != null)
                controllerLink.enabled = true;
        }

        protected virtual void Update() {

            hand.enableIK = !xrHandTrackingEvents.handIsTracked;
            hand.follow = xrHandTrackingEvents.handIsTracked ? handTrackingFollowOffset : handControllerFollow;
            if(controllerLink != null) {
                controllerLink.enabled = !xrHandTrackingEvents.handIsTracked;
                controllerTrackingActive = controllerLink.enabled;
            }

            handTrackingActive = xrHandTrackingEvents.handIsTracked;
        }

        protected virtual void UpdateSkeletonTransform(XRHandJointsUpdatedEventArgs args) {
            if(controllerTrackingActive || !handTrackingActive)
                return;

            var xrHand = args.hand;
            if(xrHand == null)
                return;
            var map = skeletonMap;
            foreach(var bone in map) {
                var poseValue = xrHand.GetJoint(bone.Key);
                if(poseValue.TryGetPose(out var pose)) {
                    bone.Value.position = pose.position;
                    bone.Value.rotation = pose.rotation;
                }
            }

            var wristTransform = map[XRHandJointID.Wrist];
            var dist = Vector3.Distance(handTrackingFollow.position, wristTransform.position);
            var angleFrac = Quaternion.Angle(handTrackingFollow.rotation, wristTransform.rotation) / 180f;

            var movePos = dist * 60f * Time.deltaTime;
            movePos += 1f - (Time.deltaTime * 30f * followPositionSmoothing);

            var moveRot = angleFrac * 60f * Time.deltaTime;
            moveRot += 1f - (Time.deltaTime * 30f * followRotationSmoothing);

            handTrackingFollow.localPosition = Vector3.Lerp(handTrackingFollow.localPosition, wristTransform.position, movePos);
            handTrackingFollow.localRotation = Quaternion.Lerp(handTrackingFollow.localRotation, wristTransform.rotation, moveRot);
            handTrackingFollowOffset.localPosition = handOffset;
            handTrackingFollowOffset.localRotation = Quaternion.Euler(handRotationOffset);

            wristTransform.position = hand.transform.TransformPoint(-handOffset);
            wristTransform.rotation = hand.transform.rotation * Quaternion.Inverse(Quaternion.Euler(handRotationOffset));

            foreach(var finger in hand.fingers) {
                var fingerIndex = (int)finger.fingerType;
                if(hand.IsGrabbing() || hand.IsHolding())
                    _currentTargetPose[fingerIndex].SetPoseData(hand, finger);

                for(int i = 0; i < (int)FingerJointEnum.tip; i++) {
                    var jointID = GetHandJointID(finger.fingerType, (FingerJointEnum)i);
                    var skeletonJoint = map[jointID];
                    var fingerTransform = finger.FingerJoints[i];
                    var forward = GetTransformAxis(skeletonJoint, forwardAxis);
                    var up = GetTransformAxis(skeletonJoint, upAxis);
                    var targetRotation = Quaternion.LookRotation(forward, up);
                    var angleDiff = Quaternion.Angle(fingerTransform.rotation, targetRotation) / 180f;

                    var lerpPoint = angleDiff * 60f * Time.deltaTime;
                    lerpPoint += 1f - (Time.deltaTime * 30f * handPoseSmoothingSpeed);
                    fingerTransform.rotation = Quaternion.Lerp(fingerTransform.rotation, targetRotation, lerpPoint);

                    if(handPoseOffsetDictionary.TryGetValue(jointID, out var poseOffset)) {
                        fingerTransform.localPosition = poseOffset.position;
                        fingerTransform.localRotation *= poseOffset.rotation;
                    }
                }
                _currentHandTrackingPose[fingerIndex].SetPoseData(hand, finger);
                if(hand.IsGrabbing() || hand.IsHolding())
                    _currentTargetPose[fingerIndex].SetFingerPose(finger);
            }
        }




        public Vector3 GetTransformAxis(Transform t, AxisEnum axis) {
            switch(axis) {
                case AxisEnum.right: return t.right;
                case AxisEnum.up: return t.up;
                case AxisEnum.forward: return t.forward;
                case AxisEnum.down: return -t.up;
                case AxisEnum.left: return -t.right;
                case AxisEnum.back: return -t.forward;
            }
            return Vector3.zero;
        }

        protected virtual void OnDrawGizmos() {
            if(!Application.isPlaying) return;
            if(drawGizmos && skeletonMap != null) {
                Gizmos.color = gizmoColor;
                foreach(var bone in skeletonMap) {
                    if(bone.Key != XRHandJointID.Wrist) {
                        Gizmos.DrawLine(bone.Value.position, bone.Value.parent.position);
                    }
                }
            }
        }
    }
}
