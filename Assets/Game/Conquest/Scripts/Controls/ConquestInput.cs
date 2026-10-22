using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Conquest.Controls
{
    /// <summary>
    /// The one place gameplay reads input. Wraps the ConquestControls actions (keyboard/mouse and
    /// gamepad bindings from Data/ConquestControls.inputactions) and keeps the player's rebinds,
    /// which are saved to PlayerPrefs. Gameplay code asks for intents (fire, reload, look) and
    /// never polls a device directly.
    /// </summary>
    public static class ConquestInput
    {
        private const string OverridesKey = "Conquest_BindingOverrides_v1";
        /// <summary>Binding group (control scheme) of the keyboard and mouse bindings.</summary>
        public const string KeyboardMouseGroup = "KeyboardMouse";

        // Placeholder: right-stick turn speed in degrees per second at full deflection.
        public static float GamepadLookDegreesPerSecond = 180f;

        private static ConquestControls s_Controls;

        /// <summary>Raised after a rebind, a reset, or loading saved bindings, so hints can refresh.</summary>
        public static event Action BindingsChanged;

        public static ConquestControls.GameplayActions Gameplay => Controls.Gameplay;

        private static ConquestControls Controls
        {
            get
            {
                if (s_Controls == null || s_Controls.asset == null)
                {
                    s_Controls = new ConquestControls();
                    LoadOverrides();
                }
                // Anything that disabled the whole map (a rebind only disables one action) is undone here.
                if (!s_Controls.Gameplay.enabled) s_Controls.Gameplay.Enable();
                return s_Controls;
            }
        }

        // Domain reload is off in this project (Enter Play Mode options), so this static would
        // survive into the next play session after the Input System has torn down the old
        // actions' state, leaving them dead: no movement, no pause. Start every session fresh.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            if (s_Controls != null)
            {
                try
                {
                    s_Controls.Gameplay.Disable();
                    s_Controls.Dispose();
                }
                catch (Exception)
                {
                    // The old state may already be gone; it is being replaced either way.
                }
            }
            s_Controls = null;
            BindingsChanged = null;
        }

        public static Vector2 Move => Gameplay.Move.ReadValue<Vector2>();

        /// <summary>
        /// This frame's turn in degrees (x yaw, y pitch up). Mouse delta is already per frame, so it
        /// is only scaled by sensitivity; the stick is a rate, so it is scaled by frame time.
        /// </summary>
        public static Vector2 LookDegrees(float mouseSensitivity)
        {
            Vector2 mouse = Gameplay.Look.ReadValue<Vector2>() * mouseSensitivity;
            Vector2 stick = Gameplay.LookStick.ReadValue<Vector2>() * (GamepadLookDegreesPerSecond * Time.deltaTime);
            return mouse + stick;
        }

        public static bool FireHeld => Gameplay.Fire.IsPressed();
        public static bool FirePressed => Gameplay.Fire.WasPressedThisFrame();
        public static bool AimHeld => Gameplay.Aim.IsPressed();
        public static bool ReloadPressed => Gameplay.Reload.WasPressedThisFrame();
        public static bool SprintHeld => Gameplay.Sprint.IsPressed();
        public static bool JumpPressed => Gameplay.Jump.WasPressedThisFrame();
        public static bool DeployPressed => Gameplay.Deploy.WasPressedThisFrame();
        public static bool SwitchWeaponPressed => Gameplay.SwitchWeapon.WasPressedThisFrame();
        public static bool ScoreboardHeld => Gameplay.Scoreboard.IsPressed();
        public static bool PausePressed => Gameplay.Pause.WasPressedThisFrame();

        /// <summary>Short label of the keyboard/mouse binding for an action, for HUD hints such as "[R]".</summary>
        public static string KeyLabel(InputAction action)
        {
            if (action == null) return "";
            int index = action.GetBindingIndex(group: KeyboardMouseGroup);
            return index >= 0 ? action.GetBindingDisplayString(index) : "";
        }

        /// <summary>
        /// Listen for the next key or button and bind it to <paramref name="action"/>'s binding at
        /// <paramref name="bindingIndex"/>. Escape cancels. The action is disabled while listening.
        /// </summary>
        public static InputActionRebindingExtensions.RebindingOperation StartRebind(InputAction action, int bindingIndex, Action onFinished)
        {
            action.Disable();
            var operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .OnMatchWaitForAnother(0.1f);

            void Finish(InputActionRebindingExtensions.RebindingOperation op, bool saved)
            {
                op.Dispose();
                action.Enable();
                if (saved) SaveOverrides();
                onFinished?.Invoke();
            }

            operation.OnComplete(op => Finish(op, true));
            operation.OnCancel(op => Finish(op, false));
            return operation.Start();
        }

        public static void ResetAllBindings()
        {
            Controls.asset.RemoveAllBindingOverrides();
            SaveOverrides();
        }

        private static void SaveOverrides()
        {
            PlayerPrefs.SetString(OverridesKey, Controls.asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
            BindingsChanged?.Invoke();
        }

        private static void LoadOverrides()
        {
            string json = PlayerPrefs.GetString(OverridesKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                s_Controls.asset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConquestInput] Saved key bindings could not be read and were reset: {e.Message}");
                PlayerPrefs.DeleteKey(OverridesKey);
            }
            BindingsChanged?.Invoke();
        }
    }
}
