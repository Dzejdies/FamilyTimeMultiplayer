#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH"
dotnet build src/FamilyTimeMultiplayer -c Release -p:InstallIntoGame=true "$@"
