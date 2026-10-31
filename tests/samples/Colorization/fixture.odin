#+build windows
package fixture

// Line comment: TODO check colors
/* Block comment /* nested */ still a comment */

import "core:fmt"
import "core:strings"

Vec2 :: struct {
	x, y: f32,
}

Shape :: union {
	Circle,
	Rect,
}

Color :: enum u8 {
	Red,
	Green = 5,
}

Flags :: bit_set[Color; u8]

Packed :: bit_field u32 {
	a: u8 | 4,
	b: u8 | 4,
}

Matrix2 :: matrix[2, 2]f32

Handle :: distinct int

@(private)
secret: int = 0x1F

@(require_results)
add :: proc(a, b: int) -> int {
	return a + b
}

@(link_name = "ext")
foreign_thing :: proc "c" (p: rawptr) ---

main :: proc() {
	n := 42
	f := 3.14e2
	b := 0b1010
	o := 0o17
	r := 'x'
	s := "string with \"escape\" and %d placeholder"
	raw := `raw
	multi-line string`

	#assert(size_of(int) >= 4)
	loc := #location()

	defer fmt.println("done")

	when ODIN_OS == .Windows {
		fmt.println("windows")
	}

	for i in 0 ..< 3 {
		if i == 1 do continue
		switch i {
		case 0: fmt.println("zero")
		case: fallthrough
		}
	}

	v, ok := maybe().?
	x := maybe_int() or_else 0
	y := fallible() or_return
	z := cast(f32)n + transmute(f32)u32(n)
	using vec := Vec2{1, 2}
	ctx := context
	p := new(int)
	free(p)
	t := true ? 1 : 2
	_, _, _, _, _, _, _ = v, ok, x, y, z, ctx, t
}
