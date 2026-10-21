# Drive the VMX on the CoreS3 and print its screen, over USB serial or Wi-Fi.
#   python tools/drive.py "wait:7 p a wait:1 p"           (USB: /dev/ttyACM0)
#   python tools/drive.py --wifi "a wait:1 p i"           (Wi-Fi: vmx.local, key from esp32/ota_secret.ini)
# Tokens: u d l r a b s (tap), S (hold side 1 s), p (print the screen), i (speed stats), wait:SECONDS.
# See esp32/src/main.cpp hookCommand.
import os, sys, time
args = sys.argv[1:]; wifi = "--wifi" in args; args = [a for a in args if a != "--wifi"]
script = args[0]; host = args[1] if len(args) > 1 else "vmx.local"
if wifi:
    import urllib.request, urllib.parse
    key = [l.split("=", 1)[1].strip() for l in open(os.path.join(os.path.dirname(__file__), "../esp32/ota_secret.ini")) if l.startswith("password")][0]
    def send(tok):
        req = urllib.request.Request(f"http://{host}/hook?c={urllib.parse.quote(tok)}", method="POST", headers={"X-VMX-Key": key})
        out = urllib.request.urlopen(req, timeout=10).read().decode(errors="replace")
        if out.strip(): print(out.rstrip())
else:
    import serial
    s = serial.Serial('/dev/ttyACM0', 115200, timeout=0.2); time.sleep(0.5); s.read(100000)
    def send(tok):
        s.write(tok.encode()); s.flush(); time.sleep(0.4)
        if "p" in tok or "i" in tok:
            out = b""; t = time.time()
            while time.time() - t < 2: out += s.read(4096)
            print(out.decode(errors="replace").rstrip())
for tok in script.split():
    if tok.startswith("wait:"): time.sleep(float(tok[5:])); continue
    send(tok)
