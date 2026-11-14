package main

import "core:fmt"
import "RefLib:src/greeter"

main :: proc() {
	fmt.println("RefApp total:", greeter.total(5))
}
