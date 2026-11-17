// System.IO.Ports only exists with Player > Other Settings > Api Compatibility Level = ".NET Framework".
// With ".NET Standard 2.1" this component compiles to a stub that logs a warning, so the
// keyboard test still works. The Reference House Energy Corner setup applies the required API level.
#if NET_4_6 || NET_UNITY_4_8
#define EC_SERIAL_SUPPORTED
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
#if EC_SERIAL_SUPPORTED
using System.IO;
using System.IO.Ports;
#endif

namespace EnergyCorner
{
    /// <summary>
    /// USB serial link to the Energy Corner Pico.
    ///
    /// A background thread finds the port (HELLO handshake), reads bytes and splits them
    /// into lines, and writes queued commands. Lines go through a thread-safe queue and
    /// are parsed and dispatched on the main thread in Update(), so all events below
    /// fire on the main thread. If the device disappears, or goes silent for 3.5 s
    /// (it sends a heartbeat every 1 s), the port is closed and retried every 2 s.
    /// </summary>
    public class EnergyCornerSerial : MonoBehaviour
    {
        // ---------------- Events (main thread) ----------------
        public event Action<bool> OnPresenceChanged;
        public event Action<int> OnButton;
        public event Action<bool> OnSensorFault;
        /// <summary>One per measurement while DEBUG 1: rawCm, filteredCm, validRatePct, hits, win, baseline (-1 = no echo).</summary>
        public event Action<int, int, int, int, int, int> OnDebug;
        public event Action<bool> OnConnectionChanged;
        public event Action<string> OnLine;               // every received line (raw)

        // ---------------- Status (main thread) ----------------
        public bool IsConnected { get; private set; }
        public string PortName => portName;
        public string Status => status;
        public bool Presence { get; private set; }
        public bool SensorFault { get; private set; }
        // Last D line (-1 = no echo / unknown)
        public int LastRaw { get; private set; } = -1;
        public int LastFiltered { get; private set; } = -1;
        public int LastValidRate { get; private set; } = -1;
        public int LastHits { get; private set; } = -1;
        public int LastWin { get; private set; } = -1;
        /// <summary>Calibrated empty-space distance in cm; 0 = "empty gives no echo"; -1 = unknown.</summary>
        public int Baseline { get; private set; } = -1;
        /// <summary>Time.unscaledTime of the last received line.</summary>
        public float LastLineTime { get; private set; } = -1f;
        /// <summary>Values from the last "SETTINGS ..." reply (send "GET").</summary>
        public IReadOnlyDictionary<string, string> DeviceSettings => deviceSettings;
        /// <summary>Recent traffic for the debug overlay ("<" received, ">" sent). STATE and D lines are left out.</summary>
        public IEnumerable<string> RecentMessages => recent;

        const string HelloReply = "HELLO DT-ENERGY v1";
        const int RecentMax = 40;

        readonly Dictionary<string, string> deviceSettings = new Dictionary<string, string>();
        readonly Queue<string> recent = new Queue<string>();
        struct ReceivedLine
        {
            public int generation;
            public string text;
        }
        readonly ConcurrentQueue<ReceivedLine> inbox = new ConcurrentQueue<ReceivedLine>();
        readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);

        // Shared with the background thread
        volatile bool running;
        volatile bool threadConnected;
        volatile string portName = "-";
        volatile string status = "stopped";

        Thread thread;
        string configuredPort;
        int baudRate;
        int runGeneration, connectionGeneration, observedConnectionGeneration;

        bool IsRunning(int session) => running && Volatile.Read(ref runGeneration) == session;

        // ======================================================================
        // Public API (main thread)
        // ======================================================================

        public void SetButtonLed(int n, bool on) => Send("LED " + n + " " + (on ? 1 : 0));
        public void SetAllLeds(bool on) => Send("LED ALL " + (on ? 1 : 0));
        public void SetOutput(bool on) => Send("OUT " + (on ? 1 : 0));
        public void Calibrate() => Send("CAL");
        public void SetDebugStream(bool on) => Send("DEBUG " + (on ? 1 : 0));
        public void RequestSettings() => Send("GET");
        public void SetParameter(string key, int value) => Send("SET " + key + " " + value);

        /// <summary>Queues one command line. Dropped (returns false) when not connected.</summary>
        public bool Send(string line)
        {
            if (!IsConnected || string.IsNullOrEmpty(line)) return false;
            outbox.Enqueue(line.Trim());
            AddRecent("> " + line.Trim());
            return true;
        }

        // ======================================================================
        // Unity lifecycle
        // ======================================================================

        void OnEnable()
        {
            var cfg = EnergyCornerConfig.Current;
            configuredPort = (cfg.comPort ?? "auto").Trim();
            baudRate = cfg.baudRate;

            if (configuredPort.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                status = "disabled in config";
                Debug.Log("[EC] Serial disabled (comPort = none). Keyboard only.");
                return;
            }
#if EC_SERIAL_SUPPORTED
            int session = Interlocked.Increment(ref runGeneration);
            running = true;
            thread = new Thread(() => ThreadMain(session)) { IsBackground = true, Name = "EnergyCornerSerial" };
            thread.Start();
#else
            status = "unavailable: set Api Compatibility Level to .NET Framework";
            Debug.LogWarning("[EC] Serial unavailable: Player Settings > Api Compatibility Level must be '.NET Framework'.");
#endif
        }

        void OnDisable()
        {
            running = false;
            Interlocked.Increment(ref runGeneration);
            wake.Set();
            if (thread != null && thread.IsAlive) thread.Join(2000);
            thread = null;
            threadConnected = false;
            status = "stopped";
            if (IsConnected) SetConnected(false);
            while (inbox.TryDequeue(out _)) { }
            while (outbox.TryDequeue(out _)) { }
        }

        void Update()
        {
            // Connection edges
            bool c = threadConnected;
            int generation = Volatile.Read(ref connectionGeneration);
            if (generation != observedConnectionGeneration)
            {
                if (IsConnected) SetConnected(false);
                observedConnectionGeneration = generation;
            }
            if (c != IsConnected) SetConnected(c);

            // Never dispatch stale P/B lines from a connection that has already ended.
            while (inbox.TryDequeue(out var line))
                if (threadConnected && c && line.generation == generation) HandleLine(line.text);
        }

        void SetConnected(bool c)
        {
            IsConnected = c;
            if (c)
            {
                Debug.Log("[EC] Serial connected on " + portName);
                SetFault(false);       // the device repeats ERR SENSOR every second if still faulted
                deviceSettings.Clear();
                Send("GET");
            }
            else
            {
                Debug.LogWarning("[EC] Serial disconnected (" + portName + ")");
                // Without a device we cannot know someone is there: drop presence so the
                // idle timer can return to Attract instead of staying stuck in Interactive.
                SetPresence(false);
                LastRaw = LastFiltered = LastValidRate = LastHits = -1;
            }
            OnConnectionChanged?.Invoke(c);
        }

        // ======================================================================
        // Parsing (main thread)
        // ======================================================================

        void HandleLine(string line)
        {
            LastLineTime = Time.unscaledTime;
            OnLine?.Invoke(line);

            bool isNoise = line.StartsWith("D ") || line.StartsWith("STATE ") || line == "OK";
            if (!isNoise) AddRecent("< " + line);

            if (line == "P") SetPresence(true);
            else if (line == "S") SetPresence(false);
            else if (line == "STATE P") SetPresence(true);     // heartbeat: only fires on a change
            else if (line == "STATE S") SetPresence(false);
            else if (line == "ERR SENSOR") SetFault(true);
            else if (line == "OK SENSOR") SetFault(false);
            else if (line.Length >= 2 && line[0] == 'B' && int.TryParse(line.Substring(1), out int b))
            {
                Debug.Log("[EC] Button " + b);
                if (b >= 1 && b <= 9) OnButton?.Invoke(b);
            }
            else if (line.StartsWith("D ")) ParseDebug(line);
            else if (line.StartsWith("CAL OK "))
            {
                // CAL OK <baseline> <baseRatePct>
                var p = line.Split(' ');
                if (p.Length >= 3 && int.TryParse(p[2], out int bl))
                {
                    Baseline = bl;
                    deviceSettings["baseline"] = p[2];
                }
                if (p.Length >= 4) deviceSettings["baseRate"] = p[3];
                Debug.Log("[EC] Calibration done: " + line + (Baseline == 0 ? "  (empty = no echo)" : ""));
            }
            else if (line.StartsWith("CAL FAIL")) Debug.LogWarning("[EC] Calibration failed: " + line);
            else if (line.StartsWith("SETTINGS ") || line.StartsWith("STATUS "))
            {
                ParseSettings(line);
                if (line.StartsWith("STATUS "))
                {
                    if (deviceSettings.TryGetValue("sensor", out var sensor)) SetFault(sensor == "FAULT");
                    if (deviceSettings.TryGetValue("present", out var present)) SetPresence(present == "1");
                }
            }
            else if (line.StartsWith("ERR")) Debug.LogWarning("[EC] Device error: " + line);
        }

        // D <rawCm|-1> <filteredCm|-1> <validRatePct> <hits>/<win> <baseline>
        void ParseDebug(string line)
        {
            var p = line.Split(' ');
            if (p.Length < 6) return;
            var hw = p[4].Split('/');
            if (int.TryParse(p[1], out int raw) && int.TryParse(p[2], out int filtered) &&
                int.TryParse(p[3], out int rate) && hw.Length == 2 &&
                int.TryParse(hw[0], out int hits) && int.TryParse(hw[1], out int win) &&
                int.TryParse(p[5], out int bl))
            {
                LastRaw = raw;
                LastFiltered = filtered;
                LastValidRate = rate;
                LastHits = hits;
                LastWin = win;
                Baseline = bl;
                OnDebug?.Invoke(raw, filtered, rate, hits, win, bl);
            }
        }

        // SETTINGS k=v k=v ...   /   STATUS k=v ...
        void ParseSettings(string line)
        {
            foreach (var part in line.Substring(line.IndexOf(' ') + 1).Split(' '))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) deviceSettings[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            if (deviceSettings.TryGetValue("baseline", out var s) && int.TryParse(s, out int bl)) Baseline = bl;
        }

        void SetPresence(bool p)
        {
            p = p && !SensorFault;
            if (p == Presence) return;
            Presence = p;
            Debug.Log("[EC] Serial presence " + (p ? "ON" : "OFF"));
            OnPresenceChanged?.Invoke(p);
        }

        void SetFault(bool f)
        {
            if (f == SensorFault) return;
            SensorFault = f;
            if (f) SetPresence(false);
            if (f) Debug.LogWarning("[EC] SENSOR FAULT reported by device");
            else Debug.Log("[EC] Sensor OK");
            OnSensorFault?.Invoke(f);
        }

        void AddRecent(string s)
        {
            recent.Enqueue(DateTime.Now.ToString("HH:mm:ss ") + s);
            while (recent.Count > RecentMax) recent.Dequeue();
        }

        // ======================================================================
        // Background thread
        // ======================================================================
#if EC_SERIAL_SUPPORTED
        void ThreadMain(int session)
        {
            while (IsRunning(session))
            {
                SerialPort port = null;
                try
                {
                    status = "searching...";
                    port = FindDevice(session);
                    if (port != null && IsRunning(session))
                    {
                        // Drop commands that were queued for a previous connection.
                        while (outbox.TryDequeue(out _)) { }
                        status = "connected";
                        int generation = Interlocked.Increment(ref connectionGeneration);
                        threadConnected = true;
                        ReadLoop(port, session, generation);
                    }
                }
                catch (Exception e)
                {
                    if (IsRunning(session))
                    {
                        status = "error: " + e.Message;
                        Debug.LogWarning("[EC] Serial: " + e.GetType().Name + ": " + e.Message);
                    }
                }
                finally
                {
                    if (Volatile.Read(ref runGeneration) == session) threadConnected = false;
                    SafeClose(port);
                }

                if (!IsRunning(session)) break;
                if (!status.StartsWith("error")) status = "not found, retrying";
                wake.WaitOne(2000); // retry every 2 s (woken early on shutdown)
            }
            if (Volatile.Read(ref runGeneration) == session) status = "stopped";
        }

        SerialPort FindDevice(int session)
        {
            bool auto = configuredPort.Equals("auto", StringComparison.OrdinalIgnoreCase) || configuredPort == "";
            IEnumerable<string> names = auto
                ? SerialPort.GetPortNames().Select(n => n.Trim()).Distinct().OrderBy(n => n.Length).ThenBy(n => n)
                : new[] { configuredPort };

            foreach (var name in names)
            {
                if (!IsRunning(session)) return null;
                status = "trying " + name;
                SerialPort port = null;
                try
                {
                    port = Open(name);
                    if (Handshake(port, session))
                    {
                        portName = name;
                        return port;
                    }
                }
                catch (Exception)
                {
                    // Port busy, not a serial device, unplugged mid-way... try the next one.
                }
                SafeClose(port);
            }
            return null;
        }

        SerialPort Open(string name)
        {
            // COM10 and above need the \\.\ prefix with Unity's Mono serial implementation.
            string sysName = name.Length > 4 && name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                ? @"\\.\" + name : name;
            var port = new SerialPort(sysName, baudRate)
            {
                DtrEnable = true,  // the Pico only sends while DTR is set
                RtsEnable = true,
                ReadTimeout = 50,  // short, so queued commands are written quickly
                WriteTimeout = 500,
                Encoding = Encoding.ASCII,
            };
            port.Open();
            return port;
        }

        bool Handshake(SerialPort port, int session)
        {
            var reader = new LineReader();
            var lines = new List<string>();
            var start = DateTime.UtcNow;
            var lastHello = DateTime.MinValue;

            while (IsRunning(session) && (DateTime.UtcNow - start).TotalMilliseconds < 1500)
            {
                if ((DateTime.UtcNow - lastHello).TotalMilliseconds > 500)
                {
                    port.Write("HELLO\n");
                    lastHello = DateTime.UtcNow;
                }
                lines.Clear();
                reader.Read(port, lines);
                foreach (var l in lines)
                    if (l == HelloReply) return true;
            }
            return false;
        }

        void ReadLoop(SerialPort port, int session, int generation)
        {
            var reader = new LineReader();
            var lines = new List<string>();
            var lastRx = DateTime.UtcNow;

            while (IsRunning(session))
            {
                while (outbox.TryDequeue(out var cmd)) port.Write(cmd + "\n");

                lines.Clear();
                if (reader.Read(port, lines) > 0) lastRx = DateTime.UtcNow;
                foreach (var l in lines) inbox.Enqueue(new ReceivedLine { generation = generation, text = l });

                if ((DateTime.UtcNow - lastRx).TotalMilliseconds > 3500)
                    throw new IOException("no data for 3.5 s (device gone?)");
            }
        }

        static void SafeClose(SerialPort port)
        {
            if (port == null) return;
            try { if (port.IsOpen) port.Close(); } catch (Exception) { }
            try { port.Dispose(); } catch (Exception) { }
        }

        /// <summary>
        /// Splits incoming bytes into lines. We do not use SerialPort.ReadLine because
        /// Mono's version can lose partial lines on timeout.
        /// </summary>
        class LineReader
        {
            readonly byte[] buffer = new byte[512];
            readonly StringBuilder current = new StringBuilder();

            /// <summary>Reads what is available (waits up to ReadTimeout). Returns the byte count.</summary>
            public int Read(SerialPort port, List<string> lines)
            {
                int n;
                try { n = port.Read(buffer, 0, buffer.Length); }
                catch (TimeoutException) { return 0; }

                for (int i = 0; i < n; i++)
                {
                    char c = (char)buffer[i];
                    if (c == '\n')
                    {
                        var line = current.ToString().Trim();
                        current.Length = 0;
                        if (line.Length > 0) lines.Add(line);
                    }
                    else if (c != '\r' && current.Length < 256)
                    {
                        current.Append(c);
                    }
                }
                return n;
            }
        }
#endif
    }
}
