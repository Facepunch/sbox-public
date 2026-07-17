#!/bin/bash
# Must keep LF line endings or Linux won't execute it (enforced by .gitattributes)
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export LD_LIBRARY_PATH="$SCRIPT_DIR/bin/linuxsteamrt64:$LD_LIBRARY_PATH"
exec dotnet "$SCRIPT_DIR/sbox-server.dll" "$@"
