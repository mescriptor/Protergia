#!/usr/bin/env python3
"""
ec_serial_test.py - talk to the Energy Corner Pico without Unity.

    pip install pyserial
    python ec_serial_test.py            # auto-detect (sends HELLO to every COM port)
    python ec_serial_test.py COM5       # use this port
    python ec_serial_test.py --list     # list COM ports
    python ec_serial_test.py --raw      # print every D line (default: max 2 per second;
                                        # the Unity overlay graph is better for aiming)

Prints everything the Pico sends, with a timestamp. Type a command and press Enter:
    HELLO | CAL | GET | DEBUG 1 | LED 1 1 | LED ALL 0 | OUT 1
    SET <threshold|minCm|maxCm|win|hitsNeeded|hitsRelease|enterMs|exitMs|pingMs> <value>
Type  q  (or Ctrl+C) to quit. Reconnects automatically if the Pico is unplugged.
"""
import argparse
import sys
import threading
import time

try:
    import serial
    import serial.tools.list_ports
except ImportError:
    sys.exit("pyserial is missing: run  pip install pyserial")

HELLO_REPLY = "HELLO DT-ENERGY"
BAUD = 115200


def log(msg):
    print(time.strftime("%H:%M:%S ") + msg, flush=True)


def open_port(name):
    # dsrdtr/rtscts off; DTR is set high by default, which the Pico needs to send.
    return serial.Serial(name, BAUD, timeout=0.2, write_timeout=1)


def handshake(port):
    """Send HELLO and wait up to 1.5 s for the HELLO DT-ENERGY reply."""
    deadline = time.time() + 1.5
    last_hello = 0
    while time.time() < deadline:
        if time.time() - last_hello > 0.5:
            port.write(b"HELLO\n")
            last_hello = time.time()
        line = port.readline().decode("ascii", "replace").strip()
        if line.startswith(HELLO_REPLY):
            return line
    return None


def find_device(forced):
    names = [forced] if forced else [p.device for p in serial.tools.list_ports.comports()]
    for name in names:
        try:
            port = open_port(name)
        except (serial.SerialException, OSError):
            continue
        try:
            reply = handshake(port)
            if reply:
                log(f"connected to {name}: {reply}")
                return port
        except (serial.SerialException, OSError):
            pass
        port.close()
    return None


class Link:
    def __init__(self, forced, raw):
        self.forced = forced
        self.raw = raw
        self.port = None
        self.lock = threading.Lock()
        self.running = True
        self.last_d = 0.0

    def reader(self):
        while self.running:
            if self.port is None:
                port = find_device(self.forced)
                if port is None:
                    log("Pico not found, retrying in 2 s...")
                    time.sleep(2)
                    continue
                with self.lock:
                    self.port = port
            try:
                line = self.port.readline().decode("ascii", "replace").strip()
            except (serial.SerialException, OSError) as e:
                log(f"port lost ({e}), reconnecting...")
                with self.lock:
                    try:
                        self.port.close()
                    except Exception:
                        pass
                    self.port = None
                continue
            if not line:
                continue
            if line.startswith("D ") and not self.raw:
                now = time.time()
                if now - self.last_d < 0.5:
                    continue
                self.last_d = now
                # D <rawCm|-1> <filteredCm|-1> <validRatePct> <hits>/<win> <baseline>
                parts = line.split()
                if len(parts) >= 6:
                    cm = lambda v: "no echo" if v == "-1" else v + " cm"
                    line = (f"D  raw={cm(parts[1])}  filtered={cm(parts[2])}  echo={parts[3]}%  "
                            f"hits={parts[4]}  baseline={parts[5]}")
            log("< " + line)

    def send(self, text):
        with self.lock:
            if self.port is None:
                log("! not connected")
                return
            try:
                self.port.write((text + "\n").encode("ascii"))
                log("> " + text)
            except (serial.SerialException, OSError) as e:
                log(f"! write failed: {e}")


def main():
    ap = argparse.ArgumentParser(description="Energy Corner Pico serial test")
    ap.add_argument("port", nargs="?", help="COM port, e.g. COM5 (default: auto-detect)")
    ap.add_argument("--list", action="store_true", help="list serial ports and exit")
    ap.add_argument("--raw", action="store_true", help="print every D line")
    args = ap.parse_args()

    if args.list:
        for p in serial.tools.list_ports.comports():
            print(f"{p.device:8} {p.description}  [{p.hwid}]")
        return

    link = Link(args.port, args.raw)
    threading.Thread(target=link.reader, daemon=True).start()
    print(__doc__.split("Prints")[1].strip(), "\n")

    try:
        while True:
            cmd = input().strip()
            if cmd.lower() in ("q", "quit", "exit"):
                break
            if cmd:
                link.send(cmd)
    except (KeyboardInterrupt, EOFError):
        pass
    link.running = False


if __name__ == "__main__":
    main()
