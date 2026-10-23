// FATX, the Xbox's file system, on any block device of 512-byte sectors: the VMX's own storage is an Xbox memory
// unit's volume (vmx-world/docs/DESIGN.md "On the Xbox hard disk and on the VMX"), so the same bytes work on the VMX,
// over USB on the Xbox (World, the dashboard) and in the PC builds (an image file). Hardware-free; used by the VMX
// firmware (vmxvol.c) and by VMX World on the Xbox (the VMX in a controller, through nxdk's USB mass storage).
// Layout as the Xbox kernel and nxdk's nxFormatVolume make it: a 4 KB superblock ("FATX", volume ID, sectors per
// cluster, root cluster), the cluster table (16-bit entries under 65520 clusters, else 32-bit; rounded up to 4 KB),
// then 16 KB clusters numbered from 1. Directory entries are 64 bytes: name length (0xE5 deleted, 0x00 / 0xFF end),
// attributes (0x10 folder), 42 name bytes, first cluster, size, three FAT-style time stamps (year from 2000).
#ifndef FATX_H
#define FATX_H
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif

typedef struct Fatx {
  int (*read)(void *ctx, uint32_t sector, int count, uint8_t *buf);          // 1 = ok
  int (*write)(void *ctx, uint32_t sector, int count, const uint8_t *buf);   // 1 = ok
  void *ctx;
  uint32_t sectors;                     // size of the device
  uint32_t stamp;                       // the time written into entries (fatx_stamp); 0 = 1 Jan 2001
  // filled by fatx_mount
  uint32_t spc, clusterBytes, root, nclusters, dataSector, fatBytes; int ent;
  uint8_t *fat, *fatDirty, *cl;         // the table (kept in RAM), which of its sectors changed, a cluster buffer
} Fatx;

typedef struct { char name[43]; int dir; uint32_t size, first, stamp; } FatxEnt;

#define FATX_MU_BYTES (8u * 1024 * 1024)                  // an 8 MB memory unit, as Microsoft's
int  fatx_format(Fatx *v, uint32_t volumeId);             // an empty volume over the whole device; 1 = ok (mounts it)
int  fatx_mount(Fatx *v);                                 // 1 = a FATX volume
void fatx_unmount(Fatx *v);                               // frees the buffers
int  fatx_read(Fatx *v, const char *path, void *buf, int max);       // bytes read, -1 = no such file
int  fatx_size(Fatx *v, const char *path);                // -1 = none, -2 = a folder
int  fatx_write(Fatx *v, const char *path, const void *buf, int n);  // makes the folders, replaces a file; 1 = ok
int  fatx_mkdir(Fatx *v, const char *path);               // and its parents; 1 = ok (or already there)
int  fatx_remove(Fatx *v, const char *path);              // a file, or a folder and everything in it; 1 = ok
int  fatx_list(Fatx *v, const char *path, FatxEnt *out, int max);   // entries in a folder, -1 = no such folder
uint32_t fatx_free_bytes(Fatx *v);
uint32_t fatx_stamp(int year, int mon, int day, int hour, int min, int sec);
#ifdef __cplusplus
}
#endif
#endif
