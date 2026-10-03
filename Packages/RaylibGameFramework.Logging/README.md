# RaylibGameFramework.Logging

Reusable net9.0 package project, consumed by Tackle Alley through a project reference.
It is not published to a feed. It has no game or Raylib dependencies.

Use RotatingFileLogger(directory, options).Write("INFO", message) or pass an exception.
Create and dispose CrashLogging(logger) around application lifetime to subscribe to
unhandled managed exceptions and unobserved task exceptions. It does not suppress failures.
The application catches its main-thread exception, logs FATAL, and exits with code 1.

FileLogOptions defaults to 5 MiB per file and five total files (active current.log plus
archive-1.log through archive-4.log; archive-1 is newest). Limits are validated.
Entries include UTC timestamp, severity, process/thread identifiers and exception stacks.
Oversized entries are UTF-8 safely truncated with a marker. Rotation also enforces reduced
limits on existing files. One logger instance/writer per directory is supported; calls
from multiple threads to that instance are serialized. Independent concurrent processes
must use different directories. Filesystem failures report once to stderr without
crashing the application. Files are closed after every write; FATAL additionally flushes
to disk. There is no per-frame logging and no dependency on debug overlay state.

## Tackle Alley

Development logs: artifacts/logs under the repository, for both Debug and Release
executables in its bin tree. Installed/published builds default to
%LOCALAPPDATA%/RaylibTackleAlley/Logs. A publish directory is treated as installed even
when still inside the repository. Detection uses the executable location, not the
current working directory.

Configure logging.json beside the executable (copied on build/publish):
- Directory: null selects the automatic default.
- An absolute Directory uses that path.
- A relative Directory resolves against the repository for development builds,
  or against the executable directory for installed builds.
- MaxFileBytes and RetainedFiles control rotation as before.

Existing AppData logs are not moved or deleted. Only subsequent launches use the new
location; restart an already running game after rebuilding.
Invalid logging configuration is reported using default limits. No settings migration
or gameplay configuration changes are required.

The game records startup/runtime/referenced assemblies, asset initialization stages,
menu actions, state transitions, returner selection, run resets, settings failures,
managed exceptions and clean shutdown. A missing clean-shutdown entry can indicate
abrupt termination but is not proof of a crash.

After reproducing the crash, inspect current.log and the numbered archives before
relaunching repeatedly. Native Raylib faults, stack overflow, process kill and power loss
may bypass managed exception handlers. Native Raylib trace messages and crash dumps are
not captured by this implementation. If the log ends inside native work, collect a
Windows crash dump with an external debugger/ProcDump for the next investigation.
This instrumentation does not establish or fix the original crash cause.

Build/package:
dotnet pack Packages/RaylibGameFramework.Logging/RaylibGameFramework.Logging.csproj -c Release
Tests run in Tests/Controls.Tests.csproj with the full game suite.
