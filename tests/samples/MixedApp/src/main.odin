package main

import "core:fmt"

foreign {
	native_mul :: proc "c" (a, b: i32) -> i32 ---
}

main :: proc() {
	fmt.println("MixedApp 6 * 7 =", native_mul(6, 7))
}