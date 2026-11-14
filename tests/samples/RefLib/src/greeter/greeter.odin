package greeter

import "RefBase:src/basemath"

total :: proc(n: int) -> int {
	sum := 0
	for i in 0 ..< n {
		sum = basemath.add(sum, i)
	}
	return sum
}
