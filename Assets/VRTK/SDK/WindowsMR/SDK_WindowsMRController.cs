﻿using HoloToolkit.Unity.InputModule;
using System.Collections.Generic;
using UnityEngine;
#if VRTK_DEFINE_SDK_WINDOWSMR
using UnityEngine.XR.WSA.Input;
#endif

namespace VRTK {
    [SDK_Description(typeof(SDK_WindowsMRSystem))]
    public class SDK_WindowsMRController
#if VRTK_DEFINE_SDK_WINDOWSMR
        : SDK_BaseController
#else
        : SDK_FallbackController 
#endif 
        {
#if VRTK_DEFINE_SDK_WINDOWSMR
        private VRTK_TrackedController cachedLeftController;
        private VRTK_TrackedController cachedRightController;

        /// <summary>
        /// UnityEngine.XR.WSA.Input의 값을 enum올 접근할수 있도록 하려고
        /// </summary>
        enum MotionControllerButtonTypes {
            /// <summary>
            /// select pressed 
            /// </summary>
            Trigger,
            Menu,

            /// <summary>
            /// Grasped
            /// </summary>
            Grip,

            ThumbstickPressed,
            TouchpadPressed,
            TouchpadTouched,

            ButtonOne,
            ButtonTwo,
        }

        /// <summary>
        /// UnityEngine.XR.WSA.Input.InteractionSourceState
        /// 간단한 구조체로 만들어서 다루고 싶어서 MotionControllerInfo를 그대로 쓰진 않았다
        /// </summary>
        struct ControllerState {
            /// <summary>
            /// 유효한 컨트롤러 상태인지 확인하는 목적의 플래그
            /// </summary>
            public bool IsValid;

            public float SelectPressedAmount;
            public bool SelectPressed;
            public bool MenuPressed;
            public bool Grasped;
            public bool TouchpadPressed;
            public bool TouchpadTouched;
            public Vector2 TouchpadPosition;
            public bool ThumbstickPressed;
            public Vector2 ThumbstickPosition;

            public bool GetButtonValue(MotionControllerButtonTypes t) {
                switch(t) {
                    case MotionControllerButtonTypes.Trigger:
                        return SelectPressed;
                    case MotionControllerButtonTypes.Menu:
                        return MenuPressed;
                    case MotionControllerButtonTypes.Grip:
                        return Grasped;
                    case MotionControllerButtonTypes.ThumbstickPressed:
                        return ThumbstickPressed;
                    case MotionControllerButtonTypes.TouchpadPressed:
                        return TouchpadPressed;
                    case MotionControllerButtonTypes.TouchpadTouched:
                        return TouchpadTouched;

                    case MotionControllerButtonTypes.ButtonOne:
                        return TouchpadPressed;
                    case MotionControllerButtonTypes.ButtonTwo:
                        return MenuPressed;

                    default:
                        Debug.Assert(false, "do not reach");
                        return false;
                }
            }

            // for optimize GC
            readonly static InteractionSourceState[] _cache = new InteractionSourceState[3];
            public static ControllerState Current(uint index) {
                // MixedRealityCameraParent_VRTK/MotionController에 등록한 순서로 id가 붙더라
                // index 0 = left
                // index 1 = right
                // state.source.id는 진짜 id스럽다
                // 하지만 index는 0~1
                // 둘을 구분해야한다
                var leftID = SDK_WindowsMRControllerManager.Instance.LeftMotionControllerID;
                var rightID = SDK_WindowsMRControllerManager.Instance.RightMotionControllerID;
                var expectedID = (index == 0) ? leftID : rightID;

                var count = InteractionManager.GetCurrentReading(_cache);
                for (int i = 0; i < count; i++) {
                    var state = _cache[i];
                    if (state.source.kind != InteractionSourceKind.Controller) {
                        continue;
                    }
                    if(state.source.id != expectedID) {
                        continue;
                    }

                    return new ControllerState()
                    {
                        IsValid = true,
                        SelectPressedAmount = state.selectPressedAmount,
                        SelectPressed = state.selectPressed,
                        MenuPressed = state.menuPressed,
                        Grasped = state.grasped,
                        TouchpadPressed = state.touchpadPressed,
                        TouchpadTouched = state.touchpadTouched,
                        TouchpadPosition = state.touchpadPosition,
                        ThumbstickPressed = state.thumbstickPressed,
                        ThumbstickPosition = state.thumbstickPosition,
                    };
                }

                return new ControllerState()
                {
                    IsValid = false,
                };
            }
        }

        ControllerState[] currStates = null;
        ControllerState[] prevStates = null;

        private void Awake() {
            // InteractionSourceHandedness는  Unknown = 0, Left = 1, Right = 2
            currStates = new ControllerState[3];
            prevStates = new ControllerState[3];
            for (int i = 0; i < 3; i++) {
                currStates[i] = new ControllerState();
                prevStates[i] = new ControllerState();
            }
        }

        public override Transform GenerateControllerPointerOrigin(GameObject parent) {
            // oculus 구현을 가져옴
            return null;
        }

        public override Vector3 GetAngularVelocityOnIndex(uint index) {
            // TODO
            return Vector3.zero;
        }

        public override GameObject GetControllerByIndex(uint index, bool actual = false) {
            SetTrackedControllerCaches();
            var sdkManager = VRTK_SDKManager.instance;
            if (sdkManager != null) {
                if (cachedLeftController != null && cachedLeftController.index == index) {
                    return (actual ? sdkManager.actualLeftController : sdkManager.scriptAliasLeftController);
                }

                if (cachedRightController != null && cachedRightController.index == index) {
                    return (actual ? sdkManager.actualRightController : sdkManager.scriptAliasRightController);
                }
            }
            return null;
        }

        public override string GetControllerDefaultColliderPath(ControllerHand hand) {
            var returnCollider = "ControllerColliders/Fallback";
            switch (VRTK_DeviceFinder.GetHeadsetType(true)) {
                case VRTK_DeviceFinder.Headsets.OculusRift:
                    returnCollider = (hand == ControllerHand.Left ? "ControllerColliders/SteamVROculusTouch_Left" : "ControllerColliders/SteamVROculusTouch_Right");
                    break;
                case VRTK_DeviceFinder.Headsets.Vive:
                    returnCollider = "ControllerColliders/HTCVive";
                    break;
            }
            return returnCollider;
        }

        public override string GetControllerElementPath(ControllerElements element, ControllerHand hand, bool fullPath = false) {
            // TODO ???
            return null;
        }

        public override uint GetControllerIndex(GameObject controller) {
            VRTK_TrackedController trackedObject = GetTrackedObject(controller);
            return (trackedObject != null ? trackedObject.index : uint.MaxValue);
        }

        public override GameObject GetControllerLeftHand(bool actual = false) {
            var controller = GetSDKManagerControllerLeftHand(actual);
            if (!controller && actual) {
                controller = VRTK_SharedMethods.FindEvenInactiveGameObject<Transform>("MotionControllers/LeftController");
            }
            return controller;
        }

        public override GameObject GetControllerModel(GameObject controller) {
            return GetControllerModelFromController(controller);
        }

        public override GameObject GetControllerModel(ControllerHand hand) {
            var model = GetSDKManagerControllerModelForHand(hand);
            if (!model) {
                GameObject controller = null;
                switch (hand) {
                    case ControllerHand.Left:
                        controller = GetControllerLeftHand(true);
                        break;
                    case ControllerHand.Right:
                        controller = GetControllerRightHand(true);
                        break;
                }

                if (controller != null) {
                    model = (model != null && model.transform.childCount > 0 ? model.transform.GetChild(0).gameObject : null);
                }
            }
            return model;
        }

        public override Transform GetControllerOrigin(GameObject controller) {
            return VRTK_SDK_Bridge.GetPlayArea();
        }

        public override GameObject GetControllerRenderModel(GameObject controller) {
            // TODO ?
            return null;
        }

        public override GameObject GetControllerRightHand(bool actual = false) {
            var controller = GetSDKManagerControllerRightHand(actual);
            if (!controller && actual) {
                controller = VRTK_SharedMethods.FindEvenInactiveGameObject<Transform>("MotionControllers/RightController");
            }
            return controller;
        }

        public override Vector2 GetGripAxisOnIndex(uint index) {
            var state = currStates[index];
            if (state.Grasped) {
                return new Vector2(1, 0);
            } else {
                return Vector2.zero;
            }
        }

        public override float GetGripHairlineDeltaOnIndex(uint index) {
            // TODO ???
            return 0.0f;
        }

        public override SDK_ControllerHapticModifiers GetHapticModifiers() {
            var modifiers = new SDK_ControllerHapticModifiers();
            //modifiers.durationModifier = 0.8f;
            //modifiers.intervalModifier = 1f;
            return modifiers;
        }

        public override Vector2 GetTouchpadAxisOnIndex(uint index) {
            var state = currStates[index];
            var v = state.TouchpadPosition;
            return v;
        }

        public override Vector2 GetTriggerAxisOnIndex(uint index) {
            var state = currStates[index];
            var v = state.SelectPressedAmount;
            return new Vector2(v, 0);
        }

        public override float GetTriggerHairlineDeltaOnIndex(uint index) {
            // TODO ???
            return 0.0f;
        }

        public override Vector3 GetVelocityOnIndex(uint index) {
            // TODO 속도?
            return Vector3.zero;
        }

        public override void HapticPulseOnIndex(uint index, float strength = 0.5F) {
            // TODO 진동 구현
        }

        public override bool IsButtonOnePressedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressDown, MotionControllerButtonTypes.ButtonOne);
        }

        public override bool IsButtonOnePressedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Press, MotionControllerButtonTypes.ButtonOne);
        }

        public override bool IsButtonOnePressedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressUp, MotionControllerButtonTypes.ButtonOne);
        }

        public override bool IsButtonOneTouchedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchDown, MotionControllerButtonTypes.ButtonOne);
        }

        public override bool IsButtonOneTouchedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Touch, MotionControllerButtonTypes.ButtonOne);
        }

        public override bool IsButtonOneTouchedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchUp, MotionControllerButtonTypes.ButtonOne);
        }

        public override bool IsButtonTwoPressedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressDown, MotionControllerButtonTypes.ButtonTwo);
        }

        public override bool IsButtonTwoPressedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Press, MotionControllerButtonTypes.ButtonTwo);
        }

        public override bool IsButtonTwoPressedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressUp, MotionControllerButtonTypes.ButtonTwo);
        }

        public override bool IsButtonTwoTouchedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchDown, MotionControllerButtonTypes.ButtonTwo);
        }

        public override bool IsButtonTwoTouchedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Touch, MotionControllerButtonTypes.ButtonTwo);
        }

        public override bool IsButtonTwoTouchedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchUp, MotionControllerButtonTypes.ButtonTwo);
        }

        public override bool IsControllerLeftHand(GameObject controller) {
            return CheckActualOrScriptAliasControllerIsLeftHand(controller);
        }

        public override bool IsControllerLeftHand(GameObject controller, bool actual) {
            return CheckControllerLeftHand(controller, actual);
        }

        public override bool IsControllerRightHand(GameObject controller) {
            return CheckActualOrScriptAliasControllerIsRightHand(controller);
        }

        public override bool IsControllerRightHand(GameObject controller, bool actual) {
            return CheckControllerRightHand(controller, actual);
        }

        public override bool IsGripPressedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressDown, MotionControllerButtonTypes.Grip);
        }

        public override bool IsGripPressedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Press, MotionControllerButtonTypes.Grip);
        }

        public override bool IsGripPressedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressUp, MotionControllerButtonTypes.Grip);
        }

        public override bool IsGripTouchedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchDown, MotionControllerButtonTypes.Grip);
        }

        public override bool IsGripTouchedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Touch, MotionControllerButtonTypes.Grip);
        }

        public override bool IsGripTouchedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchUp, MotionControllerButtonTypes.Grip);
        }

        public override bool IsHairGripDownOnIndex(uint index) {
            return false;
        }

        public override bool IsHairGripUpOnIndex(uint index) {
            return false;
        }

        public override bool IsHairTriggerDownOnIndex(uint index) {
            // TODO ???
            return false;
        }

        public override bool IsHairTriggerUpOnIndex(uint index) {
            // TODO ???
            return false;
        }

        public override bool IsStartMenuPressedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressDown, MotionControllerButtonTypes.Menu);
        }

        public override bool IsStartMenuPressedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Press, MotionControllerButtonTypes.Menu);
        }

        public override bool IsStartMenuPressedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressUp, MotionControllerButtonTypes.Menu);
        }

        public override bool IsStartMenuTouchedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchDown, MotionControllerButtonTypes.Menu);
        }

        public override bool IsStartMenuTouchedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Touch, MotionControllerButtonTypes.Menu);
        }

        public override bool IsStartMenuTouchedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchUp, MotionControllerButtonTypes.Menu);
        }

        public override bool IsTouchpadPressedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressDown, MotionControllerButtonTypes.TouchpadPressed);
        }

        public override bool IsTouchpadPressedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Press, MotionControllerButtonTypes.TouchpadPressed);
        }

        public override bool IsTouchpadPressedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressUp, MotionControllerButtonTypes.TouchpadPressed);
        }

        public override bool IsTouchpadTouchedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchDown, MotionControllerButtonTypes.TouchpadTouched);
        }

        public override bool IsTouchpadTouchedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Touch, MotionControllerButtonTypes.TouchpadTouched);
        }

        public override bool IsTouchpadTouchedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchUp, MotionControllerButtonTypes.TouchpadTouched);
        }

        public override bool IsTriggerPressedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressDown, MotionControllerButtonTypes.Trigger);
        }

        public override bool IsTriggerPressedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Press, MotionControllerButtonTypes.Trigger);
        }

        public override bool IsTriggerPressedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.PressUp, MotionControllerButtonTypes.Trigger);
        }

        public override bool IsTriggerTouchedDownOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchDown, MotionControllerButtonTypes.Trigger);
        }

        public override bool IsTriggerTouchedOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.Touch, MotionControllerButtonTypes.Trigger);
        }

        public override bool IsTriggerTouchedUpOnIndex(uint index) {
            return IsButtonPressed(index, ButtonPressTypes.TouchUp, MotionControllerButtonTypes.Trigger);
        }

        public override void ProcessFixedUpdate(uint index, Dictionary<string, object> options) {
        }

        public override void ProcessUpdate(uint index, Dictionary<string, object> options) {
            ControllerState curr = ControllerState.Current(index);
            prevStates[index] = currStates[index];
            currStates[index] = curr;
        }

        public override void SetControllerRenderModelWheel(GameObject renderModel, bool state) {
        }

        private bool IsButtonPressed(uint index, ButtonPressTypes type, MotionControllerButtonTypes button) {
            var currState = currStates[index];
            var prevState = prevStates[index];

            if(!currState.IsValid) {
                return false;
            }

            var curr = currState.GetButtonValue(button);
            var prev = prevState.GetButtonValue(button);
            
            switch(type) {
                case ButtonPressTypes.Press:
                case ButtonPressTypes.Touch:
                    return curr;
                case ButtonPressTypes.PressDown:
                case ButtonPressTypes.TouchDown:
                    return (prev == false && curr == true);
                case ButtonPressTypes.PressUp:
                case ButtonPressTypes.TouchUp:
                    return (prev == true && curr == false);
            }
            return false;
        }

        private void SetTrackedControllerCaches(bool forceRefresh = false) {
            if (forceRefresh) {
                cachedLeftController = null;
                cachedRightController = null;
            }

            var sdkManager = VRTK_SDKManager.instance;
            if (sdkManager != null) {
                if (cachedLeftController == null && sdkManager.actualLeftController) {
                    cachedLeftController = sdkManager.actualLeftController.GetComponent<VRTK_TrackedController>();
                    cachedLeftController.index = 0;
                }
                if (cachedRightController == null && sdkManager.actualRightController) {
                    cachedRightController = sdkManager.actualRightController.GetComponent<VRTK_TrackedController>();
                    cachedRightController.index = 1;
                }
            }
        }

        private VRTK_TrackedController GetTrackedObject(GameObject controller) {
            SetTrackedControllerCaches();
            VRTK_TrackedController trackedObject = null;

            if (IsControllerLeftHand(controller)) {
                trackedObject = cachedLeftController;
            } else if (IsControllerRightHand(controller)) {
                trackedObject = cachedRightController;
            }
            return trackedObject;
        }
#endif
    }
}