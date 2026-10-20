# Drive the VMX on the CoreS3 over USB serial and print its screen: python tools/drive.py "wait:7 p a wait:1 p"
# Tokens: u d l r a b s (tap), S (hold side 1 s), p (print the screen), wait:SECONDS. See esp32/src/main.cpp serialHook.
import serial, sys, time
s = serial.Serial('/dev/ttyACM0', 115200, timeout=0.2)
time.sleep(0.5); s.read(100000)
for tok in sys.argv[1].split():
    if tok.startswith("wait:"): time.sleep(float(tok[5:])); continue
    s.write(tok.encode()); s.flush(); time.sleep(0.4)
    if "p" in tok:
        out = b""; t = time.time()
        while b"--" not in out and time.time() - t < 3: out += s.read(4096)
        print(out.decode(errors="replace"))
