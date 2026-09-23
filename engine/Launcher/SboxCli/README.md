# sbox-cli project compilation

`sbox-cli` is a one-shot, non-interactive entry point for compiling s&box project code.
Addresses #11926 with a first automation slice; it does not yet provide every command proposed there.

## Usage

```text
sbox-cli project compile --project <path> [--json]
sbox-cli --help
sbox-cli --version
```

For example:

```powershell
game\sbox-cli.exe project compile --project "samples\my project\my-project.sbproj"
game\sbox-cli.exe project compile --project "samples\my project\my-project.sbproj" --json
```

```bash
./game/sbox-cli project compile --project "samples/my project/my-project.sbproj"
```

Relative project paths are resolved from the caller's working directory, not from the s&box
installation directory. Source builds publish the executable at `game/sbox-cli.exe` on Windows and
`game/sbox-cli` on Linux, beside the other root launchers.

The compile command uses the regular s&box compiler and allow-list. It compiles the project's
`Code` and `Editor` C#, direct local projects under `Libraries`, and required built-in/package
dependencies. It does not compile assets or shaders, run project validation or tests, start the
project, or produce an export.

## Output and exit codes

Human-readable success and `--help`/`--version` output go to stdout. Progress, diagnostics, and
errors go to stderr. With `--json`, stdout contains exactly one JSON document and project
diagnostics are carried by that document; routine progress is suppressed and stderr is empty for
handled results.

JSON schema version 1 has these exact top-level fields:

```text
schemaVersion, command, success, exitCode, category, projectPath,
engineVersion, durationMilliseconds, diagnostics
```

Each item in `diagnostics` has exactly these fields:

```text
severity, id, message, file, line, column
```

Successful result (formatted here for readability; the command writes one compact line):

```json
{
  "schemaVersion": 1,
  "command": "project.compile",
  "success": true,
  "exitCode": 0,
  "category": "success",
  "projectPath": "C:\\projects\\sample\\sample.sbproj",
  "engineVersion": "2026.09.23",
  "durationMilliseconds": 1842,
  "diagnostics": []
}
```

Compilation failure:

```json
{
  "schemaVersion": 1,
  "command": "project.compile",
  "success": false,
  "exitCode": 1,
  "category": "project_failure",
  "projectPath": "C:\\projects\\sample\\sample.sbproj",
  "engineVersion": "2026.09.23",
  "durationMilliseconds": 1621,
  "diagnostics": [
    {
      "severity": "error",
      "id": "CS0103",
      "message": "The name 'missingName' does not exist in the current context",
      "file": "C:\\projects\\sample\\Code\\Example.cs",
      "line": 7,
      "column": 3
    }
  ]
}
```

Exit codes are stable for automation:

- `0`: success
- `1`: project preparation or compilation failure
- `2`: invalid command-line usage
- `3`: internal initialization, bootstrap, or shutdown failure

The command may download/cache package dependencies and writes engine logs to
`game/logs/sbox-cli.log`. Project configuration upgrades are kept in memory and are not saved by
the command.

## Platform scope

Launcher publishing currently supports `win-x64` and `linux-x64`. It requires a complete s&box
installation or source build containing the managed engine and platform-native binaries; package
dependencies must also be locally available or downloadable with the current credentials.

There are not yet commands for `test`, `validate`, `run`, `export`, or `exec`, nor dedicated timeout,
graceful Ctrl+C, or custom log-file options.
