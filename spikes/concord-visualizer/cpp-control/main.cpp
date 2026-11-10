// M5.5 Phase F control: C++ and C locals that the Odin visualizer component must NOT change.
// Break on the `puts("done")` line and compare Locals with and without the component.
#include <cstdio>
#include <intrin.h>
#include <map>
#include <string>
#include <vector>

extern "C" void c_locals(void);

// A user type literally named `string` (collides with Odin's `string` natvis entry if that matches by name).
struct string {
    char* data;
    long long len;
};

int main() {
    string odin_like = { const_cast<char*>("hello"), 5 };
    std::string std_text = "std string";
    std::vector<int> numbers = { 1, 2, 3 };
    std::map<int, int> table = { { 1, 10 }, { 2, 20 } };
    float f = 1.5f;
    double d = 2.5;
    unsigned char byte = 200;
    long long big = 1234567890123LL;
    int* pointer = &numbers[0];
    char32_t wide = U'q';
    c_locals();
    __debugbreak();  // stops here without a breakpoint, so a click-only session can inspect Locals
    std::puts("done");
    return (int)(odin_like.len + std_text.size() + numbers.size() + table.size() + (long long)f + (long long)d + byte + big + *pointer + wide) & 0;
}
