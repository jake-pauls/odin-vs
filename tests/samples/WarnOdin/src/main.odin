package main

import "core:fmt"

@(deprecated="use new_thing instead")
old_thing :: proc() {
	fmt.println("old")
}

main :: proc() {
	old_thing()
}
