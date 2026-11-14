// The native half of the mixed sample: a C++ DLL the Odin project links through its import library.
extern "C" __declspec(dllexport) int native_mul(int a, int b)
{
    return a * b;
}