package main

import "core:fmt"

// The referenced DLL project's import library is already on the link line, so a plain foreign block is enough.
foreign {
	dll_mul :: proc "c" (a, b: i32) -> i32 ---
}

main :: proc() {
	fmt.println("LinkApp 6 * 7 =", dll_mul(6, 7))
}
