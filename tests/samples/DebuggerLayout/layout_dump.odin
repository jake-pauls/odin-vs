package main

// Dumps the real memory layout of Odin maps for several key/value sizes, asking Odin's own runtime procedures where the
// keys, values and hashes are. OdinMapLayoutTests (tests/OdinVS.Core.Tests) runs this with `odin run -file` and compares
// every number with the decoding math in OdinVS.Core (OdinMapLayout), which the debugger visualizer uses. Output lines:
//   LAYOUT key=<size> value=<size> data len cap keys values hashes key_cell key_per_cell value_cell value_per_cell
//   SLOT   <index> hash key_offset value_offset key0 value0
//   DELETED <index> hash   (a tombstone: non-zero hash with the top bit set)
// Offsets are bytes from the start of the keys array (the map's data pointer without its capacity bits).

import "base:runtime"
import "core:fmt"

dump :: proc($K: typeid, $V: typeid, entries: int) {
	m := make(map[K]V)
	defer delete(m)

	for i in 1 ..= entries {
		k: K
		k[0] = u8(i)
		v: V
		v[0] = u8(i + 100)
		m[k] = v
	}
	// Tombstones: a deleted slot must not be listed as used.
	for i in 1 ..= entries {
		if i % 3 == 0 {
			k: K
			k[0] = u8(i)
			delete_key(&m, k)
		}
	}

	raw := (^runtime.Raw_Map)(&m)^
	info := runtime.map_info(map[K]V)
	ks, vs, hs, _, _ := runtime.map_kvh_data_dynamic(raw, info)
	fmt.printfln(
		"LAYOUT key=%v value=%v data=%v len=%v cap=%v keys=%v values=%v hashes=%v key_cell=%v key_per_cell=%v value_cell=%v value_per_cell=%v",
		size_of(K), size_of(V), raw.data, len(m), cap(m), 0, vs - ks, uintptr(hs) - ks,
		info.ks.size_of_cell, info.ks.elements_per_cell, info.vs.size_of_cell, info.vs.elements_per_cell,
	)
	for i in 0 ..< cap(m) {
		if runtime.map_hash_is_valid(hs[i]) {
			key_address := runtime.map_cell_index_dynamic(ks, info.ks, uintptr(i))
			value_address := runtime.map_cell_index_dynamic(vs, info.vs, uintptr(i))
			fmt.printfln("SLOT %v hash=%v key_offset=%v value_offset=%v key0=%v value0=%v", i, hs[i], key_address - ks, value_address - vs, (^u8)(key_address)^, (^u8)(value_address)^)
		} else if hs[i] != 0 {
			fmt.printfln("DELETED %v hash=%v", i, hs[i])
		}
	}
}

main :: proc() {
	dump([1]u8, [1]u8, 20)
	dump([3]u8, [5]u8, 20)
	dump([4]u8, [8]u8, 20)
	dump([8]u8, [4]u8, 3)
	dump([12]u8, [4]u8, 3)
	dump([12]u8, [12]u8, 100)
	dump([16]u8, [100]u8, 20)
	dump([24]u8, [24]u8, 20)
	dump([63]u8, [64]u8, 20)
	dump([64]u8, [65]u8, 20)
	dump([100]u8, [12]u8, 20)
	dump([130]u8, [80]u8, 9)
}
