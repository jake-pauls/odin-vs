package linkdll

// Exported with the C calling convention so another Odin project (or C++) can import it.
@(export)
dll_mul :: proc "c" (a, b: i32) -> i32 {
	return a * b
}
