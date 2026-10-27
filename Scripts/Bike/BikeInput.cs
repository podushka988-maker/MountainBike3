using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BikeGame
{
    /// <summary>
    /// Собирает управление с клавиатуры и экранных кнопок:
    ///   Pedal (0..1), Brake (0..1), Lean (-1 = наклон назад, +1 = вперёд) и переключения передач.
    /// Клавиатура: W/↑ — педали, S/↓ — тормоз, A/← — наклон назад, D/→ — наклон вперёд,
    ///             + (или =, + на цифровой клавиатуре, E) — передача тяжелее, − (или − на цифровой клавиатуре, Q) — легче,
    ///             пробел / J — прыжок (банни-хоп).
    /// Работает и со старой, и с новой системой ввода Unity.
    /// </summary>
    public class BikeInput : MonoBehaviour
    {
        [Header("Экранные кнопки (необязательно)")]
        public TouchButton pedalButton;
        public TouchButton brakeButton;
        public TouchButton leanBackButton;
        public TouchButton leanForwardButton;
        [Tooltip("Передача тяжелее (+).")]
        public TouchButton gearUpButton;
        [Tooltip("Передача легче (−).")]
        public TouchButton gearDownButton;
        [Tooltip("Прыжок (банни-хоп).")]
        public TouchButton jumpButton;

        public float Pedal { get; private set; }
        public float Brake { get; private set; }
        public float Lean { get; private set; }

        /// <summary>Выключается при падении.</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>Автопилот (гонщик в главном меню): управление задаёт скрипт через SetAuto, клавиатура и кнопки не читаются.</summary>
        public bool Autopilot { get; set; }
        private float _autoPedal, _autoBrake, _autoLean;

        /// <summary>Управление от автопилота: педали, тормоз (0…1), наклон (−1…1), прыжок.</summary>
        public void SetAuto(float pedal, float brake, float lean, bool jump)
        {
            _autoPedal = pedal; _autoBrake = brake; _autoLean = lean;
            if (jump) _jump = true;
        }

        private int _shift;   // накопленные нажатия: +N — тяжелее, −N — легче
        private bool _jump;   // нажали «прыжок» (забирает физика)

        /// <summary>Нажат ли прыжок с прошлого раза (и сбросить).</summary>
        public bool ConsumeJump()
        {
            bool j = _jump;
            _jump = false;
            return j;
        }

        /// <summary>Забирает накопленные переключения передач (+ — тяжелее, − — легче) и обнуляет их.</summary>
        public int ConsumeShift()
        {
            int s = _shift;
            _shift = 0;
            return s;
        }

        private void OnEnable()
        {
            if (gearUpButton != null) gearUpButton.Pressed += OnGearUp;
            if (gearDownButton != null) gearDownButton.Pressed += OnGearDown;
            if (jumpButton != null) jumpButton.Pressed += OnJump;
        }

        private void OnDisable()
        {
            if (gearUpButton != null) gearUpButton.Pressed -= OnGearUp;
            if (gearDownButton != null) gearDownButton.Pressed -= OnGearDown;
            if (jumpButton != null) jumpButton.Pressed -= OnJump;
        }

        private void OnGearUp() { if (InputEnabled) _shift++; }
        private void OnGearDown() { if (InputEnabled) _shift--; }
        private void OnJump() { if (InputEnabled) _jump = true; }

        private void Update()
        {
            if (!InputEnabled)
            {
                Pedal = Brake = Lean = 0f;
                _shift = 0;
                _jump = false;
                return;
            }

            if (Autopilot)
            {
                Pedal = _autoPedal; Brake = _autoBrake; Lean = _autoLean;
                return;
            }

            bool pedal = Held(pedalButton);
            bool brake = Held(brakeButton);
            bool back = Held(leanBackButton);
            bool fwd = Held(leanForwardButton);
            bool up = false, down = false;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                pedal |= kb.wKey.isPressed || kb.upArrowKey.isPressed;
                brake |= kb.sKey.isPressed || kb.downArrowKey.isPressed;
                back |= kb.aKey.isPressed || kb.leftArrowKey.isPressed;
                fwd |= kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                up = kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame;
                down = kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame;
                if (kb.spaceKey.wasPressedThisFrame || kb.jKey.wasPressedThisFrame) _jump = true;
            }
#else
            pedal |= Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            brake |= Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            back |= Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            fwd |= Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            up = Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.E);
            down = Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Q);
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.J)) _jump = true;
#endif

            Pedal = pedal ? 1f : 0f;
            Brake = brake ? 1f : 0f;
            Lean = (fwd ? 1f : 0f) - (back ? 1f : 0f);
            if (up) _shift++;
            if (down) _shift--;
        }

        private static bool Held(TouchButton b) => b != null && b.IsHeld;
    }
}
