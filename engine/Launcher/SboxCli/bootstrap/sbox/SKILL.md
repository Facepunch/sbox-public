---
name: sbox
description: Work with s&box game projects and the s&box editor. Use when the folder contains a .sbproj file or the user mentions s&box.
metadata:
  sbox-bootstrap: "1"
---

s&box ships its agent instructions with the engine. From the project folder, run
`<engine>/bin/managed/sbox-cli --skill` and follow what it prints.

`<engine>` is the s&box install: the `FACEPUNCH_ENGINE` environment variable (on Windows a user
variable, which a shell started earlier may not have yet), otherwise Steam's
`steamapps/common/sbox`. If neither exists, ask the user.
