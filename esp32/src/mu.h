// The VMX's memory-unit volume on the CoreS3 and the USB link: see mu.cpp.
#pragma once
#include <Preferences.h>
#include "../../core/fatx.h"
bool mu_begin(Preferences &prefs);     // mounts (or makes) the volume, starts USB
void mu_loop();                        // every frame: links to a host, lets go of it
void mu_flush();                       // after the firmware's own writes
Fatx *mu_vol();
