# Regenerate all procedural art from source. Every target is deterministic:
# delete out/ and re-run, the .glb files come back byte-for-byte identical
# (modulo glTF export timestamps), because geometry is defined by code, not
# by hand-sculpted state saved in a .blend file.

BLENDER := /Applications/Blender.app/Contents/MacOS/Blender
SCRIPTS := $(wildcard scripts/*.py)
OUT     := out

.PHONY: all bowl clean

all: bowl

bowl:
	$(BLENDER) --background --python scripts/bowl.py -- --variant all

clean:
	rm -rf $(OUT)/*.glb
