package main

import "core:fmt"

// Manual language-configuration checks (M5). Open this file in Exp, put the caret where each line says and type the text
// after "type:". Record what happened in docs/milestones.md. Do not save edits to this file.
//
// wordPattern / completion span (ols completions; the replaced span comes from ols's textEdit):
//   W1  line 21: type "pri" after "fmt." -> Tab      expect "fmt.println", nothing left over before "fmt."
//   W2  line 22: type "." after "fmt"                expect a member list (println, ...) and no stray "fmt" replaced
//   W3  line 23: type "f" at the end of "1.5"        expect no completion list, or one that does not replace "1.5"
//   W4  line 24: type "ab" after "my_" -> Ctrl+Space expect the span to cover all of "my_ab", not just "ab"
//   W5  line 25: type "a" after "-" in "n := -"      expect the "-" is kept
//
// Auto-close / surround (M1 open item; known not to work in Exp so far, see docs/pitfalls.md):
//   A1  line 29: type "(" "{" "[" in turn            note whether a closing character appears
//   A2  line 30: type '"' and "'"                    note whether a closing quote appears
//   A3  line 31: select "word" and type "("          note whether it wraps (surround-with)
main :: proc() {
	x := 1
	fmt.
	fmt
	y := 1.5
	my_
	n := -
	_ = y; _ = n

	// auto-close scratch lines:
	
	
	word
}
