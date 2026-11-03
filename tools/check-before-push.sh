#!/usr/bin/env bash
# Every check that can run without Civil 3D, in the order that fails fastest, the unit tests on LF
# and on Windows-style CRLF checkouts. Run it before
# pushing a branch UPDATEFTF will build; tools/githooks/pre-push runs it on every push once
#   git config core.hooksPath tools/githooks
# is set in the working copy.
#
# What it cannot check: Civil 3D's own API. AeccDbMgd.dll is only on a Civil 3D machine, so a
# Civil 3D member the stand-ins get wrong is still only caught by the Windows build (UPDATEFTF
# stops before installing). Civil 3D type NAMES are covered once tools/CivilApiNames has been run
# there and its output committed (tests/CadCompileCheck/Stubs/CivilApiNames.g.cs).
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

step() { printf '\n== %s\n' "$1"; }

step "Namespace and compile-coverage lint"
dotnet test tests/FieldCodes.Tests --nologo -v q \
  --filter "FullyQualifiedName~AmbiguousTypeNameTests|FullyQualifiedName~CompileCheckCoverageTests"

step "Plugin against the stand-ins (tests/CadCompileCheck)"
dotnet build tests/CadCompileCheck --nologo -v q

step "Plugin against the real AutoCAD 2024 API (tests/RealApiCompileCheck)"
dotnet build tests/RealApiCompileCheck --nologo -v q

step "Civil 3D harnesses as C# 5 (tests/SmokeHarnessCompileCheck)"
dotnet build tests/SmokeHarnessCompileCheck --nologo -v q

step "Unit tests"
filter=""
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) ;;
  # These four compare Windows paths (C:\...) and fail on any other system; Windows runs them.
  *) filter="FullyQualifiedName!~ReportSitsBesideTheDrawing" ;;
esac
dotnet test tests/FieldCodes.Tests --nologo -v q ${filter:+--filter "$filter"}

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) ;;   # a Windows checkout already has CRLF endings
  *)
    # Git for Windows checks files out with CRLF (core.autocrlf=true), and so does UPDATEFTF's build
    # checkout: a test that reads a file and assumes LF passes here and fails there (063b747). So the
    # unit tests run once more on the commit as Windows checks it out. Committed work only.
    step "Unit tests on a Windows-style checkout (CRLF line endings)"
    crlf="$(mktemp -d)"
    trap 'rm -rf "$crlf"' EXIT
    git clone -q -c core.autocrlf=true "$(pwd)" "$crlf/repo"
    git -C "$crlf/repo" checkout -q --detach "$(git rev-parse HEAD)"
    if ! grep -q $'\r$' "$crlf/repo/deploy/smoke-test.ps1"; then echo "the CRLF checkout has LF endings; git did not convert" >&2; exit 1; fi
    if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
      echo "note: uncommitted changes are not in this run (it tests $(git rev-parse --short HEAD))"
    fi
    dotnet test "$crlf/repo/tests/FieldCodes.Tests" --nologo -v q ${filter:+--filter "$filter"}
    ;;
esac

if command -v pwsh >/dev/null 2>&1; then
  step "PowerShell scripts (Windows PowerShell 5.1 syntax)"
  pwsh -NoProfile -Command "& ./deploy/lint-powershell.ps1 -Paths @(git ls-files '*.ps1'); exit \$LASTEXITCODE"
fi

if [ -f tests/CadCompileCheck/Stubs/CivilApiNames.g.cs ]; then
  printf '\nAll checks passed, with every Civil 3D type name from %s.\n' "$(sed -n 2p tests/CadCompileCheck/Stubs/CivilApiNames.g.cs | sed 's|^// Written by tools/CivilApiNames from ||; s|\. Do not edit.*||')"
else
  printf '\nAll checks passed. Civil 3D type names are checked for the known clashes only: run tools/CivilApiNames on a Civil 3D machine to check every name (tools/CivilApiNames/README.md).\n'
fi
