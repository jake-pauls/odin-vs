package main

import "core:fmt"
import "basemath"

main :: proc() {
	fmt.println("RefBase:", basemath.add(2, 3))
}
