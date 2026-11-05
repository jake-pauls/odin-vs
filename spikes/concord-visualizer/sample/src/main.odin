#+feature dynamic-literals
package main

import "core:fmt"

// M5.5 type-coverage target. Set a breakpoint on the `fmt.println("done")` line at the end of main
// and look at Locals/Watch. Must-have types come first, stretch types after.

Color :: enum {
	Red,
	Green,
	Blue,
}

Flag :: enum {
	Read,
	Write,
	Exec,
}

Flags :: bit_set[Flag]

Circle :: struct {
	radius: f32,
}

Rect :: struct {
	w, h: f32,
}

Shape :: union {
	Circle,
	Rect,
}

Thing :: struct {
	name:  string,
	id:    int,
	items: []int,
	tags:  [dynamic]string,
}

main :: proc() {
	// --- must-have: slices and dynamic arrays ---
	ints := []int{10, 20, 30}
	strs := []string{"alpha", "beta", "gamma"}
	empty_slice := []int{}
	nil_slice: []int
	dyn_ints := [dynamic]int{1, 2, 3, 4}
	dyn_strs := [dynamic]string{"a", "b"}
	dyn_empty: [dynamic]int
	reserve(&dyn_empty, 8)
	big: [dynamic]int
	for i in 0 ..< 100000 {
		append(&big, i)
	}
	things := []Thing{
		{name = "one", id = 1, items = ints},
		{name = "two", id = 2, items = ints[1:]},
	}
	things[0].tags = {"x", "y"}
	one := things[0]
	sub := ints[1:3]

	// --- stretch ---
	scores := map[string]int {
		"ann" = 90,
		"bob" = 75,
	}
	names := map[int]string {
		1 = "one",
		2 = "two",
	}
	color := Color.Green
	flags := Flags{.Read, .Exec}
	shape: Shape = Rect{3, 4}
	anything: any = 42
	number := 7
	ptr := &number
	multi: [^]int = raw_data(ints)
	fixed := [4]int{5, 6, 7, 8}
	mat := matrix[2, 2]f32{1, 2, 3, 4}
	text := "hello"
	cstr: cstring = "c string"
	letter := 'q'
	ratio := f32(0.5)
	ok := true

	fmt.println("done", ints, strs, empty_slice, nil_slice, dyn_ints, dyn_strs, dyn_empty, len(big), one, sub, scores, names, color, flags, shape, anything, ptr, multi, fixed, mat, text, cstr, letter, ratio, ok)

	delete(dyn_ints)
	delete(dyn_strs)
	delete(dyn_empty)
	delete(big)
	delete(one.tags)
	delete(scores)
	delete(names)
}
