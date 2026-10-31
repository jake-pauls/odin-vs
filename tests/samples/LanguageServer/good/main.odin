package main

import "core:fmt"

Vec2 :: struct {
	x, y: f32,
}

add :: proc(a, b: Vec2) -> Vec2 {
	return Vec2{a.x + b.x, a.y + b.y}
}

main :: proc() {
	v := add(Vec2{1, 2}, Vec2{3, 4})
	fmt.println(v.x, v.y)
}
