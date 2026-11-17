using UnityEngine;
using UnityEngine.Events;

namespace EnergyCorner
{
    public enum CornerState
    {
        Attract,      // videos loop
        Interactive,  // house scene + call to action
        Product       // popup for product n (see CurrentProduct)
    }

    /// <summary>
    /// The state machine of the Energy Corner.
    ///
    ///   Attract --presence--> Interactive --button n--> Product(n)
    ///   Product(n) --same button n / popup timeout--> Interactive
    ///   Product(n) --other button m--> Product(m)
    ///   Interactive/Product --no presence AND no press for idleTimeoutSec--> Attract
    ///
    /// Hook animations to the UnityEvents in the Inspector; this script does not need to change.
    ///
    /// Product is a sub-state of Interactive (the house stays up behind the popup):
    ///   onEnterInteractive  fires once when the house scene comes up (leaving Attract),
    ///   onExitInteractive   fires once when it goes away (returning to Attract),
    ///   onEnterProduct(n) / onExitProduct(n) fire around each popup.
    /// Exit events always fire before enter events. Example, Product(2) -> Attract:
    ///   onExitProduct(2), onExitInteractive, onEnterAttract.
    /// </summary>
    public class EnergyCornerController : MonoBehaviour
    {
        [Header("Inputs (either can be empty)")]
        public EnergyCornerSerial serial;
        public KeyboardInputSource keyboard;

        [Header("State events")]
        public UnityEvent onEnterAttract = new UnityEvent();
        public UnityEvent onExitAttract = new UnityEvent();
        public UnityEvent onEnterInteractive = new UnityEvent();
        public UnityEvent onExitInteractive = new UnityEvent();
        public UnityEvent<int> onEnterProduct = new UnityEvent<int>();
        public UnityEvent<int> onExitProduct = new UnityEvent<int>();

        [Header("Input events (fire in every state)")]
        public UnityEvent<bool> onPresenceChanged = new UnityEvent<bool>();
        public UnityEvent<int> onButtonPressed = new UnityEvent<int>();
        public UnityEvent<bool> onSensorFault = new UnityEvent<bool>();

        public CornerState State { get; private set; } = CornerState.Attract;
        /// <summary>1-based product number while in Product, otherwise 0.</summary>
        public int CurrentProduct { get; private set; }
        public bool Presence { get; private set; }
        public bool SensorPresence { get; private set; }
        public bool IsSimulatingPresence { get; private set; }
        bool sensorFault;

        /// <summary>Seconds until the return to Attract (only counts down while nobody is present).</summary>
        public float IdleRemaining =>
            State == CornerState.Attract || Presence ? -1f : Mathf.Max(0f, cfg.idleTimeoutSec - (Now - lastActivity));

        EnergyCornerConfig cfg;
        float lastActivity;
        float productEnteredAt;

        static float Now => Time.unscaledTime;

        void Awake()
        {
            cfg = EnergyCornerConfig.Current;
        }

        void OnEnable()
        {
            if (serial != null)
            {
                serial.OnPresenceChanged += SetPresence;
                serial.OnButton += HandleButton;
                serial.OnSensorFault += HandleSensorFault;
                serial.OnConnectionChanged += HandleConnection;
            }
            if (keyboard != null)
            {
                keyboard.OnPresenceChanged += SetSimulatedPresence;
                keyboard.OnButton += HandleButton;
                keyboard.OnUseSensor += UseSensorPresence;
                keyboard.OnCloseProduct += CloseProduct;
            }
            if (serial != null)
            {
                sensorFault = serial.SensorFault;
                SetPresence(serial.IsConnected && !sensorFault && serial.Presence);
            }
        }

        void OnDisable()
        {
            if (serial != null)
            {
                serial.OnPresenceChanged -= SetPresence;
                serial.OnButton -= HandleButton;
                serial.OnSensorFault -= HandleSensorFault;
                serial.OnConnectionChanged -= HandleConnection;
            }
            if (keyboard != null)
            {
                keyboard.OnPresenceChanged -= SetSimulatedPresence;
                keyboard.OnButton -= HandleButton;
                keyboard.OnUseSensor -= UseSensorPresence;
                keyboard.OnCloseProduct -= CloseProduct;
            }
        }

        void Start()
        {
            // Let every listener initialise itself for the start state.
            lastActivity = Now;
            Debug.Log("[EC] State -> " + StateName + " (start)");
            if (State == CornerState.Attract) onEnterAttract.Invoke();
            else
            {
                onEnterInteractive.Invoke();
                if (State == CornerState.Product) onEnterProduct.Invoke(CurrentProduct);
            }
            ApplyOutputs();
        }

        void Update()
        {
            if (Presence) lastActivity = Now;  // standing on the sticker counts as activity

            if (State != CornerState.Attract && !Presence && Now - lastActivity >= cfg.idleTimeoutSec)
            {
                GoTo(CornerState.Attract);
                return;
            }

            if (State == CornerState.Product && cfg.popupTimeoutSec > 0f &&
                Now - productEnteredAt >= cfg.popupTimeoutSec)
            {
                GoTo(CornerState.Interactive);
            }
        }

        // ======================================================================
        // Inputs
        // ======================================================================

        /// <summary>Latest real sensor value. It continues updating during manual simulation.</summary>
        public void SetPresence(bool present)
        {
            SensorPresence = present && !sensorFault;
            if (!IsSimulatingPresence) ApplyPresence(SensorPresence);
        }

        /// <summary>P/S deliberately enter test mode; sensor heartbeats cannot silently cancel it.</summary>
        public void SetSimulatedPresence(bool present)
        {
            IsSimulatingPresence = true;
            Debug.Log("[EC] Presence source -> simulation (" + (present ? "present" : "absent") + ")");
            ApplyPresence(present);
        }

        /// <summary>F2 returns to the latest real sensor value, even if no new edge arrives.</summary>
        public void UseSensorPresence()
        {
            IsSimulatingPresence = false;
            Debug.Log("[EC] Presence source -> sensor (auto)");
            ApplyPresence(SensorPresence);
        }

        void ApplyPresence(bool present)
        {
            if (present == Presence) return;
            Presence = present;
            lastActivity = Now; // the idle countdown starts when presence is lost
            onPresenceChanged.Invoke(present);

            if (present && State == CornerState.Attract) GoTo(CornerState.Interactive);
            // Losing presence does nothing immediately: Update() resets after idleTimeoutSec,
            // so a visitor who steps off and back on within the timeout keeps their session.
        }

        public void CloseProduct()
        {
            if (State != CornerState.Product) return;
            lastActivity = Now;
            GoTo(CornerState.Interactive);
        }

        /// <summary>Button n (1-based) was pressed. Public so other inputs (e.g. touch) can call it.</summary>
        public void HandleButton(int n)
        {
            if (n < 1 || n > cfg.buttonCount)
            {
                Debug.LogWarning("[EC] Ignoring button " + n + " (buttonCount = " + cfg.buttonCount + ")");
                return;
            }

            lastActivity = Now; // a press counts as activity even when nobody is detected
            onButtonPressed.Invoke(n);

            switch (State)
            {
                case CornerState.Attract:
                    if (cfg.buttonWakesFromAttract) GoTo(CornerState.Product, n);
                    break;
                case CornerState.Interactive:
                    GoTo(CornerState.Product, n);
                    break;
                case CornerState.Product:
                    if (n == CurrentProduct) GoTo(CornerState.Interactive); // same button closes
                    else GoTo(CornerState.Product, n);                      // other button switches
                    break;
            }
        }

        void HandleSensorFault(bool fault)
        {
            sensorFault = fault;
            if (fault) SetPresence(false);
            onSensorFault.Invoke(fault);
        }

        void HandleConnection(bool connected)
        {
            if (!connected) SetPresence(false);
            else
            {
                sensorFault = serial.SensorFault;
                SetPresence(serial.Presence);
                ApplyOutputs();
            }
        }

        // ======================================================================
        // Transitions
        // ======================================================================

        /// <summary>Forces a state (also usable from UnityEvents / debug tools).</summary>
        public void GoTo(CornerState next, int product = 0)
        {
            if ((next != CornerState.Attract && next != CornerState.Interactive && next != CornerState.Product) ||
                (next == CornerState.Product && (product < 1 || product > cfg.buttonCount)))
            {
                Debug.LogWarning("[EC] Ignoring invalid state/product transition.");
                return;
            }
            if (next != CornerState.Product) product = 0;
            if (next == State && product == CurrentProduct) return;

            var previous = State;
            int previousProduct = CurrentProduct;
            bool houseWasUp = previous != CornerState.Attract;
            bool houseIsUp = next != CornerState.Attract;

            // Exits, innermost first
            if (previous == CornerState.Product) onExitProduct.Invoke(previousProduct);
            if (houseWasUp && !houseIsUp) onExitInteractive.Invoke();
            if (previous == CornerState.Attract) onExitAttract.Invoke();

            State = next;
            CurrentProduct = product;
            // Automatic popup closure must not extend the no-presence/no-input idle deadline.
            if (previous == CornerState.Attract) lastActivity = Now;
            if (next == CornerState.Product) productEnteredAt = Now;
            Debug.Log("[EC] State -> " + StateName);

            // Enters, outermost first
            if (next == CornerState.Attract) onEnterAttract.Invoke();
            if (!houseWasUp && houseIsUp) onEnterInteractive.Invoke();
            if (next == CornerState.Product) onEnterProduct.Invoke(product);

            ApplyOutputs();
        }

        /// <summary>"Attract", "Interactive" or "Product(n)".</summary>
        public string StateName => State == CornerState.Product ? "Product(" + CurrentProduct + ")" : State.ToString();

        // ======================================================================
        // Button LEDs and floor output
        // ======================================================================

        void ApplyOutputs()
        {
            if (!cfg.enableHardwareOutputs || serial == null || !serial.IsConnected) return;

            string mode;
            switch (State)
            {
                case CornerState.Attract: mode = cfg.leds.attract; break;
                case CornerState.Interactive: mode = cfg.leds.interactive; break;
                default: mode = cfg.leds.product; break;
            }
            mode = (mode ?? "off").Trim().ToLowerInvariant();

            if (mode == "all" || mode == "off")
            {
                serial.SetAllLeds(mode == "all");
            }
            else
            {
                // "selected" / "others": per-LED. Outside Product there is no selection,
                // so "selected" means all off and "others" means all on.
                for (int n = 1; n <= cfg.physicalButtonCount; n++)
                {
                    bool isSelected = n == CurrentProduct;
                    serial.SetButtonLed(n, mode == "selected" ? isSelected : !isSelected);
                }
            }

            serial.SetOutput(State == CornerState.Attract ? cfg.floorOutput.attract : cfg.floorOutput.interactive);
        }
    }
}
