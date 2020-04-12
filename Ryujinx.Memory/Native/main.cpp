#define TARGET_WINDOWS

#ifdef TARGET_WINDOWS
#include "pch.h"
#include <Windows.h>
#define EXPORT extern "C" __declspec(dllexport)
#else
#include <sys/mman.h>
#include <signal.h>
#define EXPORT extern
#endif
#include <stdio.h>
#include <stdlib.h>

#define MAX_TRACKED_REGIONS (32)
#define PAGE_SIZE (4096)
#define PAGE_MASK (PAGE_SIZE - 1)

#define C_ULONG(x) ((unsigned long long)x)

typedef struct
{
    int Valid;
    size_t Size;
    void* Address;
    void* Handle;
    int (*Action)(void*, int);
} TrackedRegion;

TrackedRegion g_regions[MAX_TRACKED_REGIONS];

static TrackedRegion* AllocateRegion()
{
    int i;

    for (i = 0; i < MAX_TRACKED_REGIONS; i++)
    {
        if (!g_regions[i].Valid)
        {
            g_regions[i].Valid = 1;

            return &g_regions[i];
        }
    }

    return NULL;
}

static void FreeRegion(TrackedRegion* region)
{
    region->Valid = 0;
}

#ifdef TARGET_WINDOWS
int ExceptionHandler(EXCEPTION_POINTERS* exceptionPointers)
{
    if (exceptionPointers->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION)
    {
        return EXCEPTION_CONTINUE_SEARCH;
    }

    void* exceptionAddress = (void*)exceptionPointers->ExceptionRecord->ExceptionInformation[1];

    int i;

    for (i = 0; i < MAX_TRACKED_REGIONS; i++)
    {
        TrackedRegion rg = g_regions[i];

        if (rg.Valid &&
            C_ULONG(rg.Address) <= C_ULONG(exceptionAddress) &&
            C_ULONG(rg.Address) + C_ULONG(rg.Size) > C_ULONG(exceptionAddress))
        {
            int handled = rg.Action((void*)(C_ULONG(exceptionAddress) - C_ULONG(rg.Address)), exceptionPointers->ExceptionRecord->ExceptionInformation[0]);

            if (handled)
            {
                //should be done by the whatever

                //unsigned int oldProtect;

                //VirtualProtect(exceptionAddress, 1, PAGE_READWRITE, (PDWORD)&oldProtect);

                return EXCEPTION_CONTINUE_EXECUTION;
            }
        }
    }

    return EXCEPTION_CONTINUE_SEARCH;
}
#else
void ExceptionHandler(int sig, siginfo_t* info, void* ucontext)
{
    void* exceptionAddress = info->si_addr;

    int i;

    greg_t error = ((ucontext_t*)ucontext)->uc_mcontext.gregs[REG_ERR];

    for (i = 0; i < MAX_TRACKED_REGIONS; i++)
    {
        TrackedRegion rg = g_regions[i];

        if (rg.Valid &&
            C_ULONG(rg.Address) <= C_ULONG(exceptionAddress) &&
            C_ULONG(rg.Address) + C_ULONG(rg.Size) > C_ULONG(exceptionAddress))
        {
            bool handled = rg.Action((void*)(C_ULONG(exceptionAddress) - C_ULONG(rg.Address)), (err & 0x2) == 0x2);

            if (handled)
            {
                //should be done by the handler

                //mprotect((void*)(C_ULONG(exceptionAddress) & ~C_ULONG(PAGE_MASK)), PAGE_SIZE, PROT_READ | PROT_WRITE);

                return EXCEPTION_CONTINUE_EXECUTION;
            }

            return;
        }
    }

    return; // TODO: Call previous signal handler.
}
#endif

EXPORT TrackedRegion* StartTrackingRegion(void* address, size_t size, int (*action)(void*, int))
{
    TrackedRegion* region = AllocateRegion();

    if (region == NULL)
    {
        return NULL;
    }

#ifdef TARGET_WINDOWS
    void* handle = AddVectoredExceptionHandler(1, (PVECTORED_EXCEPTION_HANDLER)ExceptionHandler);

    if (handle == NULL)
    {
        FreeRegion(region);

        return NULL;
    }
#else
    struct sigaction sa;

    sigemptyset(&sa.sa_mask);

    sa.sa_sigaction = ExceptionHandler;
    sa.sa_flags = SA_SIGINFO;

    sigaction(SIGSEGV, &sa, NULL);

    void* handle = NULL;
#endif

    region->Address = address;
    region->Size = size;
    region->Handle = handle;
    region->Action = action;

    return region;
}

EXPORT void StopTrackingRegion(TrackedRegion* region)
{
#ifdef TARGET_WINDOWS
    RemoveVectoredExceptionHandler(region->Handle);
#else

#endif

    FreeRegion(region);
}
