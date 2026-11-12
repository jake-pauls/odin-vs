package main

import "core:fmt"
import "greeter"

main :: proc() {
	fmt.println("RefLib:", greeter.total(5))
}
