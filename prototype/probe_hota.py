"""Read-only diagnostic, candidate SoD layouts from RoseKavalier/H3API.
Not an agent observation API: layouts and visibility are not validated.
"""
import ctypes as c
from ctypes import wintypes as w
import json
import struct
import sys

k = c.WinDLL('kernel32', use_last_error=True)
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k.OpenProcess.restype = w.HANDLE
k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
k.ReadProcessMemory.restype = w.BOOL
k.CloseHandle.argtypes = [w.HANDLE]
h = k.OpenProcess(0x0400 | 0x0010, False, int(sys.argv[1]))
if not h:
    raise c.WinError(c.get_last_error())

def read(address, size):
    if not 0x10000 <= address < 0x100000000 or size > 0x50000:
        raise ValueError('Address or size outside diagnostic bounds')
    b = c.create_string_buffer(size)
    count = c.c_size_t()
    if not k.ReadProcessMemory(h, address, b, size, c.byref(count)) or count.value != size:
        raise c.WinError(c.get_last_error())
    return b.raw

def unpack(fmt, address):
    return struct.unpack(fmt, read(address, struct.calcsize(fmt)))

try:
    if '--disasm' in sys.argv:
        from pathlib import Path
        sys.path.insert(0, str(Path(__file__).parent / 'probe_deps'))
        from capstone import Cs, CS_ARCH_X86, CS_MODE_32
        decoder = Cs(CS_ARCH_X86, CS_MODE_32)
        for address in [int(a, 0) for a in sys.argv[3:]]:
            for ins in decoder.disasm(read(address, 96), address):
                print(f'{ins.address:08x}: {ins.mnemonic} {ins.op_str}')
        sys.exit(0)
    base, = unpack('<I', 0x699538)
    result = {'pid': int(sys.argv[1]), 'mode': 'read_only_candidate_layout', 'main_pointer': hex(base)}
    result['date_day_week_month'] = unpack('<3H', base + 0x1F63E)
    p = read(base + 0x20AD0, 0x168)  # red only, matching supplied screenshot
    hero_id, = struct.unpack_from('<i', p, 4)
    result['red_player'] = {'owner': p[0], 'hero_count': p[1], 'selected_hero_id': hero_id,
        'resources_wood_mercury_ore_sulfur_crystal_gems_gold': struct.unpack_from('<7i', p, 0x9C)}
    if 0 <= hero_id < 156:
        # Verify the live GetHero instruction sequence. HotA patches the
        # displacement while retaining the 0x492-byte element stride.
        code = read(0x4317E1, 19)
        if code[:13] != bytes.fromhex('8bc2c1e00603c28d04c08d8441') or code[17:] != bytes.fromhex('5dc2'):
            raise RuntimeError('Unrecognized GetHero instructions; refusing guessed hero address')
        displacement = struct.unpack_from('<i', code, 13)[0]
        result['hero_array_displacement_from_live_code'] = hex(displacement)
        hero = read(base + displacement + hero_id * 0x492, 0x492)
        if struct.unpack_from('<i', hero, 0x1A)[0] != hero_id or hero[0x22] != p[0]:
            raise RuntimeError('Hero identity does not match selected owned hero')
        result['candidate_hero'] = {
            'id': struct.unpack_from('<i', hero, 0x1A)[0], 'owner': hero[0x22],
            'name': hero[0x23:0x30].split(b'\0')[0].decode('cp1251', errors='replace'),
            'xyz': struct.unpack_from('<3h', hero),
            'mana': struct.unpack_from('<h', hero, 0x18)[0],
            'max_movement_movement_experience': struct.unpack_from('<3i', hero, 0x49),
            'level': struct.unpack_from('<h', hero, 0x55)[0],
            'attack_defense_power_knowledge': struct.unpack_from('<4b', hero, 0x476),
            'army_types_counts': struct.unpack_from('<14i', hero, 0x91)}
    print(json.dumps(result, ensure_ascii=True, indent=2))
finally:
    k.CloseHandle(h)
