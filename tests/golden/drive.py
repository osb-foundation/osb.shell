#!/usr/bin/env python3
"""Directive-driven QEMU serial driver for the golden harness (F4b).

Usage: drive.py <iso> <img> <case-file> [--out FILE] [--boot-delay SEC]

Case files:
  *.in   - simple format: the driver logs in as "root" (legacy default
           account), then each non-empty line is sent as a shell command and
           the driver waits for the next shell prompt.
  *.scm  - directive format, one per line (# = comment):
      nologin            do not auto-login; the script drives login itself
                         (required for 1b spec cases: login only exists in 1c)
      expect <text>      wait until <text> appears in the serial output
      expectre <regex>   wait until the regex matches the output
      prompt             wait for the shell prompt (legacy: "user@OSB path>",
                         or a bare "path>" for the 1b shell)
      send <line>        send the line + CRLF, then wait for the prompt
      sendnowait <line>  send the line + CRLF without waiting
      sendlf <line>      send the line + LF only (login name/password: the
                         legacy ReadLine stops at CR and leaves the LF, which
                         would be read as an empty line by the next prompt)
      sendraw <hex>      send raw bytes (hex, no spaces)
      ctrl-c             send 0x03 on the serial console (Ctrl+C)
      key <name>         PS/2 keyboard via the QEMU monitor (sendkey): tab,
                         up, down, ret, spc, backspace, ctrl-c, ... then wait
                         for the prompt
      keynowait <name>   same, without waiting
      sleep <sec>        idle wait (no I/O)
      timeout <sec>      overall case timeout from now on

By default the driver logs in as "root" before running a *.scm script; a
leading `nologin` directive suppresses that so a case controls its own login.

Output: the raw serial transcript (bytes) to --out or stdout.
Exit: 0 = all directives satisfied, 1 = failure (message + partial
transcript on stderr), 2 = usage error.

F4c: prints the elapsed wall time to stderr.
"""

import os
import re
import select
import socket
import subprocess
import sys
import time

PROMPT_RE = re.compile(r"> ?$")
LOGIN_RE = re.compile(r"login: ?$")


def last_line(buf: bytes) -> str:
    # The last non-empty line. The legacy shell prints the prompt with no
    # trailing newline, but the post-login prompt is followed by "\r\n"
    # (Shell.cs LoginSequence -> Vga.PutChar('\n')), so a naive "text after
    # the last \n" is empty exactly when the driver happens to read that
    # chunk. Strip trailing CR/LF first so a prompt followed by a newline
    # still matches (fixes the 13b-nonroot batch flake).
    stripped = buf.rstrip(b"\r\n")
    line = stripped[stripped.rfind(b"\n") + 1:]
    return line.decode("latin-1").rstrip("\r")


def at_prompt(buf: bytes) -> bool:
    return bool(PROMPT_RE.search(last_line(buf)))


def at_login(buf: bytes) -> bool:
    return bool(LOGIN_RE.search(last_line(buf)))


class Driver:
    def __init__(self, iso: str, img: str, boot_delay: float):
        self.buf = bytearray()
        self.deadline = time.monotonic() + boot_delay + 60.0
        # QEMU monitor socket for the PS/2 path (sendkey): lets a case press
        # TAB / arrows / Ctrl+C on the virtual keyboard, not just serial bytes.
        self.mon_path = "/tmp/osb-mon-%d.sock" % os.getpid()
        try:
            os.unlink(self.mon_path)
        except OSError:
            pass
        self.qemu = subprocess.Popen(
            ["qemu-system-x86_64",
             "-cdrom", iso, "-m", os.environ.get("OSB_MEM", "64M"),
             "-drive", "file=%s,if=ide,index=0,format=raw" % img,
             "-netdev", "user,id=net0", "-device", "e1000,netdev=net0",
             "-vga", "std", "-display", "none",
             "-monitor", "unix:%s,server,nowait" % self.mon_path,
             "-no-reboot",
             # signal=off: pass a raw serial 0x03 (Ctrl+C) through to COM1
             # instead of QEMU turning it into SIGINT for itself. The driver
             # writes/reads the process stdin/stdout, so stdio is the chardev.
             "-chardev", "stdio,id=ser0,signal=off",
             "-serial", "chardev:ser0"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, bufsize=0)
        self.fd = self.qemu.stdout.fileno()
        os.set_blocking(self.fd, False)
        # Position before the last send: prompt-waiting only accepts text
        # produced after it, so a stale earlier prompt cannot satisfy a wait.
        self.mark = 0
        # Drain the boot output until the first prompt/login shows up (or
        # boot_delay elapses), so the driver starts at a known state.
        self.wait_until(lambda b: at_prompt(b) or at_login(b), boot_delay)

    def pump(self, timeout: float) -> bool:
        """Read whatever is available; True if new bytes arrived."""
        try:
            r, _, _ = select.select([self.fd], [], [], max(0.0, timeout))
        except (OSError, ValueError):
            return False
        if not r:
            return False
        try:
            chunk = os.read(self.fd, 65536)
        except OSError:
            return False
        if not chunk:
            return False
        self.buf += chunk
        return True

    def wait_until(self, pred, timeout: float, since: int = 0) -> bool:
        end = min(time.monotonic() + timeout, self.deadline)
        while True:
            if pred(bytes(self.buf[since:])):
                return True
            left = end - time.monotonic()
            if left <= 0 or time.monotonic() > self.deadline:
                return pred(bytes(self.buf[since:]))
            self.pump(min(left, 0.1))
        return False

    def expect(self, text: str, timeout: float = 25.0) -> bool:
        needle = text.encode("latin-1")
        ok = self.wait_until(lambda b: needle in b, timeout, self.mark)
        if not ok:
            print("EXPECT FAILED: %r" % text, file=sys.stderr)
        return ok

    def expect_re(self, pattern: str, timeout: float = 25.0) -> bool:
        rx = re.compile(pattern.encode("latin-1"), re.DOTALL)
        ok = self.wait_until(lambda b: rx.search(b), timeout, self.mark)
        if not ok:
            print("EXPECTRE FAILED: %r" % pattern, file=sys.stderr)
        return ok

    def wait_prompt(self, timeout: float = 25.0) -> bool:
        ok = self.wait_until(at_prompt, timeout, self.mark)
        if not ok:
            print("PROMPT TIMEOUT (tail: %r)" % last_line(bytes(self.buf)),
                  file=sys.stderr)
        return ok

    def send_line(self, line: str) -> bool:
        self.mark = len(self.buf)
        try:
            os.write(self.qemu.stdin.fileno(),
                     line.encode("latin-1") + b"\r\n")
        except OSError:
            return False
        return True

    def send_lf(self, line: str) -> bool:
        # Line terminated by LF only. The legacy ReadLine stops at '\r' and
        # leaves the '\n' in the ring, which the next reader (a password
        # prompt) would consume as an empty line; login name/password entry
        # must use LF so no stray byte is left behind.
        self.mark = len(self.buf)
        try:
            os.write(self.qemu.stdin.fileno(),
                     line.encode("latin-1") + b"\n")
        except OSError:
            return False
        return True

    def send_raw(self, hexstr: str) -> bool:
        self.mark = len(self.buf)
        data = bytes.fromhex(hexstr.replace(" ", ""))
        try:
            os.write(self.qemu.stdin.fileno(), data)
        except OSError:
            return False
        return True

    def sendkey(self, name: str) -> bool:
        """Press a PS/2 key via the QEMU monitor (H2)."""
        self.mark = len(self.buf)
        s = socket.socket(socket.AF_UNIX)
        try:
            for _ in range(100):
                try:
                    s.connect(self.mon_path)
                    break
                except OSError:
                    time.sleep(0.05)
            else:
                print("MONITOR CONNECT FAILED", file=sys.stderr)
                return False
            s.sendall(("sendkey %s\n" % name).encode())
            time.sleep(0.1)
            try:
                s.recv(65536)
            except OSError:
                pass
        except OSError as e:
            print("SENDKEY FAILED: %r (%s)" % (name, e), file=sys.stderr)
            return False
        finally:
            s.close()
        return True

    def close(self) -> bytes:
        # Drain trailing output: read until ~1s of idle (or the deadline).
        idle_end = time.monotonic() + 1.0
        while time.monotonic() < idle_end and time.monotonic() < self.deadline:
            if not self.pump(0.2):
                break
            idle_end = time.monotonic() + 0.6
        try:
            self.qemu.stdin.close()
        except OSError:
            pass
        try:
            self.qemu.wait(timeout=3)
        except subprocess.TimeoutExpired:
            self.qemu.terminate()
            try:
                self.qemu.wait(timeout=3)
            except subprocess.TimeoutExpired:
                self.qemu.kill()
        try:
            os.unlink(self.mon_path)
        except OSError:
            pass
        return bytes(self.buf)


def run_scm(drv: Driver, path: str) -> bool:
    # A leading `nologin` directive means the script drives login itself
    # (1b spec cases have no login at all). Otherwise log in as root first.
    with open(path, "r", encoding="latin-1") as f:
        raw_lines = [ln.strip() for ln in f
                     if ln.strip() and not ln.startswith("#")]
    do_login = True
    if raw_lines and raw_lines[0].split(None, 1)[0] == "nologin":
        do_login = False
        raw_lines = raw_lines[1:]
    if do_login:
        if not drv.wait_until(at_login, 30.0):
            print("LOGIN PROMPT TIMEOUT", file=sys.stderr)
            return False
        drv.send_line("root")
        if not drv.wait_prompt():
            return False
    for line in raw_lines:
        parts = line.split(None, 1)
        cmd = parts[0]
        arg = parts[1] if len(parts) > 1 else ""
        if cmd == "expect":
            ok = drv.expect(arg)
        elif cmd == "expectre":
            ok = drv.expect_re(arg)
        elif cmd == "prompt":
            ok = drv.wait_prompt()
        elif cmd == "send":
            drv.send_line(arg)
            ok = drv.wait_prompt()
        elif cmd == "sendnowait":
            drv.send_line(arg)
            ok = True
        elif cmd == "sendlf":
            # LF-terminated send, no wait: the next prompt may be "Password: "
            # rather than "> ", so the script follows with prompt/expect.
            drv.send_lf(arg)
            ok = True
        elif cmd == "sendraw":
            ok = drv.send_raw(arg)
        elif cmd == "ctrl-c":
            ok = drv.send_raw("03")
        elif cmd == "key":
            drv.sendkey(arg)
            ok = drv.wait_prompt()
        elif cmd == "keynowait":
            ok = drv.sendkey(arg)
        elif cmd == "sleep":
            time.sleep(float(arg))
            ok = True
        elif cmd == "timeout":
            drv.deadline = time.monotonic() + float(arg)
            ok = True
        else:
            print("UNKNOWN DIRECTIVE: %r" % line, file=sys.stderr)
            return False
        if not ok:
            return False
    return True


def run_in(drv: Driver, path: str) -> bool:
    # *.in format: every non-empty line is a shell command. The driver logs
    # in as "root" (the legacy default account) before feeding the lines.
    with open(path, "r", encoding="latin-1") as f:
        lines = [ln.rstrip("\r\n") for ln in f if ln.strip()]
    if not drv.wait_until(at_login, 30.0):
        # No login prompt (e.g. a shell that boots straight to a prompt):
        # treat every line as a command.
        for line in lines:
            drv.send_line(line)
            if not drv.wait_prompt():
                return False
        return True
    drv.send_line("root")
    if not drv.wait_prompt():
        return False
    for line in lines:
        drv.send_line(line)
        if not drv.wait_prompt():
            return False
    return True


def main() -> int:
    out_path = None
    boot_delay = float(os.environ.get("BOOT_DELAY", "12"))
    args = []
    argv = sys.argv[1:]
    i = 0
    while i < len(argv):
        if argv[i] == "--out":
            out_path = argv[i + 1]; i += 2
        elif argv[i] == "--boot-delay":
            boot_delay = float(argv[i + 1]); i += 2
        else:
            args.append(argv[i]); i += 1
    if len(args) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    iso, img, case = args
    start = time.monotonic()
    drv = Driver(iso, img, boot_delay)
    if case.endswith(".scm"):
        ok = run_scm(drv, case)
    else:
        ok = run_in(drv, case)
    transcript = drv.close()
    end = time.monotonic()
    if out_path:
        with open(out_path, "wb") as f:
            f.write(transcript)
    else:
        sys.stdout.buffer.write(transcript)
    print("drive.py: %s, %.1fs" % ("PASS" if ok else "FAIL",
                                   end - start), file=sys.stderr)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())