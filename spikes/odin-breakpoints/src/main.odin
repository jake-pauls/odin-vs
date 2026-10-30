package main

import "core:fmt"

add :: proc(a, b: int) -> int {
	sum := a + b
	return sum
}

main :: proc() {
	total := 0
	for i in 0 ..< 5 {
		total = add(total, i)
	}
	name := "odin"
	fmt.println("total:", total, name)
}
