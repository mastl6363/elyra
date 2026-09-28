#!/bin/sh
# Launcher installed at /usr/bin/elyra. The app is a self-contained publish
# (bundles its own .NET runtime), so no `dotnet` install is required on the
# target machine.
exec /opt/elyra/Elyra.Desktop "$@"
