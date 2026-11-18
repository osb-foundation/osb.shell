"""VGA text-buffer + CRTC cursor capture for the golden harness (A2).

The serial transcript shows the bytes the console emits, but cursor placement
(the point of the "\\b"-based redraw) is only visible on the screen. These
helpers read the 80x25 text buffer at physical 0xB8000 and the CRTC cursor
registers (0x3D4/0x3D5) through the QEMU monitor, without disturbing the
guest.
"""

import re
import socket
import time

# VGA text mode: 80x25 cells of [char, attr] at physical 0xB8000.
VGA_BASE = 0xB8000
VGA_COLS = 80
VGA_ROWS = 25
VGA_BYTES = VGA_COLS * VGA_ROWS * 2


class Vga:
    def __init__(self, mon_path: str):
        self.mon_path = mon_path

    def mon(self, cmd: str) -> str:
        """Send one HMP command, return the reply (readline echo included)."""
        s = socket.socket(socket.AF_UNIX)
        try:
            for _ in range(100):
                try:
                    s.connect(self.mon_path)
                    break
                except OSError:
                    time.sleep(0.05)
            else:
                raise OSError("monitor connect failed")
            s.settimeout(0.8)
            # Drain the banner/prompt first: without this, the first recv can
            # return only the banner and the reply of the *previous* command
            # is read as this one's output (no fixed sleep needed).
            data = b""
            while not data.rstrip().endswith(b"(qemu)"):
                chunk = s.recv(65536)
                if not chunk:
                    break
                data += chunk
            s.sendall((cmd + "\n").encode())
            data = b""
            while True:
                try:
                    chunk = s.recv(65536)
                except OSError:
                    break
                if not chunk:
                    break
                data += chunk
                if data.rstrip().endswith(b"(qemu)"):
                    break
        finally:
            s.close()
        return data.decode("latin-1")

    def text(self) -> list:
        reply = self.mon("xp /%dbx 0x%x" % (VGA_BYTES, VGA_BASE))
        raw = bytearray()
        for line in reply.splitlines():
            m = re.match(r"^[0-9a-fA-F]{16}: (.*)$", line)
            if not m:
                continue
            for tok in re.findall(r"0x([0-9a-fA-F]{2})", m.group(1)):
                raw.append(int(tok, 16))
        # text cells are [char, attr]; keep the char of each cell
        chars = raw[0:VGA_BYTES:2][:VGA_COLS * VGA_ROWS]
        lines = []
        for r in range(VGA_ROWS):
            s = "".join(chr(b) if 32 <= b < 127 else "."
                        for b in chars[r * VGA_COLS:(r + 1) * VGA_COLS])
            lines.append(s.rstrip())
        return lines

    def crtc(self):
        self.mon("o /b 0x3d4 0x0e")            # cursor location, high
        hi = self.mon("i /b 0x3d5")
        self.mon("o /b 0x3d4 0x0f")            # cursor location, low
        lo = self.mon("i /b 0x3d5")
        mh = re.search(r"portb\[0x03d5\] = 0x([0-9a-fA-F]{2})", hi)
        ml = re.search(r"portb\[0x03d5\] = 0x([0-9a-fA-F]{2})", lo)
        if not mh or not ml:
            raise OSError("CRTC read failed: %r %r" % (hi, lo))
        pos = (int(mh.group(1), 16) << 8) | int(ml.group(1), 16)
        return pos // VGA_COLS, pos % VGA_COLS

    def dump(self, label: str) -> bytes:
        out = ("=== VGA %s ===\n" % label).encode("latin-1")
        for line in self.text():
            out += line.encode("latin-1") + b"\n"
        r, c = self.crtc()
        out += ("=== CRTC row=%d col=%d ===\n" % (r, c)).encode("latin-1")
        return out

    def cursor(self) -> bytes:
        r, c = self.crtc()
        return ("=== CRTC row=%d col=%d ===\n" % (r, c)).encode("latin-1")