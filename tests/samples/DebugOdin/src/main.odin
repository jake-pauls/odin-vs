#+feature dynamic-literals
package main

import "core:fmt"
import "core:os"

// F5 check target: break on the `fmt.println` line and inspect count, name, values, words in Locals/Watch.
main :: proc() {
	count := 42
	name := "odin"
	values := []int{1, 2, 3}
	words := [dynamic]string{"a", "b"}
	defer delete(words)

	fmt.println("args:", os.args[1:])
	fmt.println("count:", count, "name:", name, "values:", values, "words:", words)
}