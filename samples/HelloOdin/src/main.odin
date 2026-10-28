package main

import "core:fmt"
import "hellolib:greeter"

main :: proc() {
	fmt.println("Hello, World!")
	fmt.println(greeter.message())
}
