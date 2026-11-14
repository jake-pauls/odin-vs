package lib

// Exported with the C calling convention so other Odin projects (and C/C++) can call it.
// A project that references this one (ReferenceKind="Link") declares the procedure in a foreign block; the DLL's
// import library is already on its link line, so no foreign import is needed:
//   foreign { add :: proc "c" (a, b: i32) -> i32 --- }
@(export)
add :: proc "c" (a, b: i32) -> i32 {
	return a + b
}
