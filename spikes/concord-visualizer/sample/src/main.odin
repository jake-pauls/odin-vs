#+feature dynamic-literals
package main

import "base:runtime"
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

Pair :: struct {
	a, b: i32,
}

Thing :: struct {
	name:  string,
	id:    int,
	items: []int,
	tags:  [dynamic]string,
}

// Package-level variables: Locals does not list these, so check them in Watch (`g_ints`, `main::g_ints`, `g_map`, ...).
// Experiment: g_count/g_name/g_ints had constant initializers on the declaration and were "undefined" in Watch;
// g_dyn/g_map/g_things (no initializer, assigned in main) were visible. They are now assigned in main like the others.
// g_const_count keeps a constant initializer as the control.
g_const_count: int = 41
g_count: int
g_name: string
g_ints: []int
g_dyn: [dynamic]string
g_map: map[string]int
g_things: [dynamic]Thing

main :: proc() {
	g_count = 41
	g_name = "global"
	g_ints = []int{7, 8, 9}
	append(&g_dyn, "x", "y", "z")
	g_map = make(map[string]int)
	g_map["one"] = 1
	g_map["two"] = 2
	append(&g_things, Thing{name = "g", id = 99})

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
	// Map layout edge cases: 12-byte key (cells hold 5 with 4 bytes padding), growth past 8 slots, a deleted slot.
	Triple :: struct {
		a, b, c: i32,
	}
	triple_keys := map[Triple]int {
		Triple{1, 2, 3} = 10,
		Triple{4, 5, 6} = 20,
		Triple{7, 8, 9} = 30,
	}
	pair_keys := map[Pair]string {
		Pair{1, 2} = "a",
		Pair{3, 4} = "b",
	}
	thing_values := map[int]Thing {
		1 = Thing{name = "one", id = 1},
		2 = Thing{name = "two", id = 2},
	}
	many := make(map[int]int)
	for i in 1 ..= 12 {
		many[i] = i * i
	}
	delete_key(&many, 4)
	delete_key(&many, 9)
	color := Color.Green
	flags := Flags{.Read, .Exec}
	shape: Shape = Rect{3, 4}
	shape_circle: Shape = Circle{2}
	shape_nil: Shape
	anything: any = 42
	number := 7
	ptr := &number
	multi: [^]int = raw_data(ints)
	fixed := [4]int{5, 6, 7, 8}
	mat := matrix[2, 2]f32{1, 2, 3, 4}
	// Non-square (2 rows, 3 columns) in both storage orders, to tell rows from columns in the display; literals list rows.
	cmat := matrix[2, 3]f32{1, 2, 3, 4, 5, 6}
	rmat := #row_major matrix[2, 3]f32{1, 2, 3, 4, 5, 6}
	cmat3 := matrix[3, 3]f32{1, 2, 3, 4, 5, 6, 7, 8, 9}
	anything_text: any = "any text"
	anything_struct: any = one.id
	// Unions: Maybe(int) is a tagged single-variant union; Maybe(^int) uses the nil-pointer niche (no tag).
	maybe_int: Maybe(int) = 5
	maybe_none: Maybe(int)
	maybe_ptr: Maybe(^int) = ptr
	color_array := [3]Color{.Red, .Green, .Blue}
	flags_empty: Flags
	text := "hello"
	cstr: cstring = "c string"
	letter := 'q'
	ratio := f32(0.5)
	ok := true

	// --- C4 edge cases (not printed: the garbage slices must never be touched by the program) ---
	// 1M elements: stepping and expansion performance.
	million: [dynamic]int
	reserve(&million, 1_000_000)
	for i in 0 ..< 1_000_000 {
		append(&million, i)
	}
	// A slice whose len runs past mapped memory (50M, within our sanity limit) and one far past it (5G, rejected).
	garbage_len := transmute([]int)runtime.Raw_Slice{data = raw_data(ints), len = 50_000_000}
	garbage_huge := transmute([]int)runtime.Raw_Slice{data = raw_data(ints), len = 5_000_000_000}
	garbage_neg := transmute([]int)runtime.Raw_Slice{data = raw_data(ints), len = -1}

	fmt.println("done", g_const_count, g_count, g_name, g_ints, g_dyn, g_map, g_things,ints, strs, empty_slice, nil_slice, dyn_ints, dyn_strs, dyn_empty, len(big), one, sub, scores, names, triple_keys, pair_keys, thing_values, many, color, flags, shape, anything, ptr, multi, fixed, mat, text, cstr, letter, ratio, ok)

	delete(dyn_ints)
	delete(dyn_strs)
	delete(dyn_empty)
	delete(big)
	delete(one.tags)
	delete(scores)
	delete(names)
	delete(triple_keys)
	delete(pair_keys)
	delete(thing_values)
	delete(many)
	delete(million)
}
